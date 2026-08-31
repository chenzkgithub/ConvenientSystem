using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Desktop;

/// <summary>Git 代码管理工作台接口。</summary>
[ApiController]
[Route("api/Common/Git")]
public class GitController : ControllerBase
{
    private readonly GitService _gitService;
    private readonly ILogger<GitController> _logger;

    public GitController(GitService gitService, ILogger<GitController> logger)
    {
        _gitService = gitService;
        _logger = logger;
    }

    /// <summary>仓库列表（附带实时状态）。</summary>
    [HttpPost]
    [Route("Repos")]
    public IReadOnlyList<GitRepoStatusDto> Repos()
        => _gitService.GetReposWithStatus();

    /// <summary>添加仓库（自动解析仓库根目录，子目录自动归属）。</summary>
    [HttpPost]
    [Route("AddRepo")]
    public IActionResult AddRepo([FromBody] GitPathRequest request)
    {
        try
        {
            var result = _gitService.AddRepo(request.Path);
            return result.Ok ? Ok(result) : BadRequest(new { message = result.Message });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "添加仓库失败: {Path}", request.Path);
            return BadRequest(new { message = "添加失败: " + ex.Message });
        }
    }

    /// <summary>移除仓库（仅移除列表记录）。</summary>
    [HttpPost]
    [Route("RemoveRepo")]
    public IActionResult RemoveRepo([FromBody] GitPathRequest request)
        => _gitService.RemoveRepo(request.Path) ? Ok() : NotFound(new { message = "仓库不在列表中" });

    /// <summary>扫描一级子目录，发现其中的 Git 仓库。</summary>
    [HttpPost]
    [Route("Discover")]
    public IReadOnlyList<GitDiscoveredRepoDto> Discover([FromBody] GitPathRequest request)
        => _gitService.DiscoverRepos(request.Path);

    /// <summary>查询仓库状态总览。</summary>
    [HttpPost]
    [Route("Status")]
    public GitRepoStatusDto Status([FromBody] GitPathRequest request)
        => _gitService.GetStatus(request.Path);

    /// <summary>分支列表（本地 + 远程）。</summary>
    [HttpPost]
    [Route("Branches")]
    public IActionResult Branches([FromBody] GitPathRequest request)
    {
        try
        {
            return Ok(_gitService.GetBranches(request.Path));
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>拉取当前分支。</summary>
    [HttpPost]
    [Route("Pull")]
    public IActionResult Pull([FromBody] GitPathRequest request)
    {
        try { return Ok(_gitService.Pull(request.Path, request.OpId)); }
        catch (Exception ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>推送当前分支（无上游时自动建立跟踪）。</summary>
    [HttpPost]
    [Route("Push")]
    public IActionResult Push([FromBody] GitPathRequest request)
    {
        try { return Ok(_gitService.Push(request.Path, request.OpId)); }
        catch (Exception ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>合并来源分支到当前分支。</summary>
    [HttpPost]
    [Route("Merge")]
    public IActionResult Merge([FromBody] GitMergeRequest request)
    {
        try { return Ok(_gitService.Merge(request.Path, request.SourceBranch, request.OpId)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (Exception ex) { return BadRequest(new { message = "合并失败: " + ex.Message }); }
    }

    /// <summary>切换/新建分支。</summary>
    [HttpPost]
    [Route("Checkout")]
    public IActionResult Checkout([FromBody] GitCheckoutRequest request)
    {
        try { return Ok(_gitService.Checkout(request.Path, request.Branch, request.NewBranch, request.OpId)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (Exception ex) { return BadRequest(new { message = "切换失败: " + ex.Message }); }
    }

    /// <summary>白名单执行 git 命令（必须以 git 开头，参数直传不经 shell）。</summary>
    [HttpPost]
    [Route("Exec")]
    public IActionResult Exec([FromBody] GitExecRequest request)
    {
        try { return Ok(_gitService.Exec(request.Path, request.Command, request.OpId)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (TimeoutException ex) { return BadRequest(new { message = ex.Message }); }
        catch (Exception ex) { return BadRequest(new { message = "执行失败: " + ex.Message }); }
    }

    /// <summary>克隆远程仓库，成功后自动添加到仓库列表。</summary>
    [HttpPost]
    [Route("Clone")]
    public IActionResult CloneRepo([FromBody] GitCloneRequest request)
    {
        try { return Ok(_gitService.Clone(request.Url, request.ParentDir, request.DirName, request.OpId)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (TimeoutException ex) { return BadRequest(new { message = ex.Message }); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "克隆仓库失败: {Url}", request.Url);
            return BadRequest(new { message = "克隆失败: " + ex.Message });
        }
    }

    /// <summary>提交历史（新→旧，含父提交与 refs，前端画分支线）。</summary>
    [HttpPost]
    [Route("Log")]
    public IActionResult Log([FromBody] GitLogRequest request)
    {
        try { return Ok(_gitService.GetLog(request.Path, request.Branch, request.Keyword, request.Skip, request.Take)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (Exception ex) { return BadRequest(new { message = "获取历史失败: " + ex.Message }); }
    }

    /// <summary>单提交详情：元信息 + 变更文件 + 按文件切分的 diff。</summary>
    [HttpPost]
    [Route("Commit")]
    public IActionResult Commit([FromBody] GitCommitDetailRequest request)
    {
        try { return Ok(_gitService.GetCommitDetail(request.Path, request.Hash)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (Exception ex) { return BadRequest(new { message = "获取提交详情失败: " + ex.Message }); }
    }

    /// <summary>工作区改动列表（已暂存/未暂存两组，含未跟踪与冲突）。</summary>
    [HttpPost]
    [Route("Changes")]
    public IActionResult Changes([FromBody] GitPathRequest request)
    {
        try { return Ok(_gitService.GetChanges(request.Path)); }
        catch (Exception ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>暂存/取消暂存（单文件或全部）。</summary>
    [HttpPost]
    [Route("Stage")]
    public IActionResult Stage([FromBody] GitStageRequest request)
    {
        try { return Ok(_gitService.Stage(request.Path, request.FilePath, request.Stage)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (Exception ex) { return BadRequest(new { message = "暂存操作失败: " + ex.Message }); }
    }

    /// <summary>提交已暂存改动（可选顺带推送）。
    /// 注：路由用 CommitChanges 避开历史功能的提交详情端点 Commit。</summary>
    [HttpPost]
    [Route("CommitChanges")]
    public IActionResult CommitChanges([FromBody] GitCommitRequest request)
    {
        try { return Ok(_gitService.Commit(request.Path, request.Message, request.Push, request.OpId)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (Exception ex) { return BadRequest(new { message = "提交失败: " + ex.Message }); }
    }

    /// <summary>放弃改动（不可恢复，前端二次确认）。</summary>
    [HttpPost]
    [Route("Discard")]
    public IActionResult Discard([FromBody] GitDiscardRequest request)
    {
        try { return Ok(_gitService.Discard(request.Path, request.FilePath, request.IncludeUntracked)); }
        catch (Exception ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>单文件 diff 预览（已暂存/工作区，未跟踪合成 + 行）。</summary>
    [HttpPost]
    [Route("FileDiff")]
    public IActionResult FileDiff([FromBody] GitFileDiffRequest request)
    {
        try { return Ok(_gitService.GetFileDiff(request.Path, request.FilePath, request.Staged)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (Exception ex) { return BadRequest(new { message = "获取 diff 失败: " + ex.Message }); }
    }

    /// <summary>取消运行中操作：杀对应 git 进程树，原请求随 WaitForExit 返回。</summary>
    [HttpPost]
    [Route("Cancel")]
    public IActionResult Cancel([FromBody] GitCancelRequest request)
        => Ok(_gitService.Cancel(request.OpId));

    /// <summary>合并中间状态（合并进行中横幅 + 一键放弃）。</summary>
    [HttpPost]
    [Route("MergeState")]
    public GitMergeStateDto MergeState([FromBody] GitPathRequest request)
        => _gitService.GetMergeState(request.Path);

    /// <summary>Git 环境检测（git 是否已安装、版本、全局身份）。</summary>
    [HttpPost]
    [Route("Env")]
    public GitEnvDto Env() => _gitService.GetEnv();

    /// <summary>将当前未提交改动储藏（git stash push）。</summary>
    [HttpPost]
    [Route("Stash")]
    public IActionResult Stash([FromBody] GitStashRequest request)
    {
        try { return Ok(_gitService.Stash(request.Path, request.Message)); }
        catch (Exception ex) { return BadRequest(new { message = "储藏失败: " + ex.Message }); }
    }

    /// <summary>获取 Stash 列表。</summary>
    [HttpPost]
    [Route("StashList")]
    public IActionResult StashList([FromBody] GitPathRequest request)
    {
        try { return Ok(_gitService.GetStashList(request.Path)); }
        catch (Exception ex) { return BadRequest(new { message = "获取储藏列表失败: " + ex.Message }); }
    }

    /// <summary>应用指定 Stash（git stash pop）。</summary>
    [HttpPost]
    [Route("StashPop")]
    public IActionResult StashPop([FromBody] GitStashIndexRequest request)
    {
        try { return Ok(_gitService.StashPop(request.Path, request.Index)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (Exception ex) { return BadRequest(new { message = "应用储藏失败: " + ex.Message }); }
    }

    /// <summary>删除指定 Stash（git stash drop）。</summary>
    [HttpPost]
    [Route("StashDrop")]
    public IActionResult StashDrop([FromBody] GitStashIndexRequest request)
    {
        try { return Ok(_gitService.StashDrop(request.Path, request.Index)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (Exception ex) { return BadRequest(new { message = "删除储藏失败: " + ex.Message }); }
    }

    /// <summary>读取全局 git 配置列表。</summary>
    [HttpPost]
    [Route("ConfigList")]
    public List<GitConfigItemDto> ConfigList() => _gitService.GetConfigList();

    /// <summary>设置或删除一项全局配置（Value 为 null 时删除）。</summary>
    [HttpPost]
    [Route("ConfigSet")]
    public IActionResult ConfigSet([FromBody] GitConfigSetRequest request)
    {
        try { return Ok(_gitService.SetConfig(request.Key, request.Value)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (Exception ex) { return BadRequest(new { message = "配置失败: " + ex.Message }); }
    }
}
