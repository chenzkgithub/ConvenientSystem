namespace ConvenientSystem;

/// <summary>启动时清理项目目录下的 obj / bin 编译缓存。</summary>
internal static class BuildArtifactCleaner
{
    /// <summary>执行清理。失败时静默忽略，不影响启动。</summary>
    public static void Clean()
    {
        var exeDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var projectDir = Directory.Exists(Path.Combine(exeDir, "ConvenientSystem"))
            ? Path.Combine(exeDir, "ConvenientSystem")
            : exeDir;

        if (Path.GetFileName(projectDir).Equals("publish", StringComparison.OrdinalIgnoreCase))
            projectDir = Path.GetDirectoryName(projectDir) ?? projectDir;

        foreach (var dirName in new[] { "obj", "bin" })
        {
            var targetPath = Path.Combine(projectDir, dirName);
            if (!Directory.Exists(targetPath)) continue;
            try { Directory.Delete(targetPath, recursive: true); }
            catch { /* 文件被占用或删除失败时静默忽略 */ }
        }
    }
}
