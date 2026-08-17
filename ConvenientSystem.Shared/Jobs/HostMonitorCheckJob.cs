using ConvenientSystem.Shared.Common.Email;
using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Entity.Email;
using ConvenientSystem.Shared.Model.Common;
using FreeSql;
using Hangfire;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;

namespace ConvenientSystem.Shared.Jobs
{
    /// <summary>
    /// 主机资源监控探测 Hangfire 定时 Job（每分钟触发一次）：
    /// - 遍历启用的监控目标，按各自探测间隔判定是否到期，到期则通过 PowerShell 采集本机指标
    /// - 指标类型：磁盘已用率 / 内存使用率 / CPU 使用率（阈值超限告警），Windows 服务运行状态
    /// - 每次探测写 HostMonitorLog（保留 30 天，每日凌晨清理），并回写目标最近状态/数值/时间
    /// - 状态变化（正常↔异常）且开启邮件告警时，给拥有 host-monitor 菜单权限的有邮箱用户发送告警/恢复邮件
    /// </summary>
    public class HostMonitorCheckJob
    {
        private readonly IFreeSql _fsql;
        private readonly IEmailService _emailService;
        private readonly ILogger<HostMonitorCheckJob> _logger;

        /// <summary>系统告警在 EmailLog 中的任务名</summary>
        private const string TaskName = "主机监控告警";

        /// <summary>探测结果：正常</summary>
        public const byte StatusOk = 1;
        /// <summary>探测结果：异常</summary>
        public const byte StatusFail = 2;

        /// <summary>无 CPU 历史快照时首次采样后的等待秒数（用该区间计算使用率）</summary>
        private const int CpuFirstSampleDelayMs = 2000;

        public HostMonitorCheckJob(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql fsql,
            IEmailService emailService,
            ILogger<HostMonitorCheckJob> logger)
        {
            _fsql = fsql;
            _emailService = emailService;
            _logger = logger;
        }

