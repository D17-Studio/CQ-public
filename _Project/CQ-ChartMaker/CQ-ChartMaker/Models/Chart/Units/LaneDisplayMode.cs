namespace CQ_ChartMaker.Models;

/// <summary>
/// Events 页的显示模式。
/// ByType = 轨道：列是可见轨道，只显示当前类型的事件；
/// ByLane = 类型：列是 6 种事件类型，只显示当前轨道的事件；
/// Free = 平铺：列 = 可见轨道 × 可见类型（先分轨道再分类型）。
/// 注：UI 名称描述的是列轴方向，与枚举名“按什么浏览”的语义相反。
/// </summary>
public enum LaneDisplayMode
{
    ByType,
    ByLane,
    Free
}
