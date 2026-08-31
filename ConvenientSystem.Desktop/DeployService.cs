using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Renci.SshNet;

namespace ConvenientSystem.Desktop;

/// <summary>
/// 部署编排服务：把本地构建产物打包、SFTP 上传到服务器，通过 SSH 执行
/// Docker Compose 重建和重启。支持 Linux（Docker Compose）和 Windows（IIS/服务）两种目标。
/// 采用两阶段原子切换：先解压到 {target}.new，校验后一次性切换到正式目录（旧版本备份为 .old），
/// 取消部署时自动回滚到部署前状态，保证远程环境不被半成品破坏。
/// </summary>
public sealed class DeployService
{
    private readonly ConcurrentDictionary<string, DeployJob> _jobs = new();
    private readonly ILogger<DeployService> _logger;

    // ============================ 部署历史（JSON 文件持久化） ============================

    private readonly object _historyLock = new();
    private List<DeployHistoryItem> _history = new();
    private const int MaxHistoryItems = 100;
    private static string HistoryFilePath => Path.Combine(AppContext.BaseDirectory, "deploy-history.json");
    private static readonly JsonSerializerOptions HistoryJsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public DeployService(ILogger<DeployService> logger)
    {
        _logger = logger;
        LoadHistory();
    }

    /// <summary>启动部署任务。</summary>
    public string StartDeploy(DeployRequest request)
    {
        var jobId = Guid.NewGuid().ToString("N")[..12];
        var job = new DeployJob
        {
            Id = jobId,
            BuildName = request.BuildName,
            BuildType = request.BuildType,
            TargetOS = request.TargetOS,
            SiteName = string.IsNullOrWhiteSpace(request.SiteName) ? "convenient" : request.SiteName.Trim(),
            Host = request.Host.Trim(),
            Status = DeployStatus.Running,
            StartTime = DateTime.Now,
            Log = new StringBuilder(),
            Cts = new CancellationTokenSource(),
        };
        _jobs[jobId] = job;

        _ = Task.Run(() => RunDeployAsync(job, request));
        return jobId;
    }

