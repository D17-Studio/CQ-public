using System;
using CQ_ChartMaker.Models;

namespace CQ_ChartMaker.ViewModels;

/// <summary>
/// Events 画布上的一个列格子（轨道条的一格）。
/// 平铺模式下每格显示两行：上行轨道号、下行类型名；其余模式只显示一行。
/// </summary>
public sealed class LaneColumnItem
{
    /// <summary> 上一行文字（仅平铺模式显示轨道号） </summary>
    public string TopLabel { get; init; } = "";

    /// <summary> 下一行文字（轨道=轨道号；类型=类型名；平铺=类型名） </summary>
    public string BottomLabel { get; init; } = "";

    /// <summary> 是否显示上一行（平铺模式为 true） </summary>
    public bool ShowTopLabel { get; init; }

    /// <summary> 列宽（随缩放变化，重建列时写入） </summary>
    public double Width { get; init; }
}

/// <summary>
/// 事件类型栏按钮：三种模式共用同一控件，只是 IsChecked/IsEnabled 的语义不同。
/// 点击时通过回调把新状态交给 EditorViewModel 处理。
/// </summary>
public sealed class TypeBarItem
{
    private readonly Action<LaneEventKind, bool>? _onCheckedChanged;
    private bool _isChecked;

    public TypeBarItem(LaneEventKind kind, string text, bool isChecked, bool isEnabled,
        Action<LaneEventKind, bool>? onCheckedChanged)
    {
        Kind = kind;
        Text = text;
        _isChecked = isChecked;
        IsEnabled = isEnabled;
        _onCheckedChanged = onCheckedChanged;
    }

    public LaneEventKind Kind { get; }

    public string Text { get; }

    public bool IsEnabled { get; }

    /// <summary> 勾选状态；ToggleButton 双向绑定，变化时回调 VM </summary>
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;
            _isChecked = value;
            _onCheckedChanged?.Invoke(Kind, value);
        }
    }
}
