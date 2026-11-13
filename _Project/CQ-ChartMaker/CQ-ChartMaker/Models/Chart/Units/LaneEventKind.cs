namespace CQ_ChartMaker.Models;

/// <summary>
/// 六类 LaneEvent 的编辑类型，顺序同时决定 Events 页 ←→ 切页顺序。
/// </summary>
public enum LaneEventKind
{
    Transform,
    Opacity,
    Length,
    Speed,
    Zone,
    Parent
}

/// <summary> LaneEvent 类型的显示信息：枚举顺序即类型模式的列顺序 </summary>
public static class LaneEventKindInfo
{
    /// <summary> 类型数量（类型模式的固定列数） </summary>
    public const int Count = 6;

    /// <summary> 类型模式下每个类型列的显示名 </summary>
    public static string GetLabel(LaneEventKind kind) => kind switch
    {
        LaneEventKind.Transform => "变换",
        LaneEventKind.Opacity => "不透明度",
        LaneEventKind.Length => "长度",
        LaneEventKind.Speed => "流速",
        LaneEventKind.Zone => "判定区",
        LaneEventKind.Parent => "父轨",
        _ => kind.ToString()
    };
}
