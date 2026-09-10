namespace ConvenientSystem;

/// <summary>为 <see cref="QuickLauncher"/> 提供运行时回调的工厂。</summary>
internal sealed class QuickLauncherFactory
{
    private readonly AppIndexService _appIndex;
    private readonly FileIndexService _fileIndex;

    public QuickLauncherFactory(
        AppIndexService appIndex,
        FileIndexService fileIndex)
    {
        _appIndex = appIndex;
        _fileIndex = fileIndex;
    }

    public QuickLauncher Create(
        LauncherStore store,
        Action<string, string, bool> openPage,
        Action<string> openUrl)
    {
        return new QuickLauncher(
            _appIndex,
            store,
            _fileIndex,
            openPage,
            openUrl);
    }
}
