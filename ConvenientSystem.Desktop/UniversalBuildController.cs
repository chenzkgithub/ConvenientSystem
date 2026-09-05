using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ConvenientSystem.Desktop;

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

    /// <summary>弹出文件夹选择对话框，返回用户选择的目录路径。</summary>
    [HttpPost]
    [Route("SelectFolder")]
    public string? SelectFolder()
    {
        string? selectedPath = null;
        var mainForm = Application.OpenForms.Count > 0 ? Application.OpenForms[0] : null;
        mainForm?.Invoke(() =>
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "选择项目目录",
                ShowNewFolderButton = false,
            };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                selectedPath = dialog.SelectedPath;
            }
        });
        return selectedPath;
    }

    /// <summary>在资源管理器中打开指定目录（构建输出目录）。</summary>
    [HttpPost]
    [Route("OpenFolder")]
    public IActionResult OpenFolder([FromQuery] string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return BadRequest(new { message = "目录不存在，请先完成构建" });
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{path}\"")
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
