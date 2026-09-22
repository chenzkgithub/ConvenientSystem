using System.Diagnostics;
using System.Windows.Forms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem;

/// <summary>
/// 本地宿主信息接口：前端启动时探测宿主身份与能力（接口分离的锚点）。
/// 仅桌面端 Kestrel 注册本控制器；服务器端对 /api/local/* 一律返回 410。
/// </summary>
[ApiController]
[Route("api/local/host")]
[AllowAnonymous]
public class LocalHostController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public LocalHostController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>宿主探测：前端据此确认运行于桌面端环境（hostContext 的 primary 校验源）。</summary>
    [HttpGet]
    [Route("ping")]
    public object Ping()
        => new { hostKind = "desktop", appVersion = GetAppVersion() };

    /// <summary>
    /// 宿主能力集：hostContext 启动时拉取一次，供宿主徽标与双宿主视图（考勤引导页等）按能力渲染。
    /// localDbConfigured=内网考勤库（YhSystemDb）连接串已配置；gitAvailable=本机可执行 git。
    /// </summary>
    [HttpGet]
    [Route("capabilities")]
    public object Capabilities()
        => new
        {
            hostKind = "desktop",
            appVersion = GetAppVersion(),
            localDbConfigured = !string.IsNullOrWhiteSpace(_configuration.GetConnectionString("YhSystemDb")),
            gitAvailable = ProbeGitAvailable(),
        };

    /// <summary>探测本机 git 可执行（git --version，3s 超时按不可用处理）。</summary>
    private static bool ProbeGitAvailable()
    {
        try
        {
            using var p = new Process();
            p.StartInfo = new ProcessStartInfo("git")
            {
                Arguments = "--version",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            p.Start();
            if (!p.WaitForExit(3000))
            {
                try { p.Kill(); } catch { /* 进程已退出 */ }
                return false;
            }
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string GetAppVersion()
    {
        try
        {
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(Application.ExecutablePath);
            return info.ProductVersion ?? info.FileVersion ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }
}
