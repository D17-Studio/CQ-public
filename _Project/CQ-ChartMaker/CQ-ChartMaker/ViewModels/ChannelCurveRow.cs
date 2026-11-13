using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CQ_ChartMaker.Common;
using CQ_ChartMaker.Models;
using CQMusicGame.Shared;

namespace CQ_ChartMaker.ViewModels;

/// <summary> From/To 模式下拉项：实际枚举值 + 显示名 </summary>
public sealed record ValueModeItem(ValueMode Mode, string DisplayName);

/// <summary> 缓动方向下拉项：实际方向 + 显示名 </summary>
public sealed record EaseDirectionItem(EaseCurveDirection Direction, string DisplayName);

/// <summary>
/// Inspector 中的单条 ChannelCurve 编辑行。
/// 通过 get/set 回调读写当前选中事件；多选取值不同时显示空白，输入后应用到全部。
/// </summary>
public partial class ChannelCurveRow : ObservableObject
{
    // 三组下拉选项都随语言开关重建；语言切换时整体替换列表以触发 ItemsSource 刷新
    private List<ValueModeItem> _modeOptions = BuildModeOptions();
    private List<EaseDirectionItem> _directionOptions = BuildDirectionOptions();
    private List<CurveKindItem> _easeKindOptions = BuildEaseKindOptions();

    /// <summary> From/To 模式下拉项（受 Inspector 界面汉化控制） </summary>
    public IReadOnlyList<ValueModeItem> ModeOptions => _modeOptions;

    /// <summary> 缓动方向下拉项（受缓动曲线汉化控制） </summary>
    public IReadOnlyList<EaseDirectionItem> DirectionOptions => _directionOptions;

    /// <summary> 缓动种类下拉项（受缓动曲线汉化控制） </summary>
    public IReadOnlyList<CurveKindItem> EaseKindOptions => _easeKindOptions;

    private static List<ValueModeItem> BuildModeOptions() => new()
    {
        new(ValueMode.Absolute, InspectorLocalization.Text("绝对", "Absolute")),
        new(ValueMode.Relative, InspectorLocalization.Text("相对", "Relative")),
    };

    private static List<EaseDirectionItem> BuildDirectionOptions() => new()
    {
        new(EaseCurveDirection.In, InspectorLocalization.CurveText("缓入", "In")),
        new(EaseCurveDirection.Out, InspectorLocalization.CurveText("缓出", "Out")),
        new(EaseCurveDirection.InOut, InspectorLocalization.CurveText("缓入缓出", "InOut")),
    };

    private static List<CurveKindItem> BuildEaseKindOptions()
        => CurveKindItem.CreateOptions(InspectorLocalization.IsEaseCurveLocalized);

    private readonly Func<ChannelCurve?> _get;
    private readonly Action<ChannelCurve> _set;

    // 拖拽按钮配置：Step 可固定、也可按当前模式动态取值（Speed Fixed/Multiplier）
    private readonly float _dragStep;
    private readonly float? _dragMin;
    private readonly float? _dragMax;
    private readonly Func<float>? _dragStepProvider;

    public ChannelCurveRow(
        Func<ChannelCurve?> get,
        Action<ChannelCurve> set,
        float dragStep = 0.1f,
        float? dragMin = null,
        float? dragMax = null,
        Func<float>? dragStepProvider = null)
    {
        _get = get;
        _set = set;
        _dragStep = dragStep;
        _dragMin = dragMin;
        _dragMax = dragMax;
        _dragStepProvider = dragStepProvider;
    }

    /// <summary> 拖拽按钮每像素步长（配置了动态来源时按当前事件状态取值） </summary>
    public float DragStep => _dragStepProvider?.Invoke() ?? _dragStep;

    /// <summary> 拖拽调值下限（null = 不钳制；只约束拖拽，不影响手动输入） </summary>
    public float? DragMin => _dragMin;

    /// <summary> 拖拽调值上限（null = 不钳制；只约束拖拽，不影响手动输入） </summary>
    public float? DragMax => _dragMax;

