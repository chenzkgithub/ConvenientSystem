using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 桌面程序自更新服务：管理端上传/激活/删除安装包，客户端检查更新并下载。
    /// </summary>
    public interface IDesktopUpdateService
    {
        /// <summary>获取全部桌面安装包列表（按上传时间倒序）。</summary>
        List<DesktopPackageDto> GetList();

        /// <summary>获取当前激活的桌面安装包。</summary>
        DesktopPackageDto? GetActive();

        /// <summary>检查指定本地版本是否需要更新。</summary>
        DesktopUpdateCheckResult Check(string currentVersion);

        /// <summary>上传桌面安装包（保存文件 + 插入记录 + 自动激活）。</summary>
        DesktopPackageDto Upload(string version, IFormFile file, string? description, Guid? userId);

        /// <summary>激活指定版本（其余取消激活）。</summary>
        void Activate(int id);

        /// <summary>停用指定版本（取消激活状态）。</summary>
        void Deactivate(int id);

        /// <summary>删除指定版本（不能删除当前激活版本）。</summary>
        void Delete(int id);

        /// <summary>更新指定版本的元数据（版本号、说明）。</summary>
        void Update(int id, string version, string? description);

        /// <summary>获取当前激活安装包的本地文件路径和文件名（供下载接口使用）。</summary>
        (string FilePath, string FileName) GetActiveFilePath();
    }
}
