using System;
using System.Collections.Generic;
using CQMusicGame.Shared;

namespace CQ_ChartMaker.Models;

/// <summary>
/// 编辑器位置计算 —— 时间与屏幕 Y 坐标互转、网格吸附
/// </summary>
public static class PositionCalculator
{
    /// <summary>
    /// 目标时间（ms）→ 编辑器 Y 坐标（0=顶）
    /// </summary>
    public static double TimeToY(int targetTimeMs, int currentTimeMs,
        double pixelPerSecond, double judgmentLinePosition, double viewHeight)
    {
        double judgmentLineY = judgmentLinePosition * viewHeight;
        double deltaSec = (targetTimeMs - currentTimeMs) / 1000.0;
        return judgmentLineY - deltaSec * pixelPerSecond;
    }

    /// <summary>
    /// 编辑器 Y 坐标 → 时间（ms），反推用
    /// </summary>
    public static int YToTime(double y, int currentTimeMs,
        double pixelPerSecond, double judgmentLinePosition, double viewHeight)
    {
        double judgmentLineY = judgmentLinePosition * viewHeight;
        double deltaSec = (judgmentLineY - y) / pixelPerSecond;
        return currentTimeMs + (int)(deltaSec * 1000.0);
    }

    /// <summary>
    /// 将目标时间吸附到最近的节拍网格线。Free 时返回原值。
    /// bpmList 按 StartTime 升序排列。
    /// </summary>
    public static int TimeSnapToGrid(int targetTimeMs, List<BpmData> bpmList,
        Subdivision subdivision)
    {
        if (subdivision == Subdivision.Free || bpmList == null || bpmList.Count == 0)
            return targetTimeMs;

        // 找到目标时间所属的 BPM 段（最后一个 StartTime <= target 的点）
        // 若 target 早于所有 BPM 点，用第一个点作为参考
        int refIdx = 0;
        for (int i = 0; i < bpmList.Count; i++)
        {
            if (bpmList[i].StartTime <= targetTimeMs)
                refIdx = i;
            else
                break;
        }

        int denominator = (int)subdivision;
        var refBpm = bpmList[refIdx];

        // 参考段 BPM 非正时不吸附：60000/0 得到 Infinity，0*Infinity 会产生 NaN 无效结果
        if (refBpm.Bpm <= 0) return targetTimeMs;

        double subInterval = (60000.0 / refBpm.Bpm) / denominator;

        // 整除 + 四舍五入，找到最近网格线
        int snapped = (int)Math.Round(refBpm.StartTime
            + Math.Round((targetTimeMs - refBpm.StartTime) / subInterval)
            * subInterval);

        // 防止吸附越过下一个 BPM 变更点（不同 BPM 下网格间隔不同，结果无效）
        if (refIdx + 1 < bpmList.Count && snapped > bpmList[refIdx + 1].StartTime)
            snapped = bpmList[refIdx + 1].StartTime;

        return snapped;
    }

    /// <summary>
    /// 将 Canvas 内 X 坐标吸附到轨道编号（LaneIndex）。
    /// 每个轨道有效区域为轨宽 + 左右各一半间距，边界位于两轨正中间。
    /// 不在任何轨道范围内返回 -4。
    /// </summary>
    public static int XSnapToLane(double xInCanvas, double laneWidth, double laneSpacing,
        IList<int> visibleLanes)
    {
        double halfSpace = laneSpacing / 2.0;
        double cellWidth = laneWidth + laneSpacing;

        if (xInCanvas < -halfSpace)
            return -4;

        int ordinal = (int)((xInCanvas + halfSpace) / cellWidth);

        if (ordinal >= visibleLanes.Count)
            return -4;

        return visibleLanes[ordinal];
    }
}
