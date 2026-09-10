using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ConvenientSystem;

/// <summary>浏览器窗口状态视图模型：把标题、置顶等状态从窗体代码中分离。</summary>
internal sealed class BrowserFormViewModel : INotifyPropertyChanged
{
    private string? _fixedTitle;
    private bool _pinned;

    /// <summary>固定标题：由调用方指定，非空时标题不随网页 DocumentTitle 变化。</summary>
    public string? FixedTitle
    {
        get => _fixedTitle;
        set => SetProperty(ref _fixedTitle, value);
    }

    /// <summary>窗口是否置顶。</summary>
    public bool Pinned
    {
        get => _pinned;
        set => SetProperty(ref _pinned, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
