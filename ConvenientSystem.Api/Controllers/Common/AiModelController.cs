using ConvenientSystem.Api.Auth;
using ConvenientSystem.Service.Ai;
using ConvenientSystem.Shared.Model.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// AI 模型管理接口：模型 CRUD / 连通测试 / 默认切换，复用 sys-config:save 权限
    /// （与系统配置页保存按钮同一权限码，管理员语义一致，无需新增授权点）。
    /// List 同样受 sys-config:save 保护（管理页专用；助手抽屉的模型选项由 Ai/MyStatus 提供）。
    /// </summary>
    [Area("Common")]
    [Authorize]
    [PermissionAuthorize("sys-config:save")]
    public class AiModelController : BaseController
    {
        private readonly IAiModelService _modelService;

        public AiModelController(IAiModelService modelService)
        {
            _modelService = modelService;
        }

        /// <summary>模型管理列表（含 HasKey，永不回传明文 Key）</summary>
        [HttpGet]
        public IActionResult List() => Ok(_modelService.List());

        /// <summary>新增/编辑模型（Id=0 新增；ApiKey 留空 = 保持原 Key 不变）</summary>
        [HttpPost]
        public IActionResult Save([FromBody] AiModelSaveRequest request) => Ok(_modelService.Save(request));

        /// <summary>连通性测试（保存草稿即可测：未保存的新增表单直接带值）</summary>
        [HttpPost]
        public IActionResult Test([FromBody] AiModelTestRequest request) => Ok(_modelService.Test(request));

        /// <summary>设为全局默认模型</summary>
        [HttpPost]
        public IActionResult SetDefault([FromQuery] int id)
        {
            _modelService.SetDefault(id);
            return Ok(new { message = "已设为默认模型" });
        }

        /// <summary>删除模型（删除默认后自动迁移默认到首个启用模型）</summary>
        [HttpDelete]
        public IActionResult Delete([FromQuery] int id)
        {
            _modelService.Delete(id);
            return Ok(new { message = "已删除" });
        }
    }
}
