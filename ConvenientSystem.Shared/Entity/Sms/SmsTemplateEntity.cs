using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Sms
{
    /// <summary>
    /// 短信模板表（见 db/init.sql dbo.SmsTemplate）
    /// </summary>
    [Table(Name = "SmsTemplate")]
    public class SmsTemplateEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>模板名称</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>模板内容（支持 {姓名} {公司} 变量）</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>短信签名</summary>
        public string Signature { get; set; } = "zk";

        /// <summary>分类：营销/通知/提醒</summary>
        public string Category { get; set; } = "通知";

        /// <summary>是否启用</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>创建时间</summary>
        [Column(CanInsert = false, CanUpdate = false)]
        public DateTime CreateTime { get; set; }

        /// <summary>更新时间</summary>
        public DateTime UpdateTime { get; set; } = DateTime.Now;

        /// <summary>创建人用户 Id（SysUser.Id，GUID；用于数据权限过滤，列表关联 SysUser 展示账号与姓名）</summary>
        public Guid? CreatedById { get; set; }
    }
}
