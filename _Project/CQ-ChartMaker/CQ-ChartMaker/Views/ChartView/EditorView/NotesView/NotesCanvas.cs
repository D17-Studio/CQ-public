using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CQ_ChartMaker.Common;
using CQ_ChartMaker.ViewModels;
using CQMusicGame.Shared;

namespace CQ_ChartMaker.Views;

/// <summary>
/// Note 显示层的自绘控件：整个区域没有任何子控件，Render 中按列表顺序直接绘制
/// Note 元素与放置预览，避免旧方案每帧创建 Border/Polygon 带来的分配与布局开销。
/// 绘制数据仍由 NotesViewModel 准备（画布坐标），本控件只负责呈现和命中测试。
/// </summary>
public class NotesCanvas : Control
{
    // 与旧 BuildHead 中的内芯尺寸保持一致
    private const double InnerHeight = 8;
    private const double CompactInnerWidth = 12; // Mute / Tune 的内芯宽

    // 选中高亮色：HSB(199,100%,100%)，与旧代码一致
    private static readonly IBrush HighlightBrush = new SolidColorBrush(Color.FromRgb(0, 174, 255));
    private static readonly IBrush PreviewBrush = Brushes.White;

    // 重叠展开选择条：列背景（与轨道底色一致）与悬浮高亮
    private static readonly IBrush OverlapPanelBrush = new SolidColorBrush(Color.Parse("#FF1A1A1A"));
    private static readonly IBrush OverlapPanelHoverBrush = new SolidColorBrush(Color.Parse("#FF2E2E2E"));

    // 每列上下边缘的浅灰细线（列间无缝时用于区分边界）
    private static readonly Pen OverlapPanelEdgePen = new(
        new SolidColorBrush(Color.Parse("#FFAAAAAA")), 1, null, PenLineCap.Flat, PenLineJoin.Miter, 10);

    // === 画刷缓存（初始化时从资源读取一次，绘制时不再创建） ===
    private IBrush? _dotBrush;
    private IBrush? _dashBrush;
    private IBrush? _muteBrush;
    private IBrush? _tuneBrush;
    private IBrush? _strokeBrush;
    private bool _brushesLoaded;

