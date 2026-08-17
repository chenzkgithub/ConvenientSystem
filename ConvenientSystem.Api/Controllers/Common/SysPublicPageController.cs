using ConvenientSystem.Api.Auth;
using ConvenientSystem.Shared.Model.Common;
using ConvenientSystem.Service.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 外部公开页面控制器：管理免登录（standalone=1）可直接访问的公开页面配置。
    /// ListEnabled 无需鉴权（前端启动时注册路由用），其余方法需要 "sys-public-page" 权限。
    /// </summary>
    [Area("Common")]
    public class SysPublicPageController : BaseController
    {
        private readonly ISysPublicPageService _pageService;

        public SysPublicPageController(ISysPublicPageService pageService)
        {
            _pageService = pageService;
        }

        /// <summary>获取启用的公开页面（无需登录，前端路由注册用）</summary>
        [HttpGet]
        [AllowAnonymous]
        public ActionResult<List<SysPublicPageItemDto>> ListEnabled()
            => Ok(_pageService.ListEnabled());

        /// <summary>获取全部公开页面（管理列表）</summary>
        [HttpGet]
        [PermissionAuthorize("sys-public-page")]
        public ActionResult<List<SysPublicPageItemDto>> GetAll()
            => Ok(_pageService.GetAll());

        /// <summary>新增公开页面</summary>
        [HttpPost]
        [PermissionAuthorize("sys-public-page")]
        public IActionResult Create([FromBody] SysPublicPageCreateDto dto)
        {
            try
            {
                var id = _pageService.Create(dto);
                return Ok(new { id, message = "创建成功" });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>编辑公开页面</summary>
        [HttpPut]
        [PermissionAuthorize("sys-public-page")]
        public IActionResult Update([FromBody] SysPublicPageUpdateDto dto)
        {
            try
            {
                _pageService.Update(dto);
                return Ok(new { message = "更新成功" });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>删除公开页面</summary>
        [HttpDelete]
        [PermissionAuthorize("sys-public-page")]
        public IActionResult Delete(int id)
        {
            _pageService.Delete(id);
            return Ok(new { message = "删除成功" });
        }
    }
}
