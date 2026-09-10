using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ConvenientSystem;

/// <summary>通用构建发布接口。</summary>
[ApiController]
[Route("api/Common/UniversalBuild")]
public class UniversalBuildController : ControllerBase
{
    private readonly UniversalBuildService _buildService;
    private readonly DeployService _deployService;
    private readonly UniversalScheduleService _scheduleService;
    private readonly SshCredentialStore _credentialStore;
    private readonly ILogger<UniversalBuildController> _logger;

    public UniversalBuildController(
        UniversalBuildService buildService,
        DeployService deployService,
        UniversalScheduleService scheduleService,
        SshCredentialStore credentialStore,
        ILogger<UniversalBuildController> logger)
    {
        _buildService = buildService;
        _deployService = deployService;
        _scheduleService = scheduleService;
        _credentialStore = credentialStore;
        _logger = logger;
    }

    /// <summary>检测全部环境。</summary>
    [HttpPost]
    [Route("Environment")]
    public IReadOnlyList<UniversalEnvironmentInfo> Environment()
        => _buildService.CheckEnvironment();

    /// <summary>检测指定类型所需环境。</summary>
    [HttpPost]
    [Route("EnvironmentForType")]
    public IReadOnlyList<UniversalEnvironmentInfo> EnvironmentForType([FromBody] EnvironmentForTypeRequest request)
        => _buildService.CheckEnvironmentForType(request.Type);

    /// <summary>启动构建任务。</summary>
    [HttpPost]
    [Route("Build")]
    public UniversalBuildJobDto Build([FromBody] UniversalBuildRequest request)
        => _buildService.StartBuild(request);

    /// <summary>获取任务进度。</summary>
    [HttpPost]
    [Route("Progress")]
    public UniversalBuildJobDto? Progress([FromBody] ProgressRequest request)
        => _buildService.GetProgress(request.Id);

    /// <summary>获取所有任务。</summary>
    [HttpPost]
    [Route("AllJobs")]
    public IReadOnlyList<UniversalBuildJobDto> AllJobs()
        => _buildService.GetAllJobs();

    /// <summary>取消任务。</summary>
    [HttpPost]
    [Route("Cancel")]
    public IActionResult Cancel([FromBody] CancelRequest request)
    {
        var ok = _buildService.Cancel(request.Id);
        return ok ? Ok() : NotFound();
    }

    /// <summary>获取默认输出目录。</summary>
    [HttpPost]
    [Route("DefaultOutputDir")]
    public string DefaultOutputDir([FromBody] DefaultOutputDirRequest request)
        => UniversalBuildService.GetDefaultOutputDir(request.Type, request.Name);

    /// <summary>检查远程站点/服务是否已存在。</summary>
    [HttpPost]
    [Route("CheckSiteExists")]
    public SiteExistsResult CheckSiteExists([FromBody] CheckSiteExistsRequest request)
        => _deployService.CheckSiteExists(request);

    /// <summary>启动部署任务：把构建产物打包部署到远程服务器。</summary>
    [HttpPost]
    [Route("Deploy")]
    public DeployJobDto Deploy([FromBody] DeployRequest request)
    {
        var jobId = _deployService.StartDeploy(request);
        return _deployService.GetJob(jobId)!;
    }

    /// <summary>查询部署任务进度。</summary>
    [HttpPost]
    [Route("DeployProgress")]
    public DeployJobDto? DeployProgress([FromBody] DeployProgressRequest request)
        => _deployService.GetJob(request.Id);

    /// <summary>手动回滚：把最近一次部署的 .old 备份换回正式目录并重启服务。</summary>
    [HttpPost]
    [Route("Rollback")]
    public DeployJobDto Rollback([FromBody] RollbackRequest request)
    {
        var jobId = _deployService.StartRollback(request);
        return _deployService.GetJob(jobId)!;
    }

    /// <summary>取消部署任务：中断执行并自动还原部署前环境。</summary>
    [HttpPost]
    [Route("DeployCancel")]
    public IActionResult DeployCancel([FromBody] DeployCancelRequest request)
    {
        var (ok, message) = _deployService.Cancel(request.Id);
        return ok ? Ok(new { message }) : BadRequest(new { message });
    }