        /// <summary>每分钟巡检：探测所有到期的启用目标；每天凌晨 3 点档清理 30 天前的探测日志</summary>
        [AutomaticRetry(Attempts = 0)]
        public async Task CheckDueAsync(CancellationToken ct = default)
        {
            var now = DateTime.Now;

            // 每天凌晨 3:00 档清理过期日志（保留 30 天）
            if (now.Hour == 3 && now.Minute == 0)
            {
                var removed = _fsql.Delete<HostMonitorLogEntity>()
                    .Where(l => l.CheckAt < now.AddDays(-30))
                    .ExecuteAffrows();
                if (removed > 0)
                    _logger.LogInformation("主机监控：已清理 30 天前的探测日志 {Count} 条", removed);
            }

            var targets = _fsql.Select<HostMonitorTargetEntity>()
                .Where(t => t.Enabled)
                .ToList();
            // 按各自探测间隔判定到期（未探测过的立即探测）
            var due = targets
                .Where(t => t.LastCheckAt == null || t.LastCheckAt <= now.AddMinutes(-Math.Max(1, t.IntervalMinutes)))
                .ToList();
            if (due.Count == 0) return;

            foreach (var target in due)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await CheckTargetAsync(target, notify: true, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "主机监控：{Name} 探测过程发生未预期异常", target.Name);
                }
            }
        }

        /// <summary>
        /// 对单个目标执行一次探测：写探测日志、回写最近状态；状态变化时可选邮件告警。
        /// 供定时巡检与页面"立即检测"共用。
        /// </summary>
        public async Task<HostMonitorLogEntity> CheckTargetAsync(HostMonitorTargetEntity target, bool notify, CancellationToken ct = default)
        {
            var prevStatus = target.LastStatus;
            decimal? value = null;
            string? error = null;

            try
            {
                (value, error) = await ProbeAsync(target, ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                error = $"探测超时（{target.TimeoutSeconds} 秒）";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                error = Truncate(ex.InnerException?.Message ?? ex.Message);
            }

            var newStatus = error == null ? StatusOk : StatusFail;
            var now = DateTime.Now;

            var log = new HostMonitorLogEntity
            {
                TargetId = target.Id,
                Status = newStatus,
                Value = value,
                ErrorMsg = error,
                MetricsJson = target.MetricType == HostMonitorMetrics.Host ? target.MetricsJson : null,
                CheckAt = now
            };
            log.Id = _fsql.Insert(log).ExecuteIdentity();

            // 回写目标最近状态（SnapshotJson 供 CPU 增量计算使用，MetricsJson 供整机概览快照使用）
            _fsql.Update<HostMonitorTargetEntity>()
                .Set(t => t.LastStatus, newStatus)
                .Set(t => t.LastValue, value)
                .Set(t => t.LastErrorMsg, error)
                .Set(t => t.LastCheckAt, now)
                .Set(t => t.SnapshotJson, target.SnapshotJson)
                .Set(t => t.MetricsJson, target.MetricsJson)
                .Where(t => t.Id == target.Id)
                .ExecuteAffrows();
            target.LastStatus = newStatus;
            target.LastValue = value;
            target.LastErrorMsg = error;
            target.LastCheckAt = now;

            if (error != null)
                _logger.LogWarning("主机监控：{Name} 探测异常：{Error}", target.Name, error);

            // 状态变化（首次探测不告警）且开启邮件通知时发送告警/恢复邮件
            if (notify && target.NotifyEmail && prevStatus.HasValue && prevStatus.Value != newStatus)
            {
                try
                {
                    await NotifyStatusChangeAsync(target, newStatus, value, error, now, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "主机监控：{Name} 状态告警邮件发送失败", target.Name);
                }
            }

            return log;
        }

        /// <summary>按指标类型分发探测，返回（探测值，异常原因）</summary>
        private async Task<(decimal? Value, string? Error)> ProbeAsync(HostMonitorTargetEntity target, CancellationToken ct)
        {
            var timeout = TimeSpan.FromSeconds(Math.Max(5, target.TimeoutSeconds));
            return target.MetricType switch
            {
                HostMonitorMetrics.Disk => await ProbeDiskAsync(target, timeout, ct),
                HostMonitorMetrics.Memory => await ProbeMemoryAsync(target.ThresholdPercent, timeout, ct),
                HostMonitorMetrics.Cpu => await ProbeCpuAsync(target, timeout, ct),
                HostMonitorMetrics.Service => await ProbeServiceAsync(target, timeout, ct),
                HostMonitorMetrics.Host => await ProbeHostAsync(target, timeout, ct),
                _ => (null, $"未知指标类型：{target.MetricType}"),
            };
        }

        /// <summary>磁盘探测：输出每个固定磁盘的已用率，任一超阈值即异常</summary>
        private async Task<(decimal?, string?)> ProbeDiskAsync(HostMonitorTargetEntity target, TimeSpan timeout, CancellationToken ct)
        {
            var cmd = "Get-CimInstance Win32_LogicalDisk | Where-Object { $_.DriveType -eq 3 }";
            if (!string.IsNullOrWhiteSpace(target.DriveLetter))
            {
                var drive = $"{SanitizeDrive(target.DriveLetter!)}:";
                cmd += $" | Where-Object {{ $_.DeviceID -eq '{drive}' }}";
            }
            cmd += " | ForEach-Object { if ($_.Size -gt 0) { $u=[math]::Round(($_.Size-$_.FreeSpace)*100/$_.Size,1); Write-Output ('{0}|{1}' -f $u,$_.DeviceID) } }";

            var (stdout, stderr, ok) = await RunPowerShellAsync(cmd, timeout, ct);
            if (!ok) return (null, $"PowerShell 执行失败：{Truncate(stderr)}");

            // 逐行解析 "已用率|盘符"
            var details = new List<string>();
            decimal maxUsed = 0;
            foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = line.Split('|');
                if (parts.Length != 2 || !decimal.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var used))
                    continue;
                maxUsed = Math.Max(maxUsed, used);
                details.Add($"{parts[1]} 盘已用 {used}%");
            }
            if (details.Count == 0)
                return (null, string.IsNullOrWhiteSpace(target.DriveLetter) ? "未找到任何固定磁盘" : $"盘符 {target.DriveLetter} 不存在或不是固定磁盘");

            var value = Math.Round(maxUsed, 1);
            if (target.ThresholdPercent.HasValue && value > target.ThresholdPercent.Value)
                return (value, $"{string.Join("；", details)}，最高已用率 {value}% 超过阈值 {target.ThresholdPercent}%");
            return (value, null);
        }

        /// <summary>内存探测：输出系统内存使用率</summary>
        private async Task<(decimal?, string?)> ProbeMemoryAsync(decimal? threshold, TimeSpan timeout, CancellationToken ct)
        {
            const string cmd = "$os = Get-CimInstance Win32_OperatingSystem; "
                + "$u=[math]::Round(($os.TotalVisibleMemorySize-$os.FreePhysicalMemory)*100/$os.TotalVisibleMemorySize,1); Write-Output $u";

            var (stdout, stderr, ok) = await RunPowerShellAsync(cmd, timeout, ct);
            if (!ok || !decimal.TryParse(stdout.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
                return (null, $"内存探测失败：{Truncate(stderr)}");

            value = Math.Round(value, 1);
            if (threshold.HasValue && value > threshold.Value)
                return (value, $"内存使用率 {value}% 超过阈值 {threshold}%");
            return (value, null);
        }

        /// <summary>
        /// CPU 探测：基于 Processor Time 计数器两次采样的增量计算使用率。
        /// 上次快照存于目标 SnapshotJson（时间戳Ticks|RawCount）；无快照时先采样并等待 2 秒再采样。
        /// </summary>
        private async Task<(decimal?, string?)> ProbeCpuAsync(HostMonitorTargetEntity target, TimeSpan timeout, CancellationToken ct)
        {
            const string cmd = "$s=(Get-Counter '\\Processor Information(_Total)\\Processor Time').CounterSamples[0]; "
                + "Write-Output ('{0}|{1}' -f $s.Timestamp.Ticks,$s.RawCount)";

            (long Ticks, long Raw)? prev = ParseCpuSnapshot(target.SnapshotJson);
            if (prev == null)
            {
                // 首次探测无历史快照：采样一次作为基准，等待 2 秒后再采样计算
                var first = await SampleCpuAsync(cmd, timeout, ct);
                if (first == null) return (null, "CPU 计数器采样失败");
                await Task.Delay(CpuFirstSampleDelayMs, ct);
                prev = first;
            }

            var current = await SampleCpuAsync(cmd, timeout, ct);
            if (current == null) return (null, "CPU 计数器采样失败");
            target.SnapshotJson = $"{current.Value.Ticks}|{current.Value.Raw}";

            var deltaTicks = current.Value.Ticks - prev.Value.Ticks;
            if (deltaTicks <= 0) return (null, null); // 采样间隔无效，本次跳过判定

            // Processor Time 为反向计数器：Raw 累计空闲时间，使用率 = 1 - 空闲增量/时间增量
            var deltaRaw = current.Value.Raw - prev.Value.Raw;
            var cpu = Math.Clamp(100m * (1m - (decimal)deltaRaw / deltaTicks), 0m, 100m);
            cpu = Math.Round(cpu, 1);

            if (target.ThresholdPercent.HasValue && cpu > target.ThresholdPercent.Value)
                return (cpu, $"CPU 使用率 {cpu}% 超过阈值 {target.ThresholdPercent}%");
            return (cpu, null);
        }

        private async Task<(long Ticks, long Raw)?> SampleCpuAsync(string cmd, TimeSpan timeout, CancellationToken ct)
        {
            var (stdout, _, ok) = await RunPowerShellAsync(cmd, timeout, ct);
            if (!ok) return null;
            var parts = stdout.Trim().Split('|');
            if (parts.Length != 2
                || !long.TryParse(parts[0], out var ticks)
                || !long.TryParse(parts[1], out var raw))
                return null;
            return (ticks, raw);
        }

        /// <summary>
        /// 整机概览探测：一次采集 CPU/内存/磁盘/开机时长（本机或远程 IP，远程走 WinRM + 凭据）。
        /// 探测值 = 各项使用率最大值；超阈值时异常原因列出所有超限项。快照序列化存入 target.MetricsJson。
        /// </summary>
        private async Task<(decimal?, string?)> ProbeHostAsync(HostMonitorTargetEntity target, TimeSpan timeout, CancellationToken ct)
        {
            var isRemote = !IsLocalAddress(target.HostAddress);
            var cred = string.Empty;   // 凭据构造语句（仅远程）
            if (isRemote)
            {
                if (string.IsNullOrWhiteSpace(target.AuthAccount) || string.IsNullOrWhiteSpace(target.AuthPassword))
                    return (null, "远程目标必须配置采集账号与密码");
                cred = $"$pw=ConvertTo-SecureString '{EscapePs(target.AuthPassword!)}' -AsPlainText -Force; "
                    + $"$cred=New-Object System.Management.Automation.PSCredential('{EscapePs(target.AuthAccount!)}',$pw); ";
            }

            // 输出格式：
            // OS|系统名|内存使用率|CPU使用率|开机小时数|内存总量GB|已用内存GB|CPU核数|进程数
            // RATE|网络接收KB/s|网络发送KB/s|磁盘读MB/s|磁盘写MB/s
            // DISK|盘符|已用率|总容量GB|剩余GB（每磁盘一行）
            // 注意：Get-CimInstance 无 -Credential 参数，远程采集与扫描/清理一致用 Invoke-Command 包装，凭据在远程会话内生效
            var body = $"$os=Get-CimInstance Win32_OperatingSystem; "
                + $"$proc=Get-CimInstance Win32_Processor; "
                + "$cpu=[math]::Round(($proc | Measure-Object -Property LoadPercentage -Average).Average,1); "
                + "$cores=($proc | Measure-Object -Property NumberOfLogicalProcessors -Sum).Sum; "
                + "$mem=[math]::Round(($os.TotalVisibleMemorySize-$os.FreePhysicalMemory)*100/$os.TotalVisibleMemorySize,1); "
                + "$usedGb=[math]::Round(($os.TotalVisibleMemorySize-$os.FreePhysicalMemory)/1MB,1); "
                + "$gb=[math]::Round($os.TotalVisibleMemorySize/1MB,1); "
                + $"$sys=Get-CimInstance Win32_PerfFormattedData_PerfOS_System; "
                // 开机时长用 PerfOS_System.SystemUpTime（自开机秒数）：CIM 的 LastBootUpTime 在 WinRM 远程会话内
                // 时区偏移转换与本机不一致（差恰为时区小时数），导致本机直采与 IP 远程采集的开机时长两边不符
                + "$up=[math]::Round($sys.SystemUpTime/3600,1); "
                // 进程数直接统计 Win32_Process 实例数，避免性能计数器采样差异导致本机与远程两边不一致
                + "$procCount=(Get-CimInstance Win32_Process).Count; "
                + $"$net=Get-CimInstance Win32_PerfFormattedData_Tcpip_NetworkInterface | Where-Object {{ $_.Name -notlike '*isatap*' -and $_.Name -notlike '*Pseudo*' }}; "
                + "$inB=($net | Measure-Object -Property BytesReceivedPersec -Sum).Sum; "
                + "$outB=($net | Measure-Object -Property BytesSentPersec -Sum).Sum; "
                + $"$io=Get-CimInstance Win32_PerfFormattedData_PerfDisk_PhysicalDisk | Where-Object {{ $_.Name -eq '_Total' }}; "
                + "Write-Output ('OS|'+$os.Caption+'|'+$mem+'|'+$cpu+'|'+$up+'|'+$gb+'|'+$usedGb+'|'+$cores+'|'+$procCount); "
                + "Write-Output ('RATE|'+[math]::Round($inB/1KB,1)+'|'+[math]::Round($outB/1KB,1)"
                + "+'|'+[math]::Round($io.DiskReadBytesPersec/1MB,2)+'|'+[math]::Round($io.DiskWriteBytesPersec/1MB,2)); "
                + $"Get-CimInstance Win32_LogicalDisk | Where-Object {{ $_.DriveType -eq 3 }} | ForEach-Object {{ "
                + "if ($_.Size -gt 0) { Write-Output ('DISK|'+$_.DeviceID+'|'+[math]::Round(($_.Size-$_.FreeSpace)*100/$_.Size,1)"
                + "+'|'+[math]::Round($_.Size/1GB,1)+'|'+[math]::Round($_.FreeSpace/1GB,1)) } }";

            var cmd = "[Console]::OutputEncoding=[Text.Encoding]::UTF8; " + cred
                + (isRemote
                    ? "$probeScript={" + body + "}; Invoke-Command -ComputerName '" + SanitizeHost(target.HostAddress!)
                        + "' -Credential $cred -ScriptBlock $probeScript"
                    : body);

            var (stdout, stderr, ok) = await RunPowerShellAsync(cmd, timeout, ct);
            if (!ok) return (null, $"整机概览采集失败：{Truncate(stderr)}");

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
                return (null, isRemote ? "无法连接目标电脑（请确认目标已开启 WinRM 且账号密码正确）" : "未采集到任何指标");

            target.MetricsJson = JsonConvert.SerializeObject(snapshot);

            // 探测值取各项使用率最大值，超阈值时列出所有超限项
            var over = new List<string>();
            var value = 0m;
            if (snapshot.CpuPercent.HasValue)
            {
                value = Math.Max(value, snapshot.CpuPercent.Value);
                if (target.ThresholdPercent.HasValue && snapshot.CpuPercent.Value > target.ThresholdPercent.Value)
                    over.Add($"CPU {snapshot.CpuPercent.Value}%");
            }
            if (snapshot.MemoryPercent.HasValue)
            {
                value = Math.Max(value, snapshot.MemoryPercent.Value);
                if (target.ThresholdPercent.HasValue && snapshot.MemoryPercent.Value > target.ThresholdPercent.Value)
                    over.Add($"内存 {snapshot.MemoryPercent.Value}%");
            }
            foreach (var d in snapshot.Disks)
            {
                value = Math.Max(value, d.UsedPercent);
                if (target.ThresholdPercent.HasValue && d.UsedPercent > target.ThresholdPercent.Value)
                    over.Add($"{d.Drive} 盘 {d.UsedPercent}%");
            }
            value = Math.Round(value, 1);

            if (over.Count > 0)
                return (value, $"{string.Join("、", over)} 超过阈值 {target.ThresholdPercent}%");
            return (value, null);
        }

        private static (long Ticks, long Raw)? ParseCpuSnapshot(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            var parts = json.Split('|');
            if (parts.Length != 2 || !long.TryParse(parts[0], out var ticks) || !long.TryParse(parts[1], out var raw))
                return null;
            return (ticks, raw);
        }

        /// <summary>服务探测：全部目标服务处于运行中才算正常，Value=运行中服务数</summary>
        private async Task<(decimal?, string?)> ProbeServiceAsync(HostMonitorTargetEntity target, TimeSpan timeout, CancellationToken ct)
        {
            var names = ParseServiceNames(target.ServiceNames);
            if (names.Count == 0) return (null, "未配置服务名列表");

            var arr = string.Join(",", names.Select(n => $"'{n}'"));
            var cmd = "[Console]::OutputEncoding=[Text.Encoding]::UTF8; "
                + $"$names=@({arr}); $run=@(); $bad=@(); foreach($n in $names){{ "
                + "$s=Get-Service -Name $n -ErrorAction SilentlyContinue; "
                + "if($s -and $s.Status -eq 'Running'){{ $run+=$n }} else {{ $bad+=$n }} }}; "
                + "Write-Output (($run -join ',')+'|'+($bad -join ','))";

            var (stdout, stderr, ok) = await RunPowerShellAsync(cmd, timeout, ct);
            if (!ok) return (null, $"PowerShell 执行失败：{Truncate(stderr)}");

            var parts = stdout.Trim().Split('|');
            var running = (parts.Length > 0 ? parts[0] : "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            var bad = (parts.Length > 1 ? parts[1] : "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

            if (bad.Count > 0)
                return (running.Count, $"以下服务未运行或不存在：{string.Join("、", bad)}");
            return (running.Count, null);
        }

        /// <summary>磁盘垃圾扩展名（非系统盘扫描/清理共用）</summary>
        private const string JunkExtArray = "@('.tmp','.temp','.log','.bak','.chk','.old')";

        /// <summary>
        /// 非系统盘垃圾文件扫描脚本体：递归扫描 rootExpr 目录下常见垃圾扩展名文件（排除
        /// 回收站与系统卷信息目录），输出格式与系统类别扫描一致（FILE|DRV_JUNK|…，每 100 条 PROGRESS）。
        /// </summary>
        private static string JunkScanBody(string rootExpr)
        {
            return "$exts=" + JunkExtArray + "; $root=" + rootExpr + "; if (Test-Path -LiteralPath $root) { "
                + "Get-ChildItem -LiteralPath $root -Recurse -Force -File -ErrorAction SilentlyContinue "
                + "| Where-Object { ($exts -contains $_.Extension.ToLower()) -and ($_.FullName -notlike '*\\$Recycle.Bin\\*') -and ($_.FullName -notlike '*\\System Volume Information\\*') } "
                + "| ForEach-Object { $cnt++; $sz+=$_.Length; if ($cnt -le $cap) { "
                + "Write-Output ('FILE|DRV_JUNK|'+[math]::Round($_.Length/1KB,1)+'|'+$_.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')+'|'+$_.FullName) }; "
                + "if ($cnt % 100 -eq 0) { Write-Output ('PROGRESS|'+$cnt+'|'+[math]::Round($sz/1KB,1)) } } }; ";
        }

        /// <summary>
        /// 扫描磁盘可清理候选文件（仅读取不删除）：按勾选分类列出全部文件（不按修改时间过滤）
        /// （名称/路径/大小/最后修改时间，最多 3000 条），回收站单独统计项目数与占用空间。
        /// onProgress：扫描中逐百条实时上报（已扫描文件数, 已发现大小KB），供前端展示扫描进度。
        /// </summary>
        public async Task<HostDiskScanDto> ScanDiskAsync(HostMonitorTargetEntity target, HostDiskCleanRequestDto req, Action<int, decimal>? onProgress = null)
        {
            var isRemote = !IsLocalAddress(target.HostAddress);
            var cred = BuildCredential(target, isRemote);
            // 盘符校验：单个字母 A-Z，防止拼接非法内容进脚本
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
            // 新增清理类别
            if (req.BrowserCache) cats.Add(("BROWSER_CACHE", "($env:LOCALAPPDATA+'\\Google\\Chrome\\User Data\\Default\\Cache')"));
            if (req.ThumbnailCache) cats.Add(("THUMBNAIL_CACHE", "($env:LOCALAPPDATA+'\\Microsoft\\Windows\\Explorer')"));
            if (req.LogFiles) cats.Add(("LOG_FILE", "$env:TEMP"));  // 日志文件单独处理，下面特殊逻辑
            if (req.OldDownloads) cats.Add(("OLD_DOWNLOAD", "($env:USERPROFILE+'\\Downloads')"));

            // 输出格式：FILE|分类|大小KB|最后修改时间|完整路径（最多 3000 条）、PROGRESS|已扫描|已发现大小KB、STRUNC（截断）、RECYCLE|项目数|占用KB
            // 不做修改时间过滤，列出勾选分类目录下的全部文件（旧下载除外，按天数过滤）；
            // 系统缓存类目录只存在系统盘：选非系统盘时改扫该盘垃圾文件（DRV_JUNK）
            var body = "$cnt=0; $cap=3000; $sz=0; $drv='" + drv + "'; $sysl=$env:SystemDrive.Substring(0,1); if ($drv -eq $sysl) { ";
            
            // 常规目录扫描（临时目录、Prefetch、UpdateCache）
            foreach (var (code, dir) in cats.Where(c => !IsSpecialCategory(c.Code)))
            {
                body += "$d=" + dir + "; if (Test-Path $d) { Get-ChildItem $d -Recurse -Force -ErrorAction SilentlyContinue "
                    + "| Where-Object { -not $_.PSIsContainer } "
                    + "| ForEach-Object { $cnt++; $sz+=$_.Length; if ($cnt -le $cap) { "
                    + "Write-Output ('FILE|" + code + "|'+[math]::Round($_.Length/1KB,1)+'|'+$_.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')+'|'+$_.FullName) }; "
                    + "if ($cnt % 100 -eq 0) { Write-Output ('PROGRESS|'+$cnt+'|'+[math]::Round($sz/1KB,1)) } } }; ";
            }
            
            // 浏览器缓存：扫描多个浏览器的缓存目录
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
            
            // 缩略图缓存：只扫描 thumbcache_*.db 文件
            if (req.ThumbnailCache)
            {
                body += "$td=$env:LOCALAPPDATA+'\\Microsoft\\Windows\\Explorer'; if (Test-Path $td) { Get-ChildItem $td -Force -Filter 'thumbcache_*.db' -ErrorAction SilentlyContinue "
                    + "| ForEach-Object { $cnt++; $sz+=$_.Length; if ($cnt -le $cap) { "
                    + "Write-Output ('FILE|THUMBNAIL_CACHE|'+[math]::Round($_.Length/1KB,1)+'|'+$_.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')+'|'+$_.FullName) }; "
                    + "if ($cnt % 100 -eq 0) { Write-Output ('PROGRESS|'+$cnt+'|'+[math]::Round($sz/1KB,1)) } } }; ";
            }
            
            // 日志文件：扫描 TEMP 和 ProgramData 下的 *.log 文件
            if (req.LogFiles)
            {
                body += "$logPaths=@($env:TEMP, ($env:ProgramData)); foreach ($lp in $logPaths) { if (Test-Path $lp) { Get-ChildItem $lp -Recurse -Force -Filter '*.log' -ErrorAction SilentlyContinue "
                    + "| Where-Object { -not $_.PSIsContainer } "
                    + "| ForEach-Object { $cnt++; $sz+=$_.Length; if ($cnt -le $cap) { "
                    + "Write-Output ('FILE|LOG_FILE|'+[math]::Round($_.Length/1KB,1)+'|'+$_.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')+'|'+$_.FullName) }; "
                    + "if ($cnt % 100 -eq 0) { Write-Output ('PROGRESS|'+$cnt+'|'+[math]::Round($sz/1KB,1)) } } } }; ";
            }
            
            // 旧下载文件：按最后访问时间过滤（超过指定天数）
            if (req.OldDownloads)
            {
                var days = req.OldDownloadsDays > 0 ? req.OldDownloadsDays : 30;
                body += "$od=$env:USERPROFILE+'\\Downloads'; $cut=(Get-Date).AddDays(-" + days + "); if (Test-Path $od) { Get-ChildItem $od -Recurse -Force -ErrorAction SilentlyContinue "
                    + "| Where-Object { -not $_.PSIsContainer -and $_.LastAccessTime -lt $cut } "
                    + "| ForEach-Object { $cnt++; $sz+=$_.Length; if ($cnt -le $cap) { "
                    + "Write-Output ('FILE|OLD_DOWNLOAD|'+[math]::Round($_.Length/1KB,1)+'|'+$_.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')+'|'+$_.FullName) }; "
                    + "if ($cnt % 100 -eq 0) { Write-Output ('PROGRESS|'+$cnt+'|'+[math]::Round($sz/1KB,1)) } } }; ";
            }
            // 非系统盘：系统缓存类别不适用，改扫该盘常见垃圾扩展名文件（排除回收站与系统卷信息目录）；
            // 此处的 " } " 负责关闭开头的 if ($drv -eq $sysl) 块，各分类片段自身均已闭合，不能再多带 "}"
            body += hasAnyFileCat
                ? " } else { " + JunkScanBody("$drv+':\\'") + " }; "
                : " }; ";
            // 回收站不作为勾选选项：扫描默认实时枚举全部盘（每个盘符都有独立的 $Recycle.Bin 存储），逐条输出 RB|大小KB|删除时间|名称|原目录|物理路径，
            // 另输出 RECYCLE|项目数|占用KB 汇总。
            // 首选磁盘直读：逐个解析各盘 $Recycle.Bin\<SID>\$I* 元数据（UTF-16LE：原大小+删除时间+原始全路径），
            // Shell Namespace(0xA) 枚举存在漏项（实测磁盘 9 项只返回 5 项），仅作为兜底补充
            {
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
                // Shell 兜底：仅补充磁盘未枚举到的条目（如非常规存储），按物理路径去重
                body += "try { $sh=New-Object -ComObject Shell.Application; $rb=$sh.Namespace(0xA); "
                    + "if ($rb -ne $null) { $rb.Items() | ForEach-Object { try { if ($seen.Contains($_.Path.ToLower())) { return } } catch { }; "
                    + "try { $null=$seen.Add($_.Path.ToLower()) } catch { }; $rn++; try { $rs+=$_.Size } catch { }; "
                    + "if ($rn -le $cap) { $dd=''; try { $dd=$_.ExtendedProperty('System.Recycle.DateDeleted').ToString('yyyy-MM-dd HH:mm:ss') } catch { }; "
                    + "$df=''; try { $df=$_.ExtendedProperty('System.Recycle.DeletedFrom') } catch { }; "
                    + "Write-Output ('RB|'+[math]::Round($_.Size/1KB,1)+'|'+$dd+'|'+$_.Name+'|'+$df+'|'+$_.Path) } } } } catch { }; "
                    + "Write-Output ('RECYCLE|'+$rn+'|'+[math]::Round($rs/1KB,1))";
            }
            body += "if ($cnt -gt $cap) { Write-Output 'STRUNC' }";

            var cmd = "[Console]::OutputEncoding=[Text.Encoding]::UTF8; " + cred
                + (isRemote
                    ? "$scanScript={" + body + "}; Invoke-Command -ComputerName '" + SanitizeHost(target.HostAddress!)
                        + "' -Credential $cred -ScriptBlock $scanScript"
                    : body);

            var (stdout, stderr, ok) = await RunPowerShellAsync(cmd, TimeSpan.FromMinutes(10), CancellationToken.None,
                onProgress == null ? null : line =>
                {
                    // PROGRESS|已扫描数|已发现大小KB：脚本内每 100 个文件上报一次，实时回调给调用方
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
                    // RB|大小KB|删除时间|名称|原目录|物理路径（名称/路径中可能含 |，限制分段数）
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
                            // Path 保留物理路径（清理时按它删除 $R/$I），OriginalPath 展示原位置
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

        /// <summary>
        /// 清理磁盘临时文件（本机或远程 IP，远程走 WinRM + 凭据）：
        /// 指定 Paths 时仅删除选定的文件（需位于勾选分类目录下），否则删除勾选项下全部文件；
        /// 回收站按 RecyclePaths 勾选条目逐条删除（不整体清空）；系统缓存类目录只存在系统盘，
        /// 选非系统盘时仅清理该盘垃圾扩展名文件（与扫描 DRV_JUNK 范围一致），盘符同时决定可用空间统计；
        /// 返回释放空间与已删除文件清单（最多 2000 条）。
        /// onProgress：清理中逐 20 条实时上报（已删除数, 已释放MB），供前端展示清理进度。
        /// </summary>
        public async Task<HostDiskCleanDto> CleanDiskAsync(HostMonitorTargetEntity target, HostDiskCleanRequestDto req, Action<int, decimal>? onProgress = null)
        {
            var hasPaths = req.Paths is { Count: > 0 };
            var isRemote = !IsLocalAddress(target.HostAddress);
            var cred = BuildCredential(target, isRemote);
            // 盘符校验：单个字母 A-Z，防止拼接非法内容进脚本
            var drv = (req.Drive ?? "C").Trim().ToUpperInvariant();
            if (drv.Length != 1 || drv[0] < 'A' || drv[0] > 'Z')
                throw new InvalidOperationException("盘符不合法，应为单个字母 A-Z");

            // 按勾选项拼接允许目录根（选择性删除时用于校验路径归属，防越界删除）
            var roots = new List<string>();
            if (req.UserTemp) roots.Add("$env:TEMP");
            if (req.WindowsTemp) roots.Add("($env:WINDIR+'\\Temp')");
            if (req.Prefetch) roots.Add("($env:WINDIR+'\\Prefetch')");
            if (req.UpdateCache) roots.Add("($env:WINDIR+'\\SoftwareDistribution\\Download')");
            // 新增清理类别的目录根
            if (req.BrowserCache)
            {
                roots.Add("($env:LOCALAPPDATA+'\\Google\\Chrome\\User Data\\Default\\Cache')");
                roots.Add("($env:LOCALAPPDATA+'\\Microsoft\\Edge\\User Data\\Default\\Cache')");
                roots.Add("($env:LOCALAPPDATA+'\\Mozilla\\Firefox\\Profiles')");
            }
            if (req.ThumbnailCache) roots.Add("($env:LOCALAPPDATA+'\\Microsoft\\Windows\\Explorer')");
            if (req.LogFiles) { roots.Add("$env:TEMP"); roots.Add("$env:ProgramData"); }
            if (req.OldDownloads) roots.Add("($env:USERPROFILE+'\\Downloads')");

            // 选择性清理时另按勾选文件自身扫描分类推导目录根（文件能出现在扫描结果即已通过分类校验，
            // 不再依赖分类布尔标志重传，避免漏传标志导致合法文件被误判越界跳过）
            if (hasPaths && req.PathCategories is { Count: > 0 })
            {
                foreach (var cat in req.PathCategories.Values.Distinct(StringComparer.OrdinalIgnoreCase))
                    foreach (var r in CategoryRootExprs(cat))
                        if (!roots.Contains(r)) roots.Add(r);
            }

            // 清理脚本体（本机/远程同用）：记录清理前所选盘可用空间 → 删除勾选文件并记录路径
            // → 按勾选删除回收站条目（先枚举匹配） → 输出文件清单与结果
            // 输出格式：F|文件路径（每行一条，最多 2000 条）、FTRUNC（存在截断）、CLEAN|释放MB|剩余GB|删除数、
            // CPROG|已删除数|已释放MB（每 20 条上报一次，供前端展示清理进度）、
            // ITEM|1/0|原因|路径（每个勾选项的逐项结果，原因含 | 时已替换为 /，供前端展示成功/失败原因）；
            // 系统缓存类目录只存在系统盘：选非系统盘时仅删除该盘垃圾扩展名文件（与扫描 DRV_JUNK 范围一致）
            var body = "$before=(Get-CimInstance Win32_LogicalDisk | Where-Object { $_.DeviceID -eq '" + drv + ":' }).FreeSpace; "
                + "$n=0; $fd=0; $files=New-Object System.Collections.Generic.List[string]; $cap=2000; "
                + "$drv='" + drv + "'; $sysl=$env:SystemDrive.Substring(0,1); "
                // 提权状态：用于区分“未以管理员运行导致的拒绝访问”与“已提权但文件被占用/系统保护”
                + "$elev=$false; try { $elev=([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent())"
                + ".IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) } catch { }; "
                // 删除失败原因诊断：“拒绝访问”既可能是文件被独占打开也可能是权限不足，
                // 独占试开一次即可区分，输出用户可执行的处置建议而非原始系统消息
                + "function Get-DelFail { param([string]$fp,[string]$msg) "
                + "$m=([string]$msg) -replace '\\|','/'; "
                + "if ($m -notmatch '拒绝|denied|Denied|UnauthorizedAccess') { if ($m) { return $m } else { return '文件可能被占用' } }; "
                + "$lk=$false; try { $st=[IO.File]::Open($fp,'Open','ReadWrite','None'); $st.Close() } catch { $lk=$true }; "
                + "if ($lk) { return '文件正被其他程序占用，关闭占用它的程序后重试' }; "
                + "if (-not $elev) { return '权限不足，请以管理员身份运行本程序后重试' }; "
                + "return '系统保护或被系统进程占用，无法删除' }; ";
            if (hasPaths)
            {
                // 选择性清理：仅删除选定路径，且必须位于勾选分类目录下（防越界删除）；不做修改时间过滤；
                // 每个勾选项输出 ITEM 行（不存在/是目录/不在分类目录/删除失败/已删除），便于前端展示逐项原因
                var sysSel = "$roots=@(" + string.Join(",", roots) + "); "
                    // 目录根归一化：环境变量可能是 8.3 短路径（如 %TEMP% 常为 C:\Users\ADMINI~1\...），
                    // 而扫描列出的文件路径是长路径，直接前缀比对会全部失配导致合法文件被误判越界；
                    // 故每个根同时保留原始形式与 Get-Item 解析出的长路径形式，比对时任一命中即通过
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
                    // 用 Get-Item 解析出的 FullName 参与比对，与归一化后的目录根口径一致（同为长路径）
                    + "$cp=$it.FullName; if (-not $cp) { $cp=$p }; "
                    + "$ok=$false; foreach ($r in $rn) { if ($cp.StartsWith($r+'\\',[System.StringComparison]::OrdinalIgnoreCase)) { $ok=$true; break } }; "
                    + "if (-not $ok) { Write-Output ('ITEM|0|不在勾选分类目录下，安全跳过|'+$p); continue }; "
                    // 先清掉只读/隐藏/系统属性再删（临时文件常带隐藏位）；Remove-Item 失败时再试一次 .NET 删除，
                    // 两者均失败才报错，且把异常消息交由 Get-DelFail 翻译为可执行的原因
                    + "try { $fsz=$it.Length; if (([int]$it.Attributes -band 7) -ne 0) { try { $it.Attributes='Normal' } catch { } }; "
                    + "Remove-Item -LiteralPath $p -Force -ErrorAction Stop; $n++; $fd+=$fsz; "
                    + "if ($n % 20 -eq 0) { Write-Output ('CPROG|'+$n+'|'+[math]::Round($fd/1MB,1)) }; "
                    + "if ($files.Count -lt $cap) { $files.Add($p) }; Write-Output ('ITEM|1|已删除|'+$p) } "
                    + "catch { $em=$_.Exception.Message; "
                    + "try { [IO.File]::Delete($cp); $n++; $fd+=$fsz; "
                    + "if ($n % 20 -eq 0) { Write-Output ('CPROG|'+$n+'|'+[math]::Round($fd/1MB,1)) }; "
                    + "if ($files.Count -lt $cap) { $files.Add($p) }; Write-Output ('ITEM|1|已删除|'+$p) } "
                    + "catch { Write-Output ('ITEM|0|删除失败：'+(Get-DelFail $cp $em)+'|'+$p) } } }";
                // 非系统盘选择性清理：路径须位于该盘且为垃圾扩展名（双重校验防越界删除）；同样逐项输出 ITEM 行
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
                // 非系统盘整类清理：仅删垃圾扩展名文件（绝不能无差别清空数据盘）
                var junkAll = "$exts=" + JunkExtArray + "; $root=$drv+':\\'; if (Test-Path -LiteralPath $root) { "
                    + "Get-ChildItem -LiteralPath $root -Recurse -Force -File -ErrorAction SilentlyContinue "
                    + "| Where-Object { ($exts -contains $_.Extension.ToLower()) -and ($_.FullName -notlike '*\\$Recycle.Bin\\*') -and ($_.FullName -notlike '*\\System Volume Information\\*') } "
                    // 先把管道当前对象存入 $fi：catch 块内 $_ 已变为异常对象，不再是文件对象；
                    // 删除必须用 -LiteralPath，否则文件名含 [ ] 时会被当通配符解析而删不掉
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
                    // 仅勾“磁盘垃圾”（该选项只对非系统盘展示）：系统盘不清理此类，非系统盘清理垃圾扩展名文件
                    body += "if ($drv -ne $sysl) { " + junkAll + " }; ";
                }
            }
            if (req.RecycleBin && req.RecyclePaths is { Count: > 0 })
            {
                // 选择性清理回收站：RecyclePaths 为扫描带出的物理路径（$R 文件/目录），
                // 直接 Remove-Item 删除 $R 并同步删除配对的 $I 元数据文件，不依赖 Shell 动词（其 CanonicalName 在回收站命名空间为空）；
                // $R 实体已丢失的孤立条目（仅存 $I 元数据）也按配对删除，否则条目永远无法从回收站移除
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
                // 释放空间必须用 double 运算：FreeSpace 是 UInt64 且释放量常超 2GB，
                // 不能写 [math]::Max(0,...)——字面量 0 为 Int32，会命中 Max(Int32,Int32) 重载并抛“值对于 Int32 太大”
                + "$rel=[double]$after-[double]$before; if ($rel -lt 0) { $rel=0 }; "
                + "Write-Output ('CLEAN|'+[math]::Round($rel/1MB,1)+'|'+[math]::Round([double]$after/1GB,1)+'|'+$n)";

            var script = "[Console]::OutputEncoding=[Text.Encoding]::UTF8; " + cred
                + (isRemote
                    ? "$cleanScript={" + body + "}; Invoke-Command -ComputerName '" + SanitizeHost(target.HostAddress!)
                        + "' -Credential $cred -ScriptBlock $cleanScript"
                    : body);

            // 携带大量选定路径时超出 -Command 命令行长度上限，统一以临时脚本文件方式执行；
            // CPROG 行实时回调给调用方（已删除数/已释放MB），供前端展示清理进度
            var (stdout, stderr, ok) = await RunPowerShellScriptAsync(script, TimeSpan.FromMinutes(5), CancellationToken.None,
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
                    // ITEM|1/0|原因|路径（限制分段数 4：路径中可能含 |，原因中的 | 已在脚本侧替换为 /）
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

        // Shell API P/Invoke：与桌面壳 HostFileService 相同的打开文件夹实现，
        // 通过 shell32 通知已运行的资源管理器打开窗口，不派生任何进程，不会被 360 拦截。
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr ILCreateFromPathW(string pszPath);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl, IntPtr[]? apidl, uint dwFlags);

        [DllImport("shell32.dll")]
        private static extern void ILFree(IntPtr pidl);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr ShellExecuteW(IntPtr hwnd, string? lpOperation, string lpFile, string? lpParameters, string? lpDirectory, int nShowCmd);

        /// <summary>回收站 Shell CLSID</summary>
        private const string RecycleBinClsid = "::{645FF040-5081-101B-9F08-00AA002F954E}";

        /// <summary>
        /// 打开指定候选文件的所在文件夹并选中该文件（仅本机目标）。桌面壳内优先由前端
        /// 直接走 Shell API 桥接（hostFileBridge.hostOpenLocation）；浏览器部署/开发模式
        /// 则走到这里的后端 Shell API 实现。两者均不派生 explorer.exe 进程，不被安全软件拦截。
        /// 文件已不存在（如已被清理/系统自动删除）时不报错：降级为打开所在目录，目录也不存在则静默跳过。
        /// </summary>
        public Task OpenFolderAsync(HostMonitorTargetEntity target, string path)
        {
            if (!IsLocalAddress(target.HostAddress))
                throw new InvalidOperationException("仅本机目标支持打开所在文件夹，远程主机请在远程电脑上打开");

            // 目标实体：文件存在则选中该文件；已不存在则降级为其所在目录（仍不存在则静默跳过不报错）
            var openTarget = path;
            if (!File.Exists(path))
            {
                var dir = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return Task.CompletedTask;
                openTarget = dir;
            }

            // SHOpenFolderAndSelectItems 要求在 STA 线程调用（ASP.NET 请求线程为 MTA），
            // 在独立 STA 线程执行并同步等待结果。
            int hr = -1;
            var thread = new Thread(() =>
            {
                var pidl = ILCreateFromPathW(openTarget);
                if (pidl == IntPtr.Zero) { hr = 0; return; }   // 无法解析路径时不阻断，视为已跳过
                try
                {
                    hr = SHOpenFolderAndSelectItems(pidl, 0, null, 0);
                }
                finally
                {
                    ILFree(pidl);
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join(TimeSpan.FromSeconds(10));

            if (hr != 0)
                throw new InvalidOperationException("打开所在文件夹失败，请手动定位：" + path);
            return Task.CompletedTask;
        }

        /// <summary>
        /// 打开系统回收站（仅本机目标）：Shell 打开 CLSID 路径等同双击桌面回收站，直接进入回收站窗口。
        /// 注意不能用 SHOpenFolderAndSelectItems：它对回收站 PIDL 的语义是“打开父级（桌面）并选中回收站图标”，
        /// 不会进入回收站内部；Shell API 调用，不派生 explorer.exe 进程。远程主机无意义（回收站在远程电脑上）。
        /// </summary>
        public Task OpenRecycleBinAsync(HostMonitorTargetEntity target)
        {
            if (!IsLocalAddress(target.HostAddress))
                throw new InvalidOperationException("仅本机目标支持打开回收站，远程主机请在远程电脑上打开");

            // ShellExecuteW 要求 STA 线程，独立 STA 线程执行并同步等待；返回值 >32 为成功
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

        #region 文件系统图标提取

        // SHGetFileInfo 标志：按扩展名关联提取与资源管理器一致的真实系统图标（SHGFI_USEFILEATTRIBUTES 无需文件真实存在）
        private const uint ShgfiIcon = 0x100;
        private const uint ShgfiLargeIcon = 0x0;   // 32x32 大图标，前端缩小显示更清晰
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

        /// <summary>系统图标缓存：key = 主机标识|扩展名，value = data URL（图标为 OS 级资源，提取一次长期复用）</summary>
        private static readonly ConcurrentDictionary<string, string> IconCache = new();

        /// <summary>
        /// 提取文件扩展名对应的真实系统图标（与资源管理器展示一致）：本机目标用 Shell API（SHGetFileInfo）提取，
        /// 远程目标经 WinRM 在目标机用 System.Drawing 提取；结果以 data URL 返回，按主机+扩展名缓存。
        /// 提取失败的扩展名不在结果中，前端回退通用图标。
        /// </summary>
        public async Task<Dictionary<string, string>> GetFileIconsAsync(HostMonitorTargetEntity target, IEnumerable<string> exts)
        {
            var isRemote = !IsLocalAddress(target.HostAddress);
            var hostKey = isRemote ? target.HostAddress!.Trim().ToLowerInvariant() : "local";
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
                if (IconCache.TryGetValue(hostKey + "|" + ext, out var url)) result[ext] = url;
                else missing.Add(ext);
            }
            if (missing.Count == 0) return result;

            var fetched = isRemote
                ? await ExtractRemoteIconsAsync(target, missing)
                : ExtractLocalIcons(missing);
            foreach (var kv in fetched)
            {
                IconCache[hostKey + "|" + kv.Key] = kv.Value;
                result[kv.Key] = kv.Value;
            }
            return result;
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
                        // FromHandle 不拥有句柄：Clone 出独立副本用于转换，原句柄随即 DestroyIcon 释放
                        using var icon = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(shfi.hIcon).Clone();
                        using var bmp = icon.ToBitmap();
                        using var ms = new MemoryStream();
                        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        fetched[ext] = "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
                    }
                    catch { /* 单个扩展名图标转换失败跳过，前端回退通用图标 */ }
                    finally { DestroyIcon(shfi.hIcon); }
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join(TimeSpan.FromSeconds(15));
            return fetched;
        }

        /// <summary>远程图标提取：目标机创建对应扩展名临时文件，System.Drawing 提取关联图标转 PNG base64，输出 ICON|ext|base64 行；失败静默降级</summary>
        private async Task<Dictionary<string, string>> ExtractRemoteIconsAsync(HostMonitorTargetEntity target, List<string> exts)
        {
            var arr = string.Join(",", exts.Select(e => "'" + EscapePs(e) + "'"));
            var body = "Add-Type -AssemblyName System.Drawing; $exts=@(" + arr + "); foreach ($e in $exts) { "
                + "$tmp=Join-Path $env:TEMP ('csicon_'+[guid]::NewGuid().ToString('N')+$e); "
                + "try { New-Item -ItemType File -Path $tmp -Force | Out-Null; "
                + "$ic=[System.Drawing.Icon]::ExtractAssociatedIcon($tmp); "
                + "if ($ic -ne $null) { $bmp=$ic.ToBitmap(); $ms=New-Object System.IO.MemoryStream; "
                + "$bmp.Save($ms,[System.Drawing.Imaging.ImageFormat]::Png); "
                + "Write-Output ('ICON|'+$e+'|'+[Convert]::ToBase64String($ms.ToArray())); "
                + "$ms.Dispose(); $bmp.Dispose(); $ic.Dispose() } } "
                + "finally { Remove-Item $tmp -Force -ErrorAction SilentlyContinue } }";
            var cred = BuildCredential(target, true);
            var cmd = "[Console]::OutputEncoding=[Text.Encoding]::UTF8; " + cred
                + "$iconScript={" + body + "}; Invoke-Command -ComputerName '" + SanitizeHost(target.HostAddress!)
                + "' -Credential $cred -ScriptBlock $iconScript";
            var (stdout, stderr, ok) = await RunPowerShellAsync(cmd, TimeSpan.FromSeconds(60), CancellationToken.None);
            var fetched = new Dictionary<string, string>();
            if (!ok) return fetched;
            foreach (var line in stdout.Split('\n'))
            {
                var t = line.Trim();
                if (!t.StartsWith("ICON|")) continue;
                var p = t.Split('|');
                if (p.Length >= 3 && p[2].Length > 0) fetched[p[1]] = "data:image/png;base64," + p[2];
            }
            return fetched;
        }

        #endregion

        /// <summary>
        /// 采集设备规格（设备名/处理器/内存/显卡/存储/设备ID/产品ID/系统类型/笔和触控）
        /// 与 ipconfig /all 原始输出（本机或远程 WinRM）。输出协议：KV|键|值 行 +
        /// NETBEGIN/NETEND 之间的 ipconfig 原文。
        /// </summary>
        public async Task<HostSystemInfoDto> SystemInfoAsync(HostMonitorTargetEntity target)
        {
            var isRemote = !IsLocalAddress(target.HostAddress);
            var cred = BuildCredential(target, isRemote);

            // 采集脚本体（本机/远程同用）：CIM + 注册表 + GetSystemMetrics(92) 判定笔和触控
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
  # ipconfig 等原生程序按 OEM 代码页（中文系统为 GBK）输出原始字节，
  # 经 cmd 重定向落盘保留原始字节，再按 OEM 代码页显式解码，彻底避免管道解码乱码
  cmd /c ('ipconfig /all > ""' + $tmp + '"" 2>&1')
  if (Test-Path -LiteralPath $tmp) { $net=[IO.File]::ReadAllText($tmp, [Text.Encoding]::GetEncoding((Get-Culture).TextInfo.OEMCodePage)) }
} finally {
  Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
}
Write-Output 'NETBEGIN'
Write-Output $net
Write-Output 'NETEND'";

            var script = "[Console]::OutputEncoding=[Text.Encoding]::UTF8; " + cred
                + (isRemote
                    ? "$infoScript={" + body + "}; Invoke-Command -ComputerName '" + SanitizeHost(target.HostAddress!)
                        + "' -Credential $cred -ScriptBlock $infoScript"
                    : body);

            var (stdout, stderr, ok) = await RunPowerShellScriptAsync(script, TimeSpan.FromSeconds(60), CancellationToken.None);
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

        #region PowerShell 执行

        private static string PowerShellExe =>
            Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");

        /// <summary>
        /// 执行 PowerShell 单行命令并采集输出。命令内部一律使用单引号，避免与 -Command 外层双引号冲突。
        /// </summary>
        private static Task<(string StdOut, string StdErr, bool Ok)> RunPowerShellAsync(string command, TimeSpan timeout, CancellationToken ct, Action<string>? onStdoutLine = null)
            => RunPsProcessAsync($"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{command}\"", timeout, ct, onStdoutLine);

        /// <summary>
        /// 将脚本写入临时文件后以 -File 执行（适用于携带大量路径的长脚本），执行完毕立即删除临时文件。
        /// onStdoutLine 非空时逐行实时回调（用于清理进度上报）。
        /// </summary>
        private static async Task<(string StdOut, string StdErr, bool Ok)> RunPowerShellScriptAsync(string script, TimeSpan timeout, CancellationToken ct, Action<string>? onStdoutLine = null)
        {
            var scriptPath = Path.Combine(Path.GetTempPath(), $"cs_hostclean_{Guid.NewGuid():N}.ps1");
            try
            {
                File.WriteAllText(scriptPath, script, new UTF8Encoding(true));   // 带 BOM，确保 PowerShell 5.1 按 UTF-8 解析中文路径
                return await RunPsProcessAsync($"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\"", timeout, ct, onStdoutLine);
            }
            finally
            {
                try { File.Delete(scriptPath); } catch { /* 忽略临时脚本清理失败 */ }
            }
        }

        /// <summary>启动 PowerShell 进程并采集输出（-Command / -File 共用）；
        /// onStdoutLine 非空时逐行实时回调（用于扫描进度上报），同时仍累积完整 stdout 供后续解析</summary>
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
            // stdout 逐行读取：边累积边回调进度；进程退出后 ReadLineAsync 返回 null 结束
            var sbOut = new StringBuilder();
            var outTask = Task.Run(async () =>
            {
                while (true)
                {
                    var line = await process.StandardOutput.ReadLineAsync(ct);
                    if (line == null) break;
                    lock (sbOut) sbOut.AppendLine(line);
                    try { onStdoutLine?.Invoke(line); } catch { /* 进度回调异常不影响扫描本身 */ }
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
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { /* 忽略清理异常 */ }
        }

        #endregion

        #region 告警邮件

        /// <summary>状态变化告警邮件：收件人 = 启用且有邮箱且拥有 host-monitor 菜单权限的用户</summary>
        private async Task NotifyStatusChangeAsync(HostMonitorTargetEntity target, byte status,
            decimal? value, string? error, DateTime checkAt, CancellationToken ct)
        {
            var recipients = GetAlertRecipients();
            if (recipients.Count == 0) return;

            var isOk = status == StatusOk;
            var subject = isOk
                ? $"【主机监控】{target.Name} 已恢复正常"
                : $"【主机监控告警】{target.Name} 探测异常";

            var sb = new StringBuilder();
            sb.Append("<div style=\"font-family:'Microsoft YaHei',sans-serif;font-size:14px;color:#303133;line-height:1.8\">");
            sb.Append($"<div style=\"border-left:4px solid {(isOk ? "#67c23a" : "#f56c6c")};padding:10px 14px;"
                + $"background:{(isOk ? "#f0f9eb" : "#fef0f0")};border-radius:4px;font-weight:bold\">");
            sb.Append(isOk ? "监控目标已恢复正常" : "监控目标探测异常，请及时关注");
            sb.Append("</div>");
            sb.Append("<table border=\"0\" cellspacing=\"0\" cellpadding=\"0\" style=\"border-collapse:collapse;margin-top:12px;font-size:13px\">");
            AppendRow(sb, "监控目标", System.Net.WebUtility.HtmlEncode(target.Name));
            AppendRow(sb, "监控指标", System.Net.WebUtility.HtmlEncode(MetricDesc(target)));
            AppendRow(sb, "探测结果", isOk ? "<b style=\"color:#67c23a\">正常</b>" : "<b style=\"color:#f56c6c\">异常</b>");
            AppendRow(sb, "当前值", FormatValue(target.MetricType, value));
            if (error != null) AppendRow(sb, "异常原因", System.Net.WebUtility.HtmlEncode(error));
            AppendRow(sb, "探测时间", checkAt.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.Append("</table>");
            sb.Append("<p style=\"color:#909399;font-size:12px;margin-top:14px\">本邮件由主机监控自动发送，请勿回复。</p>");
            sb.Append("</div>");

            var sw = Stopwatch.StartNew();
            var result = await _emailService.SendAsync(string.Join(";", recipients), subject, sb.ToString());
            sw.Stop();

            _fsql.Insert(new EmailLogEntity
            {
                TaskId = 0,
                TaskName = TaskName,
                Recipients = string.Join(";", recipients),
                Subject = subject,
                Content = sb.ToString(),
                Status = (byte)(result.Success ? 1 : 0),
                ErrorMessage = result.ErrorMessage,
                CostMs = (int)sw.ElapsedMilliseconds,
            }).ExecuteAffrows();

            if (result.Success)
                _logger.LogInformation("主机监控：{Name} 状态变化告警邮件已发送（{Count} 人）", target.Name, recipients.Count);
            else
                _logger.LogWarning("主机监控：{Name} 告警邮件发送失败：{Err}", target.Name, result.ErrorMessage);
        }

        /// <summary>告警收件人：启用且有邮箱、且通过启用角色拥有 host-monitor 菜单权限的用户邮箱</summary>
        private List<string> GetAlertRecipients()
        {
            var menuId = _fsql.Select<SysMenuEntity>()
                .Where(m => m.Name == "host-monitor")
                .First(m => m.Id);
            if (menuId == 0) return new List<string>();

            var roleIds = _fsql.Select<SysRoleMenuEntity>()
                .Where(rm => rm.MenuId == menuId)
                .ToList(rm => rm.RoleId);
            if (roleIds.Count == 0) return new List<string>();

            var enabledRoleIds = _fsql.Select<SysRoleEntity>()
                .Where(r => r.Enabled)
                .ToList(r => r.Id)
                .Where(id => roleIds.Contains(id))
                .ToHashSet();
            if (enabledRoleIds.Count == 0) return new List<string>();

            var userIds = _fsql.Select<SysUserRoleEntity>()
                .Where(ur => enabledRoleIds.Contains(ur.RoleId))
                .ToList(ur => ur.UserId)
                .Distinct()
                .ToList();
            if (userIds.Count == 0) return new List<string>();

            return _fsql.Select<SysUserEntity>()
                .Where(u => u.Enabled && u.Email != null && u.Email != "" && userIds.Contains(u.Id))
                .ToList(u => u.Email!);
        }

        #endregion

        #region 工具方法

        /// <summary>指标描述（含盘符/服务名，用于告警邮件展示）</summary>
        private static string MetricDesc(HostMonitorTargetEntity target) => target.MetricType switch
        {
            HostMonitorMetrics.Disk => string.IsNullOrWhiteSpace(target.DriveLetter)
                ? "磁盘已用率（所有固定磁盘）"
                : $"磁盘已用率（{target.DriveLetter!.ToUpperInvariant()} 盘）",
            HostMonitorMetrics.Memory => "内存使用率",
            HostMonitorMetrics.Cpu => "CPU 使用率",
            HostMonitorMetrics.Service => $"Windows 服务（{target.ServiceNames}）",
            HostMonitorMetrics.Host => string.IsNullOrWhiteSpace(target.HostAddress)
                ? "整机概览（本机）"
                : $"整机概览（{target.HostAddress}）",
            _ => target.MetricType,
        };

        /// <summary>探测值展示：百分比指标带 %，服务指标为运行中服务数</summary>
        private static string FormatValue(string metricType, decimal? value)
        {
            if (!value.HasValue) return "—";
            return metricType == HostMonitorMetrics.Service
                ? $"{value.Value:0} 个服务运行中"
                : $"{value.Value:0.0}%";
        }

        /// <summary>
        /// 判断目标地址是否实际指向本机：空地址、环回地址、本机计算机名（含 FQDN）或任一本机网卡 IP 均视为本机。
        /// 用户常用本机 IP 配置本机监控，此类目标应与空地址目标同等对待（本机执行、支持打开文件夹）。
        /// </summary>
        public static bool IsLocalAddress(string? hostAddress)
        {
            if (string.IsNullOrWhiteSpace(hostAddress)) return true;
            var host = hostAddress.Trim();
            if (host is "localhost" or "127.0.0.1" or "::1" or "[::1]" or ".") return true;
            return LocalAddressSet.Value.Contains(host);
        }

        /// <summary>本机地址集合（计算机名/FQDN/全部网卡单播 IP），首次使用时枚举一次并缓存</summary>
        private static readonly Lazy<HashSet<string>> LocalAddressSet = new(() =>
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Environment.MachineName };
            try
            {
                var dom = IPGlobalProperties.GetIPGlobalProperties().DomainName;
                if (!string.IsNullOrEmpty(dom)) set.Add(Environment.MachineName + "." + dom);
            }
            catch { /* 域信息获取失败时仅按计算机名匹配 */ }
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                        set.Add(ua.Address.ToString());
            }
            catch { /* 网卡枚举失败时退回计算机名匹配 */ }
            return set;
        });

        /// <summary>盘符清洗：仅保留字母数字（防注入）</summary>
        private static string SanitizeDrive(string drive)
            => new string(drive.Trim().TrimEnd(':').ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

        /// <summary>主机地址清洗：仅保留字母/数字/点/连字符/冒号（兼容 IPv6，防注入）</summary>
        private static string SanitizeHost(string host)
            => new string(host.Trim().Where(c => char.IsLetterOrDigit(c) || c is '.' or '-' or ':' or '_').ToArray());

        /// <summary>PowerShell 单引号字符串转义（防注入）</summary>
        private static string EscapePs(string s) => s.Replace("'", "''");

        /// <summary>远程 WinRM 凭据构造语句（本机返回空串；远程缺账号密码时报错）</summary>
        private static string BuildCredential(HostMonitorTargetEntity target, bool isRemote)
        {
            if (!isRemote) return string.Empty;
            if (string.IsNullOrWhiteSpace(target.AuthAccount) || string.IsNullOrWhiteSpace(target.AuthPassword))
                throw new InvalidOperationException("远程目标必须配置采集账号与密码");
            return "$pw=ConvertTo-SecureString '" + EscapePs(target.AuthPassword!) + "' -AsPlainText -Force; "
                + "$cred=New-Object System.Management.Automation.PSCredential('" + EscapePs(target.AuthAccount!) + "',$pw); ";
        }

        /// <summary>服务名解析：逗号/分号/空格分隔，仅保留合法服务名字符（防注入）</summary>
        private static List<string> ParseServiceNames(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return new List<string>();
            return raw.Split([',', ';', '，'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(n => new string(n.Where(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-' or ' ').ToArray()).Trim())
                .Where(n => n.Length > 0)
                .Distinct()
                .ToList();
        }

        private static void AppendRow(StringBuilder sb, string label, string value)
            => sb.Append($"<tr><td style=\"padding:6px 12px;color:#909399;white-space:nowrap\">{label}</td>"
                + $"<td style=\"padding:6px 12px\">{value}</td></tr>");

        /// <summary>异常文本截断至 450 字符：调用处会拼接不超过 20 字符的前缀，确保总长不超过 ErrorMsg 列宽（500），杜绝写入截断异常</summary>
        private static string Truncate(string s) => s.Length <= 450 ? s : s[..450];

        /// <summary>判断是否为特殊处理类别（需要单独扫描逻辑，不走常规目录遍历）</summary>
        private static bool IsSpecialCategory(string code) => code switch
        {
            "BROWSER_CACHE" or "THUMBNAIL_CACHE" or "LOG_FILE" or "OLD_DOWNLOAD" => true,
            _ => false,
        };

        /// <summary>扫描分类码 → 允许目录根表达式（与扫描枚举口径完全一致）：
        /// 选择性清理时按勾选文件自身分类推导，不依赖分类布尔标志重传</summary>
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
}
