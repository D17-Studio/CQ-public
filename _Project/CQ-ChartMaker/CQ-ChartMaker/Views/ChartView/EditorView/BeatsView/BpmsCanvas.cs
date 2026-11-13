using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CQ_ChartMaker.Common;
using CQ_ChartMaker.ViewModels;

namespace CQ_ChartMaker.Views;

/// <summary>
/// BPM 节点显示层的自绘控件：无子控件，Render 中按列表顺序直接绘制
/// BPM 节点与放置预览，避免旧方案每帧创建 Border 带来的分配与布局开销。
/// 数据仍由 BeatsViewModel 准备（画布坐标），本控件只负责呈现和命中测试。
/// </summary>
public class BpmsCanvas : Control
{
    // 与旧 BuildBpmNode 中的内芯高度保持一致
    private const double InnerHeight = 8;

    // 选中高亮色：与 Notes 层一致
    private static readonly IBrush HighlightBrush = new SolidColorBrush(Color.FromRgb(0, 174, 255));
    private static readonly IBrush PreviewBrush = Brushes.White;

    // 重叠展开选择条：列背景与悬浮高亮
    private static readonly IBrush OverlapPanelBrush = new SolidColorBrush(Color.Parse("#FF1A1A1A"));
    private static readonly IBrush OverlapPanelHoverBrush = new SolidColorBrush(Color.Parse("#FF2E2E2E"));

    // 每列上下边缘的浅灰细线（列间无缝时用于区分边界）
    private static readonly Pen OverlapPanelEdgePen = new(
        new SolidColorBrush(Color.Parse("#FFAAAAAA")), 1, null, PenLineCap.Flat, PenLineJoin.Miter, 10);

    private IBrush? _bpmBrush;
    private IBrush? _strokeBrush;
    private bool _brushesLoaded;

    /// <summary> 挂载后读取 BeatsView 的资源画刷（父级资源字典此时已可查） </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        EnsureBrushes();
    }

    private void EnsureBrushes()
    {
        if (_brushesLoaded) return;
        _bpmBrush    = TryFindBrush("BpmBrush");
        _strokeBrush = TryFindBrush("NodeStrokeBrush");

        // 资源尚未就绪时保持未加载状态，下次 Render 再试
        _brushesLoaded = _bpmBrush != null && _strokeBrush != null;
    }

    private IBrush? TryFindBrush(object key)
        => this.TryFindResource(key, out object? value) ? value as IBrush : null;

    /// <summary> 每帧绘制整个 BPM 节点层与放置预览（只提交绘制指令，不创建控件） </summary>
    public override void Render(DrawingContext context)
    {
        EnsureBrushes();
        if (DataContext is not BeatsViewModel vm) return;

        var items = vm.BpmVisualItems;
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

        // 重叠提示画在最上层：即使 BPM 节点被其它元素盖住，也能看到感叹号
        OverlapMarkerPainter.Draw(context, vm.OverlapMarkers);

        DrawOverlapPanel(context, vm);
    }

    /// <summary> 绘制单个可视元素：高亮层 / 节点 </summary>
    private void DrawItem(DrawingContext context, BpmVisualItem item)
    {
        var rect = new Rect(item.X, item.Y, item.Width, item.Height);

        if (item.IsHighlight)
        {
            if (item.IsSelected)
                context.FillRectangle(HighlightBrush, rect);
            return;
        }

        if (_strokeBrush == null || _bpmBrush == null) return;

        // 节点：白色外框 + 居中的灰色内芯（宽 = 节点宽 - 6）
        context.FillRectangle(_strokeBrush, rect);
        double innerW = item.Width - 6;
        double x = item.X + (item.Width - innerW) / 2;
        double y = item.Y + (item.Height - InnerHeight) / 2;
        context.FillRectangle(_bpmBrush, new Rect(x, y, innerW, InnerHeight));
    }

    /// <summary>
    /// 绘制横向展开选择条：每列一个深灰背景 + 列内 BPM 节点。
    /// 列内节点的 Y 保持原时间位置，仅整体平移到对应列的水平位置。
    /// </summary>
    private void DrawOverlapPanel(DrawingContext context, BeatsViewModel vm)
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
    /// 命中测试：从列表末尾（最上层）向前遍历，返回第一个包含该点且参与命中的元素。
    /// 返回完整可视项而不是 SelectionTarget：View 需要拿到 IsSelected 判断是否延迟收窄。
    /// 高亮层不参与命中。
    /// </summary>
    public BpmVisualItem? HitTestItem(Point point)
    {
        if (DataContext is not BeatsViewModel vm) return null;

        var items = vm.BpmVisualItems;
        for (int i = items.Count - 1; i >= 0; i--)
        {
            var item = items[i];
            if (item.IsHighlight) continue;
            if (new Rect(item.X, item.Y, item.Width, item.Height).Contains(point))
                return item;
        }
        return null;
    }
}
