using System.IO;
using System.Net.Http.Headers;
using ConvenientSystem.Shared.Entity.Common;
using ConvenientSystem.Shared.Model.Common;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 桌面程序自更新服务实现。
    /// 文件存储到 /data/desktop-packages/（Docker volume 持久化），
    /// 上传时自动激活为新版本，桌面端通过 Check/Download 拉取安装包。
    /// </summary>
    public class DesktopUpdateService : IDesktopUpdateService
    {
        private readonly ILogger<DesktopUpdateService> _logger;
        private readonly IFreeSql _configDb;
        private readonly INoticeService _noticeService;
        private readonly string _storageDir;

        public DesktopUpdateService(
            ILogger<DesktopUpdateService> logger,
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql configDb,
            INoticeService noticeService)
        {
            _logger = logger;
            _configDb = configDb;
            _noticeService = noticeService;
            _storageDir = Environment.GetEnvironmentVariable("DESKTOP_PACKAGE_DIR") ?? "/data/desktop-packages";
            if (!Directory.Exists(_storageDir))
            {
                try { Directory.CreateDirectory(_storageDir); }
                catch { /* Docker 启动时 volume 可能尚未挂载，忽略 */ }
            }
        }

        public List<DesktopPackageDto> GetList()
        {
            var list = _configDb.Select<DesktopPackageEntity>()
                .OrderByDescending(p => p.CreateTime)
                .ToList();

            var userIds = list.Select(p => p.CreatedById).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
            var userMap = userIds.Count == 0
                ? new Dictionary<Guid, string>()
                : _configDb.Select<SysUserEntity>()
                    .Where(u => userIds.Contains(u.Id))
                    .ToDictionary(u => u.Id, u => u.DisplayName ?? string.Empty);

            return list.Select(p => new DesktopPackageDto
            {
                Id = p.Id,
                Version = p.Version,
                FileName = p.FileName,
                FileSize = p.FileSize,
                Description = AppendServerPath(p.Description, p.FileName),
                IsActive = p.IsActive,
                CreatedByName = p.CreatedById.HasValue && userMap.TryGetValue(p.CreatedById.Value, out var name) ? name : null,
                CreateTime = p.CreateTime,
            }).ToList();
        }

        public DesktopPackageDto? GetActive()
        {
            var entity = _configDb.Select<DesktopPackageEntity>()
                .Where(p => p.IsActive)
                .First();
            if (entity == null) return null;
            return new DesktopPackageDto
            {
                Id = entity.Id,
                Version = entity.Version,
                FileName = entity.FileName,
                FileSize = entity.FileSize,
                Description = AppendServerPath(entity.Description, entity.FileName),
                IsActive = true,
                CreateTime = entity.CreateTime,
            };
        }

        public DesktopUpdateCheckResult Check(string currentVersion)
        {
            var entity = _configDb.Select<DesktopPackageEntity>()
                .Where(p => p.IsActive)
                .First();

            if (entity == null)
            {
                return new DesktopUpdateCheckResult { HasUpdate = false };
            }

            var hasUpdate = IsHigherVersion(entity.Version, currentVersion);
            return new DesktopUpdateCheckResult
            {
                HasUpdate = hasUpdate,
                Version = entity.Version,
                Description = AppendServerPath(entity.Description, entity.FileName),
                FileSize = entity.FileSize,
                DownloadUrl = "/api/Common/DesktopUpdate/Download",
            };
        }

        public DesktopPackageDto Upload(string version, IFormFile file, string? description, Guid? userId)
        {
            if (string.IsNullOrWhiteSpace(version))
                throw new ArgumentException("版本号不能为空");
            if (file == null || file.Length == 0)
                throw new ArgumentException("文件不能为空");

            var safeVersion = version.Trim();
            var ext = Path.GetExtension(file.FileName);
            var fileName = $"desktop-{safeVersion}-{DateTime.Now:yyyyMMddHHmmss}{ext}";
            var filePath = Path.Combine(_storageDir, fileName);

            using (var fs = File.Create(filePath))
            {
                file.CopyTo(fs);
            }

            var entity = new DesktopPackageEntity
            {
                Version = safeVersion,
                FileName = fileName,
                FileSize = file.Length,
                Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                IsActive = true,
                CreatedById = userId,
            };

            _configDb.Transaction(() =>
            {
                _configDb.Update<DesktopPackageEntity>()
                    .Set(p => p.IsActive, false)
                    .Where(p => p.IsActive)
                    .ExecuteAffrows();
                _configDb.Insert(entity).ExecuteAffrows();
            });

            _logger.LogInformation("上传桌面安装包 Version={Version} FileName={FileName} Size={Size}",
                safeVersion, fileName, file.Length);

            // 与 Web 前端版本上传对齐：发一条全员可见的系统通知，在线用户登录后可见
            NotifyVersionChanged(safeVersion, entity.Description);

            return new DesktopPackageDto
            {
                Id = entity.Id,
                Version = entity.Version,
                FileName = entity.FileName,
                FileSize = entity.FileSize,
                Description = AppendServerPath(entity.Description, entity.FileName),
                IsActive = true,
                CreateTime = entity.CreateTime,
            };
        }

        /// <summary>发布一条"桌面程序已更新"的系统通知，全员可见且不触发外部推送。</summary>
        private void NotifyVersionChanged(string version, string? description)
        {
            try
            {
                var desc = string.IsNullOrWhiteSpace(description) ? "" : $"更新说明：{description.Trim()}";
                var content = $"系统已上传并激活桌面程序版本 {version}，桌面端下次启动时会弹窗提示安装。{desc}".Trim();
                _noticeService.CreateSystemNotice(
                    $"桌面程序已更新至 {version}",
                    content,
                    level: 2); // 重要：登录后会触发 NoticeAlert 弹窗提醒
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "桌面版本更新通知发送失败 Version={Version}", version);
            }
        }

        /// <summary>
        /// 描述尾部动态拼接服务器存储路径行（读取时拼接，不写库，历史记录同样生效）。
        /// </summary>
        private string? AppendServerPath(string? description, string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return description;
            var pathLine = $"服务器路径：{Path.Combine(_storageDir, fileName)}";
            if (string.IsNullOrWhiteSpace(description)) return pathLine;
            if (description.Contains("服务器路径：")) return description;
            return $"{description}\n{pathLine}";
        }

        public void Activate(int id)
        {
            var entity = _configDb.Select<DesktopPackageEntity>().Where(p => p.Id == id).First();
            if (entity == null) throw new ArgumentException("安装包不存在");

            _configDb.Transaction(() =>
            {
                _configDb.Update<DesktopPackageEntity>()
                    .Set(p => p.IsActive, false)
                    .Where(p => p.IsActive)
                    .ExecuteAffrows();
                _configDb.Update<DesktopPackageEntity>()
                    .Set(p => p.IsActive, true)
                    .Where(p => p.Id == id)
                    .ExecuteAffrows();
            });
            _logger.LogInformation("激活桌面安装包 Id={Id} Version={Version}", id, entity.Version);
        }

        public void Deactivate(int id)
        {
            var entity = _configDb.Select<DesktopPackageEntity>().Where(p => p.Id == id).First();
            if (entity == null) return;
            if (!entity.IsActive) return;

            _configDb.Update<DesktopPackageEntity>()
                .Set(p => p.IsActive, false)
                .Where(p => p.Id == id)
                .ExecuteAffrows();
            _logger.LogInformation("停用桌面安装包 Id={Id} Version={Version}", id, entity.Version);
        }

        public void Delete(int id)
        {
            var entity = _configDb.Select<DesktopPackageEntity>().Where(p => p.Id == id).First();
            if (entity == null) return;
            if (entity.IsActive) throw new ArgumentException("不能删除当前激活版本");

            var filePath = Path.Combine(_storageDir, entity.FileName);
            if (File.Exists(filePath))
            {
                try { File.Delete(filePath); } catch { /* 忽略文件删除失败 */ }
            }

            _configDb.Delete<DesktopPackageEntity>().Where(p => p.Id == id).ExecuteAffrows();
            _logger.LogInformation("删除桌面安装包 Id={Id} Version={Version}", id, entity.Version);
        }

        public void Update(int id, string version, string? description)
        {
            if (string.IsNullOrWhiteSpace(version))
                throw new ArgumentException("版本号不能为空");

            var safeVersion = version.Trim();
            var duplicate = _configDb.Select<DesktopPackageEntity>()
                .Where(p => p.Version == safeVersion && p.Id != id)
                .First();
            if (duplicate != null)
                throw new ArgumentException($"版本号「{safeVersion}」已被其他记录使用");

            var affected = _configDb.Update<DesktopPackageEntity>()
                .Set(p => p.Version, safeVersion)
                .Set(p => p.Description, string.IsNullOrWhiteSpace(description) ? null : description.Trim())
                .Where(p => p.Id == id)
                .ExecuteAffrows();

            if (affected == 0) throw new ArgumentException("安装包不存在");
            _logger.LogInformation("更新桌面安装包 Id={Id} Version={Version}", id, safeVersion);
        }

        public (string FilePath, string FileName) GetActiveFilePath()
        {
            var entity = _configDb.Select<DesktopPackageEntity>()
                .Where(p => p.IsActive)
                .First();
            if (entity == null) throw new ArgumentException("没有已激活的桌面安装包");

            var filePath = Path.Combine(_storageDir, entity.FileName);
            if (!File.Exists(filePath)) throw new ArgumentException("桌面安装包文件不存在");

            return (filePath, entity.FileName);
        }

        /// <summary>
        /// 语义版本比较：判断 remote 是否高于 local。
        /// 支持 major.minor.patch 格式，逐段数值比较；local 为空时视为有新版本。
        /// </summary>
        private static bool IsHigherVersion(string remote, string current)
        {
            if (string.IsNullOrEmpty(current)) return true;

            var remoteParts = ParseVersion(remote);
            var currentParts = ParseVersion(current);

            for (int i = 0; i < Math.Max(remoteParts.Length, currentParts.Length); i++)
            {
                var r = i < remoteParts.Length ? remoteParts[i] : 0;
                var c = i < currentParts.Length ? currentParts[i] : 0;
                if (r > c) return true;
                if (r < c) return false;
            }
            return false;
        }

        private static int[] ParseVersion(string version)
        {
            return version.Split('.', '-')
                .Select(s => int.TryParse(s, out var n) ? n : 0)
                .ToArray();
        }
    }
}
