using Microsoft.Web.WebView2.Core;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ConvenientSystem;

/// <summary>
/// 文件操作消息处理（代码编辑器打开/保存/另存为/打开所在位置）。
/// 共享给 MainForm 和 BrowserForm，确保独立窗口中也能正常保存文件。
/// </summary>
public static class HostFileService
{
    // ===================== Shell API P/Invoke =====================
    // 使用 SHOpenFolderAndSelectItems 替代 explorer.exe /select，避免安全软件（如 360）拦截

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ILCreateFromPathW(string pszPath);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl, IntPtr[]? apidl, uint dwFlags);

    [DllImport("shell32.dll")]
    private static extern void ILFree(IntPtr pidl);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ShellExecuteW(IntPtr hwnd, string? lpOperation, string lpFile, string? lpParameters, string? lpDirectory, int nShowCmd);

    /// <summary>回收站 Shell CLSID</summary>
    private const string RecycleBinClsid = "::{645FF040-5081-101B-9F08-00AA002F954E}";
    /// <summary>
    /// 尝试处理文件操作消息。返回 true 表示已处理（是文件操作消息），false 表示不是。
    /// </summary>
    public static bool TryHandleMessage(JsonElement root, CoreWebView2 core, IWin32Window? owner = null)
    {
        if (!root.TryGetProperty("type", out var typeEl)) return false;
        var type = typeEl.GetString();

        switch (type)
        {
            case "file:open":
                HandleFileOpen(core, owner);
                return true;
            case "file:save":
                HandleFileSave(root, core);
                return true;
            case "file:saveAs":
                HandleFileSaveAs(root, core, owner);
                return true;
            case "file:openExplorer":
                HandleOpenExplorer(root);
                return true;
            case "file:openRecycleBin":
                HandleOpenRecycleBin();
                return true;
            default:
                return false;
        }
    }

    /// <summary>向指定 WebView2 内核发送 JSON 消息。</summary>
    private static void PostMessage(CoreWebView2 core, object message)
    {
        try
        {
            var json = JsonSerializer.Serialize(message);
            core.PostWebMessageAsJson(json);
        }
        catch { /* 序列化/发送失败忽略 */ }
    }

    /// <summary>处理前端"打开文件"请求：弹出 OpenFileDialog，读取文件内容后回发前端。</summary>
    private static void HandleFileOpen(CoreWebView2 core, IWin32Window? owner)
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "所有文件 (*.*)|*.*",
            Title = "打开文件",
            Multiselect = false,
        };
        if (dlg.ShowDialog(owner) != DialogResult.OK)
        {
            PostMessage(core, new { type = "file:cancelled", action = "open" });
            return;
        }

        try
        {
            var content = File.ReadAllText(dlg.FileName);
            PostMessage(core, new
            {
                type = "file:opened",
                path = dlg.FileName,
                fileName = Path.GetFileName(dlg.FileName),
                content,
            });
        }
        catch (Exception ex)
        {
            PostMessage(core, new { type = "file:error", action = "open", message = ex.Message });
        }
    }

    /// <summary>处理前端"保存文件"请求：将内容写入指定路径。</summary>
    private static void HandleFileSave(JsonElement root, CoreWebView2 core)
    {
        if (!root.TryGetProperty("path", out var pathEl)) return;
        var path = pathEl.GetString();
        if (string.IsNullOrEmpty(path)) return;

        if (!root.TryGetProperty("content", out var contentEl)) return;
        var content = contentEl.GetString() ?? string.Empty;

        try
        {
            File.WriteAllText(path, content);
            PostMessage(core, new
            {
                type = "file:saved",
                path,
                fileName = Path.GetFileName(path),
            });
        }
        catch (Exception ex)
        {
            PostMessage(core, new { type = "file:error", action = "save", message = ex.Message });
        }
    }

    /// <summary>处理前端"另存为"请求：弹出 SaveFileDialog，写入文件后回发前端。</summary>
    private static void HandleFileSaveAs(JsonElement root, CoreWebView2 core, IWin32Window? owner)
    {
        var suggestedName = root.TryGetProperty("fileName", out var nameEl)
            ? nameEl.GetString() ?? "未命名.txt"
            : "未命名.txt";
        var content = root.TryGetProperty("content", out var contentEl)
            ? contentEl.GetString() ?? string.Empty
            : string.Empty;

        using var dlg = new SaveFileDialog
        {
            FileName = suggestedName,
            Filter = "所有文件 (*.*)|*.*",
            Title = "另存为",
        };
        if (dlg.ShowDialog(owner) != DialogResult.OK)
        {
            PostMessage(core, new { type = "file:cancelled", action = "saveAs" });
            return;
        }

        try
        {
            File.WriteAllText(dlg.FileName, content);
            PostMessage(core, new
            {
                type = "file:saved",
                path = dlg.FileName,
                fileName = Path.GetFileName(dlg.FileName),
            });
        }
        catch (Exception ex)
        {
            PostMessage(core, new { type = "file:error", action = "save", message = ex.Message });
        }
    }

    /// <summary>处理前端"打开文件所在位置"请求：用 Shell API 打开资源管理器并选中文件。</summary>
    private static void HandleOpenExplorer(JsonElement root)
    {
        if (!root.TryGetProperty("path", out var pathEl)) return;
        var path = pathEl.GetString();
        if (string.IsNullOrEmpty(path)) return;

        // 与后端 OpenFolderAsync 同策略：文件不存在时降级打开所在目录（回收站文件夹类条目物理路径为 $R 目录），目录也不存在则静默跳过
        var openTarget = path;
        if (!File.Exists(path))
        {
            if (!Directory.Exists(path))
            {
                var dir = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
                openTarget = dir;
            }
        }

        try
        {
            // SHOpenFolderAndSelectItems 是 Windows 原生 Shell API，
            // 不通过 Process.Start 启动进程，不会触发 360 等安全软件拦截
            var pidl = ILCreateFromPathW(openTarget);
            if (pidl == IntPtr.Zero) return;
            try
            {
                SHOpenFolderAndSelectItems(pidl, 0, null, 0);
            }
            finally
            {
                ILFree(pidl);
            }
        }
        catch { /* 打开失败忽略 */ }
    }

    /// <summary>打开系统回收站：Shell 打开 CLSID 路径等同双击桌面回收站，直接进入回收站窗口。
    /// 注意不能用 SHOpenFolderAndSelectItems：它对回收站 PIDL 的语义是“打开父级（桌面）并选中回收站图标”，
    /// 不会进入回收站内部；Shell API 调用，不派生 explorer.exe 进程</summary>
    private static void HandleOpenRecycleBin()
    {
        try
        {
            ShellExecuteW(IntPtr.Zero, "open", RecycleBinClsid, null, null, 1);
        }
        catch { /* 打开失败忽略 */ }
    }
}
