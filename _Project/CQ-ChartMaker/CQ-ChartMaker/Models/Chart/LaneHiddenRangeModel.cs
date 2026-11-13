using System;
using System.Collections.Generic;
using CQMusicGame.Shared;

namespace CQ_ChartMaker.Models;

/// <summary> 一段"轨道完全隐藏"的时间区间：Head/Line/Label/Note 四个不透明度通道同时为 0 </summary>
public readonly record struct LaneHiddenRange(int StartTime, int EndTime);

/// <summary>
/// 计算"轨道完全隐藏"的时间区间，供 Notes/Events 页的区间变暗提示使用。
/// 判定以 LaneState 的实际求值为准：三通道默认 0、Note 默认 1，
/// 相对值/缓动/末段持续都与游戏运行时保持一致。
/// 数据只在 Opacity 事件变化时重建（见 ChartSheetModel），播放滚动不重算。
/// </summary>
public sealed class LaneHiddenRangeModel
{
    private static readonly List<LaneHiddenRange> Empty = new();

    private readonly Dictionary<int, List<LaneHiddenRange>> _ranges = new();

    /// <summary> 是否存在任何隐藏区间（渲染侧可据此跳过逐轨查询） </summary>
    public bool HasAny { get; private set; }

    /// <summary> 查询某条轨道的隐藏区间（无则返回空列表，区间按时间升序且互不重叠） </summary>
    public IReadOnlyList<LaneHiddenRange> GetRanges(int laneIndex)
        => _ranges.TryGetValue(laneIndex, out var list) ? list : Empty;

    /// <summary> 按当前事件数据重建全部轨道的隐藏区间 </summary>
    public void Rebuild(ChartSheetModel sheet)
    {
        _ranges.Clear();
        HasAny = false;

        var opacities = sheet.ChartSheet.GetOpacityEvents();
        if (opacities.Count == 0) return;

        // 现场构建 LaneState：与游戏运行时求值语义一致（忽略父轨，透明度通道不受父轨影响）
        LaneState state = sheet.CreateLaneState(includeParents: false);

        // 每条轨道收集所有 Opacity 事件的起止时间作为断点：
        // 断点之间四个通道各自处于同一条曲线内，只需采样一次即可判定整段是否全 0
        var breakpointsByLane = new Dictionary<int, List<int>>();
        foreach (var e in opacities)
        {
            if (!breakpointsByLane.TryGetValue(e.LaneIndex, out var points))
                breakpointsByLane[e.LaneIndex] = points = new List<int>(4);
            points.Add(e.StartTime);
            points.Add(e.StartTime + e.Duration);
        }

        foreach (var pair in breakpointsByLane)
        {
            int lane = pair.Key;
            var points = pair.Value;
            points.Sort();

            var ranges = new List<LaneHiddenRange>();
            for (int i = 0; i < points.Count - 1; i++)
            {
                int start = points[i];
                int end = points[i + 1];
                if (end <= start) continue;

                int mid = start + (end - start) / 2;
                if (IsHidden(state, lane, mid))
                    AppendRange(ranges, start, end);
            }

            // 最后一段事件结束后会保持 EndValue（DoPersistEnd），不会回落到默认值：
            // 在末尾断点之后再采样一次，确认隐藏则一直延续到无穷
            int tail = points[^1];
            if (tail < int.MaxValue && IsHidden(state, lane, tail + 1))
                AppendRange(ranges, tail, int.MaxValue);

            if (ranges.Count == 0) continue;
            _ranges[lane] = ranges;
            HasAny = true;
        }
    }

    /// <summary> 某一时刻四个不透明度通道是否全部为 0（≤0 视为完全透明） </summary>
    private static bool IsHidden(LaneState state, int laneIndex, int time)
        => state.HeadOpacity(laneIndex, time) <= 0f
           && state.LineOpacity(laneIndex, time) <= 0f
           && state.LabelOpacity(laneIndex, time) <= 0f
           && state.NoteOpacity(laneIndex, time) <= 0f;

    /// <summary> 追加区间；与上一段首尾相接时合并 </summary>
    private static void AppendRange(List<LaneHiddenRange> ranges, int start, int end)
    {
        if (ranges.Count > 0 && ranges[^1].EndTime == start)
            ranges[^1] = new LaneHiddenRange(ranges[^1].StartTime, end);
        else
            ranges.Add(new LaneHiddenRange(start, end));
    }
}