    /// <summary> 挂载到视觉树后读取 NotesView 的资源画刷（父级资源字典在挂载后才可查） </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        EnsureBrushes();
    }

    /// <summary> 从祖先资源字典读取颜色，保证与 XAML 中工具栏等处的颜色来源一致 </summary>
    private void EnsureBrushes()
    {
        if (_brushesLoaded) return;
        _dotBrush    = TryFindBrush("DotBrush");
        _dashBrush   = TryFindBrush("DashBrush");
        _muteBrush   = TryFindBrush("MuteBrush");
        _tuneBrush   = TryFindBrush("TuneBrush");
        _strokeBrush = TryFindBrush("NoteStrokeBrush");

        // 资源尚未就绪时保持未加载状态，下次 Render 再试
        _brushesLoaded = _dotBrush != null && _dashBrush != null &&
                         _muteBrush != null && _tuneBrush != null && _strokeBrush != null;
    }

    /// <summary> 按资源键查画刷，找不到返回 null（对应元素不绘制） </summary>
    private IBrush? TryFindBrush(object key)
        => this.TryFindResource(key, out object? value) ? value as IBrush : null;

    /// <summary>
    /// 每帧绘制整个 Note 层与放置预览。Avalonia 在控件失效后调用此方法，
    /// 我们只在这里提交绘制指令，不再创建任何控件。
    /// </summary>
    public override void Render(DrawingContext context)
    {
        EnsureBrushes();
        if (DataContext is not NotesViewModel vm) return;

        // 列表已按底→顶排序，直接顺序绘制即可得到正确层级
        var items = vm.NoteVisualItems;
        for (int i = 0; i < items.Count; i++)
            DrawItem(context, items[i]);

        // 放置预览层：旧实现是每个 Border 单独设 Opacity=0.4，这里统一压一次透明度
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

        // 重叠提示画在最上层：即使 unit 被其它元素盖住，也能看到感叹号
        OverlapMarkerPainter.Draw(context, vm.OverlapMarkers);

        DrawOverlapPanel(context, vm);
    }

    /// <summary> 绘制单个可视元素：高亮层 / 尾部 / 头 / 身体 </summary>
    private void DrawItem(DrawingContext context, NoteVisualItem item)
    {
        var rect = new Rect(item.X, item.Y, item.Width, item.Height);

        if (item.IsHighlight)
        {
            // 旧高亮控件未选中时背景为 Transparent，等价于不绘制
            if (item.IsSelected)
                context.FillRectangle(HighlightBrush, rect);
            return;
        }

        if (item.IsTail)
            DrawTail(context, item);
        else if (item.IsHead)
            DrawHead(context, item);
        else
            DrawBody(context, item);
    }

    /// <summary> Note 头：白色外框矩形 + 居中的彩色内芯 </summary>
    private void DrawHead(DrawingContext context, NoteVisualItem item)
    {
        if (_strokeBrush == null) return;
        context.FillRectangle(_strokeBrush, new Rect(item.X, item.Y, item.Width, item.Height));

        IBrush? fill = GetNoteBrush(item.Type);
        if (fill == null) return;

        // Mute/Tune 内芯固定 12×8；其余类型内芯宽 = 头宽 - 6
        double innerW = item.Type is NoteVisualType.Mute or NoteVisualType.Tune
            ? CompactInnerWidth
            : item.Width - 6;
        double x = item.X + (item.Width - innerW) / 2;
        double y = item.Y + (item.Height - InnerHeight) / 2;
        context.FillRectangle(fill, new Rect(x, y, innerW, InnerHeight));
    }

    /// <summary> Note 身体：纯色矩形 </summary>
    private void DrawBody(DrawingContext context, NoteVisualItem item)
    {
        IBrush? brush = GetNoteBrush(item.Type);
        if (brush == null) return;
        context.FillRectangle(brush, new Rect(item.X, item.Y, item.Width, item.Height));
    }

    /// <summary> 尾部结束点横条：与头的描边同色的白色实心矩形 </summary>
    private void DrawTail(DrawingContext context, NoteVisualItem item)
    {
        if (_strokeBrush == null) return;
        context.FillRectangle(_strokeBrush, new Rect(item.X, item.Y, item.Width, item.Height));
    }

    /// <summary> 根据可视类型获取对应颜色 Brush </summary>
    private IBrush? GetNoteBrush(NoteVisualType type) => type switch
    {
        NoteVisualType.Dot  => _dotBrush,
        NoteVisualType.Dash => _dashBrush,
        NoteVisualType.Mute => _muteBrush,
        NoteVisualType.Tune => _tuneBrush,
        _ => null
    };

    /// <summary>
    /// 绘制横向展开选择条：每列一个深灰背景 + 列内 unit 可视项。
    /// 列内 unit 的 Y 保持原时间位置，仅整体平移到对应列的水平位置。
    /// </summary>
    private void DrawOverlapPanel(DrawingContext context, NotesViewModel vm)
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
    /// 返回完整可视项而不是 SelectionTarget：View 需要区分头/身体/尾部、Note 类型与是否选中
    /// （拖拽允许从 Dot 的头、Dash/Mute 的头/身体起拖，尾部则用于改变 duration）。高亮层不参与命中。
    /// 尾部的命中区向下多延伸一个自身高度（伸进身体顶段），比 3px 的可视横条更好抓
    /// </summary>
    /// <param name="point">NoteCanvas 坐标系中的点</param>
    public NoteVisualItem? HitTestItem(Point point)
    {
        if (DataContext is not NotesViewModel vm) return null;

        var items = vm.NoteVisualItems;
        for (int i = items.Count - 1; i >= 0; i--)
        {
            var item = items[i];
            if (item.IsHighlight) continue;

            var rect = item.IsTail
                ? new Rect(item.X, item.Y, item.Width, item.Height * 2)
                : new Rect(item.X, item.Y, item.Width, item.Height);
            if (rect.Contains(point))
                return item;
        }
        return null;
    }
}
