using System.Text;

namespace ConvenientSystem;

/// <summary>
/// 本地构建工具类：提供命令路径查找和候选安装目录的静态方法，
/// 供 UniversalBuildService 等服务调用。
/// </summary>
public static class LocalBuildService
{
    /// <summary>在 PATH 环境变量中查找可执行文件。Windows 上优先 .exe/.cmd/.bat，避免选中无扩展名的 shell 脚本。</summary>
    internal static string? FindInPath(string command)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var paths = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        // 如果命令已包含扩展名，按原样查找
        if (Path.HasExtension(command))
        {
            foreach (var dir in paths)
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                try
                {
                    var fullPath = Path.Combine(dir, command);
                    if (File.Exists(fullPath))
                        return fullPath;
                }
                catch
                {
                    // 忽略非法路径
                }
            }
            return null;
        }

        // Windows 上无扩展名时只找可执行扩展名，避免找到 npm（Unix shell 脚本）
        var extensions = new[] { ".exe", ".cmd", ".bat" };
        foreach (var dir in paths)
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            foreach (var ext in extensions)
            {
                try
                {
                    var fullPath = Path.Combine(dir, command + ext);
                    if (File.Exists(fullPath))
                        return fullPath;
                }
                catch
                {
                    // 忽略非法路径
                }
            }
        }
        return null;
    }

    internal static string[] DotNetCandidates() =>
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "dotnet", "dotnet.exe"),
    ];

    internal static string[] NodeCandidates() =>
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs", "node.exe"),
    ];

    internal static string[] NpmCandidates() =>
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "npm.cmd"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs", "npm.cmd"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "npm"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs", "npm"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "npm.cmd"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop", "apps", "nodejs", "current", "npm.cmd"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop", "apps", "nodejs", "current", "npm"),
        @"C:\ProgramData\nvm\npm.cmd",
        @"C:\nvm\npm.cmd",
    ];

    internal static string[] InnoSetupCandidates() =>
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Inno Setup 6", "iscc.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Inno Setup 6", "iscc.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Inno Setup 7", "iscc.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Inno Setup 7", "iscc.exe"),
        @"D:\innosetup\Inno Setup 6\iscc.exe",
    ];
}