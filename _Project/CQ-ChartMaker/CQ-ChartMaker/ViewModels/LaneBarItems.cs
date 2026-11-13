using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CQ_ChartMaker.ViewModels;

/// <summary> 底栏轨道栏的项类型：已有轨道 / 跳号新增 / 尾部新增 </summary>
public enum LaneBarItemKind
{
    Lane,
    GapAdd,
    TailAdd
}

/// <summary>
/// 底栏轨道栏的一项。
/// 轨道项显示标号并双向控制显隐；新增项显示 “+”，点击创建 <see cref="LaneIndex"/> 号轨道。
/// 删除入口通过右键 Flyout 的按钮调用，是否可用由“默认轨道 / 是否有数据”决定。
/// </summary>
public sealed class LaneBarItem : ObservableObject
{
    private readonly Action<int, bool>? _onVisibilityChanged;
    private readonly Action<LaneBarItem>? _onDelete;
    private readonly Action<LaneBarItem>? _onAdd;
    private bool _isVisible;

    public LaneBarItem(
        LaneBarItemKind kind,
        int laneIndex,
        bool isVisible = false,
        bool isDefault = false,
        bool hasData = false,
        Action<int, bool>? onVisibilityChanged = null,
        Action<LaneBarItem>? onDelete = null,
        Action<LaneBarItem>? onAdd = null)
    {
        Kind = kind;
        LaneIndex = laneIndex;
        _isVisible = isVisible;
        IsDefault = isDefault;
        HasData = hasData;
        Text = kind == LaneBarItemKind.Lane ? laneIndex.ToString() : "+";
        _onVisibilityChanged = onVisibilityChanged;
        _onDelete = onDelete;
        _onAdd = onAdd;

        DeleteCommand = new RelayCommand(() => _onDelete?.Invoke(this));
        AddCommand = new RelayCommand(() => _onAdd?.Invoke(this));
    }

    public LaneBarItemKind Kind { get; }
    public int LaneIndex { get; }
    public string Text { get; }

    /// <summary> 0~3 的永久轨道，不可删除 </summary>
    public bool IsDefault { get; }

    /// <summary> 当前是否有 Note/Tune/LaneEvent 数据 </summary>
    public bool HasData { get; }

    public bool IsLane => Kind == LaneBarItemKind.Lane;
    public bool IsAdd => Kind != LaneBarItemKind.Lane;

    /// <summary> 非默认且无数据的空轨道：关闭项目后会丢失，底栏用淡红背景提示 </summary>
    public bool IsEmpty => Kind == LaneBarItemKind.Lane && !IsDefault && !HasData;

    /// <summary> 显隐状态：ToggleButton 双向绑定，写入 LaneCatalogModel </summary>
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value) return;
            _isVisible = value;
            OnPropertyChanged();
            _onVisibilityChanged?.Invoke(LaneIndex, value);
        }
    }

    /// <summary> 只有“非默认 + 无数据”的轨道允许右键删除 </summary>
    public bool CanDelete => Kind == LaneBarItemKind.Lane && !IsDefault && !HasData;

    public ICommand DeleteCommand { get; }
    public ICommand AddCommand { get; }
}
