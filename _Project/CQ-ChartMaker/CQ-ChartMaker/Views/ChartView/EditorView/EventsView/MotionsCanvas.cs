using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CQ_ChartMaker.Common;
using CQ_ChartMaker.Models;
using CQ_ChartMaker.ViewModels;
using CQMusicGame.Shared;

namespace CQ_ChartMaker.Views;

/// <summary> 事件条命中类型：身体（选中/移动）或上下白带外侧的拖拽把手（改变终点/起点） </summary>
public enum MotionHitKind
{
    Body,
    StartHandle,  // 下方把手：改 StartTime，终点不变
    EndHandle     // 上方把手：改 Duration，起点不变
}

/// <summary>
/// LaneMotion 显示层的自绘控件：无子控件，Render 中按列表顺序直接绘制
/// Motion 元素与放置预览，避免旧方案每帧创建 Border 带来的分配与布局开销。
/// 数据仍由 EventsViewModel 准备（画布坐标），本控件只负责呈现和命中测试。
/// </summary>
public class MotionsCanvas : Control
{
    // 与 EventsViewModel 的事件矩形几何保持一致：左右描边 2px、上下白带 5.25px
    private const double OutlineThickness = 2;
    private const double CapThickness = 5.25;

    // 白带外侧的把手命中厚度：比白带略大，鼠标更容易抓住
    private const double HandleThickness = 6;

    // 选中高亮色：与 Notes 层一致
    private static readonly IBrush HighlightBrush = new SolidColorBrush(Color.FromRgb(0, 174, 255));
    private static readonly IBrush PreviewBrush = Brushes.White;

    // 重叠展开选择条：列背景与悬浮高亮
    private static readonly IBrush OverlapPanelBrush = new SolidColorBrush(Color.Parse("#FF1A1A1A"));
    private static readonly IBrush OverlapPanelHoverBrush = new SolidColorBrush(Color.Parse("#FF2E2E2E"));

    // 每列上下边缘的浅灰细线（列间无缝时用于区分边界）
    private static readonly Pen OverlapPanelEdgePen = new(
        new SolidColorBrush(Color.Parse("#FFAAAAAA")), 1, null, PenLineCap.Flat, PenLineJoin.Miter, 10);

    // 数值曲线显示：中线浅灰、曲线 #FFDE38 细线（画笔建一次复用）
    private static readonly Pen MidValuePen = CreatePen(Color.FromRgb(0x8C, 0x8C, 0x8C), 1);
    private static readonly Pen CurvePen = CreatePen(Color.Parse("#FFDE38"), 1.5);

    private static Pen CreatePen(Color color, double thickness)
        => new(new SolidColorBrush(color), thickness, null, PenLineCap.Flat, PenLineJoin.Round, 10);

    private IBrush? _motionBrush;   // 内矩形
    private IBrush? _sideBrush;     // 两侧描边
    private IBrush? _strokeBrush;   // 头尾白带
    private bool _brushesLoaded;

