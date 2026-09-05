using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ConvenientSystem.Desktop;

/// <summary>
/// 流水线接口：定义 CRUD + 启动/轮询/取消运行 + 运行历史。
/// 流水线 = 可保存复用的阶段序列（构建/部署），引擎复用通用构建与部署服务。
/// </summary>
[ApiController]
[Route("api/Common/Pipeline")]
public class PipelineController : ControllerBase
{
    private readonly PipelineStore _store;
    private readonly PipelineService _service;
    private readonly ILogger<PipelineController> _logger;

    public PipelineController(PipelineStore store, PipelineService service, ILogger<PipelineController> logger)
    {
        _store = store;
        _service = service;
        _logger = logger;
    }

    /// <summary>流水线定义列表。</summary>
    [HttpPost]
    [Route("List")]
    public IReadOnlyList<PipelineDefinition> List()
        => _store.GetAll();

    /// <summary>新增/更新流水线定义（按 Id 匹配）。</summary>
    [HttpPost]
    [Route("Save")]
    public IActionResult Save([FromBody] PipelineDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Name))
            return BadRequest(new { message = "流水线名称不能为空" });
        definition.Name = definition.Name.Trim();
        foreach (var stage in definition.Stages)
        {
            if (string.IsNullOrWhiteSpace(stage.Name))
                return BadRequest(new { message = "阶段名称不能为空" });
            stage.Name = stage.Name.Trim();
            if (stage.Type == PipelineStageType.Build && string.IsNullOrWhiteSpace(stage.ProjectDir))
                return BadRequest(new { message = $"阶段【{stage.Name}】未配置项目目录" });
            if (stage.Type == PipelineStageType.Deploy && string.IsNullOrWhiteSpace(stage.Host))
                return BadRequest(new { message = $"阶段【{stage.Name}】未配置服务器地址" });
            if (stage.Type == PipelineStageType.Sql && string.IsNullOrWhiteSpace(stage.ConnectionString))
                return BadRequest(new { message = $"阶段【{stage.Name}】未配置数据库连接串" });
        }
        return Ok(_store.Save(definition));
    }

    /// <summary>删除流水线定义。</summary>
    [HttpPost]
    [Route("Remove")]
    public IActionResult Remove([FromQuery] string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return BadRequest(new { message = "id 不能为空" });
        return _store.Remove(id)
            ? Ok()
            : BadRequest(new { message = "流水线不存在（可能已被删除）" });
    }

    /// <summary>启动一次流水线运行。</summary>
    [HttpPost]
    [Route("Start")]
    public IActionResult Start([FromBody] PipelineStartRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PipelineId))
            return BadRequest(new { message = "pipelineId 不能为空" });
        return Ok(_service.StartRun(request.PipelineId));
    }

    /// <summary>查询单次运行详情（含汇总日志）。</summary>
    [HttpPost]
    [Route("Run")]
    public IActionResult Run([FromQuery] string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return BadRequest(new { message = "id 不能为空" });
        var run = _service.GetRun(id);
        return run == null ? NotFound(new { message = "运行记录不存在" }) : Ok(run);
    }

    /// <summary>查询运行历史（默认最近 30 条，按开始时间倒序）。</summary>
    [HttpPost]
    [Route("Runs")]
    public IReadOnlyList<PipelineRunDto> Runs([FromQuery] string? pipelineId, [FromQuery] int limit = 30)
        => _service.GetRuns(pipelineId, Math.Clamp(limit, 1, 100));

    /// <summary>取消运行中的流水线（部署阶段取消会自动还原部署前环境）。</summary>
    [HttpPost]
    [Route("CancelRun")]
    public IActionResult CancelRun([FromBody] PipelineCancelRunRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RunId))
            return BadRequest(new { message = "runId 不能为空" });
        return _service.CancelRun(request.RunId)
            ? Ok(new { message = "已发送取消请求" })
            : BadRequest(new { message = "取消失败：运行不存在或已结束" });
    }
}

public sealed class PipelineStartRequest
{
    public string PipelineId { get; set; } = string.Empty;
}

public sealed class PipelineCancelRunRequest
{
    public string RunId { get; set; } = string.Empty;
}
