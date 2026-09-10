using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ConvenientSystem;

/// <summary>桌面端单实例守卫：通过命名互斥体确保只运行一个实例，并支持激活已有窗口。</summary>
internal sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = "ConvenientSystem.SingleInstance.{9F2C4B1E-7A3D-4C58-9E21-1B6A0D5F3C77}";
    private const int SW_RESTORE = 9;

    private readonly Mutex _mutex;
    private bool _owned;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    public SingleInstanceGuard()
    {
        _mutex = new Mutex(true, MutexName, out _owned);
    }

    /// <summary>
    /// 尝试获取单实例所有权。
    /// </summary>
    /// <param name="isRestart">是否为重启接力模式。</param>
    /// <returns>true 表示可以继续启动；false 表示已有实例在运行。</returns>
    public bool TryAcquire(bool isRestart)
    {
        if (_owned) return true;

        if (isRestart)
        {
            try
            {
                if (!_mutex.WaitOne(TimeSpan.FromSeconds(15)))
                {
                    ActivateExistingInstance();
                    return false;
                }
                _owned = true;
                return true;
            }
            catch (AbandonedMutexException)
            {
                _owned = true;
                return true;
            }
        }

        ActivateExistingInstance();
        return false;
    }

    /// <summary>激活已运行实例的主窗口。</summary>
    public static void ActivateExistingInstance()
    {
        try
        {
            var current = Process.GetCurrentProcess();
            foreach (var p in Process.GetProcessesByName(current.ProcessName))
            {
                if (p.Id == current.Id) continue;
                var h = p.MainWindowHandle;
                if (h == IntPtr.Zero) continue;
                if (IsIconic(h)) ShowWindow(h, SW_RESTORE);
                SetForegroundWindow(h);
                break;
            }
        }
        catch
        {
            // 激活失败不影响"不多开"语义
        }
    }

    public void Dispose()
    {
        if (_owned)
        {
            _mutex.ReleaseMutex();
        }
        _mutex.Dispose();
    }
}
