using ConvenientSystem.Service.Common;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem;

/// <summary>
/// 统一异步任务查询接口（本机）：查询本地 AsyncTaskCenter 登记的后台任务（ApiSpec 扫描/生成等）。
/// 本地任务无登录用户（userId=null）故免认证（同其他本地控制器）；进度无 SignalR 推送，前端轮询本接口。
/// 云端任务（Apifox 导入/删除）不在此查询——前端按任务类型路由到云端接口（经反向代理转发）。
/// </summary>
[ApiController]
// 本地接口新路由（接口分离）：与旧路由并存过渡，前端全部切换后移除旧路由
[Route("api/local/async-task")]
[Route("api/Common/AsyncTask")]
public class AsyncTaskController : ControllerBase
{
    private readonly AsyncTaskCenter _center;

    public AsyncTaskController(AsyncTaskCenter center)
    {
        _center = center;
    }

    /// <summary>查询单个任务进度（轻量快照，不含 Result）；任务不存在、已过期（完成超 30 分钟）返回 404。</summary>
    [HttpGet]
    [Route("Get")]
    public IActionResult Get([FromQuery] Guid taskId)
    {
        var task = _center.GetTask(taskId, null);
        return task is null ? NotFound(new { message = "任务不存在或已过期" }) : Ok(task);
    }
    
    /// <summary>读取已完成任务的完整 Result（生成任务可达数 MB，轮询不携带，页面终态后按需拉取）；
    /// 任务不存在、未完成或已过期返回 404。</summary>
    [HttpGet]
    [Route("GetResult")]
    public IActionResult GetResult([FromQuery] Guid taskId)
    {
        var task = _center.GetResult(taskId, null);
        return task is null ? NotFound(new { message = "任务不存在、未完成或已过期" }) : Ok(task);
    }

    /// <summary>本机全部未清理任务（运行中 + 完成未超 30 分钟），页面刷新后恢复任务面板用。</summary>
    [HttpGet]
    [Route("GetAll")]
    public IActionResult GetAll()
        => Ok(_center.GetTasks(null));
}
