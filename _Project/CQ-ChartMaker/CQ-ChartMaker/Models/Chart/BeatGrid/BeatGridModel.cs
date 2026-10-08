using System;
using System.Collections.Generic;
using CQMusicGame.Shared;

namespace CQ_ChartMaker.Models;

/// <summary>
/// 根据 BPM 列表生成节拍网格。缓存型，BPM 不变时不重建。
/// 边界规则：beatTime ∈ [segStart, segEnd)，重合线归属后一段。
/// 最后一段没有下一个 BPM 点，边界取请求区间的末端，因此节拍线可延伸到任意时间。
/// </summary>
public class BeatGridModel
{
    private readonly List<BpmData> _bpmPoints;

    public BeatGridModel(List<BpmData> bpmPoints)
    {
        _bpmPoints = bpmPoints;
    }

    /// <summary>
    /// 填充指定时间区间内的所有节拍到 result 列表（先 Clear 再填充）
    /// </summary>
    public void GetBeatsInRange(int startTimeMs, int endTimeMs, List<BeatInfo> result)
    {
        result.Clear();
        if (_bpmPoints.Count == 0) return;

        int beatIndex = 0;

        for (int i = 0; i < _bpmPoints.Count; i++)
        {
            int segStart = _bpmPoints[i].StartTime;
            // 最后一段没有下一个 BPM 点作边界，延伸到请求区间末端，使时间轴滚动超过音频末尾后仍能生成节拍线
            int segEnd = i + 1 < _bpmPoints.Count
                ? _bpmPoints[i + 1].StartTime
                : endTimeMs;

            double interval = 60000.0 / _bpmPoints[i].Bpm;
            double t = segStart;

            while (t < segEnd)
            {
                int timeMs = (int)Math.Round(t);
                if (timeMs >= startTimeMs && timeMs <= endTimeMs)
                    result.Add(new BeatInfo { TimeMs = timeMs, BeatIndex = beatIndex });

                beatIndex++;
                t += interval;
            }
        }
    }
    /// <summary>
    /// 填充指定区间内的细分线到 result 列表。denominator 为细分分母（2/3/4/6/8），<=1 时不生成。
    /// 每个主节拍区间内均匀插入 denominator-1 条细分线，跳过主拍位置（0号位）。
    /// </summary>
    public void GetSubBeatsInRange(int startTimeMs, int endTimeMs, int denominator, List<BeatInfo> result)
    {
        result.Clear();
        if (_bpmPoints.Count == 0 || denominator <= 1) return;

        for (int i = 0; i < _bpmPoints.Count; i++)
        {
            int segStart = _bpmPoints[i].StartTime;
            // 同 GetBeatsInRange：最后一段延伸到请求区间末端
            int segEnd = i + 1 < _bpmPoints.Count
                ? _bpmPoints[i + 1].StartTime
                : endTimeMs;

            double interval = 60000.0 / _bpmPoints[i].Bpm;
            double subInterval = interval / denominator;

            double t = segStart;
            while (t < segEnd)
            {
                for (int s = 1; s < denominator; s++)
                {
                    int timeMs = (int)Math.Round(t + s * subInterval);
                    if (timeMs >= startTimeMs && timeMs <= endTimeMs)
                        result.Add(new BeatInfo { TimeMs = timeMs });
                }
                t += interval;
            }
        }
    }
}


/// <summary> 单个节拍的信息（值类型，避免堆分配） </summary>
public struct BeatInfo
{
    public int TimeMs;
    public int BeatIndex;
}
