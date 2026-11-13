using System;
using System.Collections.Generic;
using CQMusicGame.Shared;

namespace CQ_ChartMaker.Models;

/// <summary>
/// 根据 BPM 列表生成节拍网格。缓存型，BPM 不变时不重建。
/// 边界规则：beatTime ∈ [segStart, segEnd)，重合线归属后一段。
/// 最后一段没有下一个 BPM 点，边界取请求区间的末端，因此节拍线可延伸到任意时间。
/// 密度控制：可见主拍超过 MaxBeatLines 时按 2 的幂抽稀（2/4/8… 根取 1 根）；
/// 细分线超过 MaxBeatLines 时同样按 2 的幂抽稀（等同于自动降低细分档位）；
/// 可见主拍超过 MaxSubBeatBeats 时不生成细分线。避免 BPM 极高时控件数量爆炸导致卡死。
/// </summary>
public class BeatGridModel
{
    /// <summary> 屏幕上主拍线数量上限（超过后按 2 的幂抽稀） </summary>
    private const int MaxBeatLines = 256;

    /// <summary> 可见主拍超过该值时不再绘制细分线 </summary>
    private const int MaxSubBeatBeats = 128;

    private readonly List<BpmData> _bpmPoints;

    public BeatGridModel(List<BpmData> bpmPoints)
    {
        _bpmPoints = bpmPoints;
    }

    /// <summary>
    /// 填充指定时间区间内的所有节拍到 result 列表（先 Clear 再填充）。
    /// 可见主拍过多时按 2 的幂抽稀：&gt;256 根只画 2 的倍数、&gt;512 根只画 4 的倍数，以此类推。
    /// BeatIndex 始终是真实序号，抽稀后画面上的标签会跳号。
    /// </summary>
    public void GetBeatsInRange(int startTimeMs, int endTimeMs, List<BeatInfo> result)
    {
        result.Clear();
        if (_bpmPoints.Count == 0 || endTimeMs < startTimeMs) return;

        // 密度控制：先数出可见区间的节拍总数，据此决定抽稀步长，保证屏幕上不超过 MaxBeatLines 根。
        // 步长为 0 表示数据极端到步长溢出，直接不画
        long stride = ComputeStride(CountBeatsInRange(startTimeMs, endTimeMs));
        if (stride == 0) return;

        long beatIndexBase = 0; // 当前段第 0 拍的全局序号

        for (int i = 0; i < _bpmPoints.Count; i++)
        {
            int segStart = _bpmPoints[i].StartTime;
            if (segStart > endTimeMs) break; // BPM 点按时间升序，后面的段都在可见区间之后

            // 最后一段没有下一个 BPM 点作边界，延伸到请求区间末端，使时间轴滚动超过音频末尾后仍能生成节拍线
            int segEnd = i + 1 < _bpmPoints.Count
                ? _bpmPoints[i + 1].StartTime
                : endTimeMs;
            if (segEnd <= segStart) continue; // 空段或时间倒退的异常数据

            double interval = 60000.0 / _bpmPoints[i].Bpm;
            if (!IsValidInterval(interval)) continue; // 非法 BPM 段不产生线，序号也不累计

            // 段内总拍数：满足 segStart + k*interval < segEnd 的 k 的个数，用于累计全局序号
            long segTotal = SafeLong(Math.Max(0, Math.Ceiling(((double)segEnd - segStart) / interval)));

            // 绘制下标区间：从可见起点快进（避免长段从段首逐拍空转），到可见末端截断
            double segLimit = Math.Min((double)segEnd, endTimeMs + 0.5);
            long kLast = SafeLong(Math.Ceiling((segLimit - segStart) / interval)) - 1;
            long kFirst = SafeLong(Math.Max(0, Math.Ceiling((startTimeMs - 0.5 - segStart) / interval)));

            // 对齐到 stride 的倍数：只画段内下标为 0、stride、2*stride… 的拍
            long k = kFirst / stride * stride;
            if (k < kFirst) k += stride;

            while (k >= 0 && k <= kLast)
            {
                double t = segStart + (double)k * interval;
                if (t >= int.MinValue && t <= int.MaxValue)
                {
                    int timeMs = (int)Math.Round(t);
                    if (timeMs >= startTimeMs && timeMs <= endTimeMs)
                        result.Add(new BeatInfo { TimeMs = timeMs, BeatIndex = SafeInt(beatIndexBase + k) });
                }

                if (k > long.MaxValue - stride) break; // 防极端值下 k 溢出成负数导致死循环
                k += stride;
            }

            beatIndexBase = SafeLong((double)beatIndexBase + segTotal);
            if (beatIndexBase > int.MaxValue) beatIndexBase = int.MaxValue; // 序号只用于显示，封顶
        }
    }

