using System;
using System.Collections.Generic;

namespace CQ_ChartMaker.Models;

/// <summary>
/// 参与重叠判定的一个 unit：它当前显示在哪一列、起始时间是多少。
/// 列号由各编辑页按自己的显示模式计算（Notes = 可见轨道序号，Events = GetColumnOfUnit，Beats = 0）。
/// </summary>
public struct OverlapEntry
{
    public object Unit;
    public int Column;
    public int StartTime;
}

/// <summary> 重叠组内的一个成员；保留各自起始时间，供每个 unit 单独绘制感叹号 </summary>
public struct OverlapMember
{
    public object Unit;
    public int StartTime;
}

/// <summary> 同一显示列内起始时间相近（视觉上叠在一起）的一组 unit，成员数至少为 2 </summary>
public sealed class OverlapGroup
{
    public int Column;
    public List<OverlapMember> Members = new();
}

/// <summary>
/// 重叠提示的判定逻辑：只比较“显示列 + 起始时间”，duration 不参与。
/// 结果是每次刷新时现场派生的，不写回任何 Unit，避免撤销/改轨/切换显示模式后出现脏数据。
/// </summary>
public static class OverlapAnalyzer
{
    /// <summary> 判定容差（屏幕像素）：Note 头高 14px 的一半 </summary>
    public const double TolerancePx = 7.0;

    /// <summary> 展开选择条每列宽度相对轨道宽度的倍数；要调整列宽改这里 </summary>
    public const double PanelColumnWidthFactor = 1.0;

    /// <summary> 展开选择条背景上下留白（像素） </summary>
    public const double PanelPaddingPx = 6.0;

    /// <summary>
    /// 把屏幕像素容差按当前 PPS（像素/秒）折算成毫秒。
    /// 向上取整且至少 1ms：保证任何缩放下，视觉上重叠的起点都会被判定为重叠。
    /// </summary>
    public static int PxToMs(double px, double pps)
    {
        if (pps <= 0) return 1;
        return Math.Max(1, (int)Math.Ceiling(px / pps * 1000.0));
    }

    /// <summary>
    /// 按列分组，列内按起始时间排序，再把相邻时间差不超过容差的 unit 串成一组。
    /// 采用链式聚类：A≈B、B≈C 时 A/B/C 同组，与“视觉上叠成一摞”的语义一致。
    /// 只返回成员数 ≥ 2 的组。
    /// </summary>
    public static List<OverlapGroup> Build(List<OverlapEntry> entries, int toleranceMs)
    {
        var groups = new List<OverlapGroup>();
        if (entries.Count < 2) return groups;

        // 先按列聚合，避免跨列比较；列数量等于当前界面的轨道/类型列数，通常很小
        var byColumn = new Dictionary<int, List<OverlapEntry>>();
        foreach (var entry in entries)
        {
            if (!byColumn.TryGetValue(entry.Column, out var list))
            {
                list = new List<OverlapEntry>();
                byColumn.Add(entry.Column, list);
            }
            list.Add(entry);
        }

        foreach (var pair in byColumn)
        {
            var list = pair.Value;
            if (list.Count < 2) continue;
            list.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));

            // 从第一个 unit 开始尝试成组；与上一个成员的时间差超限就收尾并另起一组
            var group = new OverlapGroup { Column = pair.Key };
            group.Members.Add(ToMember(list[0]));
            int prevTime = list[0].StartTime;

            for (int i = 1; i < list.Count; i++)
            {
                var current = list[i];
                if (current.StartTime - prevTime <= toleranceMs)
                {
                    group.Members.Add(ToMember(current));
                }
                else
                {
                    if (group.Members.Count >= 2) groups.Add(group);
                    group = new OverlapGroup { Column = pair.Key };
                    group.Members.Add(ToMember(current));
                }
                prevTime = current.StartTime;
            }

            if (group.Members.Count >= 2) groups.Add(group);
        }

        return groups;
    }

    private static OverlapMember ToMember(in OverlapEntry entry)
        => new() { Unit = entry.Unit, StartTime = entry.StartTime };
}