    /// <summary> 挂载后读取 EventsView 的资源画刷（父级资源字典此时已可查） </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        EnsureBrushes();
    }

    private void EnsureBrushes()
    {
        if (_brushesLoaded) return;
        _motionBrush = TryFindBrush("MotionInnerBrush");
        _sideBrush = TryFindBrush("MotionSideBrush");
        _strokeBrush = TryFindBrush("NodeStrokeBrush");

        // 资源尚未就绪时保持未加载状态，下次 Render 再试
        _brushesLoaded = _motionBrush != null && _sideBrush != null && _strokeBrush != null;
    }

    private IBrush? TryFindBrush(object key)
        => this.TryFindResource(key, out object? value) ? value as IBrush : null;

    /// <summary> 每帧绘制整个 Motion 层与放置预览（只提交绘制指令，不创建控件） </summary>
    public override void Render(DrawingContext context)
    {
        EnsureBrushes();
        if (DataContext is not EventsViewModel vm) return;

        var items = vm.MotionVisualItems;
        for (int i = 0; i < items.Count; i++)
            DrawItem(context, items[i]);

        var preview = vm.PreviewItems;
        if (preview.Count > 0)
        {
            using (context.PushOpacity(0.4))
            {
                for (int i = 0; i < preview.Count; i++)
                {
                    var p = preview[i];
                    context.FillRectangle(PreviewBrush, new Rect(p.X, p.Y, p.Width, p.Height));
                }
            }
        }

        // 重叠提示画在最上层：即使事件条被其它元素盖住，也能看到感叹号
        OverlapMarkerPainter.Draw(context, vm.OverlapMarkers);

        DrawOverlapPanel(context, vm);
    }

    /// <summary>
    /// 绘制单个可视元素：高亮层 / 外框 / 内矩形 / 头尾白带。
    /// 外框 = 左右 2px 浅灰描边 + 上下 5.25px 白带；内矩形高度 = duration，
    /// duration 为 0 时内高为 0，只剩中间 10.5px 白条。
    /// </summary>
    private void DrawItem(DrawingContext context, MotionVisualItem item)
    {
        var rect = new Rect(item.X, item.Y, item.Width, item.Height);

        if (item.IsHighlight)
        {
            if (item.IsSelected)
                context.FillRectangle(HighlightBrush, rect);
            return;
        }

        if (_strokeBrush == null || _sideBrush == null || _motionBrush == null) return;

        // 浅灰铺满整个外框（作为两侧描边），上下再用白带覆盖
        context.FillRectangle(_sideBrush, rect);

        double innerW = item.Width - OutlineThickness * 2;
        double innerH = item.Height - CapThickness * 2;
        if (innerW > 0 && innerH > 0)
        {
            context.FillRectangle(_motionBrush, new Rect(
                item.X + OutlineThickness, item.Y + CapThickness, innerW, innerH));
        }

        // 头尾白带：横向铺满外框，覆盖上下边框
        context.FillRectangle(_strokeBrush, new Rect(item.X, item.Y, item.Width, CapThickness));
        context.FillRectangle(_strokeBrush, new Rect(
            item.X, item.Y + item.Height - CapThickness, item.Width, CapThickness));

        // 内矩形内的数值参考中线与曲线（没有曲线时也画中线）
        if (innerW > 0 && innerH > 0)
        {
            DrawValueCurves(context, item, new Rect(
                item.X + OutlineThickness, item.Y + CapThickness, innerW, innerH));
        }
    }

    /// <summary>
    /// 在内矩形里画数值参考中线与各通道的变化曲线。
    /// 横轴为数值（左 = 最小、中 = 中间值、右 = 最大），纵轴为时间（下 = 起始、上 = 结束）；
    /// 超出内矩形的部分由 PushClip 裁掉（如 Back 缓动的过冲、参考范围外的值）。
    /// 只采样视口内可见的那一段，超长事件不会产生大量不可见的绘制指令
    /// </summary>
    private void DrawValueCurves(DrawingContext context, MotionVisualItem item, Rect inner)
    {
        // 与时间轴使用同一视口高度（垂直方向不滚动）；Bounds 尚未完成布局时按完全可见处理
        double viewHeight = Bounds.Height;
        double visibleTop = viewHeight > 0 ? Math.Max(inner.Top, 0) : inner.Top;
        double visibleBottom = viewHeight > 0 ? Math.Min(inner.Bottom, viewHeight) : inner.Bottom;
        if (visibleBottom <= visibleTop) return;

        // 中线：所有有时长的事件都画，代表参考范围的中间值；只画视口内的一段
        double centerX = inner.Center.X;
        context.DrawLine(MidValuePen,
            new Point(centerX, visibleTop), new Point(centerX, visibleBottom));

        var curves = item.Curves;
        if (curves == null || curves.Length == 0) return;

        // 可见段对应的 t 范围：t = 0 在矩形底部（起始时间），t = 1 在顶部（结束时间）
        double tMin = Math.Clamp((inner.Bottom - visibleBottom) / inner.Height, 0, 1);
        double tMax = Math.Clamp((inner.Bottom - visibleTop) / inner.Height, 0, 1);
        if (tMax <= tMin) return;

        // 采样段数随可见像素高度自适应：矮矩形不浪费指令，高矩形也保持平滑
        int samples = Math.Clamp((int)((visibleBottom - visibleTop) / 6), 8, 48);
        double halfWidth = inner.Width * 0.5;

        // 裁剪到内矩形：值超出参考范围或缓动过冲的部分不显示
        using (context.PushClip(inner))
        {
            for (int ci = 0; ci < curves.Length; ci++)
            {
                CurveVisualItem curve = curves[ci];
                float halfSpan = (curve.MaxValue - curve.MinValue) * 0.5f;
                if (halfSpan <= 0f) continue;
                float mid = (curve.MaxValue + curve.MinValue) * 0.5f;

                Point prev = default;
                for (int i = 0; i <= samples; i++)
                {
                    double t = tMin + (tMax - tMin) * i / samples;
                    // 与游戏运行时、Inspector 曲线预览同源的缓动求值
                    float value = curve.StartValue
                        + (curve.EndValue - curve.StartValue) * Easing.Apply(curve.Ease, (float)t);
                    var point = new Point(
                        centerX + (value - mid) / halfSpan * halfWidth,
                        inner.Bottom - t * inner.Height);
                    if (i > 0) context.DrawLine(CurvePen, prev, point);
                    prev = point;
                }
            }
        }
    }

    /// <summary>
    /// 绘制横向展开选择条：每列一个深灰背景 + 列内完整事件（含内部曲线）。
    /// 列内事件的 Y 保持原时间位置，仅整体平移到对应列的水平位置。
    /// </summary>
    private void DrawOverlapPanel(DrawingContext context, EventsViewModel vm)
    {
        if (!vm.IsOverlapPanelOpen) return;

        var columns = vm.OverlapPanelColumns;
        if (columns.Count == 0) return;

        double colWidth = vm.OverlapPanelColumnWidth;
        double spacing = vm.OverlapPanelSpacing;
        double top = vm.OverlapPanelTop;
        double height = vm.OverlapPanelHeight;

        for (int i = 0; i < columns.Count; i++)
        {
            double left = vm.OverlapPanelLeft + i * (colWidth + spacing);
            var rect = new Rect(left, top, colWidth, height);
            context.FillRectangle(
                i == vm.OverlapPanelHoverIndex ? OverlapPanelHoverBrush : OverlapPanelBrush, rect);

            using (context.PushTransform(Matrix.CreateTranslation(left, 0)))
            {
                var items = columns[i].Items;
                for (int j = 0; j < items.Count; j++)
                    DrawItem(context, items[j]);
            }

            // 上下边缘浅灰细线
            context.DrawLine(OverlapPanelEdgePen,
                new Point(left, top), new Point(left + colWidth, top));
            context.DrawLine(OverlapPanelEdgePen,
                new Point(left, top + height), new Point(left + colWidth, top + height));
        }
    }

    /// <summary>
    /// 命中测试（不含把手）：从列表末尾向前遍历，返回第一个包含该点的元素。
    /// 列表按 startTime 升序绘制（末尾是 startTime 最大、视觉最上层），倒序即从上层往下找。
    /// 高亮层不参与命中。
    /// </summary>
    public MotionVisualItem? HitTestItem(Point point) => HitTestItem(point, false, out _);

    /// <summary>
    /// 命中测试重载：includeHandles 为 true 时，额外识别上下白带外侧的拖拽把手。
    /// 分两遍判定：先认身体、再认把手——可见的条永远优先于不可见的把手，
    /// 避免把手抢走另一个事件身体的点击；同一遍内按绘制顺序倒序，上层优先。
    /// </summary>
    public MotionVisualItem? HitTestItem(Point point, bool includeHandles, out MotionHitKind kind)
    {
        kind = MotionHitKind.Body;
        if (DataContext is not EventsViewModel vm) return null;

        var items = vm.MotionVisualItems;

        // 第一遍：身体（含白带与描边，即整个外框）
        for (int i = items.Count - 1; i >= 0; i--)
        {
            var item = items[i];
            if (item.IsHighlight) continue;
            if (new Rect(item.X, item.Y, item.Width, item.Height).Contains(point))
                return item;
        }

        if (!includeHandles) return null;

        // 第二遍：把手。只有有时长的事件才有起止两个把手
        for (int i = items.Count - 1; i >= 0; i--)
        {
            var item = items[i];
            if (item.IsHighlight) continue;
            if (item.SelectionTarget is not ILaneEventUnit { HasDuration: true }) continue;

            // 画布 Y 向下：上方 = 终点（时间更晚），下方 = 起点（时间更早）
            if (new Rect(item.X, item.Y - HandleThickness, item.Width, HandleThickness).Contains(point))
            {
                kind = MotionHitKind.EndHandle;
                return item;
            }
            if (new Rect(item.X, item.Y + item.Height, item.Width, HandleThickness).Contains(point))
            {
                kind = MotionHitKind.StartHandle;
                return item;
            }
        }
        return null;
    }
}
