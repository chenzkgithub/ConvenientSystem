using System.Runtime.InteropServices;

namespace ConvenientSystem;

/// <summary>
/// 全局热键管理：通过 RegisterHotKey 注册系统级热键，在隐藏消息窗口接收 WM_HOTKEY 后回调。
/// 热键为系统级，主窗口隐藏到托盘时仍能触发。回调在主线程（消息循环）执行，可直接操作 UI。
/// </summary>
internal sealed class GlobalHotkeyManager : IDisposable
{
    private readonly HotkeyWindow _window;
    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 0xC000;

    public GlobalHotkeyManager()
    {
        _window = new HotkeyWindow();
        _window.CreateHandle(new CreateParams { Caption = "ConvenientSystemHotkey" });
        _window.OnHotkey = id =>
        {
            if (_actions.TryGetValue(id, out var a)) a();
        };
    }

    /// <summary>注册全局热键。组合键形如 "Ctrl+Alt+Space"，返回是否注册成功（失败多为被占用）。</summary>
    public bool Register(string combination, Action callback)
    {
        var (mods, key) = Parse(combination);
        if (key == Keys.None) return false;
        int id = _nextId++;
        if (!RegisterHotKey(_window.Handle, id, (uint)mods, (uint)(key & Keys.KeyCode)))
            return false;
        _actions[id] = callback;
        return true;
    }

    /// <summary>解析 "Ctrl+Alt+Space" → (修饰符位掩码, 主键)。</summary>
    private static (int mods, Keys key) Parse(string combo)
    {
        int mods = 0;
        Keys key = Keys.None;
        var parts = combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var p in parts)
        {
            switch (p.ToLowerInvariant())
            {
                case "ctrl":
                case "control": mods |= MOD_CONTROL; break;
                case "alt": mods |= MOD_ALT; break;
                case "shift": mods |= MOD_SHIFT; break;
                case "win":
                case "super": mods |= MOD_WIN; break;
                default:
                    if (Enum.TryParse(p, ignoreCase: true, out Keys k)) key = k;
                    break;
            }
        }
        return (mods, key);
    }

    public void Dispose()
    {
        foreach (var id in _actions.Keys)
            UnregisterHotKey(_window.Handle, id);
        _actions.Clear();
        if (_window.Handle != IntPtr.Zero) _window.DestroyHandle();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int MOD_ALT = 0x0001;
    private const int MOD_CONTROL = 0x0002;
    private const int MOD_SHIFT = 0x0004;
    private const int MOD_WIN = 0x0008;
    private const int WM_HOTKEY = 0x0312;

    private sealed class HotkeyWindow : NativeWindow
    {
        public Action<int>? OnHotkey;
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && OnHotkey is not null)
            {
                OnHotkey((int)m.WParam);
                return;
            }
            base.WndProc(ref m);
        }
    }
}
