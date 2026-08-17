using System.Security.Cryptography;

namespace ConvenientSystem.Shared.Common.Security
{
    /// <summary>
    /// 密码哈希：PBKDF2（Rfc2898，SHA256，10 万次迭代），存储格式 pbkdf2$iter$saltB64$hashB64。
    /// 兼容历史明文密码：无 pbkdf2$ 前缀时按明文比对，由调用方在比对成功后自动升级为哈希。
    /// </summary>
    public static class PasswordHasher
    {
        private const string Prefix = "pbkdf2$";
        private const int Iterations = 100_000;
        private const int SaltSize = 16;
        private const int HashSize = 32;

        /// <summary>生成哈希（含随机盐）。</summary>
        public static string Hash(string password)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
            return $"{Prefix}{Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        }

        /// <summary>判断存储值是否已是 PBKDF2 哈希（否则视为历史明文）。</summary>
        public static bool IsHashed(string? stored)
            => !string.IsNullOrEmpty(stored) && stored.StartsWith(Prefix, StringComparison.Ordinal);

        /// <summary>校验密码：哈希则 PBKDF2 比对，明文则直接比对。</summary>
        public static bool Verify(string password, string? stored)
        {
            if (string.IsNullOrEmpty(stored)) return false;
            if (!IsHashed(stored))
                return string.Equals(password, stored, StringComparison.Ordinal);
            try
            {
                var parts = stored.Split('$');
                if (parts.Length != 4) return false;
                var iterations = int.Parse(parts[1]);
                var salt = Convert.FromBase64String(parts[2]);
                var expected = Convert.FromBase64String(parts[3]);
                var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch
            {
                return false;
            }
        }
    }
}
