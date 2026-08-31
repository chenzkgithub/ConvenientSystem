using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// Web 前端版本包管理服务：上传、列表、激活、删除、下载。
    /// </summary>
    public interface IWebPackageService
    {
        /// <summary>获取全部版本包列表（按上传时间倒序）。</summary>
        List<WebPackageDto> GetList();

        /// <summary>获取当前激活的版本包（桌面端启动时查询）。</summary>
        WebPackageDto? GetActive();

        /// <summary>上传版本包（保存文件 + 插入记录 + 自动激活）。</summary>
        WebPackageDto Upload(string version, IFormFile file, string? description, Guid? userId);

        /// <summary>激活指定版本（其余取消激活）。</summary>
        void Activate(int id);

        /// <summary>停用指定版本（取消激活状态）。</summary>
        void Deactivate(int id);

        /// <summary>删除版本（文件 + 记录，激活的不允许删除）。</summary>
        void Delete(int id);

        /// <summary>修改版本号和更新说明（版本号不可与其他记录重复）。</summary>
        void Update(int id, string version, string? description);

        /// <summary>获取当前激活版本的文件路径和文件名（供 Controller 流式下载）。</summary>
        (string FilePath, string FileName) GetActiveFilePath();
    }
}