    /// <summary>获取部署任务状态。</summary>
    public DeployJobDto? GetJob(string jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var job)) return null;
        return ToDto(job);
    }

    /// <summary>
    /// 取消部署任务：发送取消信号，正在运行的部署会在安全检查点中断并自动还原环境。
    /// 临界区（重启容器/启停服务）期间拒绝取消，返回原因。
    /// </summary>
    public (bool Ok, string Message) Cancel(string jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
            return (false, "任务不存在");
        if (job.Status != DeployStatus.Running)
            return (false, "任务已结束，无需取消");
        if (job.InCriticalSection)
            return (false, "正在执行关键切换步骤（启停容器/服务，仅需数秒），暂无法取消，请稍候");
        job.Cts?.Cancel();
        return (true, "已发送取消信号，正在中断并还原部署前环境...");
    }

    /// <summary>检查站点是否已存在（Linux 查 Docker 容器，Windows 查 Windows 服务）。</summary>
    public SiteExistsResult CheckSiteExists(CheckSiteExistsRequest request)
    {
        var siteName = string.IsNullOrWhiteSpace(request.SiteName) ? "convenient" : request.SiteName.Trim();
        var serviceName = string.IsNullOrWhiteSpace(request.ServiceName) ? string.Empty : request.ServiceName.Trim();

        if (request.BuildType == UniversalBuildType.Installer)
        {
            return new SiteExistsResult { Exists = false, Message = "安装包类型不涉及站点检查" };
        }

        try
        {
            var connectionInfo = new Renci.SshNet.ConnectionInfo(
                request.Host.Trim(),
                request.UserName.Trim(),
                new PasswordAuthenticationMethod(request.UserName.Trim(), request.Password));

            using var ssh = new SshClient(connectionInfo);
            ssh.Connect();

            if (request.TargetOS == DeployTargetOS.Linux)
            {
                var filter = string.IsNullOrEmpty(serviceName) ? siteName : $"{siteName}-{serviceName}";
                var result = ExecuteSshCommand(ssh, $"docker ps -a --filter name=^{filter}$ --format '{{{{.Names}}}}' 2>&1");
                var exists = result.Split('\n')
                    .Select(line => line.Trim())
                    .Any(name => name.Equals(filter, StringComparison.OrdinalIgnoreCase));
                ssh.Disconnect();
                return new SiteExistsResult
                {
                    Exists = exists,
                    Message = exists ? "已有站点，将更新现有容器" : "新站点，将首次创建容器"
                };
            }
            else
            {
                if (string.IsNullOrEmpty(serviceName))
                {
                    return new SiteExistsResult { Exists = false, Message = "未指定服务名，无法判断" };
                }
                var result = ExecuteSshCommand(ssh, $"powershell -Command \"Get-Service -Name '{serviceName}' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name\"");
                var exists = result.Contains(serviceName, StringComparison.OrdinalIgnoreCase);
                ssh.Disconnect();
                return new SiteExistsResult
                {
                    Exists = exists,
                    Message = exists ? "已有站点，将更新现有服务" : "新站点，将首次创建服务"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "检查站点 {SiteName} 是否存在失败", siteName);
            return new SiteExistsResult { Exists = false, Message = "检查失败：" + ex.Message };
        }
    }

    private async Task RunDeployAsync(DeployJob job, DeployRequest request)
    {
        try
        {
            var outputDir = request.OutputDir.Trim();
            if (!Directory.Exists(outputDir))
            {
                Fail(job, $"构建产物目录不存在: {outputDir}");
                return;
            }

            // 空产物校验：目录存在但无任何文件时中止（空包上线等于清空站点）
            if (Directory.GetFileSystemEntries(outputDir).Length == 0)
            {
                Fail(job, $"构建产物目录为空: {outputDir}，请先完成构建再部署");
                return;
            }

            job.Log.AppendLine($"===== 开始部署 [{job.BuildName}] → {job.TargetOS} {request.Host} [{job.SiteName}] =====");
            job.Log.AppendLine($"构建产物: {outputDir}");
            job.Log.AppendLine();

            if (job.TargetOS == DeployTargetOS.Linux)
                await DeployToLinuxAsync(job, request, outputDir);
            else
                await DeployToWindowsAsync(job, request, outputDir);
        }
        catch (OperationCanceledException)
        {
            // 取消：中断部署并还原到部署前状态
            job.Status = DeployStatus.Cancelled;
            job.CompletedTime = DateTime.Now;
            job.Log.AppendLine();
            job.Log.AppendLine($"===== ⛔ 部署已取消（中断于第 {job.CurrentStep} 步） =====");
            await RollbackAsync(job, request);
        }
        catch (Exception ex)
        {
            // 失败同样必须回滚：部署中途失败时还原环境，避免正式目录被挪走后无人还原
            job.Status = DeployStatus.Failed;
            job.CompletedTime = DateTime.Now;
            await RollbackAsync(job, request);
            job.Log.AppendLine();
            job.Log.AppendLine($"===== ❌ 部署失败: {ex.Message} =====");
            _logger.LogError(ex, "部署失败: {BuildName}", job.BuildName);
        }
        finally
        {
            // 无论成功/失败/取消，结束时落一条历史记录（日志不持久化，仅存摘要）
            RecordHistory(job);
        }
    }

    // ============================ Linux 部署 ============================

    private async Task DeployToLinuxAsync(DeployJob job, DeployRequest request, string outputDir)
    {
        var token = job.Cts!.Token;
        var siteName = job.SiteName;
        var deployBase = string.IsNullOrWhiteSpace(request.DeployPath)
            ? $"/opt/{siteName}"
            : request.DeployPath.Trim();

        // 根据构建类型确定远程目标路径和 Docker 服务名
        var (defaultService, defaultDir) = GetLinuxDeployTarget(job.BuildType, deployBase);
        var serviceName = string.IsNullOrWhiteSpace(request.ServiceName) ? defaultService : request.ServiceName.Trim();
        var remoteTargetDir = string.IsNullOrWhiteSpace(request.RemoteDir) ? defaultDir : request.RemoteDir.Trim();

        var tempDir = $"/tmp/convenient-deploy-{siteName}";
        // 两阶段切换：新文件先解压到 .new，校验后原子切换；旧版本备份到 .old
        var newDir = $"{remoteTargetDir}.new";
        var oldDir = $"{remoteTargetDir}.old";
        RecordRemotePaths(job, remoteTargetDir, newDir, oldDir, tempDir, serviceName);

        var archiveName = string.IsNullOrWhiteSpace(request.ArchiveName)
            ? $"{siteName}-{serviceName}.tar.gz"
            : request.ArchiveName.Trim();
        var localArchivePath = Path.Combine(Path.GetTempPath(), archiveName);
        var remoteArchivePath = $"{tempDir}/{archiveName}";

        var connectionInfo = new Renci.SshNet.ConnectionInfo(
            request.Host.Trim(),
            request.UserName.Trim(),
            new PasswordAuthenticationMethod(request.UserName.Trim(), request.Password));

        try
        {
            // ① 打包构建产物（可取消，无副作用）
            SetStep(job, 1, "打包构建产物");
            await Task.Run(() => CreateTarGz(outputDir, localArchivePath, token), token);
            var size = new FileInfo(localArchivePath).Length;
            job.Log.AppendLine($"      ✓ {archiveName} ({FormatSize(size)})");

            // ② SFTP 上传（可取消，无副作用）
            SetStep(job, 2, "SFTP 上传到服务器");
            using (var sftp = new SftpClient(connectionInfo))
            {
                sftp.Connect();
                EnsureRemoteDirectory(sftp, tempDir);
                var totalBytes = (ulong)new FileInfo(localArchivePath).Length;
                using var fs = File.OpenRead(localArchivePath);
                sftp.UploadFile(fs, remoteArchivePath, true, (sent) =>
                {
                    token.ThrowIfCancellationRequested();
                    if (totalBytes <= 0) return;
                    var percent = (int)(sent * 100 / totalBytes);
                    if (percent % 25 == 0 && percent > 0)
                        job.Log.AppendLine($"      上传进度: {percent}%");
                });
                sftp.Disconnect();
            }
            job.Log.AppendLine($"      ✓ 上传完成");

            // ③ 解压到 .new 临时目录（正式目录未动，取消无副作用）
            SetStep(job, 3, "解压到临时目录");
            using var ssh = new SshClient(connectionInfo);
            ssh.Connect();

            await ExecuteSshAsync(ssh, $"rm -rf {newDir} && mkdir -p {newDir}", job, token);
            await ExecuteSshAsync(ssh, $"tar -xzf {remoteArchivePath} -C {newDir}", job, token);
            job.Log.AppendLine($"      ✓ 解压完成（{newDir}，正式目录未受影响）");

            // ④ 原子切换（拆两步，保证回滚判定准确）：
            //    第一步备份旧版，成功即置 FilesSwapped（此后任何失败/取消都会精确还原）；
            //    首次部署时正式目录不存在，mv 失败用 || true 允许跳过。
            SetStep(job, 4, "切换到新版本");
            // 防御性校验：新版本目录必须存在才允许备份旧版（否则备份后陷入无版本可用状态）
            await ExecuteSshAsync(ssh,
                $"test -d {newDir} || (echo '新版本目录不存在: {newDir}，中止切换' && exit 1)", job, token);
            await ExecuteSshAsync(ssh,
                $"rm -rf {oldDir}; mv {remoteTargetDir} {oldDir} 2>/dev/null || true", job, token);
            job.FilesSwapped = true;
            await ExecuteSshAsync(ssh, $"mv {newDir} {remoteTargetDir}", job, token);
            job.Log.AppendLine($"      ✓ 已切换（旧版本备份于 {oldDir}）");

            // ⑤ Docker 镜像构建（可取消：旧容器仍在运行，取消后回滚文件即完全还原）
            SetStep(job, 5, "构建 Docker 镜像");
            if (!string.IsNullOrEmpty(serviceName))
            {
                await ExecuteSshAsync(ssh,
                    $"cd {deployBase} && docker compose -p {siteName} build --no-cache {serviceName} 2>&1", job, token);
                job.Log.AppendLine($"      ✓ 镜像构建完成");
            }
            else
            {
                job.Log.AppendLine($"      跳过（该类型无需重建镜像）");
            }

            // ⑥ 重启容器（临界区：执行期间拒绝取消；完成后新版本即生效）
            SetStep(job, 6, "重启容器");
            if (!string.IsNullOrEmpty(serviceName))
            {
                job.InCriticalSection = true;
                try
                {
                    await ExecuteSshAsync(ssh,
                        $"cd {deployBase} && docker compose -p {siteName} up -d {serviceName} 2>&1", job, CancellationToken.None);
                }
                finally
                {
                    job.InCriticalSection = false;
                }
                job.Log.AppendLine($"      ✓ 容器已重启");
            }
            else
            {
                job.Log.AppendLine($"      跳过");
            }
            job.Committed = true;

            // ⑦ 验证与清理（.old 备份保留，供手动回滚）
            SetStep(job, 7, "验证与清理");
            if (request.VerifyHealth && !string.IsNullOrEmpty(serviceName))
            {
                await Task.Delay(3000, token); // 等待容器启动
                await ExecuteSshAsync(ssh,
                    $"docker ps --filter name={siteName}-{serviceName} --format \"table {{{{.Names}}}}\\t{{{{.Status}}}}\" 2>&1", job, token);

                // 健康检查
                var healthUrl = serviceName == "api"
                    ? "http://localhost:51943/api/health"
                    : "http://localhost:80";
                await ExecuteSshAsync(ssh,
                    $"curl -s -o /dev/null -w \"%{{http_code}}\" {healthUrl} 2>&1 || echo 'health check failed'", job, token);
                job.Log.AppendLine($"      ✓ 验证完成");
            }

            await ExecuteSshAsync(ssh, $"rm -rf {tempDir}", job, token);
            job.Log.AppendLine($"      ✓ 清理完成（旧版本备份保留在 {oldDir}，下次部署时覆盖）");

            ssh.Disconnect();

            job.Status = DeployStatus.Success;
            job.CompletedTime = DateTime.Now;
            var elapsed = (int)(job.CompletedTime.Value - job.StartTime).TotalSeconds;
            job.Log.AppendLine();
            job.Log.AppendLine($"===== ✅ 部署完成！耗时 {elapsed} 秒 =====");
            job.Log.AppendLine($"🌐 站点地址: {GetSiteUrl(request, serviceName)}");
        }
        finally
        {
            try { File.Delete(localArchivePath); } catch { /* ignore */ }
        }
    }

    // ============================ Windows 部署 ============================

    private async Task DeployToWindowsAsync(DeployJob job, DeployRequest request, string outputDir)
    {
        var token = job.Cts!.Token;
        var siteName = job.SiteName;
        var deployBase = string.IsNullOrWhiteSpace(request.DeployPath)
            ? @$"D:\apps\{siteName}"
            : request.DeployPath.Trim();

        var (defaultService, defaultDir) = GetWindowsDeployTarget(job.BuildType, deployBase);
        var serviceName = string.IsNullOrWhiteSpace(request.ServiceName) ? defaultService : request.ServiceName.Trim();
        var remoteTargetDir = string.IsNullOrWhiteSpace(request.RemoteDir) ? defaultDir : request.RemoteDir.Trim();

        var tempDir = @$"C:\Temp\convenient-deploy-{siteName}";
        // 两阶段切换：新文件先解压到 .new，停止服务后原子切换；旧版本备份到 .old
        var newDir = $@"{remoteTargetDir}.new";
        var oldDir = $@"{remoteTargetDir}.old";
        RecordRemotePaths(job, remoteTargetDir, newDir, oldDir, tempDir, serviceName);
        // Rename-Item 的新名只能是叶名（不含路径），预先提取
        var leafName = Path.GetFileName(remoteTargetDir.TrimEnd('\\', '/'));

        var archiveName = string.IsNullOrWhiteSpace(request.ArchiveName)
            ? $"{siteName}-{serviceName}.zip"
            : request.ArchiveName.Trim();
        var localArchivePath = Path.Combine(Path.GetTempPath(), archiveName);
        var remoteArchivePath = $@"{tempDir}\{archiveName}";

        var connectionInfo = new Renci.SshNet.ConnectionInfo(
            request.Host.Trim(),
            request.UserName.Trim(),
            new PasswordAuthenticationMethod(request.UserName.Trim(), request.Password));

        try
        {
            // ① 打包构建产物（可取消，无副作用）
            SetStep(job, 1, "打包构建产物");
            token.ThrowIfCancellationRequested();
            await Task.Run(() => CreateZip(outputDir, localArchivePath));
            token.ThrowIfCancellationRequested();
            var size = new FileInfo(localArchivePath).Length;
            job.Log.AppendLine($"      ✓ {archiveName} ({FormatSize(size)})");

            // ② SFTP 上传（可取消，无副作用）
            SetStep(job, 2, "SFTP 上传到服务器");
            using (var sftp = new SftpClient(connectionInfo))
            {
                sftp.Connect();
                EnsureRemoteDirectory(sftp, tempDir.Replace("\\", "/"));
                var totalBytes = (ulong)new FileInfo(localArchivePath).Length;
                using var fs = File.OpenRead(localArchivePath);
                sftp.UploadFile(fs, remoteArchivePath.Replace("\\", "/"), true, (sent) =>
                {
                    token.ThrowIfCancellationRequested();
                    if (totalBytes <= 0) return;
                    var percent = (int)(sent * 100 / totalBytes);
                    if (percent % 25 == 0 && percent > 0)
                        job.Log.AppendLine($"      上传进度: {percent}%");
                });
                sftp.Disconnect();
            }
            job.Log.AppendLine($"      ✓ 上传完成");

            // ③ 解压到 .new 临时目录（正式目录未动，取消无副作用）
            SetStep(job, 3, "解压到临时目录");
            using var ssh = new SshClient(connectionInfo);
            ssh.Connect();

            await ExecuteSshAsync(ssh,
                $"powershell -Command \"if (Test-Path '{newDir}') {{ Remove-Item '{newDir}' -Recurse -Force }}; " +
                $"New-Item -ItemType Directory -Force -Path '{newDir}' | Out-Null; " +
                $"Expand-Archive -Path '{remoteArchivePath}' -DestinationPath '{newDir}' -Force\"", job, token);
            job.Log.AppendLine($"      ✓ 解压完成（{newDir}，正式目录未受影响）");

            // ④ 停止服务 + 原子切换目录（拆两步，保证回滚判定准确；与 Linux 流程一致）
            SetStep(job, 4, "停止服务并切换文件");
            if (!string.IsNullOrEmpty(serviceName))
            {
                await ExecuteSshAsync(ssh,
                    $"powershell -Command \"Stop-Service -Name '{serviceName}' -Force -ErrorAction SilentlyContinue\"", job, token);
                job.ServiceStopped = true;
            }
            // 第一步：备份旧版（正式目录→.old）。$ErrorActionPreference='Stop' 让失败返回非 0 退出码；
            // 首次部署时正式目录不存在，Test-Path 守卫自动跳过。成功即置 FilesSwapped。
            // 防御性校验：新版本目录必须存在才允许备份旧版（否则备份后陷入无版本可用状态）
            await ExecuteSshAsync(ssh,
                $"powershell -Command \"if (-not (Test-Path '{newDir}')) {{ Write-Host '新版本目录不存在: {newDir}，中止切换'; exit 1 }}\"", job, token);
            await ExecuteSshAsync(ssh,
                $"powershell -Command \"$ErrorActionPreference='Stop'; " +
                $"if (Test-Path '{oldDir}') {{ Remove-Item '{oldDir}' -Recurse -Force }}; " +
                $"if (Test-Path '{remoteTargetDir}') {{ Rename-Item '{remoteTargetDir}' '{leafName}.old' -Force }}\"", job, token);
            job.FilesSwapped = true;
            // 第二步：新版本就位（.new→正式目录）
            await ExecuteSshAsync(ssh,
                $"powershell -Command \"$ErrorActionPreference='Stop'; Rename-Item '{newDir}' '{leafName}' -Force\"", job, token);
            job.Log.AppendLine($"      ✓ 已切换（旧版本备份于 {oldDir}）");

            // ⑤ 启动服务（临界区：执行期间拒绝取消；完成后新版本即生效）
            SetStep(job, 5, "启动服务");
            if (!string.IsNullOrEmpty(serviceName))
            {
                job.InCriticalSection = true;
                try
                {
                    await ExecuteSshAsync(ssh,
                        $"powershell -Command \"Start-Service -Name '{serviceName}' -ErrorAction SilentlyContinue\"", job, CancellationToken.None);
                }
                finally
                {
                    job.InCriticalSection = false;
                }
                job.Log.AppendLine($"      ✓ 服务已启动");
            }
            job.Committed = true;

            // ⑥⑦ 验证与清理（.old 备份保留，供手动回滚）
            SetStep(job, 7, "验证与清理");
            if (request.VerifyHealth && !string.IsNullOrEmpty(serviceName))
            {
                await Task.Delay(3000, token);
                await ExecuteSshAsync(ssh,
                    $"powershell -Command \"Get-Service -Name '{serviceName}' | Format-Table Name,Status\"", job, token);

                var healthUrl = serviceName == "ConvenientSystem.Api"
                    ? "http://localhost:51943/api/health"
                    : "http://localhost:8080";
                await ExecuteSshAsync(ssh,
                    $"powershell -Command \"(Invoke-WebRequest -Uri '{healthUrl}' -UseBasicParsing -TimeoutSec 10).StatusCode\"", job, token);
                job.Log.AppendLine($"      ✓ 验证完成");
            }

            await ExecuteSshAsync(ssh,
                $"powershell -Command \"Remove-Item -Path '{tempDir}' -Recurse -Force -ErrorAction SilentlyContinue\"", job, token);
            job.Log.AppendLine($"      ✓ 清理完成（旧版本备份保留在 {oldDir}，下次部署时覆盖）");

            ssh.Disconnect();

            job.Status = DeployStatus.Success;
            job.CompletedTime = DateTime.Now;
            var elapsed = (int)(job.CompletedTime.Value - job.StartTime).TotalSeconds;
            job.Log.AppendLine();
            job.Log.AppendLine($"===== ✅ 部署完成！耗时 {elapsed} 秒 =====");
            job.Log.AppendLine($"🌐 站点地址: {GetSiteUrl(request, serviceName)}");
        }
        finally
        {
            try { File.Delete(localArchivePath); } catch { /* ignore */ }
        }
    }

    // ============================ 中断回滚（取消/失败共用） ============================

    /// <summary>
    /// 中断后还原部署前环境（取消与失败共用）。状态与标题由调用方先设置，这里只负责还原动作：
    /// - 文件已切换（.old 备份存在）：新文件挪走、旧版本还原为正式目录；
    /// - Windows 服务已停止：用旧文件重新启动服务；
    /// - 服务器环境尚未改动：仅清理 .new/.tmp 临时残留。
    /// </summary>
    private async Task RollbackAsync(DeployJob job, DeployRequest request)
    {
        job.InCriticalSection = false;
        job.Log.AppendLine();

        // 新版本已生效（容器/服务已切换）：不再回滚，仅中断后续验证与清理
        if (job.Committed)
        {
            job.Log.AppendLine(">> 新版本已生效，本次中断仅跳过验证与清理步骤");
            return;
        }

        // 服务器环境尚未改动：清理临时残留即可
        if (!job.FilesSwapped && !job.ServiceStopped)
        {
            job.Log.AppendLine(">> 服务器环境未受影响，正在清理临时文件...");
            await TryCleanupRemoteAsync(job, request);
            return;
        }

        // 需要还原：文件换回旧版本 + Windows 服务重启
        job.Log.AppendLine(">> 正在还原部署前环境...");
        try
        {
            var connectionInfo = new Renci.SshNet.ConnectionInfo(
                request.Host.Trim(),
                request.UserName.Trim(),
                new PasswordAuthenticationMethod(request.UserName.Trim(), request.Password));
            using var ssh = new SshClient(connectionInfo);
            ssh.Connect();

            if (job.TargetOS == DeployTargetOS.Linux)
            {
                if (job.FilesSwapped)
                {
                    // 新文件挪到 .new，旧备份还原为正式目录，最后删除新文件与临时目录；
                    // 首次部署无 .old 备份时 mv 失败属预期（部署前本来就没有正式目录），输出提示区分
                    await ExecuteSshAsync(ssh,
                        $"mv {job.TargetDir} {job.NewDir} 2>/dev/null; " +
                        $"mv {job.OldDir} {job.TargetDir} 2>/dev/null || echo '旧版本备份不存在（首次部署），正式目录已移除'; " +
                        $"rm -rf {job.NewDir} {job.TempDir}", job, CancellationToken.None);
                    job.Log.AppendLine($"      ↩ 文件已还原为旧版本（运行中的容器自始至终未受影响）");
                }
            }
            else
            {
                if (job.FilesSwapped)
                {
                    var leafName = Path.GetFileName(job.TargetDir.TrimEnd('\\', '/'));
                    await ExecuteSshAsync(ssh,
                        $"powershell -Command \"" +
                        $"if (Test-Path '{job.TargetDir}') {{ Rename-Item '{job.TargetDir}' '{leafName}.new' -Force }}; " +
                        $"if (Test-Path '{job.OldDir}') {{ Rename-Item '{job.OldDir}' '{leafName}' -Force }}; " +
                        $"Remove-Item '{job.NewDir}' -Recurse -Force -ErrorAction SilentlyContinue; " +
                        $"Remove-Item '{job.TempDir}' -Recurse -Force -ErrorAction SilentlyContinue\"", job, CancellationToken.None);
                    job.Log.AppendLine($"      ↩ 文件已还原为旧版本");
                }
                if (job.ServiceStopped && !string.IsNullOrEmpty(job.ServiceName))
                {
                    await ExecuteSshAsync(ssh,
                        $"powershell -Command \"Start-Service -Name '{job.ServiceName}' -ErrorAction SilentlyContinue\"", job, CancellationToken.None);
                    job.Log.AppendLine($"      ↩ 服务已用旧版本重新启动");
                }
            }
            ssh.Disconnect();
            job.Log.AppendLine($"===== ↩ 环境已还原到部署前状态 =====");
        }
        catch (Exception ex)
        {
            job.Log.AppendLine($"      ⚠ 自动还原失败: {ex.Message}");
            job.Log.AppendLine($"      手动还原参考：正式目录 {job.TargetDir}，旧版本备份 {job.OldDir}");
        }
    }

    /// <summary>清理远程 .new/.tmp 临时残留（取消发生在解压阶段时调用）。</summary>
    private async Task TryCleanupRemoteAsync(DeployJob job, DeployRequest request)
    {
        try
        {
            var connectionInfo = new Renci.SshNet.ConnectionInfo(
                request.Host.Trim(),
                request.UserName.Trim(),
                new PasswordAuthenticationMethod(request.UserName.Trim(), request.Password));
            using var ssh = new SshClient(connectionInfo);
            ssh.Connect();
            if (job.TargetOS == DeployTargetOS.Linux)
                await ExecuteSshAsync(ssh, $"rm -rf {job.NewDir} {job.TempDir} 2>/dev/null", job, CancellationToken.None);
            else
                await ExecuteSshAsync(ssh,
                    $"powershell -Command \"Remove-Item '{job.NewDir}','{job.TempDir}' -Recurse -Force -ErrorAction SilentlyContinue\"", job, CancellationToken.None);
            ssh.Disconnect();
            job.Log.AppendLine($"      ✓ 清理完成");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "清理远程临时文件失败: {NewDir}", job.NewDir);
            job.Log.AppendLine($"      ⚠ 临时文件清理失败，可稍后手动删除：{job.NewDir}、{job.TempDir}");
        }
    }

    /// <summary>记录当前步骤并输出阶段标题（统一按 [N/7] 格式显示）。</summary>
    private static void SetStep(DeployJob job, int step, string title)
    {
        job.CurrentStep = step;
        job.Log.AppendLine($"[{step}/7] {title}...");
    }

    /// <summary>记录远程路径到任务（供取消回滚使用）。</summary>
    private static void RecordRemotePaths(DeployJob job, string targetDir, string newDir, string oldDir, string tempDir, string serviceName)
    {
        job.TargetDir = targetDir;
        job.NewDir = newDir;
        job.OldDir = oldDir;
        job.TempDir = tempDir;
        job.ServiceName = serviceName;
    }

    // ============================ 辅助方法 ============================

    /// <summary>根据构建类型确定 Linux 部署目标路径和 Docker 服务名。</summary>
    private static (string serviceName, string remoteTargetDir) GetLinuxDeployTarget(UniversalBuildType buildType, string deployBase)
    {
        return buildType switch
        {
            UniversalBuildType.Web or UniversalBuildType.Node => ("web", $"{deployBase}/web/wwwroot"),
            UniversalBuildType.DotNet => ("api", $"{deployBase}/api"),
            UniversalBuildType.Installer => ("", "/data/desktop-packages"),
            _ => ("", $"{deployBase}/{buildType.ToString().ToLowerInvariant()}"),
        };
    }

    /// <summary>根据构建类型确定 Windows 部署目标路径和服务名。</summary>
    private static (string serviceName, string remoteTargetDir) GetWindowsDeployTarget(UniversalBuildType buildType, string deployBase)
    {
        return buildType switch
        {
            UniversalBuildType.Web or UniversalBuildType.Node => ("", $@"{deployBase}\web\wwwroot"),
            UniversalBuildType.DotNet => ("ConvenientSystem.Api", $@"{deployBase}\api"),
            UniversalBuildType.Installer => ("", @"D:\data\desktop-packages"),
            _ => ("", $@"{deployBase}\{buildType}"),
        };
    }

    /// <summary>部署完成后的站点访问地址（Linux 统一走 80 入口，Windows 按服务类型区分端口）。</summary>
    private static string GetSiteUrl(DeployRequest request, string serviceName)
    {
        var host = request.Host.Trim();
        if (request.TargetOS == DeployTargetOS.Windows)
            return serviceName == "ConvenientSystem.Api" ? $"http://{host}:51943" : $"http://{host}:8080";
        return $"http://{host}";
    }

    private static void CreateTarGz(string sourceDir, string archivePath, CancellationToken token)
    {
        if (File.Exists(archivePath)) File.Delete(archivePath);

        // 使用系统 tar 命令打包（Windows 10+ 自带 tar）；循环等待以支持中途取消
        var psi = new ProcessStartInfo
        {
            FileName = "tar",
            Arguments = $"-czf \"{archivePath}\" -C \"{sourceDir}\" .",
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using var proc = Process.Start(psi);
        if (proc is null) throw new InvalidOperationException("无法启动 tar 命令");
        while (!proc.WaitForExit(1000))
        {
            if (token.IsCancellationRequested)
            {
                try { proc.Kill(); } catch { /* ignore */ }
                token.ThrowIfCancellationRequested();
            }
        }
        if (proc.ExitCode != 0)
        {
            var err = proc.StandardError.ReadToEnd();
            throw new InvalidOperationException($"tar 打包失败: {err}");
        }
    }

    private static void CreateZip(string sourceDir, string archivePath)
    {
        if (File.Exists(archivePath)) File.Delete(archivePath);

        ZipFile.CreateFromDirectory(sourceDir, archivePath, CompressionLevel.Optimal, true);
    }

    private static async Task ExecuteSshAsync(SshClient client, string command, DeployJob job, CancellationToken token)
    {
        job.Log.AppendLine($"$ {command}");
        // CreateCommand 只创建不执行；RunCommand 会先同步执行一遍，叠加 BeginExecute 等于同一条命令跑两遍
        // （mv 等非幂等命令会被第二遍执行破坏，历史故障根因）
        var cmd = client.CreateCommand(command);
        var asyncResult = cmd.BeginExecute();
        using var outputReader = new StreamReader(cmd.OutputStream, Encoding.UTF8);
        using var errorReader = new StreamReader(cmd.ExtendedOutputStream, Encoding.UTF8);
        try
        {
            while (!asyncResult.IsCompleted)
            {
                token.ThrowIfCancellationRequested();
                var output = await outputReader.ReadToEndAsync();
                var error = await errorReader.ReadToEndAsync();
                if (!string.IsNullOrEmpty(output)) job.Log.Append(output);
                if (!string.IsNullOrEmpty(error)) job.Log.Append(error);
                await Task.Delay(500, token);
            }
            cmd.EndExecute(asyncResult);
            var remainingOutput = await outputReader.ReadToEndAsync();
            var remainingError = await errorReader.ReadToEndAsync();
            if (!string.IsNullOrEmpty(remainingOutput)) job.Log.Append(remainingOutput);
            if (!string.IsNullOrEmpty(remainingError)) job.Log.Append(remainingError);
        }
        catch (OperationCanceledException)
        {
            // 中断远端命令执行后向上抛出取消信号
            try { cmd.CancelAsync(); } catch { /* ignore */ }
            throw;
        }

        if (cmd.ExitStatus != 0)
            throw new InvalidOperationException($"命令执行失败，退出码: {cmd.ExitStatus}");
    }

    private static string ExecuteSshCommand(SshClient client, string command)
    {
        // CreateCommand + Execute 只执行一次（RunCommand 内部已执行过，再调 Execute 会跑两遍）
        var cmd = client.CreateCommand(command);
        cmd.Execute();
        return (cmd.Result ?? string.Empty) + (cmd.Error ?? string.Empty);
    }

    private static void EnsureRemoteDirectory(SftpClient client, string remoteDirectory)
    {
        if (string.IsNullOrWhiteSpace(remoteDirectory) || remoteDirectory == "/") return;
        var parts = remoteDirectory.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = "/";
        foreach (var part in parts)
        {
            current += part + "/";
            if (!client.Exists(current))
                client.CreateDirectory(current);
        }
    }

    // ============================ 部署历史读写 ============================

    /// <summary>读取部署历史文件（不存在或损坏时从空列表开始）。</summary>
    private void LoadHistory()
    {
        try
        {
            if (!File.Exists(HistoryFilePath)) return;
            _history = JsonSerializer.Deserialize<List<DeployHistoryItem>>(File.ReadAllText(HistoryFilePath), HistoryJsonOptions) ?? new();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取部署历史失败");
        }
    }

    /// <summary>任务结束时记录一条历史（只存摘要，不存日志）。</summary>
    private void RecordHistory(DeployJob job)
    {
        try
        {
            lock (_historyLock)
            {
                _history.Add(new DeployHistoryItem
                {
                    BuildName = job.BuildName,
                    BuildType = job.BuildType,
                    TargetOS = job.TargetOS,
                    SiteName = job.SiteName,
                    Host = job.Host,
                    Status = job.Status,
                    StartTime = job.StartTime,
                    CompletedTime = job.CompletedTime,
                    DurationSeconds = (job.CompletedTime ?? DateTime.Now).Subtract(job.StartTime).TotalSeconds,
                });
                if (_history.Count > MaxHistoryItems)
                    _history = _history.OrderByDescending(h => h.StartTime).Take(MaxHistoryItems).ToList();
                File.WriteAllText(HistoryFilePath, JsonSerializer.Serialize(_history, HistoryJsonOptions));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "保存部署历史失败");
        }
    }

    /// <summary>获取部署历史（按时间倒序）。</summary>
    public IReadOnlyList<DeployHistoryItem> GetHistory()
    {
        lock (_historyLock)
            return _history.OrderByDescending(h => h.StartTime).ToList();
    }

    private static void Fail(DeployJob job, string message)
    {
        job.Status = DeployStatus.Failed;
        job.CompletedTime = DateTime.Now;
        job.Log.AppendLine();
        job.Log.AppendLine($"===== ❌ {message} =====");
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / 1024.0 / 1024.0:F1} MB";
    }

    private static DeployJobDto ToDto(DeployJob job)
    {
        return new DeployJobDto
        {
            Id = job.Id,
            BuildName = job.BuildName,
            BuildType = job.BuildType,
            TargetOS = job.TargetOS,
            SiteName = job.SiteName,
            Host = job.Host,
            Status = job.Status,
            StartTime = job.StartTime,
            CompletedTime = job.CompletedTime,
            Log = job.Log.ToString(),
        };
    }
}

