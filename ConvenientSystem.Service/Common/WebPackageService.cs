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
    /// Web 前端版本包管理服务实现。
    /// 文件存储到 /data/web-packages/（Docker volume 持久化），
    /// 上传时自动激活为新版本，桌面端通过 GetActive/Download 拉取激活版本。
    /// </summary>
    public class WebPackageService : IWebPackageService
    {
        private readonly ILogger<WebPackageService> _logger;
        private readonly IFreeSql _configDb;
        private readonly INoticeService _noticeService;
        private readonly string _storageDir;

        public WebPackageService(
            ILogger<WebPackageService> logger,
            [FromKeyedServices("ConvenientSystemDb")] IFreeSql configDb,
            INoticeService noticeService)
        {
            _logger = logger;
            _configDb = configDb;
            _noticeService = noticeService;
            _storageDir = Environment.GetEnvironmentVariable("WEB_PACKAGE_DIR") ?? "/data/web-packages";
            if (!Directory.Exists(_storageDir))
            {
                try { Directory.CreateDirectory(_storageDir); }
                catch { /* Docker 启动时 volume 可能尚未挂载，忽略 */ }
            }
        }

        /// <summary>发布一条"Web 前端版本已更新"的系统通知，全员可见且不触发外部推送。</summary>
        private void NotifyVersionChanged(string version, string? description, string action)
        {
            try
            {
                var desc = string.IsNullOrWhiteSpace(description) ? "" : $"更新说明：{description.Trim()}";
                var content = $"系统已{action} Web 前端版本 {version}，桌面端下次启动或检查更新时会自动拉取。{desc}".Trim();
                _noticeService.CreateSystemNotice(
                    $"Web 前端已更新至 {version}",
                    content,
                    level: 2); // 重要：登录后会触发 NoticeAlert 弹窗提醒
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Web 版本更新通知发送失败 Version={Version}", version);
            }
        }

        public List<WebPackageDto> GetList()
        {
            return _configDb.Select<WebPackageEntity>()
                .OrderByDescending(p => p.CreateTime)
                .ToList(p => new WebPackageDto
                {
                    Id = p.Id,
                    Version = p.Version,
                    FileSize = p.FileSize,
                    Description = p.Description,
                    IsActive = p.IsActive,
                    CreateTime = p.CreateTime,
                });
        }

        public WebPackageDto? GetActive()
        {
            var entity = _configDb.Select<WebPackageEntity>()
                .Where(p => p.IsActive)
                .First();
            if (entity == null) return null;
            return new WebPackageDto
            {
                Id = entity.Id,
                Version = entity.Version,
                FileSize = entity.FileSize,
                Description = entity.Description,
                IsActive = true,
                CreateTime = entity.CreateTime,
            };
        }

        public WebPackageDto Upload(string version, IFormFile file, string? description, Guid? userId)
        {
            if (string.IsNullOrWhiteSpace(version))
                throw new ArgumentException("版本号不能为空");
            if (file == null || file.Length == 0)
                throw new ArgumentException("文件不能为空");

            var safeVersion = version.Trim();
            var ext = Path.GetExtension(file.FileName);
            var fileName = $"web-{safeVersion}-{DateTime.Now:yyyyMMddHHmmss}{ext}";
            var filePath = Path.Combine(_storageDir, fileName);

            // 保存文件
            using (var fs = File.Create(filePath))
            {
                file.CopyTo(fs);
            }

            // 事务：取消旧激活 + 插入新记录并自动激活
            var entity = new WebPackageEntity
            {
                Version = safeVersion,
                FileName = fileName,
                FileSize = file.Length,
                Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                IsActive = true,
                CreateTime = DateTime.Now,
                CreatedById = userId,
            };

            _configDb.Transaction(() =>
            {
                _configDb.Update<WebPackageEntity>()
                    .Set(p => p.IsActive, false)
                    .Where(p => p.IsActive)
                    .ExecuteAffrows();
                _configDb.Insert(entity).ExecuteAffrows();
            });

            _logger.LogInformation("上传 Web 版本包 Version={Version} FileName={FileName} Size={Size}",
                safeVersion, fileName, file.Length);

            NotifyVersionChanged(safeVersion, entity.Description, "上传并激活");

            return new WebPackageDto
            {
                Id = entity.Id,
                Version = entity.Version,
                FileSize = entity.FileSize,
                Description = entity.Description,
                IsActive = true,
                CreateTime = entity.CreateTime,
            };
        }

        public void Activate(int id)
        {
            var entity = _configDb.Select<WebPackageEntity>().Where(p => p.Id == id).First();
            if (entity == null) throw new ArgumentException("版本包不存在");

            _configDb.Transaction(() =>
            {
                _configDb.Update<WebPackageEntity>()
                    .Set(p => p.IsActive, false)
                    .Where(p => p.IsActive)
                    .ExecuteAffrows();
                _configDb.Update<WebPackageEntity>()
                    .Set(p => p.IsActive, true)
                    .Where(p => p.Id == id)
                    .ExecuteAffrows();
            });
            _logger.LogInformation("激活 Web 版本包 Id={Id} Version={Version}", id, entity.Version);

            NotifyVersionChanged(entity.Version, entity.Description, "激活");
        }

        public void Deactivate(int id)
        {
            var entity = _configDb.Select<WebPackageEntity>().Where(p => p.Id == id).First();
            if (entity == null) throw new ArgumentException("版本包不存在");
            if (!entity.IsActive) return; // 已经未激活，幂等处理

            _configDb.Update<WebPackageEntity>()
                .Set(p => p.IsActive, false)
                .Where(p => p.Id == id)
                .ExecuteAffrows();
            _logger.LogInformation("停用 Web 版本包 Id={Id} Version={Version}", id, entity.Version);
        }

        public void Delete(int id)
        {
            var entity = _configDb.Select<WebPackageEntity>().Where(p => p.Id == id).First();
            if (entity == null) return;
            if (entity.IsActive) throw new ArgumentException("不能删除当前激活版本");

            // 删除文件
            var filePath = Path.Combine(_storageDir, entity.FileName);
            if (File.Exists(filePath))
            {
                try { File.Delete(filePath); } catch { /* 忽略文件删除失败 */ }
            }

            _configDb.Delete<WebPackageEntity>().Where(p => p.Id == id).ExecuteAffrows();
            _logger.LogInformation("删除 Web 版本包 Id={Id} Version={Version}", id, entity.Version);
        }

        public void Update(int id, string version, string? description)
        {
            if (string.IsNullOrWhiteSpace(version))
                throw new ArgumentException("版本号不能为空");

            var safeVersion = version.Trim();

            // 检查版本号是否与其他记录重复
            var duplicate = _configDb.Select<WebPackageEntity>()
                .Where(p => p.Version == safeVersion && p.Id != id)
                .First();
            if (duplicate != null)
                throw new ArgumentException($"版本号「{safeVersion}」已被其他记录使用");

            var affected = _configDb.Update<WebPackageEntity>()
                .Set(p => p.Version, safeVersion)
                .Set(p => p.Description, string.IsNullOrWhiteSpace(description) ? null : description.Trim())
                .Where(p => p.Id == id)
                .ExecuteAffrows();

            if (affected == 0) throw new ArgumentException("版本包不存在");
            _logger.LogInformation("更新 Web 版本包 Id={Id} Version={Version}", id, safeVersion);
        }

        public (string FilePath, string FileName) GetActiveFilePath()
        {
            var entity = _configDb.Select<WebPackageEntity>()
                .Where(p => p.IsActive)
                .First();
            if (entity == null) throw new ArgumentException("没有已激活的版本包");

            var filePath = Path.Combine(_storageDir, entity.FileName);
            if (!File.Exists(filePath)) throw new ArgumentException("版本包文件不存在");

            return (filePath, entity.FileName);
        }
    }
}
