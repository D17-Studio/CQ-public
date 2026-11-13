using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using CQ_ChartMaker.ViewModels;

namespace CQ_ChartMaker.Common;

/// <summary>
/// 重叠提示标记的绘制器：浅红圆环 + 浅红感叹号。
/// 三个编辑画布（Notes / Events / Beats）共用同一套外观；标记只做视觉提示，不参与命中测试。
/// </summary>
public static class OverlapMarkerPainter
{
    /// <summary> 圆环直径（像素）；后续调整外观直接改这里 </summary>
    private const double Diameter = 14;

    private const double RingThickness = 2;

    // 标记颜色：#FF4938
    private static readonly IBrush MarkBrush = new SolidColorBrush(Color.Parse("#FF4938"));

    // 圆环用画笔；与项目里其它画笔一样带上圆角端点
    private static readonly Pen RingPen = new(
        MarkBrush, RingThickness, null, PenLineCap.Round, PenLineJoin.Round, 10);

    /// <summary> 在绘制上下文的最上层画出所有重叠标记（调用方应在 unit/预览之后调用） </summary>
    public static void Draw(DrawingContext context, IReadOnlyList<OverlapMarkerItem> markers)
    {
        if (markers.Count == 0) return;

        double radius = Diameter / 2;
        const double barWidth = 2;

        for (int i = 0; i < markers.Count; i++)
        {
            var marker = markers[i];
            var center = new Point(marker.X, marker.Y);

            // 圆环：不填充，内部保持透明，避免遮住 unit 本体
            context.DrawEllipse(null, RingPen, center, radius, radius);

            // 感叹号：竖条 + 下方圆点，整体在圆环内居中
            context.FillRectangle(MarkBrush,
                new Rect(marker.X - barWidth / 2, marker.Y - 4.5, barWidth, 6));
            context.FillRectangle(MarkBrush,
                new Rect(marker.X - barWidth / 2, marker.Y + 3, barWidth, barWidth));
        }
    }
}
