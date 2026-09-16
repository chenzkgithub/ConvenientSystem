using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common;

/// <summary>配置文件热编辑服务接口。</summary>
public interface IConfigEditorService
{
    /// <summary>获取可编辑的配置文件列表。</summary>
    List<ConfigFileInfoDto> ListFiles();

    /// <summary>读取指定配置文件的完整内容。</summary>
    ConfigFileContentDto ReadFile(string name);

    /// <summary>保存配置文件（自动备份旧版本）。</summary>
    void SaveFile(string name, string content);

    /// <summary>触发应用优雅停机（由进程管理器拉起等效重启）。</summary>
    void TriggerRestart();

    /// <summary>获取服务状态信息（启动时间等）。</summary>
    ConfigEditorStatusDto GetStatus();
}
