using System.Collections.Generic;
using CQ_ChartMaker.Common;
using CQ_ChartMaker.Models;
using CQMusicGame.Shared;

namespace CQ_ChartMaker.ViewModels;

/// <summary> 节拍线的一个轨道宽度的线段（值类型） </summary>
public struct BeatLineSegmentItem
{
    public double X;
    public double Y;
    public double Width;
}

/// <summary> 节拍线左侧编号（值类型） </summary>
public struct BeatLineLabelItem
{
    public double Y;
    public string Label;  // string 是引用类型，但靠 List 复用稳定
}

/// <summary>
/// Note 可视元素类型（仅制谱器渲染用）。
/// 与 Shared 的 NoteType 不同：Tune 不是 NoteData.Type，只在渲染层作为独立类型存在
/// </summary>
public enum NoteVisualType
{
    Dot,
    Dash,
    Mute,
    Tune
}

/// <summary> 单个 note 的可视元素（头/身体/尾部），传给 View 渲染（值类型） </summary>
public struct NoteVisualItem
{
    public object? SelectionTarget;
    public NoteVisualType Type;
    public bool IsHead;
    public bool IsTail;       // 尾部结束点横条（Dash/Mute 专用），独立成项以参与命中与高亮
    public double X, Y;
    public double Width, Height;
    public bool IsSelected;
    public bool IsHighlight;  // 高亮层元素
}

/// <summary> 单个 BPM 节点的可视元素，传给 View 渲染（值类型） </summary>
public struct BpmVisualItem
{
    public object? SelectionTarget;
    public double X, Y;
    public double Width, Height;
    public bool IsSelected;
    public bool IsHighlight;  // 高亮层元素
}

/// <summary> 轨道完全隐藏区间的变暗遮罩矩形（画布坐标，值类型） </summary>
public struct LaneDimRectItem
{
    public double X, Y;
    public double Width, Height;
}

/// <summary> 框选矩形（画布坐标），传给 View 画半透明选择框（值类型） </summary>
public struct SelectionRectItem
{
    public double X, Y;
    public double Width, Height;
}

/// <summary>
/// 重叠提示标记（画布坐标，值类型）：圆心位于 unit 起始时间处的轨道中心，
/// 由画布在全部 unit 与预览之后绘制，不参与命中测试。
/// </summary>
public struct OverlapMarkerItem
{
    public double X, Y;
}

/// <summary> Notes 展开选择条的一列：一个重叠 Note 及列内可视项（X 为列内坐标，Y 与原轨道一致） </summary>
public sealed class NoteOverlapPanelColumn
{
    public object Unit = null!;
    public List<NoteVisualItem> Items = new();
}

/// <summary> Events 展开选择条的一列：一个重叠事件及列内可视项（含内部曲线） </summary>
public sealed class MotionOverlapPanelColumn
{
    public object Unit = null!;
    public List<MotionVisualItem> Items = new();
}

/// <summary> Beats 展开选择条的一列：一个重叠 BPM 节点及列内可视项 </summary>
public sealed class BpmOverlapPanelColumn
{
    public object Unit = null!;
    public List<BpmVisualItem> Items = new();
}

/// <summary> 事件内一条数值曲线的绘制参数（值已解析为绝对值，由 View 映射到内矩形坐标） </summary>
public struct CurveVisualItem
{
    public float StartValue;   // 起始时间（矩形底部）的值
    public float EndValue;     // 结束时间（矩形顶部）的值
    public float MinValue;     // 矩形左边缘对应的值
    public float MaxValue;     // 矩形右边缘对应的值
    public EaseCurve Ease;     // 缓动种类，求值复用游戏的 Easing.Apply
}

/// <summary> 单个 LaneMotion 的可视元素，传给 View 渲染（值类型；一个矩形含描边与头尾白带） </summary>
public struct MotionVisualItem
{
    public object? SelectionTarget;
    public double X, Y;
    public double Width, Height;
    public bool IsSelected;
    public bool IsHighlight;  // 高亮层元素
    public int SortTime;      // 绘制排序：startTime 升序，值大的后画、盖住值小的
    public int UnitOrder;     // startTime 相同时保持原列表顺序
    public CurveVisualItem[]? Curves;  // 内矩形内的数值曲线（null/空 = 只画中线）
}

/// <summary> 幽灵放置结果：一个 note 提交时的最终时间与轨道编号（值类型） </summary>
public struct NoteGhostPlacement
{
    public NoteUnit Unit;
    public int NewTime;
    public int NewLane;
}

/// <summary> 幽灵放置结果：一个事件 Unit 提交时的最终时间与轨道编号（值类型） </summary>
public struct LaneEventGhostPlacement
{
    public ILaneEventUnit Unit;
    public int NewTime;
    public int NewLane;
}

/// <summary> 幽灵放置结果：一个 BPM 提交时的最终时间（BPM 没有轨道概念，值类型） </summary>
public struct BpmGhostPlacement
{
    public BpmUnit Unit;
    public int NewTime;
}

/// <summary> Inspector 中 Note 的 Tune 表格行：Tune 引用 + 显示用编号/时间 </summary>
public sealed class TuneRowItem
{
    public TuneUnit Tune { get; init; } = null!;
    public int Number { get; init; }
    public int TimeMs { get; init; }

    /// <summary> 行文本“编号  目标时间：时间  →”，整行作为跳转按钮显示；按 Inspector 界面汉化开关取语言 </summary>
    public string Label => InspectorLocalization.IsInspectorLocalized
        ? $"{Number}  目标时间：{TimeMs}  →"
        : $"{Number}  TargetTime：{TimeMs}  →";
}
