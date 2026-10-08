using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 手机端权限业务：SysUserAppPerm 白名单的查询与差量维护（与 PC 端角色/菜单权限完全独立）。
    /// 供手机端「权限管理」页（AppPermController）与注册/建号默认预置使用。
    /// </summary>
    public interface IAppPermService
    {
        /// <summary>权限点目录（管理页动态下发开关清单；新增权限点无需改手机端）。</summary>
        List<AppPermPointDto> GetCatalog();

        /// <summary>管理页用户列表（未删除用户 + 各自已开通权限码；管理员恒全量）。</summary>
        List<AppPermUserDto> GetUsers();

        /// <summary>
        /// 差量保存用户权限（新增插行/移除删行，审计字段保留）；内置管理员禁止配置。
        /// 保存后由调用方（AppPermController）使目标用户 app 会话失效，重新登录后按新权限签发。
        /// </summary>
        void SaveUserPerms(Guid operatorId, Guid userId, List<string>? permCodes);
    }
}
