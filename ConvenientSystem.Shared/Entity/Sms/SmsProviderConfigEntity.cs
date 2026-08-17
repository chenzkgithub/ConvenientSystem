using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.Sms
{
    /// <summary>
    /// 短信服务商配置表（见 db/init.sql dbo.SmsProviderConfig）
    /// </summary>
    [Table(Name = "dbo.SmsProviderConfig")]
    public class SmsProviderConfigEntity
    {
        [Column(IsPrimary = true, IsIdentity = true)]
        public int Id { get; set; }

        /// <summary>服务商类型：aliyun / ihuyi</summary>
        public string ProviderType { get; set; } = "aliyun";

        /// <summary>密钥 ID（AES 加密存储；阿里云=AccessKeyId，互亿无线=ApiId）</summary>
        public string AccessKeyId { get; set; } = string.Empty;

        /// <summary>密钥 Secret（AES 加密存储；阿里云=AccessKeySecret，互亿无线=ApiKey）</summary>
        public string AccessKeySecret { get; set; } = string.Empty;

        /// <summary>默认签名</summary>
        public string DefaultSignature { get; set; } = "zk";

        /// <summary>模板 Code（阿里云需要，互亿无线不需要）</summary>
        public string TemplateCode { get; set; } = string.Empty;

        /// <summary>配置名称</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>是否为默认配置（发送时用默认配置）</summary>
        public bool IsDefault { get; set; } = false;

        /// <summary>关联本地短信模板 Id（SmsTemplate.Id，用于测试发送和列表展示）</summary>
        public int? TemplateId { get; set; }

        /// <summary>是否启用</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>创建时间</summary>
        [Column(CanInsert = false, CanUpdate = false)]
        public DateTime CreateTime { get; set; }

        /// <summary>更新时间</summary>
        public DateTime UpdateTime { get; set; } = DateTime.Now;
    }
}
