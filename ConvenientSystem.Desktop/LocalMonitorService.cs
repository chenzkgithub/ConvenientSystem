using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem;

/// <summary>
/// 本机监控服务：在桌面客户端本地执行 PowerShell 采集本机指标（CPU/内存/磁盘/网络/IO/开机时长）、
/// 磁盘扫描/清理、设备规格采集与文件图标提取。无数据库依赖，无远程探测，无定时巡检。
/// 扫描/清理为后台异步任务，前端通过 jobId 轮询进度。
/// </summary>
public sealed class LocalMonitorService
{
    private readonly ILogger<LocalMonitorService> _logger;

    public LocalMonitorService(ILogger<LocalMonitorService> logger)
    {
        _logger = logger;
    }

    #region 异步任务跟踪

    private sealed class ScanJobState
    {
        public volatile bool Done;
        public string? Error;
        public int ScannedCount;
        public decimal FoundKb;
        public HostDiskScanDto? Result;
        public DateTime CreatedAt = DateTime.Now;
    }

    private readonly ConcurrentDictionary<string, ScanJobState> _scanJobs = new();

    private sealed class CleanJobState
    {
        public volatile bool Done;
        public string? Error;
        public int TotalCount;
        public int DeletedCount;
        public decimal FreedMb;
        public HostDiskCleanDto? Result;
        public DateTime CreatedAt = DateTime.Now;
    }

    private readonly ConcurrentDictionary<string, CleanJobState> _cleanJobs = new();

    #endregion

    #region 整机概览

