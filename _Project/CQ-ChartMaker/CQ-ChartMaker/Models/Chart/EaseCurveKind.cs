using System;
using System.Collections.Generic;
using CQMusicGame.Shared;

namespace CQ_ChartMaker.Models;

/// <summary>
/// 动画的曲线种类（对应 Inspector 曲线选择第一框：Linear/Quad/Cubic/...）。
/// 与 EaseCurveDirection 组合后还原为存储在 LaneMotionData 里的 EaseCurve。
/// </summary>
public enum EaseCurveKind
{
    /// <summary> 线性（无方向，第二框隐藏） </summary>
    Linear,
    /// <summary> 二次 </summary>
    Quad,
    /// <summary> 三次 </summary>
    Cubic,
    /// <summary> 四次 </summary>
    Quart,
    /// <summary> 五次 </summary>
    Quint,
    /// <summary> 正弦 </summary>
    Sine,
    /// <summary> 指数 </summary>
    Expo,
    /// <summary> 圆形 </summary>
    Circ,
    /// <summary> 回弹 </summary>
    Back
}

/// <summary> 曲线种类下拉项：Kind 为值，Label 为显示名（直接用合并枚举 ToString 会显示 "InQuad" 这种名字） </summary>
public record CurveKindItem(EaseCurveKind Kind, string Label)
{
    // 下拉框选项列表（XAML 里 x:Static 绑定 ItemsSource；按 EaseCurveKind 枚举顺序，Linear 在首位）
    private static List<CurveKindItem>? _options;
    private static readonly Dictionary<EaseCurveKind, CurveKindItem> _lookup = new();

    /// <summary> 下拉框选项：Linear + 8 个曲线种类 </summary>
    public static List<CurveKindItem> Options
    {
        get
        {
            if (_options == null)
            {
                _options = new List<CurveKindItem>();
                foreach (EaseCurveKind kind in Enum.GetValues<EaseCurveKind>())
                {
                    var item = new CurveKindItem(kind, kind.ToString());
                    _options.Add(item);
                    _lookup[kind] = item;
                }
            }
            return _options;
        }
    }

    /// <summary> 按种类取下垃项，找不到返回 null </summary>
    public static CurveKindItem? ItemOf(EaseCurveKind kind)
    {
        _ = Options; // 触发列表构建
        return _lookup.TryGetValue(kind, out var item) ? item : null;
    }
}

public static class EaseCurveKindExtensions
{
    /// <summary> 种类 + 方向 → EaseCurve（方向为 null 时按 Linear 处理） </summary>
    public static EaseCurve ToEaseCurve(EaseCurveKind kind, EaseCurveDirection? direction)
    {
        if (kind == EaseCurveKind.Linear) return EaseCurve.Linear;
        string name = direction == null ? kind.ToString() : direction.Value.ToString() + kind;
        return Enum.TryParse<EaseCurve>(name, out var curve) ? curve : EaseCurve.Linear;
    }

    /// <summary> 取 EaseCurve 的种类部分（把 In/Out/InOut 前缀剥掉；Linear 原样返回） </summary>
    public static EaseCurveKind BaseOf(EaseCurve curve)
    {
        string name = curve.ToString();
        // "InOut" 必须在 "In" 之前判断，否则 InOutQuad 会被误剥成 OutQuad
        string baseName = name.StartsWith("InOut") ? name.Substring(5)
                        : name.StartsWith("In")    ? name.Substring(2)
                        : name.StartsWith("Out")   ? name.Substring(3)
                        : name;
        return Enum.TryParse<EaseCurveKind>(baseName, out var kind) ? kind : EaseCurveKind.Linear;
    }

    /// <summary> 取 EaseCurve 的方向部分，Linear 返回 null（表示无方向） </summary>
    public static EaseCurveDirection? DirectionOf(EaseCurve curve)
    {
        string name = curve.ToString();
        if (name.StartsWith("InOut")) return EaseCurveDirection.InOut;
        if (name.StartsWith("In"))    return EaseCurveDirection.In;
        if (name.StartsWith("Out"))   return EaseCurveDirection.Out;
        return null;
    }
}