    /// <summary>
    /// 弹出文件夹选择对话框，返回用户选择的目录路径。
    /// initialDir 有值时对话框从该路径打开（输入框已填路径则就近定位，免每次从头翻）；
    /// 是文件路径时自动取其所在目录；不存在则忽略。
    /// </summary>
    [HttpPost]
    [Route("SelectFolder")]
    public string? SelectFolder([FromQuery] string? initialDir)
    {
        string? selectedPath = null;
        var mainForm = Application.OpenForms.Count > 0 ? Application.OpenForms[0] : null;
        mainForm?.Invoke(() =>
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "选择目录",
                ShowNewFolderButton = false,
                SelectedPath = ResolveInitialDir(initialDir),
            };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                selectedPath = dialog.SelectedPath;
            }
        });
        return selectedPath;
    }

    /// <summary>弹出 SQL 文件选择对话框，返回选中的文件路径；取消返回 null。initialDir 语义同 SelectFolder。</summary>
    [HttpPost]
    [Route("SelectSqlFile")]
    public string? SelectSqlFile([FromQuery] string? initialDir)
    {
        string? selected = null;
        var mainForm = Application.OpenForms.Count > 0 ? Application.OpenForms[0] : null;
        mainForm?.Invoke(() =>
        {
            using var dialog = new OpenFileDialog
            {
                Title = "选择 SQL 脚本文件",
                Filter = "SQL 脚本 (*.sql)|*.sql|所有文件 (*.*)|*.*",
                Multiselect = false,
                InitialDirectory = ResolveInitialDir(initialDir),
            };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                selected = dialog.FileName;
            }
        });
        return selected;
    }

    /// <summary>初始目录解析：目录直接用；文件取所在目录；不存在返回空（对话框用默认位置）。</summary>
    private static string ResolveInitialDir(string? initialDir)
    {
        if (string.IsNullOrWhiteSpace(initialDir)) return string.Empty;
        var path = initialDir.Trim();
        if (Directory.Exists(path)) return path;
        if (System.IO.File.Exists(path))
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
        }
        return string.Empty;
    }

    /// <summary>
    /// 独立打压缩包：把本地文件夹打包成 zip（与构建流程解耦，随时可打）。
    /// targetZip 留空 = 源文件夹同级目录，命名 {文件夹名}_{时间戳}.zip 不覆盖旧包。
    /// </summary>
    [HttpPost]
    [Route("PackFolder")]
    public IActionResult PackFolder([FromBody] PackFolderRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SourceDir))
            return BadRequest(new { message = "请填写源文件夹路径" });
        try
        {
            var (path, size) = UniversalBuildService.PackFolderToZip(request.SourceDir, request.TargetZip);
            return Ok(new { path, size });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "打压缩包失败: {SourceDir}", request.SourceDir);
            return BadRequest(new { message = $"打包失败：{ex.Message}" });
        }
    }

    /// <summary>在资源管理器中打开指定目录；传文件路径时打开所在目录并选中该文件（构建日志里的压缩包定位）。</summary>
    [HttpPost]
    [Route("OpenFolder")]
    public IActionResult OpenFolder([FromQuery] string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return BadRequest(new { message = "路径不存在，请先完成构建" });
        var isDir = Directory.Exists(path);
        var isFile = !isDir && System.IO.File.Exists(path);
        if (!isDir && !isFile)
            return BadRequest(new { message = "路径不存在，请先完成构建" });
        try
        {
            // 文件：/select 打开所在目录并选中；目录：直接打开
            var args = isFile ? $"/select,\"{path}\"" : $"\"{path}\"";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", args)
            {
                UseShellExecute = true,
            });
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "打开目录失败: {Path}", path);
            return BadRequest(new { message = "打开失败: " + ex.Message });
        }
    }

    /// <summary>查询部署历史（最近 100 条，按时间倒序）。</summary>
    [HttpPost]
    [Route("DeployHistory")]
    public IReadOnlyList<DeployHistoryItem> DeployHistory()
        => _deployService.GetHistory();

    /// <summary>读取部署任务的完整日志（仅内存中保留的任务，程序重启后旧条目不可查）。</summary>
    [HttpPost]
    [Route("DeployLog")]
    public IActionResult DeployLog([FromQuery] string jobId)
    {
        var job = _deployService.GetJob(jobId);
        if (job == null)
            return NotFound(new { message = "日志不存在（程序重启后旧日志会被清除，仅保留本次运行内的任务日志）" });
        return Ok(new DeployLogResult
        {
            Log = job.Log,
            BuildName = job.BuildName,
            Status = job.Status.ToString(),
            StartTime = job.StartTime,
            CompletedTime = job.CompletedTime,
        });
    }

    /// <summary>统计构建产物目录占用（大小/文件数/最后修改时间）。</summary>
    [HttpPost]
    [Route("ArtifactUsage")]
    public IReadOnlyList<ArtifactUsageItem> ArtifactUsage([FromBody] ArtifactUsageRequest request)
        => _buildService.GetArtifactUsage(request.Dirs);

    /// <summary>清空构建产物目录内容（保留目录本身，删除不可恢复）。</summary>
    [HttpPost]
    [Route("ArtifactClean")]
    public IActionResult ArtifactClean([FromBody] ArtifactCleanRequest request)
    {
        try
        {
            _buildService.CleanArtifact(request.Dir);
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "清理产物目录失败: {Dir}", request.Dir);
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>查询定时构建列表。</summary>
    [HttpPost]
    [Route("ScheduleList")]
    public IReadOnlyList<ScheduleItem> ScheduleList()
        => _scheduleService.GetSchedules();

    /// <summary>新增/更新定时构建。</summary>
    [HttpPost]
    [Route("ScheduleSet")]
    public IActionResult ScheduleSet([FromBody] ScheduleItem item)
    {
        try
        {
            return Ok(_scheduleService.UpsertSchedule(item));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>删除定时构建。</summary>
    [HttpPost]
    [Route("ScheduleRemove")]
    public IActionResult ScheduleRemove([FromQuery] string id)
        => _scheduleService.RemoveSchedule(id) ? Ok() : NotFound(new { message = "定时项不存在" });

    /// <summary>读取已保存的 SSH 密码（本机接口，供部署弹窗回填与自动部署取用）。</summary>
    [HttpPost]
    [Route("GetSshCredential")]
    public SshCredentialResult GetSshCredential([FromQuery] string host, [FromQuery] string userName)
        => new() { Password = _credentialStore.Get(host, userName) };

    /// <summary>删除已保存的 SSH 凭据。</summary>
    [HttpPost]
    [Route("RemoveSshCredential")]
    public IActionResult RemoveSshCredential([FromQuery] string host, [FromQuery] string userName)
        => _credentialStore.Remove(host, userName) ? Ok() : NotFound();

    /// <summary>保存 SSH 凭据（DPAPI 加密后落盘本机，供下次部署与自动部署复用）。</summary>
    [HttpPost]
    [Route("SaveSshCredential")]
    public IActionResult SaveSshCredential([FromBody] SshCredentialRequest request)
    {
        try
        {
            _credentialStore.Save(request.Host, request.UserName, request.Password);
            return Ok();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}

public sealed class EnvironmentForTypeRequest
{
    public UniversalBuildType Type { get; set; }
}

public sealed class ProgressRequest
{
    public string Id { get; set; } = string.Empty;
}

public sealed class CancelRequest
{
    public string Id { get; set; } = string.Empty;
}

public sealed class DefaultOutputDirRequest
{
    public UniversalBuildType Type { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>独立打压缩包请求。</summary>
public sealed class PackFolderRequest
{
    /// <summary>要打包的本地文件夹完整路径。</summary>
    public string SourceDir { get; set; } = string.Empty;
    /// <summary>目标 zip 完整路径（手填的明确覆盖语义）；空 = 源文件夹同级 {文件夹名}_{时间戳}.zip。</summary>
    public string? TargetZip { get; set; }
}

public sealed class DeployProgressRequest
{
    public string Id { get; set; } = string.Empty;
}

public sealed class DeployCancelRequest
{
    public string Id { get; set; } = string.Empty;
}

public sealed class SshCredentialRequest
{
    public string Host { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class SshCredentialResult
{
    /// <summary>未保存过或解密失败时为 null。</summary>
    public string? Password { get; set; }
}

/// <summary>部署任务完整日志（部署历史行查看入口用）。</summary>
public sealed class DeployLogResult
{
    public string Log { get; set; } = string.Empty;
    public string BuildName { get; set; } = string.Empty;
    /// <summary>任务状态（Success/Failed/Cancelled/Running）。</summary>
    public string Status { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime? CompletedTime { get; set; }
}

/// <summary>产物占用统计请求：目录列表。</summary>
public sealed class ArtifactUsageRequest
{
    public List<string> Dirs { get; set; } = new();
}

/// <summary>产物清理请求：清空指定目录内容（保留目录本身）。</summary>
public sealed class ArtifactCleanRequest
{
    public string Dir { get; set; } = string.Empty;
}
