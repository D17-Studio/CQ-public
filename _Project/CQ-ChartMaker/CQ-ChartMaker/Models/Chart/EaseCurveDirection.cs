namespace CQ_ChartMaker.Models;

/// <summary>
/// 动画的方向（对应 Inspector 曲线选择第二框：In/Out/InOut）。
/// 实际存储的 EaseCurve = 曲线种类 + 方向，例如 Quad + In → InQuad；
/// Linear 没有方向（其对应索引为 -1，第二框隐藏）。
/// </summary>
public enum EaseCurveDirection
{
    /// <summary> 加速（缓入） </summary>
    In,
    /// <summary> 减速（缓出） </summary>
    Out,
    /// <summary> 先加速后减速 </summary>
    InOut
}

public static class EaseCurveDirectionExtensions
{
    /// <summary> 下拉框索引 → 方向枚举，-1 或越界返回 null（表示无方向/Linear） </summary>
    public static EaseCurveDirection? FromIndex(int index)
        => index == 0 ? EaseCurveDirection.In
         : index == 1 ? EaseCurveDirection.Out
         : index == 2 ? EaseCurveDirection.InOut
         : null;

    /// <summary> 下拉框选中索引：0=In / 1=Out / 2=InOut，无方向返回 -1 </summary>
    public static int ToIndex(EaseCurveDirection? direction)
        => direction switch
        {
            EaseCurveDirection.In => 0,
            EaseCurveDirection.Out => 1,
            EaseCurveDirection.InOut => 2,
            _ => -1
        };
}
