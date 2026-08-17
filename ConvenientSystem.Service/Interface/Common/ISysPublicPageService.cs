using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 外部公开页面服务：管理免登录（standalone=1）可直接访问的公开页面配置。
    /// ListEnabled 无需鉴权（前端启动时注册路由用），其余方法需鉴权。
    /// </summary>
    public interface ISysPublicPageService
    {
        /// <summary>获取启用的公开页面（无需鉴权，前端路由注册用）</summary>
        List<SysPublicPageItemDto> ListEnabled();

        /// <summary>获取全部公开页面（管理用）</summary>
        List<SysPublicPageItemDto> GetAll();

        /// <summary>新增公开页面</summary>
        int Create(SysPublicPageCreateDto dto);

        /// <summary>编辑公开页面</summary>
        void Update(SysPublicPageUpdateDto dto);

        /// <summary>删除公开页面</summary>
        void Delete(int id);
    }
}
