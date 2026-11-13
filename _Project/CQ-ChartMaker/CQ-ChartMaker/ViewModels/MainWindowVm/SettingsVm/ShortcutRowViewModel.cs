using CommunityToolkit.Mvvm.ComponentModel;
using CQ_ChartMaker.Models;

namespace CQ_ChartMaker.ViewModels;

/// <summary> 设置页中的一行快捷键（可改键行或固定提示行） </summary>
public partial class ShortcutRowViewModel : ObservableObject
{
    public ShortcutDefinition Definition { get; }

    public string DisplayName => Definition.DisplayName;

    public bool IsEditable => Definition.IsEditable;

    /// <summary> 当前键位的显示文本 </summary>
    [ObservableProperty]
    private string _gestureText = string.Empty;

    /// <summary> 是否与其他快捷键冲突（仅提示，不阻止应用） </summary>
    [ObservableProperty]
    private bool _hasConflict;

    /// <summary> 是否处于“等待按下新键”的捕获状态 </summary>
    [ObservableProperty]
    private bool _isCapturing;

    public ShortcutRowViewModel(ShortcutDefinition definition)
    {
        Definition = definition;
    }
}