// ============================ DTO ============================

public enum DeployTargetOS
{
    Linux,
    Windows,
}

public enum DeployStatus
{
    Running,
    Success,
    Failed,
    Cancelled,
}

public sealed class DeployJob
{
    public string Id { get; set; } = string.Empty;
    public string BuildName { get; set; } = string.Empty;
    public UniversalBuildType BuildType { get; set; }
    public DeployTargetOS TargetOS { get; set; }
    public string SiteName { get; set; } = string.Empty;
    /// <summary>目标服务器地址（历史记录用）。</summary>
    public string Host { get; set; } = string.Empty;
    public DeployStatus Status { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? CompletedTime { get; set; }
    public StringBuilder Log { get; set; } = new();
    /// <summary>取消信号源。</summary>
    public CancellationTokenSource? Cts { get; set; }
    /// <summary>当前执行步骤（1-7），用于取消日志定位。</summary>
    public int CurrentStep { get; set; }
    /// <summary>正式目录是否已切换为新版本（取消时需换回 .old）。</summary>
    public bool FilesSwapped { get; set; }
    /// <summary>Windows 服务是否已停止（取消时需重启）。</summary>
    public bool ServiceStopped { get; set; }
    /// <summary>是否处于临界区（启停容器/服务），期间拒绝取消。</summary>
    public bool InCriticalSection { get; set; }
    /// <summary>新版本是否已生效（容器/服务已切换），此后取消不再回滚。</summary>
    public bool Committed { get; set; }
    /// <summary>远程正式目录（回滚用）。</summary>
    public string TargetDir { get; set; } = string.Empty;
    /// <summary>远程新文件临时目录（回滚用）。</summary>
    public string NewDir { get; set; } = string.Empty;
    /// <summary>远程旧版本备份目录（回滚用）。</summary>
    public string OldDir { get; set; } = string.Empty;
    /// <summary>远程上传临时目录（回滚用）。</summary>
    public string TempDir { get; set; } = string.Empty;
    /// <summary>远程服务名（回滚重启服务用）。</summary>
    public string ServiceName { get; set; } = string.Empty;
}

public sealed class DeployRequest
{
    /// <summary>构建产物目录（来自卡片的 outputDir）。</summary>
    public string OutputDir { get; set; } = string.Empty;
    /// <summary>构建任务名称（用于日志标识）。</summary>
    public string BuildName { get; set; } = string.Empty;
    /// <summary>构建类型（仅用于预填默认值，不影响部署逻辑）。</summary>
    public UniversalBuildType BuildType { get; set; }
    /// <summary>Docker 服务名（用户输入，如 api / web / convenient-api）。留空则按构建类型自动推断。</summary>
    public string ServiceName { get; set; } = string.Empty;
    /// <summary>远程目标目录（用户输入，解压产物的路径）。留空则按构建类型自动推断。</summary>
    public string RemoteDir { get; set; } = string.Empty;
    /// <summary>压缩包名称（用户可自定义）。留空则用 {站点名}-{服务名}.tar.gz，每次覆盖上一次。</summary>
    public string ArchiveName { get; set; } = string.Empty;
    /// <summary>目标操作系统。</summary>
    public DeployTargetOS TargetOS { get; set; } = DeployTargetOS.Linux;
    /// <summary>站点名称（Docker Compose 项目名，用于多站点隔离）。</summary>
    public string SiteName { get; set; } = "convenient";
    /// <summary>服务器地址。</summary>
    public string Host { get; set; } = string.Empty;
    /// <summary>SSH 用户名。</summary>
    public string UserName { get; set; } = string.Empty;
    /// <summary>SSH 密码。</summary>
    public string Password { get; set; } = string.Empty;
    /// <summary>远程部署路径（留空则使用默认路径）。</summary>
    public string DeployPath { get; set; } = string.Empty;
    /// <summary>部署后是否验证健康检查。</summary>
    public bool VerifyHealth { get; set; } = true;
    /// <summary>是否保留数据库容器（不重启）。</summary>
    public bool KeepDatabase { get; set; } = true;
}

public sealed class DeployJobDto
{
    public string Id { get; set; } = string.Empty;
    public string BuildName { get; set; } = string.Empty;
    public UniversalBuildType BuildType { get; set; }
    public DeployTargetOS TargetOS { get; set; }
    public string SiteName { get; set; } = string.Empty;
    /// <summary>目标服务器地址。</summary>
    public string Host { get; set; } = string.Empty;
    public DeployStatus Status { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? CompletedTime { get; set; }
    public string Log { get; set; } = string.Empty;
}

/// <summary>部署历史记录条目（JSON 持久化，只存摘要不存日志）。</summary>
public sealed class DeployHistoryItem
{
    public string BuildName { get; set; } = string.Empty;
    public UniversalBuildType BuildType { get; set; }
    public DeployTargetOS TargetOS { get; set; }
    public string SiteName { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public DeployStatus Status { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? CompletedTime { get; set; }
    /// <summary>总耗时（秒）。</summary>
    public double DurationSeconds { get; set; }
}

public sealed class CheckSiteExistsRequest
{
    public DeployTargetOS TargetOS { get; set; }
    public string SiteName { get; set; } = "convenient";
    public string ServiceName { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public UniversalBuildType BuildType { get; set; }
}

public sealed class SiteExistsResult
{
    public bool Exists { get; set; }
    public string Message { get; set; } = string.Empty;
}
