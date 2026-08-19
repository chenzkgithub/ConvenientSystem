using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Common
{
    /// <summary>
    /// 登录用户表（本地配置库 ConvenientSystem，见 db/init.sql）
    /// </summary>
    [Table(Name = "dbo.SysUser")]
    public class SysUserEntity
    {
        /// <summary>用户 Id（顺序 GUID，由 SequentialGuid 生成）</summary>
        [Column(IsPrimary = true)]
        public Guid Id { get; set; }

        /// <summary>登录账号</summary>
        public string Account { get; set; } = string.Empty;

        /// <summary>登录密码</summary>
        public string Password { get; set; } = string.Empty;

        /// <summary>显示名称</summary>
        public string? DisplayName { get; set; }

        /// <summary>头像：data:image/...;base64 内联图片（前端上传前已压缩），NVARCHAR(MAX)</summary>
        [Column(StringLength = -1)]
        public string? Avatar { get; set; }

        /// <summary>手机号</summary>
        public string? Phone { get; set; }

        /// <summary>邮箱</summary>
        public string? Email { get; set; }

        /// <summary>备注/个人简介</summary>
        public string? Remark { get; set; }

        /// <summary>是否启用</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>是否软删除（删除后标记为 true，不再硬删）</summary>
        public bool IsDeleted { get; set; } = false;

        /// <summary>创建时间（数据库默认 GETDATE()）</summary>
        [Column(CanInsert = false, CanUpdate = false)]
        public DateTime CreateTime { get; set; }
    }
}
