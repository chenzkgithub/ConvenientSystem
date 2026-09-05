using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ConvenientSystem.Desktop;

/// <summary>
/// Web 前端 UI 状态持久化接口：模板/卡片配置等非敏感数据，
/// 由前端按 key 整体读写（value 为前端序列化好的 JSON 字符串）。
/// </summary>
[ApiController]
[Route("api/Common/UiState")]
public class UiStateController : ControllerBase
{
    private readonly UiStateStore _store;
    private readonly ILogger<UiStateController> _logger;

    public UiStateController(UiStateStore store, ILogger<UiStateController> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>读取指定键的值；不存在时 Value 为 null。</summary>
    [HttpPost]
    [Route("Get")]
    public UiStateValueResult Get([FromQuery] string key)
        => new() { Value = _store.Get(key) };

    /// <summary>保存（覆盖）指定键。</summary>
    [HttpPost]
    [Route("Set")]
    public IActionResult Set([FromBody] UiStateSetRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Key))
            return BadRequest(new { message = "key 不能为空" });
        _store.Set(request.Key, request.Value);
        return Ok();
    }

    /// <summary>删除指定键。</summary>
    [HttpPost]
    [Route("Remove")]
    public IActionResult Remove([FromQuery] string key)
    {
        _store.Remove(key ?? string.Empty);
        return Ok();
    }
}

public sealed class UiStateValueResult
{
    public string? Value { get; set; }
}

public sealed class UiStateSetRequest
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