    // ChannelCurve 是值类型：必须由 mutate 返回修改后的副本再写回，
    // 若用 Action<ChannelCurve>，修改只作用于委托参数副本，写回的仍是原值
    private void Apply(Func<ChannelCurve, ChannelCurve> mutate)
    {
        // 行在曲线未启用时隐藏，_get() 为 null 正常不可达；此处兜底值不参与实际采样
        var curve = _get() ?? ChartDefaults.CreateCurve(0f);
        _set(mutate(curve));
        Refresh();
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(From));
        OnPropertyChanged(nameof(FromMode));
        OnPropertyChanged(nameof(To));
        OnPropertyChanged(nameof(ToMode));
        OnPropertyChanged(nameof(EaseKind));
        OnPropertyChanged(nameof(EaseDirection));
        OnPropertyChanged(nameof(EaseDirectionEnabled));
        OnPropertyChanged(nameof(EaseDirectionIndex));
        // Speed 切换 Fixed/Multiplier 后拖拽步长会变，需要刷新按钮绑定
        OnPropertyChanged(nameof(DragStep));
    }

    /// <summary> 语言开关变化后重建三组下拉选项并刷新绑定 </summary>
    public void RefreshLocalization()
    {
        _modeOptions = BuildModeOptions();
        _directionOptions = BuildDirectionOptions();
        _easeKindOptions = BuildEaseKindOptions();
        OnPropertyChanged(nameof(ModeOptions));
        OnPropertyChanged(nameof(DirectionOptions));
        OnPropertyChanged(nameof(EaseKindOptions));
        Refresh();
    }

    public float? From
    {
        get => _get()?.From;
        set { if (value is { } v) Apply(c => { c.From = v; return c; }); }
    }

    public ValueModeItem? FromMode
    {
        get => _get() is { } c ? FindMode(c.FromMode) : null;
        set { if (value is { } v) Apply(c => { c.FromMode = v.Mode; return c; }); }
    }

    public float? To
    {
        get => _get()?.To;
        set { if (value is { } v) Apply(c => { c.To = v; return c; }); }
    }

    public ValueModeItem? ToMode
    {
        get => _get() is { } c ? FindMode(c.ToMode) : null;
        set { if (value is { } v) Apply(c => { c.ToMode = v.Mode; return c; }); }
    }

    private ValueModeItem? FindMode(ValueMode mode)
        => _modeOptions.FirstOrDefault(item => item.Mode == mode);

    public CurveKindItem? EaseKind
    {
        get
        {
            var curve = _get();
            if (curve is not { } c) return null;
            EaseCurveKind kind = EaseCurveKindExtensions.BaseOf(c.Ease);
            return _easeKindOptions.FirstOrDefault(item => item.Kind == kind);
        }
        set
        {
            if (value is null) return;
            // Linear 没有方向；切到非线性曲线且当前无方向时默认 InOut
            Apply(c =>
            {
                c.Ease = EaseCurveKindExtensions.ToEaseCurve(
                    value.Kind,
                    value.Kind == EaseCurveKind.Linear
                        ? null
                        : EaseCurveKindExtensions.DirectionOf(c.Ease) ?? EaseCurveDirection.InOut);
                return c;
            });
        }
    }

    public EaseDirectionItem? EaseDirection
    {
        get
        {
            var curve = _get();
            if (curve is not { } c) return null;
            EaseCurveDirection? direction = EaseCurveKindExtensions.DirectionOf(c.Ease);
            return direction is { } d
                ? _directionOptions.FirstOrDefault(item => item.Direction == d)
                : null;
        }
        set
        {
            // 语言切换重建列表时 ComboBox 可能短暂回传 null，忽略以免把曲线误设为 Linear
            if (value is not { } item) return;
            Apply(c =>
            {
                c.Ease = EaseCurveKindExtensions.ToEaseCurve(
                    EaseCurveKindExtensions.BaseOf(c.Ease), item.Direction);
                return c;
            });
        }
    }

    /// <summary> 方向框是否可用：Linear 没有方向，置灰并留空 </summary>
    public bool EaseDirectionEnabled
        => _get() is { } c && EaseCurveKindExtensions.DirectionOf(c.Ease) != null;

    /// <summary> 方向下拉索引（供 CurvePreview 绘制）：0=In / 1=Out / 2=InOut，Linear 为 -1 </summary>
    public int EaseDirectionIndex
    {
        get
        {
            var curve = _get();
            return curve is { } c
                ? EaseCurveDirectionExtensions.ToIndex(EaseCurveKindExtensions.DirectionOf(c.Ease))
                : -1;
        }
    }
}