    /// <summary>
    /// 整机概览：一次采集本机 CPU/内存/磁盘/网络/IO/开机时长，返回仪表盘快照。
    /// </summary>
    public async Task<HostMetricsSnapshot> GetOverviewAsync(CancellationToken ct)
    {
        var body = "$os=Get-CimInstance Win32_OperatingSystem; "
            + "$proc=Get-CimInstance Win32_Processor; "
            + "$cpu=[math]::Round(($proc | Measure-Object -Property LoadPercentage -Average).Average,1); "
            + "$cores=($proc | Measure-Object -Property NumberOfLogicalProcessors -Sum).Sum; "
            + "$mem=[math]::Round(($os.TotalVisibleMemorySize-$os.FreePhysicalMemory)*100/$os.TotalVisibleMemorySize,1); "
            + "$usedGb=[math]::Round(($os.TotalVisibleMemorySize-$os.FreePhysicalMemory)/1MB,1); "
            + "$gb=[math]::Round($os.TotalVisibleMemorySize/1MB,1); "
            + "$sys=Get-CimInstance Win32_PerfFormattedData_PerfOS_System; "
            + "$up=[math]::Round($sys.SystemUpTime/3600,1); "
            + "$procCount=(Get-CimInstance Win32_Process).Count; "
            + "$net=Get-CimInstance Win32_PerfFormattedData_Tcpip_NetworkInterface | Where-Object { $_.Name -notlike '*isatap*' -and $_.Name -notlike '*Pseudo*' }; "
            + "$inB=($net | Measure-Object -Property BytesReceivedPersec -Sum).Sum; "
            + "$outB=($net | Measure-Object -Property BytesSentPersec -Sum).Sum; "
            + "$io=Get-CimInstance Win32_PerfFormattedData_PerfDisk_PhysicalDisk | Where-Object { $_.Name -eq '_Total' }; "
            + "Write-Output ('OS|'+$os.Caption+'|'+$mem+'|'+$cpu+'|'+$up+'|'+$gb+'|'+$usedGb+'|'+$cores+'|'+$procCount); "
            + "Write-Output ('RATE|'+[math]::Round($inB/1KB,1)+'|'+[math]::Round($outB/1KB,1)"
            + "+'|'+[math]::Round($io.DiskReadBytesPersec/1MB,2)+'|'+[math]::Round($io.DiskWriteBytesPersec/1MB,2)); "
            + "Get-CimInstance Win32_LogicalDisk | Where-Object { $_.DriveType -eq 3 } | ForEach-Object { "
            + "if ($_.Size -gt 0) { Write-Output ('DISK|'+$_.DeviceID+'|'+[math]::Round(($_.Size-$_.FreeSpace)*100/$_.Size,1)"
            + "+'|'+[math]::Round($_.Size/1GB,1)+'|'+[math]::Round($_.FreeSpace/1GB,1)) } }";

        var cmd = "[Console]::OutputEncoding=[Text.Encoding]::UTF8; " + body;
        var (stdout, stderr, ok) = await RunPowerShellAsync(cmd, TimeSpan.FromSeconds(30), ct);
        if (!ok) throw new InvalidOperationException($"整机概览采集失败：{Truncate(stderr)}");

        var snapshot = new HostMetricsSnapshot { CheckedAt = DateTime.Now };
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var p = line.Split('|');
            if (p.Length >= 9 && p[0] == "OS")
            {
                snapshot.OsName = p[1].Trim();
                if (decimal.TryParse(p[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var mem)) snapshot.MemoryPercent = mem;
                if (decimal.TryParse(p[3], NumberStyles.Any, CultureInfo.InvariantCulture, out var cpu)) snapshot.CpuPercent = cpu;
                if (double.TryParse(p[4], NumberStyles.Any, CultureInfo.InvariantCulture, out var up)) snapshot.UptimeHours = up;
                if (decimal.TryParse(p[5], NumberStyles.Any, CultureInfo.InvariantCulture, out var gb)) snapshot.MemoryTotalGb = gb;
                if (decimal.TryParse(p[6], NumberStyles.Any, CultureInfo.InvariantCulture, out var usedGb)) snapshot.MemoryUsedGb = usedGb;
                if (int.TryParse(p[7], out var cores)) snapshot.CpuCores = cores;
                if (int.TryParse(p[8], out var procCount)) snapshot.ProcessCount = procCount;
            }
            else if (p.Length >= 5 && p[0] == "RATE")
            {
                if (decimal.TryParse(p[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var netIn)) snapshot.NetInKbps = netIn;
                if (decimal.TryParse(p[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var netOut)) snapshot.NetOutKbps = netOut;
                if (decimal.TryParse(p[3], NumberStyles.Any, CultureInfo.InvariantCulture, out var ioRead)) snapshot.DiskReadMbPerSec = ioRead;
                if (decimal.TryParse(p[4], NumberStyles.Any, CultureInfo.InvariantCulture, out var ioWrite)) snapshot.DiskWriteMbPerSec = ioWrite;
            }
            else if (p.Length >= 5 && p[0] == "DISK"
                && decimal.TryParse(p[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var used)
                && decimal.TryParse(p[3], NumberStyles.Any, CultureInfo.InvariantCulture, out var total)
                && decimal.TryParse(p[4], NumberStyles.Any, CultureInfo.InvariantCulture, out var free))
            {
                snapshot.Disks.Add(new HostDiskUsage { Drive = p[1], UsedPercent = used, TotalGb = total, FreeGb = free });
            }
        }
        if (snapshot.MemoryPercent == null && snapshot.CpuPercent == null && snapshot.Disks.Count == 0)
            throw new InvalidOperationException("未采集到任何指标");
        return snapshot;
    }

    #endregion

    #region 磁盘扫描/清理异步任务

    /// <summary>启动磁盘扫描任务（后台异步执行），立即返回 jobId</summary>
    public string StartScan(string? categories, string? drive)
    {
        var dto = ParseCategories(categories);
        dto.Drive = NormalizeDrive(drive);
        if (!dto.HasAny)
            throw new InvalidOperationException("请至少勾选一项清理内容");

        CleanupOldJobs(_scanJobs);
        var jobId = Guid.NewGuid().ToString("N");
        var state = new ScanJobState();
        _scanJobs[jobId] = state;
        _ = Task.Run(async () =>
        {
            try
            {
                state.Result = await ScanDiskAsync(dto, (scanned, foundKb) =>
                {
                    state.ScannedCount = scanned;
                    state.FoundKb = foundKb;
                }, CancellationToken.None);
            }
            catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
            {
                state.Error = ex.Message;
            }
            catch (Exception ex)
            {
                state.Error = "磁盘扫描失败：" + ex.Message;
            }
            finally
            {
                state.Done = true;
            }
        });
        return jobId;
    }

    /// <summary>查询扫描任务进度/结果</summary>
    public HostDiskScanJobDto GetScanProgress(string jobId)
    {
        if (!_scanJobs.TryGetValue(jobId, out var state))
            throw new InvalidOperationException("扫描任务不存在或已过期，请重新扫描");
        return new HostDiskScanJobDto
        {
            JobId = jobId,
            Done = state.Done,
            ScannedCount = state.ScannedCount,
            FoundKb = state.FoundKb,
            Error = state.Error,
            Result = state.Done ? state.Result : null,
        };
    }

    /// <summary>启动磁盘清理任务（后台异步执行），立即返回 jobId</summary>
    public string StartClean(HostDiskCleanRequestDto dto)
    {
        dto.Drive = NormalizeDrive(dto.Drive);
        if (!dto.HasAny)
            throw new InvalidOperationException("请至少勾选一项清理内容");
        if (dto.Paths is { Count: > 0 }
            && !(dto.UserTemp || dto.WindowsTemp || dto.Prefetch || dto.UpdateCache
                || dto.BrowserCache || dto.ThumbnailCache || dto.LogFiles || dto.OldDownloads || dto.DriveJunk))
            throw new InvalidOperationException("选定文件清理需勾选对应的清理分类");
        if (dto.RecyclePaths is { Count: > 0 } && !dto.RecycleBin)
            throw new InvalidOperationException("清理回收站条目需勾选回收站");

        CleanupOldJobs(_cleanJobs);
        var jobId = Guid.NewGuid().ToString("N");
        var state = new CleanJobState
        {
            TotalCount = (dto.Paths?.Count ?? 0) + (dto.RecyclePaths?.Count ?? 0),
        };
        _cleanJobs[jobId] = state;
        _ = Task.Run(async () =>
        {
            try
            {
                state.Result = await CleanDiskAsync(dto, (deleted, freedMb) =>
                {
                    state.DeletedCount = deleted;
                    state.FreedMb = freedMb;
                }, CancellationToken.None);
            }
            catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
            {
                state.Error = ex.Message;
            }
            catch (Exception ex)
            {
                state.Error = "磁盘清理失败：" + ex.Message;
            }
            finally
            {
                state.Done = true;
            }
        });
        return jobId;
    }

    /// <summary>查询清理任务进度/结果</summary>
    public HostDiskCleanJobDto GetCleanProgress(string jobId)
    {
        if (!_cleanJobs.TryGetValue(jobId, out var state))
            throw new InvalidOperationException("清理任务不存在或已过期，请重新操作");
        return new HostDiskCleanJobDto
        {
            JobId = jobId,
            Done = state.Done,
            TotalCount = state.TotalCount,
            DeletedCount = state.DeletedCount,
            FreedMb = state.FreedMb,
            Error = state.Error,
            Result = state.Done ? state.Result : null,
        };
    }

    /// <summary>启动新任务时顺手清理 10 分钟前已完成的旧任务</summary>
    private static void CleanupOldJobs<T>(ConcurrentDictionary<string, T> jobs) where T : class
    {
        var doneProp = typeof(T).GetField("Done");
        var createdAtField = typeof(T).GetField("CreatedAt");
        if (doneProp == null || createdAtField == null) return;
        var cutoff = DateTime.Now.AddMinutes(-10);
        foreach (var kv in jobs)
        {
            var done = (bool)(doneProp.GetValue(kv.Value) ?? false);
            var createdAt = (DateTime)(createdAtField.GetValue(kv.Value) ?? DateTime.Now);
            if (done && createdAt < cutoff)
                jobs.TryRemove(kv.Key, out _);
        }
    }

    #endregion

    #region 磁盘扫描

    /// <summary>
    /// 扫描磁盘可清理候选文件（仅读取不删除）：按勾选分类列出全部文件（不按修改时间过滤）
    /// （名称/路径/大小/最后修改时间，最多 3000 条），回收站单独统计项目数与占用空间。
    /// onProgress：扫描中逐百条实时上报（已扫描文件数, 已发现大小KB），供前端展示扫描进度。
    /// </summary>
    public async Task<HostDiskScanDto> ScanDiskAsync(HostDiskCleanRequestDto req, Action<int, decimal>? onProgress, CancellationToken ct)
    {
        var drv = (req.Drive ?? "C").Trim().ToUpperInvariant();
        if (drv.Length != 1 || drv[0] < 'A' || drv[0] > 'Z')
            throw new InvalidOperationException("盘符不合法，应为单个字母 A-Z");
        var hasAnyFileCat = req.UserTemp || req.WindowsTemp || req.Prefetch || req.UpdateCache
            || req.BrowserCache || req.ThumbnailCache || req.LogFiles || req.OldDownloads || req.DriveJunk;

        var cats = new List<(string Code, string DirExpr)>();
        if (req.UserTemp) cats.Add(("USER_TEMP", "$env:TEMP"));
        if (req.WindowsTemp) cats.Add(("WIN_TEMP", "($env:WINDIR+'\\Temp')"));
        if (req.Prefetch) cats.Add(("PREFETCH", "($env:WINDIR+'\\Prefetch')"));
        if (req.UpdateCache) cats.Add(("UPDATE_CACHE", "($env:WINDIR+'\\SoftwareDistribution\\Download')"));
        if (req.BrowserCache) cats.Add(("BROWSER_CACHE", "($env:LOCALAPPDATA+'\\Google\\Chrome\\User Data\\Default\\Cache')"));
        if (req.ThumbnailCache) cats.Add(("THUMBNAIL_CACHE", "($env:LOCALAPPDATA+'\\Microsoft\\Windows\\Explorer')"));
        if (req.LogFiles) cats.Add(("LOG_FILE", "$env:TEMP"));
        if (req.OldDownloads) cats.Add(("OLD_DOWNLOAD", "($env:USERPROFILE+'\\Downloads')"));

        var body = "$cnt=0; $cap=3000; $sz=0; $drv='" + drv + "'; $sysl=$env:SystemDrive.Substring(0,1); if ($drv -eq $sysl) { ";

        foreach (var (code, dir) in cats.Where(c => !IsSpecialCategory(c.Code)))
        {
            body += "$d=" + dir + "; if (Test-Path $d) { Get-ChildItem $d -Recurse -Force -ErrorAction SilentlyContinue "
                + "| Where-Object { -not $_.PSIsContainer } "
                + "| ForEach-Object { $cnt++; $sz+=$_.Length; if ($cnt -le $cap) { "
                + "Write-Output ('FILE|" + code + "|'+[math]::Round($_.Length/1KB,1)+'|'+$_.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')+'|'+$_.FullName) }; "
                + "if ($cnt % 100 -eq 0) { Write-Output ('PROGRESS|'+$cnt+'|'+[math]::Round($sz/1KB,1)) } } }; ";
        }

        if (req.BrowserCache)
        {
            body += "$browserPaths=@("
                + "($env:LOCALAPPDATA+'\\Google\\Chrome\\User Data\\Default\\Cache'),"
                + "($env:LOCALAPPDATA+'\\Microsoft\\Edge\\User Data\\Default\\Cache'),"
                + "($env:LOCALAPPDATA+'\\Mozilla\\Firefox\\Profiles')"
                + "); foreach ($bp in $browserPaths) { if (Test-Path $bp) { Get-ChildItem $bp -Recurse -Force -ErrorAction SilentlyContinue "
                + "| Where-Object { -not $_.PSIsContainer } "
                + "| ForEach-Object { $cnt++; $sz+=$_.Length; if ($cnt -le $cap) { "
                + "Write-Output ('FILE|BROWSER_CACHE|'+[math]::Round($_.Length/1KB,1)+'|'+$_.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')+'|'+$_.FullName) }; "
                + "if ($cnt % 100 -eq 0) { Write-Output ('PROGRESS|'+$cnt+'|'+[math]::Round($sz/1KB,1)) } } } }; ";
        }

        if (req.ThumbnailCache)
        {
            body += "$td=$env:LOCALAPPDATA+'\\Microsoft\\Windows\\Explorer'; if (Test-Path $td) { Get-ChildItem $td -Force -Filter 'thumbcache_*.db' -ErrorAction SilentlyContinue "
                + "| ForEach-Object { $cnt++; $sz+=$_.Length; if ($cnt -le $cap) { "
                + "Write-Output ('FILE|THUMBNAIL_CACHE|'+[math]::Round($_.Length/1KB,1)+'|'+$_.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')+'|'+$_.FullName) }; "
                + "if ($cnt % 100 -eq 0) { Write-Output ('PROGRESS|'+$cnt+'|'+[math]::Round($sz/1KB,1)) } } }; ";
        }

        if (req.LogFiles)
        {
            body += "$logPaths=@($env:TEMP, ($env:ProgramData)); foreach ($lp in $logPaths) { if (Test-Path $lp) { Get-ChildItem $lp -Recurse -Force -Filter '*.log' -ErrorAction SilentlyContinue "
                + "| where-Object { -not $_.PSIsContainer } "
                + "| ForEach-Object { $cnt++; $sz+=$_.Length; if ($cnt -le $cap) { "
                + "Write-Output ('FILE|LOG_FILE|'+[math]::Round($_.Length/1KB,1)+'|'+$_.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')+'|'+$_.FullName) }; "
                + "if ($cnt % 100 -eq 0) { Write-Output ('PROGRESS|'+$cnt+'|'+[math]::Round($sz/1KB,1)) } } } }; ";
        }

        if (req.OldDownloads)
        {
            var days = req.OldDownloadsDays > 0 ? req.OldDownloadsDays : 30;
            body += "$od=$env:USERPROFILE+'\\Downloads'; $cut=(Get-Date).AddDays(-" + days + "); if (Test-Path $od) { Get-ChildItem $od -Recurse -Force -ErrorAction SilentlyContinue "
                + "| Where-Object { -not $_.PSIsContainer -and $_.LastAccessTime -lt $cut } "
                + "| ForEach-Object { $cnt++; $sz+=$_.Length; if ($cnt -le $cap) { "
                + "Write-Output ('FILE|OLD_DOWNLOAD|'+[math]::Round($_.Length/1KB,1)+'|'+$_.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')+'|'+$_.FullName) }; "
                + "if ($cnt % 100 -eq 0) { Write-Output ('PROGRESS|'+$cnt+'|'+[math]::Round($sz/1KB,1)) } } }; ";
        }

        body += hasAnyFileCat
            ? " } else { " + JunkScanBody("$drv+':\\'") + " }; "
            : " }; ";

        // 回收站
        body += "$rn=0; $rs=0; $seen=New-Object System.Collections.Generic.HashSet[string]; "
            + "try { $sid=[System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value; "
            + "Get-CimInstance Win32_LogicalDisk | Where-Object { $_.DriveType -eq 3 -or $_.DriveType -eq 2 } | ForEach-Object { "
            + "$bd=Join-Path ($_.DeviceID+'\\$Recycle.Bin') $sid; "
            + "if (Test-Path -LiteralPath $bd) { Get-ChildItem -LiteralPath $bd -Force -Filter '$I*' -ErrorAction SilentlyContinue | ForEach-Object { "
            + "if ($_.Name.Length -gt 2) { $id=$_.Name.Substring(2); $rp=Join-Path $_.DirectoryName ('$R'+$id); "
            + "try { $null=$seen.Add($rp.ToLower()) } catch { }; $nm=$id; $dd=''; $isz=0; $df=''; "
            + "try { $b=[IO.File]::ReadAllBytes($_.FullName); if ($b.Length -ge 28) { "
            + "$isz=[BitConverter]::ToInt64($b,8); "
            + "try { $dd=[DateTime]::FromFileTime([BitConverter]::ToInt64($b,16)).ToString('yyyy-MM-dd HH:mm:ss') } catch { }; "
            + "$op=[Text.Encoding]::Unicode.GetString($b,28,$b.Length-28).TrimEnd([char]0); "
            + "if ($op) { $df=Split-Path $op -Parent; $nm=Split-Path $op -Leaf } } } catch { }; "
            + "$rsz=$isz; if (Test-Path -LiteralPath $rp) { try { $rsz=(Get-Item -LiteralPath $rp).Length } catch { } }; $rs+=$rsz; $rn++; "
            + "if ($rn -le $cap) { Write-Output ('RB|'+[math]::Round($rsz/1KB,1)+'|'+$dd+'|'+$nm+'|'+$df+'|'+$rp) } } } } } } catch { }; ";
        // Shell 兜底
        body += "try { $sh=New-Object -ComObject Shell.Application; $rb=$sh.Namespace(0xA); "
            + "if ($rb -ne $null) { $rb.Items() | ForEach-Object { try { if ($seen.Contains($_.Path.ToLower())) { return } } catch { }; "
            + "try { $null=$seen.Add($_.Path.ToLower()) } catch { }; $rn++; try { $rs+=$_.Size } catch { }; "
            + "if ($rn -le $cap) { $dd=''; try { $dd=$_.ExtendedProperty('System.Recycle.DateDeleted').ToString('yyyy-MM-dd HH:mm:ss') } catch { }; "
            + "$df=''; try { $df=$_.ExtendedProperty('System.Recycle.DeletedFrom') } catch { }; "
            + "Write-Output ('RB|'+[math]::Round($_.Size/1KB,1)+'|'+$dd+'|'+$_.Name+'|'+$df+'|'+$_.Path) } } } } catch { }; "
            + "Write-Output ('RECYCLE|'+$rn+'|'+[math]::Round($rs/1KB,1))";
        body += "if ($cnt -gt $cap) { Write-Output 'STRUNC' }";

        var cmd = "[Console]::OutputEncoding=[Text.Encoding]::UTF8; " + body;
        var (stdout, stderr, ok) = await RunPowerShellAsync(cmd, TimeSpan.FromMinutes(10), ct,
            onProgress == null ? null : line =>
            {
                if (!line.StartsWith("PROGRESS|")) return;
                var p = line.Split('|');
                if (p.Length >= 3
                    && int.TryParse(p[1], out var scanned)
                    && decimal.TryParse(p[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var foundKb))
                    onProgress(scanned, foundKb);
            });
        if (!ok) throw new InvalidOperationException($"磁盘扫描失败：{Truncate(stderr)}");

        var result = new HostDiskScanDto();
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line == "STRUNC")
            {
                result.Truncated = true;
            }
            else if (line.StartsWith("RECYCLE|"))
            {
                var p = line.Split('|');
                if (p.Length >= 3
                    && int.TryParse(p[1], out var count)
                    && decimal.TryParse(p[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var sizeKb))
                {
                    result.RecycleCount = count;
                    result.RecycleSizeKb = sizeKb;
                }
            }
            else if (line.StartsWith("RB|"))
            {
                var p = line.Split('|', 6);
                if (p.Length >= 6
                    && decimal.TryParse(p[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var sizeKb))
                {
                    DateTime.TryParse(p[2], CultureInfo.InvariantCulture, DateTimeStyles.None, out var deleted);
                    var name = p[3];
                    var from = p[4];
                    result.RecycleFiles.Add(new HostDiskFileDto
                    {
                        Category = "RECYCLE",
                        Name = name,
                        Path = p[5],
                        OriginalPath = string.IsNullOrEmpty(from) ? name : from + "\\" + name,
                        SizeKb = sizeKb,
                        LastWriteTime = deleted,
                    });
                }
            }
            else if (line.StartsWith("FILE|"))
            {
                var p = line.Split('|');
                if (p.Length >= 5
                    && decimal.TryParse(p[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var sizeKb)
                    && DateTime.TryParse(p[3], CultureInfo.InvariantCulture, DateTimeStyles.None, out var lastWrite))
                {
                    var path = string.Join("|", p.Skip(4));
                    result.Files.Add(new HostDiskFileDto
                    {
                        Category = p[1],
                        Name = path.Contains('\\') ? path[(path.LastIndexOf('\\') + 1)..] : path,
                        Path = path,
                        SizeKb = sizeKb,
                        LastWriteTime = lastWrite,
                    });
                }
            }
        }
        return result;
    }

    #endregion

    #region 磁盘清理

    /// <summary>
    /// 清理磁盘临时文件：指定 Paths 时仅删除选定的文件（需位于勾选分类目录下），
    /// 否则删除勾选项下全部文件；回收站按 RecyclePaths 勾选条目逐条删除；
    /// 返回释放空间与已删除文件清单（最多 2000 条）。
    /// onProgress：清理中逐 20 条实时上报（已删除数, 已释放MB）。
    /// </summary>
    public async Task<HostDiskCleanDto> CleanDiskAsync(HostDiskCleanRequestDto req, Action<int, decimal>? onProgress, CancellationToken ct)
    {
        var hasPaths = req.Paths is { Count: > 0 };
        var drv = (req.Drive ?? "C").Trim().ToUpperInvariant();
        if (drv.Length != 1 || drv[0] < 'A' || drv[0] > 'Z')
            throw new InvalidOperationException("盘符不合法，应为单个字母 A-Z");

        var roots = new List<string>();
        if (req.UserTemp) roots.Add("$env:TEMP");
        if (req.WindowsTemp) roots.Add("($env:WINDIR+'\\Temp')");
        if (req.Prefetch) roots.Add("($env:WINDIR+'\\Prefetch')");
        if (req.UpdateCache) roots.Add("($env:WINDIR+'\\SoftwareDistribution\\Download')");
        if (req.BrowserCache)
        {
            roots.Add("($env:LOCALAPPDATA+'\\Google\\Chrome\\User Data\\Default\\Cache')");
            roots.Add("($env:LOCALAPPDATA+'\\Microsoft\\Edge\\User Data\\Default\\Cache')");
            roots.Add("($env:LOCALAPPDATA+'\\Mozilla\\Firefox\\Profiles')");
        }
        if (req.ThumbnailCache) roots.Add("($env:LOCALAPPDATA+'\\Microsoft\\Windows\\Explorer')");
        if (req.LogFiles) { roots.Add("$env:TEMP"); roots.Add("$env:ProgramData"); }
        if (req.OldDownloads) roots.Add("($env:USERPROFILE+'\\Downloads')");

        if (hasPaths && req.PathCategories is { Count: > 0 })
        {
            foreach (var cat in req.PathCategories.Values.Distinct(StringComparer.OrdinalIgnoreCase))
                foreach (var r in CategoryRootExprs(cat))
                    if (!roots.Contains(r)) roots.Add(r);
        }

        var body = "$before=(Get-CimInstance Win32_LogicalDisk | Where-Object { $_.DeviceID -eq '" + drv + ":' }).FreeSpace; "
            + "$n=0; $fd=0; $files=New-Object System.Collections.Generic.List[string]; $cap=2000; "
            + "$drv='" + drv + "'; $sysl=$env:SystemDrive.Substring(0,1); "
            + "$elev=$false; try { $elev=([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent())"
            + ".IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) } catch { }; "
            + "function Get-DelFail { param([string]$fp,[string]$msg) "
            + "$m=([string]$msg) -replace '\\|','/'; "
            + "if ($m -notmatch '拒绝|denied|Denied|UnauthorizedAccess') { if ($m) { return $m } else { return '文件可能被占用' } }; "
            + "$lk=$false; try { $st=[IO.File]::Open($fp,'Open','ReadWrite','None'); $st.Close() } catch { $lk=$true }; "
            + "if ($lk) { return '文件正被其他程序占用，关闭占用它的程序后重试' }; "
            + "if (-not $elev) { return '权限不足，请以管理员身份运行本程序后重试' }; "
            + "return '系统保护或被系统进程占用，无法删除' }; ";

        if (hasPaths)
        {
            var sysSel = "$roots=@(" + string.Join(",", roots) + "); "
                + "$rn=New-Object System.Collections.Generic.List[string]; "
                + "foreach ($r in $roots) { if (-not $r) { continue }; $rv=$r.TrimEnd('\\'); "
                + "if (-not $rn.Contains($rv)) { $rn.Add($rv) }; "
                + "$ri=Get-Item -LiteralPath $r -Force -ErrorAction SilentlyContinue; "
                + "if ($ri -ne $null) { $rl=$ri.FullName.TrimEnd('\\'); if (-not $rn.Contains($rl)) { $rn.Add($rl) } } }; "
                + "$paths=@(" + string.Join(",", req.Paths!.Select(p => "'" + EscapePs(p) + "'")) + "); "
                + "foreach ($p in $paths) { "
                + "if (-not (Test-Path -LiteralPath $p)) { Write-Output ('ITEM|0|文件不存在（可能已被清理）|'+$p); continue }; "
                + "$it=Get-Item -LiteralPath $p -Force -ErrorAction SilentlyContinue; "
                + "if ($it -eq $null) { Write-Output ('ITEM|0|无法读取文件信息|'+$p); continue }; "
                + "if ($it.PSIsContainer) { Write-Output ('ITEM|0|是目录，不在文件清理范围|'+$p); continue }; "
                + "$cp=$it.FullName; if (-not $cp) { $cp=$p }; "
                + "$ok=$false; foreach ($r in $rn) { if ($cp.StartsWith($r+'\\',[System.StringComparison]::OrdinalIgnoreCase)) { $ok=$true; break } }; "
                + "if (-not $ok) { Write-Output ('ITEM|0|不在勾选分类目录下，安全跳过|'+$p); continue }; "
                + "try { $fsz=$it.Length; if (([int]$it.Attributes -band 7) -ne 0) { try { $it.Attributes='Normal' } catch { } }; "
                + "Remove-Item -LiteralPath $p -Force -ErrorAction Stop; $n++; $fd+=$fsz; "
                + "if ($n % 20 -eq 0) { Write-Output ('CPROG|'+$n+'|'+[math]::Round($fd/1MB,1)) }; "
                + "if ($files.Count -lt $cap) { $files.Add($p) }; Write-Output ('ITEM|1|已删除|'+$p) } "
                + "catch { $em=$_.Exception.Message; "
                + "try { [IO.File]::Delete($cp); $n++; $fd+=$fsz; "
                + "if ($n % 20 -eq 0) { Write-Output ('CPROG|'+$n+'|'+[math]::Round($fd/1MB,1)) }; "
                + "if ($files.Count -lt $cap) { $files.Add($p) }; Write-Output ('ITEM|1|已删除|'+$p) } "
                + "catch { Write-Output ('ITEM|0|删除失败：'+(Get-DelFail $cp $em)+'|'+$p) } } }";
            var junkSel = "$exts=" + JunkExtArray + "; $pd=$drv+':\\'; "
                + "$paths=@(" + string.Join(",", req.Paths!.Select(p => "'" + EscapePs(p) + "'")) + "); "
                + "foreach ($p in $paths) { "
                + "if (-not (Test-Path -LiteralPath $p)) { Write-Output ('ITEM|0|文件不存在（可能已被清理）|'+$p); continue }; "
                + "$it=Get-Item -LiteralPath $p -Force -ErrorAction SilentlyContinue; "
                + "if ($it -eq $null) { Write-Output ('ITEM|0|无法读取文件信息|'+$p); continue }; "
                + "if ($it.PSIsContainer) { Write-Output ('ITEM|0|是目录，不在文件清理范围|'+$p); continue }; "
                + "$cp=$it.FullName; if (-not $cp) { $cp=$p }; "
                + "if (-not (($cp.StartsWith($pd,[System.StringComparison]::OrdinalIgnoreCase)) -and ($exts -contains $it.Extension.ToLower()))) { Write-Output ('ITEM|0|不在该盘或非垃圾文件类型，安全跳过|'+$p); continue }; "
                + "try { $fsz=$it.Length; if (([int]$it.Attributes -band 7) -ne 0) { try { $it.Attributes='Normal' } catch { } }; "
                + "Remove-Item -LiteralPath $p -Force -ErrorAction Stop; $n++; $fd+=$fsz; "
                + "if ($n % 20 -eq 0) { Write-Output ('CPROG|'+$n+'|'+[math]::Round($fd/1MB,1)) }; "
                + "if ($files.Count -lt $cap) { $files.Add($p) }; Write-Output ('ITEM|1|已删除|'+$p) } "
                + "catch { $em=$_.Exception.Message; "
                + "try { [IO.File]::Delete($cp); $n++; $fd+=$fsz; "
                + "if ($n % 20 -eq 0) { Write-Output ('CPROG|'+$n+'|'+[math]::Round($fd/1MB,1)) }; "
                + "if ($files.Count -lt $cap) { $files.Add($p) }; Write-Output ('ITEM|1|已删除|'+$p) } "
                + "catch { Write-Output ('ITEM|0|删除失败：'+(Get-DelFail $cp $em)+'|'+$p) } } }";
            body += "if ($drv -eq $sysl) { " + sysSel + " } else { " + junkSel + " }; ";
        }
        else if (roots.Count > 0 || req.DriveJunk)
        {
            var junkAll = "$exts=" + JunkExtArray + "; $root=$drv+':\\'; if (Test-Path -LiteralPath $root) { "
                + "Get-ChildItem -LiteralPath $root -Recurse -Force -File -ErrorAction SilentlyContinue "
                + "| Where-Object { ($exts -contains $_.Extension.ToLower()) -and ($_.FullName -notlike '*\\$Recycle.Bin\\*') -and ($_.FullName -notlike '*\\System Volume Information\\*') } "
                + "| ForEach-Object { $fi=$_; try { $fsz=$fi.Length; "
                + "if (([int]$fi.Attributes -band 7) -ne 0) { try { $fi.Attributes='Normal' } catch { } }; "
                + "Remove-Item -LiteralPath $fi.FullName -Force -ErrorAction Stop; $n++; $fd+=$fsz; "
                + "if ($n % 20 -eq 0) { Write-Output ('CPROG|'+$n+'|'+[math]::Round($fd/1MB,1)) }; "
                + "if ($files.Count -lt $cap) { $files.Add($fi.FullName) } } "
                + "catch { try { [IO.File]::Delete($fi.FullName); $n++; $fd+=$fsz; "
                + "if ($files.Count -lt $cap) { $files.Add($fi.FullName) } } catch { } } } }";
            if (roots.Count > 0)
            {
                var sysAll = "foreach ($d in @(" + string.Join(",", roots) + ")) { "
                    + "if (Test-Path $d) { Get-ChildItem $d -Recurse -Force -ErrorAction SilentlyContinue "
                    + "| Where-Object { -not $_.PSIsContainer } "
                    + "| ForEach-Object { $fi=$_; try { $fsz=$fi.Length; "
                    + "if (([int]$fi.Attributes -band 7) -ne 0) { try { $fi.Attributes='Normal' } catch { } }; "
                    + "Remove-Item -LiteralPath $fi.FullName -Force -ErrorAction Stop; $n++; $fd+=$fsz; "
                    + "if ($n % 20 -eq 0) { Write-Output ('CPROG|'+$n+'|'+[math]::Round($fd/1MB,1)) }; "
                    + "if ($files.Count -lt $cap) { $files.Add($fi.FullName) } } "
                    + "catch { try { [IO.File]::Delete($fi.FullName); $n++; $fd+=$fsz; "
                    + "if ($files.Count -lt $cap) { $files.Add($fi.FullName) } } catch { } } } } }";
                body += "if ($drv -eq $sysl) { " + sysAll + " } else { " + junkAll + " }; ";
            }
            else
            {
                body += "if ($drv -ne $sysl) { " + junkAll + " }; ";
            }
        }
        if (req.RecycleBin && req.RecyclePaths is { Count: > 0 })
        {
            body += "$rp=@(" + string.Join(",", req.RecyclePaths!.Select(p => "'" + EscapePs(p) + "'")) + "); "
                + "foreach ($p in $rp) { $okDel=$false; $fsz=0; $rem=''; $exists=(Test-Path -LiteralPath $p); "
                + "try { $ri=Get-Item -LiteralPath $p -Force -ErrorAction SilentlyContinue; if (($ri -ne $null) -and (-not $ri.PSIsContainer)) { $fsz=$ri.Length } } catch { }; "
                + "if ($exists) { try { Remove-Item -LiteralPath $p -Force -Recurse -ErrorAction Stop; $okDel=$true } catch { $rem=$_.Exception.Message } }; "
                + "$leaf=[IO.Path]::GetFileName($p); "
                + "if ($leaf.StartsWith('$R')) { $ip=Join-Path ([IO.Path]::GetDirectoryName($p)) ('$I'+$leaf.Substring(2)); "
                + "if (Test-Path -LiteralPath $ip) { try { Remove-Item -LiteralPath $ip -Force -ErrorAction SilentlyContinue; $okDel=$true } catch { } } }; "
                + "if ($okDel) { $n++; $fd+=$fsz; if ($n % 20 -eq 0) { Write-Output ('CPROG|'+$n+'|'+[math]::Round($fd/1MB,1)) }; "
                + "if ($files.Count -lt $cap) { $files.Add($p) }; Write-Output ('ITEM|1|已删除|'+$p) } "
                + "else { if (-not $exists) { Write-Output ('ITEM|0|回收站条目不存在（可能已被清理）|'+$p) } "
                + "else { Write-Output ('ITEM|0|删除失败：'+(Get-DelFail $p $rem)+'|'+$p) } } }; ";
        }
        body += "$after=(Get-CimInstance Win32_LogicalDisk | Where-Object { $_.DeviceID -eq '" + drv + ":' }).FreeSpace; "
            + "foreach ($f in $files) { Write-Output ('F|'+$f) }; "
            + "if ($n -gt $files.Count) { Write-Output 'FTRUNC' }; "
            + "$rel=[double]$after-[double]$before; if ($rel -lt 0) { $rel=0 }; "
            + "Write-Output ('CLEAN|'+[math]::Round($rel/1MB,1)+'|'+[math]::Round([double]$after/1GB,1)+'|'+$n)";

        var script = "[Console]::OutputEncoding=[Text.Encoding]::UTF8; " + body;
        var (stdout, stderr, ok) = await RunPowerShellScriptAsync(script, TimeSpan.FromMinutes(5), ct,
            onProgress == null ? null : line =>
            {
                if (!line.StartsWith("CPROG|")) return;
                var p = line.Split('|');
                if (p.Length >= 3
                    && int.TryParse(p[1], out var deleted)
                    && decimal.TryParse(p[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var freedMb))
                    onProgress(deleted, freedMb);
            });
        if (!ok) throw new InvalidOperationException($"磁盘清理执行失败：{Truncate(stderr)}");

        var result = new HostDiskCleanDto();
        var parsed = false;
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("F|"))
            {
                if (line.Length > 2) result.Files.Add(line[2..].Trim());
            }
            else if (line.StartsWith("ITEM|"))
            {
                var ip = line.Split('|', 4);
                if (ip.Length >= 4)
                    result.Items.Add(new HostDiskCleanItemDto { Ok = ip[1] == "1", Reason = ip[2], Path = ip[3].Trim() });
            }
            else if (line == "FTRUNC")
            {
                result.FilesTruncated = true;
            }
            else
            {
                var p = line.Split('|');
                if (p.Length >= 4 && p[0] == "CLEAN"
                    && decimal.TryParse(p[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var freedMb)
                    && decimal.TryParse(p[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var freeGb)
                    && int.TryParse(p[3], out var files))
                {
                    result.FreedMb = freedMb;
                    result.FreeGbAfter = freeGb;
                    result.DeletedFiles = files;
                    parsed = true;
                }
            }
        }
        if (!parsed) throw new InvalidOperationException("磁盘清理未返回结果");
        return result;
    }

    #endregion

    #region 设备规格

    /// <summary>
    /// 采集设备规格（设备名/处理器/内存/显卡/存储/设备ID/产品ID/系统类型/笔和触控）
    /// 与 ipconfig /all 原始输出。输出协议：KV|键|值 行 + NETBEGIN/NETEND 之间的 ipconfig 原文。
    /// </summary>
    public async Task<HostSystemInfoDto> GetSystemInfoAsync(CancellationToken ct)
    {
        var body = @"$ErrorActionPreference='SilentlyContinue'
function KV($k,$v){ if($null -eq $v){$v=''}; Write-Output ('KV|'+$k+'|'+(($v -join ' ') -replace '[\r\n]+',' ')) }
$cs=Get-CimInstance Win32_ComputerSystem
$cpu=Get-CimInstance Win32_Processor | Select-Object -First 1
$mem=Get-CimInstance Win32_PhysicalMemory
$os=Get-CimInstance Win32_OperatingSystem
$vid=Get-CimInstance Win32_VideoController | Select-Object -First 1
$disk=Get-CimInstance Win32_DiskDrive | Select-Object -First 1
$nt=Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
$cry=Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Cryptography'
KV DEV $cs.Name
KV MODEL (($cs.Manufacturer+' '+$cs.Model) -replace '\s+',' ')
if($cpu){ KV CPU ($cpu.Name+'  '+[math]::Round($cpu.MaxClockSpeed/1000,2)+' GHz') }
if($mem){ $tot=0; foreach($m in @($mem)){ $tot+=[long]$m.Capacity }; $spd=@($mem)[0].Speed; $s=[math]::Round($tot/1GB,1).ToString()+' GB ('+[math]::Round($os.FreePhysicalMemory/1MB,1)+' GB 可用)'; if($spd){ $s+='  '+$spd+' MHz' }; KV RAM $s }
if($vid){ $vm=0; if($vid.AdapterRAM -gt 0){ $vm=[math]::Round($vid.AdapterRAM/1MB) }; KV GPU ($vid.Name+' ('+$vm+' MB)') }
if($disk){ KV DISK ([math]::Round($disk.Size/1GB).ToString()+' GB '+$disk.Model) }
KV DEVID $cry.MachineGuid
KV PRODID $nt.ProductId
KV SYSTYPE ($os.OSArchitecture+' 位操作系统，基于 '+$cs.SystemType+' 的处理器')
try { Add-Type -Name CsUser32Kv -Namespace CsWinKv -MemberDefinition '[DllImport(""user32.dll"")] public static extern int GetSystemMetrics(int nIndex);'; $m=[CsWinKv.CsUser32Kv]::GetSystemMetrics(92); $it=@(); if($m -band 0x4){$it+='笔'}; if($m -band 0x1){$it+='触控'}; if($it.Count -gt 0){ KV PEN ('支持' + ($it -join '和') + '输入') } else { KV PEN '没有可用于此显示器的笔或触控输入' } } catch { KV PEN '' }
$net=''
$tmp=Join-Path $env:TEMP ('cs_ipcfg_'+[guid]::NewGuid().ToString('N')+'.txt')
try {
  cmd /c ('ipconfig /all > ""' + $tmp + '"" 2>&1')
  if (Test-Path -LiteralPath $tmp) { $net=[IO.File]::ReadAllText($tmp, [Text.Encoding]::GetEncoding((Get-Culture).TextInfo.OEMCodePage)) }
} finally {
  Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
}
Write-Output 'NETBEGIN'
Write-Output $net
Write-Output 'NETEND'";

        var script = "[Console]::OutputEncoding=[Text.Encoding]::UTF8; " + body;
        var (stdout, stderr, ok) = await RunPowerShellScriptAsync(script, TimeSpan.FromSeconds(60), ct);
        if (!ok) throw new InvalidOperationException($"设备规格采集失败：{Truncate(stderr)}");

        var dto = new HostSystemInfoDto();
        var net = new StringBuilder();
        var inNet = false;
        foreach (var raw in stdout.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line == "NETBEGIN") { inNet = true; continue; }
            if (line == "NETEND") { inNet = false; continue; }
            if (inNet) { net.AppendLine(line); continue; }
            if (!line.StartsWith("KV|")) continue;
            var p = line.Split('|', 3);
            if (p.Length < 3) continue;
            var v = p[2].Trim();
            switch (p[1])
            {
                case "DEV": dto.DeviceName = v; break;
                case "MODEL": dto.Model = v; break;
                case "CPU": dto.Processor = v; break;
                case "RAM": dto.Ram = v; break;
                case "GPU": dto.Gpu = v; break;
                case "DISK": dto.Storage = v; break;
                case "DEVID": dto.DeviceId = v; break;
                case "PRODID": dto.ProductId = v; break;
                case "SYSTYPE": dto.SystemType = v; break;
                case "PEN": dto.PenTouch = v; break;
            }
        }
        dto.NetworkText = net.ToString().TrimEnd();
        return dto;
    }

    #endregion

    #region 打开文件夹/回收站

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ILCreateFromPathW(string pszPath);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl, IntPtr[]? apidl, uint dwFlags);

    [DllImport("shell32.dll")]
    private static extern void ILFree(IntPtr pidl);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ShellExecuteW(IntPtr hwnd, string? lpOperation, string lpFile, string? lpParameters, string? lpDirectory, int nShowCmd);

    private const string RecycleBinClsid = "::{645FF040-5081-101B-9F08-00AA002F954E}";

    /// <summary>
    /// 打开指定候选文件的所在文件夹并选中该文件。文件已不存在时降级为打开所在目录，
    /// 目录也不存在则静默跳过。Shell API 调用，不派生 explorer.exe 进程。
    /// </summary>
    public Task OpenFolderAsync(string path)
    {
        var openTarget = path;
        if (!File.Exists(path))
        {
            var dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return Task.CompletedTask;
            openTarget = dir;
        }

        int hr = -1;
        var thread = new Thread(() =>
        {
            var pidl = ILCreateFromPathW(openTarget);
            if (pidl == IntPtr.Zero) { hr = 0; return; }
            try { hr = SHOpenFolderAndSelectItems(pidl, 0, null, 0); }
            finally { ILFree(pidl); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(10));

        if (hr != 0)
            throw new InvalidOperationException("打开所在文件夹失败，请手动定位：" + path);
        return Task.CompletedTask;
    }

    /// <summary>打开系统回收站：Shell 打开 CLSID 路径等同双击桌面回收站。</summary>
    public Task OpenRecycleBinAsync()
    {
        long h = 0;
        var thread = new Thread(() =>
        {
            h = (long)ShellExecuteW(IntPtr.Zero, "open", RecycleBinClsid, null, null, 1);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(10));

        if (h <= 32)
            throw new InvalidOperationException("打开回收站失败，请手动打开");
        return Task.CompletedTask;
    }

    #endregion

    #region 文件图标提取

    private const uint ShgfiIcon = 0x100;
    private const uint ShgfiLargeIcon = 0x0;
    private const uint ShgfiUseFileAttributes = 0x10;
    private const uint FileAttributeNormal = 0x80;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static readonly ConcurrentDictionary<string, string> IconCache = new();

    /// <summary>
    /// 提取文件扩展名对应的真实系统图标（与资源管理器展示一致），以 data URL 返回，按扩展名缓存。
    /// 提取失败的扩展名不在结果中，前端回退通用图标。
    /// </summary>
    public Task<Dictionary<string, string>> GetFileIconsAsync(string[] exts)
    {
        var list = exts
            .Select(e => e.Trim().ToLowerInvariant())
            .Where(e => e.Length >= 2 && e.Length <= 10 && e[0] == '.' && e.All(c => char.IsLetterOrDigit(c) || c == '.'))
            .Distinct()
            .Take(100)
            .ToList();

        var result = new Dictionary<string, string>();
        var missing = new List<string>();
        foreach (var ext in list)
        {
            if (IconCache.TryGetValue(ext, out var url)) result[ext] = url;
            else missing.Add(ext);
        }
        if (missing.Count == 0) return Task.FromResult(result);

        var fetched = ExtractLocalIcons(missing);
        foreach (var kv in fetched)
        {
            IconCache[kv.Key] = kv.Value;
            result[kv.Key] = kv.Value;
        }
        return Task.FromResult(result);
    }

    /// <summary>本机图标提取：SHGetFileInfo 按扩展名取 32x32 大图标转 PNG data URL（Shell 图标 API 需 STA 线程）</summary>
    private static Dictionary<string, string> ExtractLocalIcons(List<string> exts)
    {
        var fetched = new Dictionary<string, string>();
        var thread = new Thread(() =>
        {
            foreach (var ext in exts)
            {
                var shfi = new SHFILEINFO();
                var ret = SHGetFileInfo("file" + ext, FileAttributeNormal, ref shfi, (uint)Marshal.SizeOf<SHFILEINFO>(),
                    ShgfiIcon | ShgfiLargeIcon | ShgfiUseFileAttributes);
                if (ret == IntPtr.Zero || shfi.hIcon == IntPtr.Zero) continue;
                try
                {
                    using var icon = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(shfi.hIcon).Clone();
                    using var bmp = icon.ToBitmap();
                    using var ms = new MemoryStream();
                    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    fetched[ext] = "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
                }
                catch { }
                finally { DestroyIcon(shfi.hIcon); }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(15));
        return fetched;
    }

    #endregion

    #region 工具方法

    private static HostDiskCleanRequestDto ParseCategories(string? categories)
    {
        var dto = new HostDiskCleanRequestDto();
        foreach (var c in (categories ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (c)
            {
                case "userTemp": dto.UserTemp = true; break;
                case "winTemp": dto.WindowsTemp = true; break;
                case "prefetch": dto.Prefetch = true; break;
                case "updateCache": dto.UpdateCache = true; break;
                case "browserCache": dto.BrowserCache = true; break;
                case "thumbnailCache": dto.ThumbnailCache = true; break;
                case "logFiles": dto.LogFiles = true; break;
                case "oldDownloads": dto.OldDownloads = true; break;
                case "driveJunk": dto.DriveJunk = true; break;
                case "recycleBin": dto.RecycleBin = true; break;
            }
        }
        return dto;
    }

    private static string NormalizeDrive(string? drive)
    {
        var d = string.IsNullOrWhiteSpace(drive) ? "C" : drive.Trim().ToUpperInvariant();
        if (d.Length != 1 || d[0] < 'A' || d[0] > 'Z')
            throw new InvalidOperationException("盘符不合法，应为单个字母 A-Z");
        return d;
    }

    #endregion

    #region PowerShell 执行

    private static string PowerShellExe =>
        Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");

    /// <summary>执行 PowerShell 单行命令并采集输出。</summary>
    private static Task<(string StdOut, string StdErr, bool Ok)> RunPowerShellAsync(string command, TimeSpan timeout, CancellationToken ct, Action<string>? onStdoutLine = null)
        => RunPsProcessAsync($"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{command}\"", timeout, ct, onStdoutLine);

    /// <summary>将脚本写入临时文件后以 -File 执行，执行完毕立即删除临时文件。</summary>
    private static async Task<(string StdOut, string StdErr, bool Ok)> RunPowerShellScriptAsync(string script, TimeSpan timeout, CancellationToken ct, Action<string>? onStdoutLine = null)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"cs_hostclean_{Guid.NewGuid():N}.ps1");
        try
        {
            File.WriteAllText(scriptPath, script, new UTF8Encoding(true));
            return await RunPsProcessAsync($"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\"", timeout, ct, onStdoutLine);
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { }
        }
    }

    /// <summary>启动 PowerShell 进程并采集输出；onStdoutLine 非空时逐行实时回调</summary>
    private static async Task<(string StdOut, string StdErr, bool Ok)> RunPsProcessAsync(string arguments, TimeSpan timeout, CancellationToken ct, Action<string>? onStdoutLine = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = PowerShellExe,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();
        var sbOut = new StringBuilder();
        var outTask = Task.Run(async () =>
        {
            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync(ct);
                if (line == null) break;
                lock (sbOut) sbOut.AppendLine(line);
                try { onStdoutLine?.Invoke(line); } catch { }
            }
        }, ct);
        var errTask = process.StandardError.ReadToEndAsync(ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            KillQuietly(process);
            throw new TimeoutException($"PowerShell 探测超时（{timeout.TotalSeconds:0} 秒）");
        }

        await outTask;
        var stderr = await errTask;
        string stdout;
        lock (sbOut) stdout = sbOut.ToString();
        return (stdout, stderr, process.ExitCode == 0 && string.IsNullOrWhiteSpace(stderr));
    }

    private static void KillQuietly(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
    }

    #endregion

    #region 扫描/清理脚本工具

    private const string JunkExtArray = "@('.tmp','.temp','.log','.bak','.chk','.old')";

    /// <summary>非系统盘垃圾文件扫描脚本体</summary>
    private static string JunkScanBody(string rootExpr)
    {
        return "$exts=" + JunkExtArray + "; $root=" + rootExpr + "; if (Test-Path -LiteralPath $root) { "
            + "Get-ChildItem -LiteralPath $root -Recurse -Force -File -ErrorAction SilentlyContinue "
            + "| Where-Object { ($exts -contains $_.Extension.ToLower()) -and ($_.FullName -notlike '*\\$Recycle.Bin\\*') -and ($_.FullName -notlike '*\\System Volume Information\\*') } "
            + "| ForEach-Object { $cnt++; $sz+=$_.Length; if ($cnt -le $cap) { "
            + "Write-Output ('FILE|DRV_JUNK|'+[math]::Round($_.Length/1KB,1)+'|'+$_.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')+'|'+$_.FullName) }; "
            + "if ($cnt % 100 -eq 0) { Write-Output ('PROGRESS|'+$cnt+'|'+[math]::Round($sz/1KB,1)) } } }; ";
    }

    private static string SanitizeDrive(string drive)
        => new string(drive.Trim().TrimEnd(':').ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static string EscapePs(string s) => s.Replace("'", "''");

    private static string Truncate(string s) => s.Length <= 450 ? s : s[..450];

    private static bool IsSpecialCategory(string code) => code switch
    {
        "BROWSER_CACHE" or "THUMBNAIL_CACHE" or "LOG_FILE" or "OLD_DOWNLOAD" => true,
        _ => false,
    };

    private static IEnumerable<string> CategoryRootExprs(string code) => code switch
    {
        "USER_TEMP" => new[] { "$env:TEMP" },
        "WIN_TEMP" => new[] { "($env:WINDIR+'\\Temp')" },
        "PREFETCH" => new[] { "($env:WINDIR+'\\Prefetch')" },
        "UPDATE_CACHE" => new[] { "($env:WINDIR+'\\SoftwareDistribution\\Download')" },
        "BROWSER_CACHE" => new[]
        {
            "($env:LOCALAPPDATA+'\\Google\\Chrome\\User Data\\Default\\Cache')",
            "($env:LOCALAPPDATA+'\\Microsoft\\Edge\\User Data\\Default\\Cache')",
            "($env:LOCALAPPDATA+'\\Mozilla\\Firefox\\Profiles')",
        },
        "THUMBNAIL_CACHE" => new[] { "($env:LOCALAPPDATA+'\\Microsoft\\Windows\\Explorer')" },
        "LOG_FILE" => new[] { "$env:TEMP", "$env:ProgramData" },
        "OLD_DOWNLOAD" => new[] { "($env:USERPROFILE+'\\Downloads')" },
        _ => Array.Empty<string>(),
    };

    #endregion
}
