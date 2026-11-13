using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CQ_ChartMaker.Models;
using CQMusicGame.Shared;

namespace CQ_ChartMaker.Views;

/// <summary>
/// 缓动曲线预览控件：自绘一个方框，框内画对角线（暗）和当前缓动曲线（亮）。
/// 曲线数据来自 Inspector 的曲线下拉框（种类 + 方向），求值复用游戏端的 Easing.Apply，保证预览与实际动画一致。
/// </summary>
public class CurvePreview : Control
{
    // 曲线采样点数：64 段折线足以表现所有缓动形状（Step 的跳变也会接近垂直线）
    private const int SampleCount = 64;

    // 绘制内边距：让 1px 描边完整落在控件内部，不被边缘裁掉一半
    private const double Padding = 0.5;

    // 画笔建一次复用（颜色：框/对角线暗，曲线亮但比纯白略暗）
    private static readonly Pen FramePen = CreatePen(Color.FromRgb(0x55, 0x55, 0x55), 1);
    private static readonly Pen DiagonalPen = CreatePen(Color.FromRgb(0x44, 0x44, 0x44), 1);
    private static readonly Pen CurvePen = CreatePen(Color.FromRgb(0xD8, 0xD8, 0xD8), 1.5);

    /// <summary> 曲线种类下拉项（与上方种类 ComboBox 的 SelectedItem 绑定），null 表示未选择 </summary>
    public static readonly StyledProperty<CurveKindItem?> KindProperty =
        AvaloniaProperty.Register<CurvePreview, CurveKindItem?>(nameof(Kind));

    /// <summary> 曲线方向下拉索引（0=In / 1=Out / 2=InOut，-1=无方向） </summary>
    public static readonly StyledProperty<int> DirectionProperty =
        AvaloniaProperty.Register<CurvePreview, int>(nameof(Direction), -1);

    static CurvePreview()
    {
        // AffectsRender：依赖属性变化时自动请求重绘（无需手动 InvalidateVisual）
        AffectsRender<CurvePreview>(KindProperty, DirectionProperty);
    }

    public CurveKindItem? Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public int Direction
    {
        get => GetValue(DirectionProperty);
        set => SetValue(DirectionProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        double width = Bounds.Width;
        double height = Bounds.Height;
        if (width <= 0 || height <= 0) return;

        // 1. 外框：只描边不填充
        var frame = new Rect(Padding, Padding, width - 2 * Padding, height - 2 * Padding);
        context.DrawRectangle(null, FramePen, frame);

        // 2. 对角线：左下 → 右上，作为缓动曲线的参考线
        context.DrawLine(DiagonalPen,
            new Point(frame.Left, frame.Bottom),
            new Point(frame.Right, frame.Top));

        // 3. 曲线：t 从 0→1 映射 x 左→右，缓动输出 0→1 映射 y 下→上
        // StreamGeometry 是即时模式的路径绘制对象，把采样点连成一条折线后一次性交给绘制上下文
        var curve = ResolveCurve();
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            for (int i = 0; i <= SampleCount; i++)
            {
                float t = i / (float)SampleCount;
                float value = Easing.Apply(curve, t);
                var point = new Point(
                    frame.Left + t * frame.Width,
                    frame.Bottom - value * frame.Height);
                if (i == 0) ctx.BeginFigure(point, false);
                else ctx.LineTo(point);
            }
            ctx.EndFigure(false);
        }
        // Back 类曲线会超出 [0,1]；控件默认不裁剪，回弹部分会画到框外
        context.DrawGeometry(null, CurvePen, geometry);
    }

    /// <summary> 种类 + 方向 → EaseCurve；未选择时按 Linear 处理 </summary>
    private EaseCurve ResolveCurve()
    {
        var kind = Kind;
        if (kind == null) return EaseCurve.Linear;
        return EaseCurveKindExtensions.ToEaseCurve(kind.Kind, EaseCurveDirectionExtensions.FromIndex(Direction));
    }

    /// <summary> 创建画笔（平头线帽；圆角连接让折线转折处更平滑） </summary>
    private static Pen CreatePen(Color color, double thickness)
        => new(new SolidColorBrush(color), thickness, null, PenLineCap.Flat, PenLineJoin.Round, 10);
}