    /// <summary>
    /// 填充指定区间内的细分线到 result 列表。denominator 为细分分母（2/3/4/6/8/12/16），&lt;=1 时不生成。
    /// 每个主节拍区间内均匀插入 denominator-1 条细分线，跳过主拍位置（0号位）。
    /// 可见主拍超过 MaxSubBeatBeats 时直接清空；未超限时细分线总数也限流到 MaxBeatLines，
    /// 超出只保留第 stride、2*stride… 个细分位（视觉上等同于降低细分档位）。
    /// </summary>
    public void GetSubBeatsInRange(int startTimeMs, int endTimeMs, int denominator, List<BeatInfo> result)
    {
        result.Clear();
        if (_bpmPoints.Count == 0 || denominator <= 1 || endTimeMs < startTimeMs) return;

        // 密度控制：可见主拍过多时不画细分线（否则控件数量会再翻数倍）
        double beatCount = CountBeatsInRange(startTimeMs, endTimeMs);
        if (beatCount > MaxSubBeatBeats) return;

        // 细分线自身也限流：每个主拍保留 (denominator-1)/stride 条细分线（s = stride、2*stride…），
        // 取满足总数不超过 MaxBeatLines 的最小 2 的幂步长
        long subStride = 1;
        while (subStride < denominator &&
               beatCount * ((denominator - 1) / subStride) > MaxBeatLines)
        {
            subStride <<= 1;
        }

        for (int i = 0; i < _bpmPoints.Count; i++)
        {
            int segStart = _bpmPoints[i].StartTime;
            if (segStart > endTimeMs) break;

            // 同 GetBeatsInRange：最后一段延伸到请求区间末端
            int segEnd = i + 1 < _bpmPoints.Count
                ? _bpmPoints[i + 1].StartTime
                : endTimeMs;
            if (segEnd <= segStart) continue;

            double interval = 60000.0 / _bpmPoints[i].Bpm;
            if (!IsValidInterval(interval)) continue;

            double subInterval = interval / denominator;
            if (!IsValidInterval(subInterval)) continue;

            // 主拍下标区间：从可见起点前一拍开始（细分线可能跨入可见区），到可见末端截断
            double segLimit = Math.Min((double)segEnd, endTimeMs + 0.5);
            long kLast = SafeLong(Math.Ceiling((segLimit - segStart) / interval)) - 1;
            long kFirst = SafeLong(Math.Max(0, Math.Floor((startTimeMs - (double)segStart) / interval)));

            long k = kFirst;
            while (k >= 0 && k <= kLast)
            {
                double t = segStart + (double)k * interval;
                // 只画 s 为 subStride 倍数的细分位（未抽稀时 subStride = 1，即全部细分线）
                for (long s = subStride; s < denominator; s += subStride)
                {
                    double subT = t + s * subInterval;
                    if (subT < int.MinValue || subT > int.MaxValue) continue;
                    int timeMs = (int)Math.Round(subT);
                    if (timeMs >= startTimeMs && timeMs <= endTimeMs)
                        result.Add(new BeatInfo { TimeMs = timeMs });
                }

                if (k == long.MaxValue) break;
                k++;
            }
        }
    }

    /// <summary>
    /// 统计 [startTimeMs, endTimeMs] 内可见的主拍数量。
    /// 按段纯数学计算，不逐拍循环，供抽稀步长和细分线限流共用。
    /// </summary>
    private double CountBeatsInRange(int startTimeMs, int endTimeMs)
    {
        double total = 0;

        for (int i = 0; i < _bpmPoints.Count; i++)
        {
            int segStart = _bpmPoints[i].StartTime;
            if (segStart > endTimeMs) break;

            int segEnd = i + 1 < _bpmPoints.Count
                ? _bpmPoints[i + 1].StartTime
                : endTimeMs;
            if (segEnd <= segStart) continue;

            double interval = 60000.0 / _bpmPoints[i].Bpm;
            if (!IsValidInterval(interval)) continue;

            double segLimit = Math.Min((double)segEnd, endTimeMs + 0.5);
            long kLast = SafeLong(Math.Ceiling((segLimit - segStart) / interval)) - 1;
            long kFirst = SafeLong(Math.Max(0, Math.Ceiling((startTimeMs - 0.5 - segStart) / interval)));
            if (kFirst > kLast) continue;

            total += (double)kLast - kFirst + 1;
        }

        return total;
    }

    /// <summary> 节拍间隔必须为正的有限值：BPM ≤ 0、NaN、无穷都会产生无效网格甚至死循环 </summary>
    private static bool IsValidInterval(double interval)
        => double.IsFinite(interval) && interval > 0;

    /// <summary>
    /// 由可见主拍总数计算抽稀步长（1/2/4/8…），使绘制根数不超过 MaxBeatLines。
    /// 返回 0 表示数据极端到步长溢出，调用方应放弃绘制。
    /// </summary>
    private static long ComputeStride(double count)
    {
        if (count <= MaxBeatLines) return 1;

        long stride = 1;
        while (count / stride > MaxBeatLines)
        {
            if (stride > long.MaxValue / 4) return 0;
            stride <<= 1;
        }
        return stride;
    }

    /// <summary> double → long 的安全转换：越界时钳制，避免 unchecked 转换得到无意义值 </summary>
    private static long SafeLong(double value)
    {
        if (double.IsNaN(value)) return 0;
        if (value >= long.MaxValue) return long.MaxValue;
        if (value <= long.MinValue) return long.MinValue;
        return (long)value;
    }

    /// <summary> long → int 的安全转换：BeatIndex 只用于显示，越界后钳制 </summary>
    private static int SafeInt(long value)
        => value > int.MaxValue ? int.MaxValue : value < int.MinValue ? int.MinValue : (int)value;
}


/// <summary> 单个节拍的信息（值类型，避免堆分配） </summary>
public struct BeatInfo
{
    public int TimeMs;
    public int BeatIndex;
}
