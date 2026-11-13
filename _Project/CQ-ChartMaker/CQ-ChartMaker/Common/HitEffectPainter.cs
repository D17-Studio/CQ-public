using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using CQMusicGame.Shared;

namespace CQ_ChartMaker.Common;

/// <summary> 单个判定点的打击特效数据：判定时刻的轨道头世界坐标 + 距判定时刻的毫秒数 </summary>
public struct HitEffectInfo
{
    public Vec2 Position;
    public float AgeMs;
}

/// <summary>
/// 打击特效绘制器：所有特效的样式、动画曲线和组合方式集中在本文件。
/// 新增特效只需写一个私有绘制方法，在 Draw 中调用，并把持续时间计入 WindowMs。
/// </summary>
public static class HitEffectPainter
{
    // === 圆环特效参数（调整外观改这里） ===
    private const int RingDurationMs = 500;     // 圆环持续时长
    private const double RingStartRadius = 25;  // 起始半径：与轨道头白环一致
    private const double RingEndRadius = 130;   // 终止半径
    private const double RingStartAlpha = 0.85; // 初始不透明度
    private const double RingLineWidth = 4;     // 线宽（世界单位）

    /// <summary> 判定点特效的整体持续时间（ms）：ViewModel 按此窗口向前查询判定点 </summary>
    public static int WindowMs => RingDurationMs;

    // 浅黄圆环画笔（建一次复用，避免每帧分配）
    private static readonly Pen RingPen = new(
        new SolidColorBrush(Color.Parse("#FFE89A")),
        RingLineWidth, null, PenLineCap.Round, PenLineJoin.Round, 10);

    /// <summary>
    /// 绘制所有判定点的打击特效。调用方已应用相机变换，位置与半径均按世界单位处理。
    /// </summary>
    public static void Draw(DrawingContext context, IReadOnlyList<HitEffectInfo> effects)
    {
        for (int i = 0; i < effects.Count; i++)
        {
            HitEffectInfo effect = effects[i];
            DrawRing(context, effect.Position, effect.AgeMs);
            // 以后新增组合特效：在这里继续调用，各方法内部自行判断自己的时间窗口
        }
    }

    /// <summary> 扩散圆环：半径先快后慢地扩大，同时逐渐变淡 </summary>
    private static void DrawRing(DrawingContext context, Vec2 position, float ageMs)
    {
        if (ageMs >= RingDurationMs) return;

        double progress = ageMs / (double)RingDurationMs;

        // 二次缓出：半径增长先快后慢
        double eased = 1 - (1 - progress) * (1 - progress);
        double radius = RingStartRadius + (RingEndRadius - RingStartRadius) * eased;
        double alpha = RingStartAlpha * (1 - progress);

        // PushOpacity 让后续绘制整体乘以 alpha；DrawEllipse 第一参数为 null 表示只描边
        using (context.PushOpacity(alpha))
        {
            context.DrawEllipse(null, RingPen,
                new Point(position.x, position.y), radius, radius);
        }
    }
}
