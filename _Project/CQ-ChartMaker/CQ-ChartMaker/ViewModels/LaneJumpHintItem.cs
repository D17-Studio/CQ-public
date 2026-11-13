using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CQ_ChartMaker.ViewModels;

/// <summary>
/// 某一列（轨道或事件类型）的跳转提示状态。
/// 画面内没有任何 unit 时，在轨道框右上/右下角悬浮显示上/下跳转三角按钮。
/// 用 ObservableObject 而不是值类型整体替换列表：列数不变时只更新属性，
/// 播放中不会每帧重建按钮控件。
/// </summary>
public partial class LaneJumpHintItem : ObservableObject
{
    /// <summary> 是否显示右上（向上跳）按钮 </summary>
    [ObservableProperty]
    private bool _showUp;

    /// <summary> 是否显示右下（向下跳）按钮 </summary>
    [ObservableProperty]
    private bool _showDown;

    /// <summary> 向上跳转的目标时间（毫秒，向上最近 unit 的头部） </summary>
    [ObservableProperty]
    private int _upTimeMs;

    /// <summary> 向下跳转的目标时间（毫秒，向下最近 unit 的尾部） </summary>
    [ObservableProperty]
    private int _downTimeMs;

    /// <summary>
    /// 让提示集合长度与列数一致：多删少补。
    /// 长度不变时保留现有 item，避免每帧重建按钮控件。
    /// </summary>
    public static void SyncCount(ObservableCollection<LaneJumpHintItem> hints, int count)
    {
        while (hints.Count > count) hints.RemoveAt(hints.Count - 1);
        while (hints.Count < count) hints.Add(new LaneJumpHintItem());
    }
}
