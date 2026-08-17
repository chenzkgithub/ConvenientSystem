using ConvenientSystem.Shared.Entity.Common;
using FreeSql;

namespace ConvenientSystem.Shared.Common
{
    /// <summary>
    /// 用户显示信息查找帮助：列表展示创建人时按 CreatedById 批量关联 SysUser 取账号与姓名，
    /// 避免在各业务表冗余存储创建人账号字符串。
    /// </summary>
    public static class UserDisplayHelper
    {
        /// <summary>用户显示信息（账号 + 姓名）</summary>
        public sealed class UserDisplay
        {
            public UserDisplay(string account, string? displayName)
            {
                Account = account;
                DisplayName = displayName;
            }

            /// <summary>账号</summary>
            public string Account { get; }
            /// <summary>显示名称（可能为空）</summary>
            public string? DisplayName { get; }
        }

        /// <summary>
        /// 按用户 Id 集合批量查找显示信息（自动去重并忽略空 Id），返回 Id -> 显示信息字典。
        /// </summary>
        public static Dictionary<Guid, UserDisplay> GetMap(IFreeSql fsql, IEnumerable<Guid?> ids)
        {
            var set = ids
                .Where(i => i.HasValue && i.Value != Guid.Empty)
                .Select(i => i!.Value)
                .Distinct()
                .ToList();
            if (set.Count == 0) return new Dictionary<Guid, UserDisplay>();

            return fsql.Select<SysUserEntity>()
                .Where(u => set.Contains(u.Id))
                .ToList(u => new { u.Id, u.Account, u.DisplayName })
                .ToDictionary(u => u.Id, u => new UserDisplay(u.Account, u.DisplayName));
        }

        /// <summary>
        /// 从查找结果中取指定 Id 的显示信息，不存在返回 null。
        /// </summary>
        public static UserDisplay? Find(Dictionary<Guid, UserDisplay> map, Guid? id)
            => id.HasValue && map.TryGetValue(id.Value, out var d) ? d : null;
    }
}
