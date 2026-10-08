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

/// <summary> 单个 note 的可视元素，传给 View 渲染（值类型） </summary>
public struct NoteVisualItem
{
    public object? SelectionTarget;
    public NoteType Type;
    public bool IsHead;
    public double X, Y;
    public double Width, Height;
    public bool IsSelected;
    public bool IsHighlight;  // 高亮层元素
    public bool IsTriangle;   // Tune 三角（独立于 head）
    public TuneDirection Direction;
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

/// <summary> 框选矩形（画布坐标），传给 View 画半透明选择框（值类型） </summary>
public struct SelectionRectItem
{
    public double X, Y;
    public double Width, Height;
}

/// <summary> 单个 LaneMotion 的可视元素，传给 View 渲染（值类型；样式同 Dash，无 Type/三角） </summary>
public struct MotionVisualItem
{
    public object? SelectionTarget;
    public bool IsHead;
    public double X, Y;
    public double Width, Height;
    public bool IsSelected;
    public bool IsHighlight;  // 高亮层元素
}

/// <summary> 幽灵放置结果：一个 note 提交时的最终时间与轨道编号（值类型） </summary>
public struct NoteGhostPlacement
{
    public NoteUnit Unit;
    public int NewTime;
    public int NewLane;
}

/// <summary> 幽灵放置结果：一个 LaneMotion 提交时的最终时间与轨道编号（值类型） </summary>
public struct MotionGhostPlacement
{
    public MotionUnit Unit;
    public int NewTime;
    public int NewLane;
}

/// <summary> 幽灵放置结果：一个 BPM 提交时的最终时间（BPM 没有轨道概念，值类型） </summary>
public struct BpmGhostPlacement
{
    public BpmUnit Unit;
    public int NewTime;
}
