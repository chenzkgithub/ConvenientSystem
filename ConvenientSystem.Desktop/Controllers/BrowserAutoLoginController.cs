using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem;

[ApiController]
[Route("api/local/browser-auto-login")]
[AllowAnonymous]
public sealed class BrowserAutoLoginController : ControllerBase
{
    private readonly BrowserAutoLoginCoordinator _coordinator;

    public BrowserAutoLoginController(BrowserAutoLoginCoordinator coordinator)
    {
        _coordinator = coordinator;
    }

    [HttpGet("ping")]
    public object Ping() => new { ready = true };

    [HttpPost("login")]
    public async Task<ActionResult<BrowserAutoLoginResult>> Login(
        [FromBody] BrowserAutoLoginRequest request,
        CancellationToken cancellationToken)
    {
        var origin = Request.Headers.Origin.ToString();
        if (origin.Length > 0 && !origin.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase))
            return StatusCode(StatusCodes.Status403Forbidden);

        return await _coordinator.LoginAsync(request.TargetUrl, cancellationToken);
    }
}

public sealed class BrowserAutoLoginRequest
{
    public string TargetUrl { get; set; } = string.Empty;
}
