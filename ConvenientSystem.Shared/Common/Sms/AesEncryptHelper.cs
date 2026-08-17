using System.Security.Cryptography;
using System.Text;

namespace ConvenientSystem.Shared.Common.Sms
{
    /// <summary>
    /// AES 对称加密工具：用于 AccessKey 等敏感配置入库前加密。
    /// 密钥从环境变量 SMS_AES_KEY 读取（32 字节 / 256 位），
    /// 未配置时使用默认密钥（仅开发环境使用，生产环境务必配置环境变量）。
    /// </summary>
    public static class AesEncryptHelper
    {
        // 默认密钥（仅开发环境使用，32 字节）
        private static readonly byte[] DefaultKey = Encoding.UTF8.GetBytes("ConvenientSystem.Sms.AesKey.2026!!");

        /// <summary>
        /// 获取 AES 密钥（优先环境变量，否则用默认）
        /// 始终返回恰好 32 字节的密钥，不足时补零，超出时截断。
        /// </summary>
        private static byte[] GetKey()
        {
            byte[] raw;
            var envKey = Environment.GetEnvironmentVariable("SMS_AES_KEY");
            if (!string.IsNullOrEmpty(envKey) && envKey.Length >= 32)
            {
                raw = Encoding.UTF8.GetBytes(envKey.Substring(0, 32));
            }
            else
            {
                raw = DefaultKey;
            }

            // 确保恰好 32 字节（AES-256）
            if (raw.Length == 32) return raw;
            var key = new byte[32];
            Buffer.BlockCopy(raw, 0, key, 0, Math.Min(raw.Length, 32));
            return key;
        }

        /// <summary>
        /// AES 加密（返回 Base64 字符串）
        /// </summary>
        public static string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return string.Empty;
            var key = GetKey();
            using var aes = Aes.Create();
            aes.Key = key;
            aes.GenerateIV();
            using var encryptor = aes.CreateEncryptor();
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
            // IV 拼接在密文前，解密时取出
            var result = new byte[aes.IV.Length + cipherBytes.Length];
            Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
            Buffer.BlockCopy(cipherBytes, 0, result, aes.IV.Length, cipherBytes.Length);
            return Convert.ToBase64String(result);
        }

        /// <summary>
        /// AES 解密（输入 Base64 字符串）
        /// 解密失败时返回 null（而非抛异常），调用方据此提示用户重新填写密钥
        /// </summary>
        public static string? Decrypt(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return string.Empty;
            try
            {
                var key = GetKey();
                var fullBytes = Convert.FromBase64String(cipherText);
                if (fullBytes.Length <= 16) return null; // 密文太短，不可能是合法 AES 密文
                using var aes = Aes.Create();
                aes.Key = key;
                var iv = new byte[aes.IV.Length];
                Buffer.BlockCopy(fullBytes, 0, iv, 0, iv.Length);
                aes.IV = iv;
                using var decryptor = aes.CreateDecryptor();
                var cipherBytes = new byte[fullBytes.Length - iv.Length];
                Buffer.BlockCopy(fullBytes, iv.Length, cipherBytes, 0, cipherBytes.Length);
                var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch
            {
                return null; // 密钥不匹配或密文损坏
            }
        }
    }
}
