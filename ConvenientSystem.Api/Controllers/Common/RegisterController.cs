using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using ConvenientSystem.Shared.Common.Email;
using ConvenientSystem.Shared.Common.Security;
using ConvenientSystem.Shared.Entity.Common;
using FreeSql;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ConvenientSystem.Api.Controllers.Common
{
    /// <summary>
    /// 注册控制器：邮箱注册（邮箱即账号），发送验证码 → 验证 → 创建账号。
    /// 验证码使用内存缓存（5 分钟过期），适合单机场景。
    /// </summary>
    [Area("Common")]
    [AllowAnonymous]
    public class RegisterController : BaseController
    {
        private readonly IFreeSql _db;
        private readonly IEmailService _emailService;
        private readonly ILogger<RegisterController> _logger;

        // 验证码内存缓存：key = email(小写), value = (code, expireAt)
        private static readonly ConcurrentDictionary<string, (string Code, DateTime ExpireAt)> CodeCache = new();
        private const int CODE_LENGTH = 6;
        private const int CODE_EXPIRE_MINUTES = 5;
        private const int CODE_COOLDOWN_SECONDS = 60; // 防刷：两次发送间隔至少 60 秒

        // 已发送但未验证的也记录发送时间，用于冷却判断
        private static readonly ConcurrentDictionary<string, DateTime> LastSendTime = new();

        public RegisterController(
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql db,
            IEmailService emailService,
            ILogger<RegisterController> logger)
        {
            _db = db;
            _emailService = emailService;
            _logger = logger;
        }

        /// <summary>
        /// 检查邮箱是否已被注册。前端在输入邮箱后调用，即时提示用户。
        /// </summary>
        [HttpGet]
        public ActionResult<object> CheckEmail([FromQuery] string email)
        {
            if (string.IsNullOrWhiteSpace(email) || !IsValidEmail(email))
                return Ok(new { exists = false, valid = false });

            var normalized = email.Trim().ToLowerInvariant();
            var exists = _db.Select<SysUserEntity>()
                .Where(u => u.Account == normalized || u.Email == normalized)
                .Any();

            return Ok(new { exists, valid = true });
        }

        /// <summary>
        /// 发送邮箱验证码（6 位数字）。已注册邮箱不发送。
        /// 60 秒内重复请求返回冷却提示。
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<object>> SendCode([FromBody] SendCodeRequest request)
        {
            var email = request?.email?.Trim();
            if (string.IsNullOrWhiteSpace(email) || !IsValidEmail(email))
                return Ok(new { ok = false, msg = "请输入有效的邮箱地址" });

            var normalized = email.ToLowerInvariant();

            // 检查是否已注册
            var exists = _db.Select<SysUserEntity>()
                .Where(u => u.Account == normalized || u.Email == normalized)
                .Any();
            if (exists)
                return Ok(new { ok = false, msg = "该邮箱已被注册，请使用其他邮箱" });

            // 冷却检查
            if (LastSendTime.TryGetValue(normalized, out var lastSent))
            {
                var elapsed = (DateTime.Now - lastSent).TotalSeconds;
                if (elapsed < CODE_COOLDOWN_SECONDS)
                {
                    var wait = (int)(CODE_COOLDOWN_SECONDS - elapsed);
                    return Ok(new { ok = false, msg = $"发送过于频繁，请 {wait} 秒后再试" });
                }
            }

            // 生成 6 位随机验证码
            var code = GenerateCode();
            var expireAt = DateTime.Now.AddMinutes(CODE_EXPIRE_MINUTES);
            CodeCache[normalized] = (code, expireAt);
            LastSendTime[normalized] = DateTime.Now;

            // 发送邮件
            var subject = "ConvenientSystem 注册验证码";
            var body = $@"<div style='font-family: sans-serif; max-width: 480px; margin: 0 auto; padding: 24px;'>
  <h2 style='color: #2fa98f;'>ConvenientSystem 注册验证</h2>
  <p>您的注册验证码为：</p>
  <div style='font-size: 32px; font-weight: bold; letter-spacing: 8px; color: #2fa98f; padding: 16px 0;'>{code}</div>
  <p style='color: #999; font-size: 13px;'>验证码 {CODE_EXPIRE_MINUTES} 分钟内有效，请勿泄露给他人。</p>
</div>";

            var result = await _emailService.SendAsync(email, subject, body);
            if (!result.Success)
            {
                _logger.LogWarning("注册验证码邮件发送失败 -> {Email}: {Error}", email, result.ErrorMessage);
                // 清理缓存，避免无法重新发送
                CodeCache.TryRemove(normalized, out _);
                return Ok(new { ok = false, msg = $"邮件发送失败：{result.ErrorMessage}" });
            }

            _logger.LogInformation("注册验证码已发送至 {Email}", email);
            return Ok(new { ok = true, msg = "验证码已发送，请查收邮箱" });
        }

        /// <summary>
        /// 完成注册：验证邮箱验证码通过后，创建账号并设置密码。
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<object>> Register([FromBody] RegisterRequest request)
        {
            var email = request?.email?.Trim();
            var password = request?.password?.Trim();
            var code = request?.code?.Trim();
            var displayName = request?.displayName?.Trim();

            if (string.IsNullOrWhiteSpace(email) || !IsValidEmail(email))
                return Ok(new { ok = false, msg = "请输入有效的邮箱地址" });
            if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
                return Ok(new { ok = false, msg = "密码至少 6 位" });
            if (string.IsNullOrWhiteSpace(code))
                return Ok(new { ok = false, msg = "请输入验证码" });

            var normalized = email.ToLowerInvariant();

            // 验证码校验
            if (!CodeCache.TryGetValue(normalized, out var cached))
                return Ok(new { ok = false, msg = "验证码不存在或已过期，请重新发送" });
            if (DateTime.Now > cached.ExpireAt)
            {
                CodeCache.TryRemove(normalized, out _);
                return Ok(new { ok = false, msg = "验证码已过期，请重新发送" });
            }
            if (!string.Equals(code, cached.Code, StringComparison.Ordinal))
                return Ok(new { ok = false, msg = "验证码错误" });

            // 再次检查是否已注册（防止并发）
            var exists = _db.Select<SysUserEntity>()
                .Where(u => u.Account == normalized || u.Email == normalized)
                .Any();
            if (exists)
            {
                CodeCache.TryRemove(normalized, out _);
                return Ok(new { ok = false, msg = "该邮箱已被注册" });
            }

            // 创建账号（邮箱即账号，密码哈希存储；主键为顺序 GUID）
            var hashedPassword = PasswordHasher.Hash(password);
            var user = new SysUserEntity
            {
                Id = ConvenientSystem.Shared.Common.SequentialGuid.NewId(),
                Account = normalized,
                Password = hashedPassword,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? normalized.Split('@')[0] : displayName,
                Email = normalized,
                Enabled = true,
            };

            try
            {
                var userId = user.Id;
                _db.Insert(user).ExecuteAffrows();

                // 自动赋予普通用户角色
                var userRoleId = _db.Select<SysRoleEntity>()
                    .Where(r => r.Code == "user" && r.Enabled)
                    .First(r => r.Id);
                if (userRoleId > 0)
                {
                    _db.Insert(new SysUserRoleEntity { UserId = userId, RoleId = userRoleId }).ExecuteAffrows();
                }

                // 清理验证码缓存
                CodeCache.TryRemove(normalized, out _);
                LastSendTime.TryRemove(normalized, out _);

                _logger.LogInformation("新用户注册成功: {Account}, UserId={Id}", normalized, userId);
                return Ok(new { ok = true, msg = "注册成功，请使用邮箱和密码登录" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建注册账号失败: {Email}", email);
                return Ok(new { ok = false, msg = "注册失败：" + ex.Message });
            }
        }

        private static string GenerateCode()
        {
            var rng = new Random();
            var code = rng.Next(0, 1_000_000).ToString($"D{CODE_LENGTH}");
            return code;
        }

        private static bool IsValidEmail(string email)
        {
            return Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
        }
    }

    public class SendCodeRequest
    {
        public string? email { get; set; }
    }

    public class RegisterRequest
    {
        public string? email { get; set; }
        public string? password { get; set; }
        public string? code { get; set; }
        public string? displayName { get; set; }
    }
}
