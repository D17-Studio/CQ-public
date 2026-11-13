using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CQ_ChartMaker.Common;
using CQ_ChartMaker.ViewModels;
using CQMusicGame.Shared;

namespace CQ_ChartMaker.Views;

/// <summary>
/// 游戏画面预览自绘控件：整个画面只有一个控件，Render 中每帧重画，无子控件/无布局。
/// 数据由 ViewViewModel.CollectFrame() 准备（世界坐标），本控件负责相机变换与绘制。
/// </summary>
public class GameCanvas : Control
{
    // === 游戏显示区域（世界单位，以世界原点为中心） ===
    private const double WorldWidth = 1600;
    private const double WorldHeight = 900;

    // === 默认视角与复位动画 ===
    private const double DefaultViewScale = 0.8;     // 默认画面 = 最大内接 16:9 矩形的 80%（宽高各 ×0.8）
    private const double ResetDurationSeconds = 0.1; // 双击复位动画时长（秒）

    // === 元素规格（世界单位） ===
    private const double HeadOutlineRadius = 31;   // Note 头黑圆 d62 打底
    private const double HeadFillRadius = 25;      // Note 头白圆 d50 覆盖
    private const double TuneRadius = 10;          // Tune 白圆 d20
    private const double LaneHeadRadius = 25;      // 轨道头白色圆环半径
    private const double LaneHeadLineHalf = 25;    // 过圆心横线半长（全长 50）
    private const double LaneLabelDistance = 60;   // 标号位于轨道头反方向 60 处
    private const double LaneLabelFontSize = 28;   // 标号字号
    private const double MuteFillWidth = 20;       // Mute 黑矩形宽
    private const double MuteOutlineWidth = 28;    // Mute 白矩形宽
    private const double MuteMinLength = 20;       // Mute 最小绘制长度
    private const double DashBodyHalfWidth = 10;   // Dash 身体 20 宽的一半
    private const double LaneLineHalfWidth = 2;    // 轨道线 4 宽的一半

    // === 选中提示光效（细而亮的黄色光晕） ===
    // 每层光晕的内边缘都贴住物体轮廓、只向外扩散，因此最后绘制也不会盖住被选中物本身。
    // 层宽从内到外递增、不透明度递减，叠加后形成由内向外渐隐的近似虚化效果。
    private static readonly Color GlowColor = Color.FromArgb(0xFF, 0xFF, 0xD2, 0x4A);
    private static readonly double[] GlowLayerWidths = { 3, 6, 10 };
    private static readonly double[] GlowLayerAlphas = { 0.6, 0.3, 0.15 };

    // 身体光效起点沿轨道/身体方向内缩的距离，避免光带端头盖住头部圆
    private const double GlowNoteHeadClearance = HeadOutlineRadius + 2; // Note 头黑圆外沿之外
    private const double GlowLaneHeadClearance = LaneHeadRadius + 3;    // 轨道头白环外沿之外

    // === 轨道锚点标记（选中轨道时显示） ===
    private const double AnchorRadius = LaneHeadRadius / 2;     // 锚点圆直径为轨道头的一半
    private const double AnchorCrossHalf = AnchorRadius + 3;   // 十字稍微超出圆圈
    private const double AnchorPenWidth = 2;

    // === 画刷/画笔（建一次复用，避免每帧分配） ===
    private static readonly IBrush BlackBrush = new SolidColorBrush(Colors.Black);
    private static readonly IBrush WhiteBrush = new SolidColorBrush(Colors.White);
    private static readonly IBrush AxisBrush = new SolidColorBrush(Color.FromRgb(0x4D, 0x4D, 0x4D));
    private static readonly IBrush LaneLineBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
    private static readonly Pen LaneLinePen = CreatePen(LaneLineBrush, 4);
    private static readonly Pen LaneHeadPen = CreatePen(WhiteBrush, 4);
    private static readonly Pen LaneHeadCrossPen = CreatePen(WhiteBrush, 4);
    private static readonly Pen DashBodyPen = CreatePen(WhiteBrush, 20);
    private static readonly Pen ReferenceFramePen = CreatePen(WhiteBrush, 1); // 屏幕坐标，固定 1px
    private static readonly Pen AxisPen = CreatePen(AxisBrush, 1);            // 屏幕坐标，固定 1px
    private static readonly Pen[] GlowPens = CreateGlowPens();                // 选中光晕各层画笔
    private static readonly Pen AnchorPen = CreatePen(new SolidColorBrush(GlowColor), AnchorPenWidth); // 锚点标记画笔

    // === 相机状态（纯视图状态，不进 VM/谱面数据） ===
    private double _zoom;        // 当前缩放（世界→屏幕）；0 表示尚未初始化
    private double _panX;        // 屏幕像素平移
    private double _panY;
    private bool _panning;       // 是否正在左键拖动
    private Point _lastPointerPosition;

    // === 复位动画状态 ===
    // DispatcherTimer 是 Avalonia 的 UI 线程定时器：按固定间隔回调 Tick，用于逐帧驱动动画
    private readonly DispatcherTimer _animationTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private Stopwatch? _animationStopwatch; // 高精度计时器，计算动画已流逝时间；null 表示动画未运行
    private double _animStartZoom;
    private double _animStartPanX;
    private double _animStartPanY;
    private double _animTargetZoom;
    private double _animTargetPanX;
    private double _animTargetPanY;

    private ViewViewModel? _vm;

    // 轨道标号文字缓存（FormattedText 创建成本较高，按轨道编号惰性创建）
    private readonly Dictionary<int, FormattedText> _laneLabels = new();

    public GameCanvas()
    {
        ClipToBounds = true;
        IsHitTestVisible = true; // 需要接收左键拖动；滚轮由 MainWindow 的隧道处理器转发
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;

        _animationTimer.Tick += OnAnimationTick;
    }

    /// <summary> 每帧重画整个预览画面（所有元素直接绘制，无控件树） </summary>
    public override void Render(DrawingContext context)
    {
        long start = Stopwatch.GetTimestamp();
        RenderFrame(context);
        FrameDiag.AddRender(Stopwatch.GetTimestamp() - start); // 临时性能诊断，验证完删除
    }

    private void RenderFrame(DrawingContext context)
    {
        // 背景纯黑（黑区即标题栏与底部控制栏之间的填充区域）
        context.FillRectangle(BlackBrush, new Rect(Bounds.Size));

        if (DataContext is not ViewViewModel vm) return;
        if (!EnsureCamera()) return;

        Matrix camera = GetCameraMatrix();

        // 十字坐标轴：世界原点在屏幕上的投影，贯穿整个画布（最底层，固定 2px）
        Point origin = camera.Transform(new Point(0, 0));
        context.DrawLine(AxisPen, new Point(0, origin.Y), new Point(Bounds.Width, origin.Y));
        context.DrawLine(AxisPen, new Point(origin.X, 0), new Point(origin.X, Bounds.Height));

        // 收集本帧渲染数据（世界坐标）
        vm.CollectFrame();

        using (context.PushTransform(camera))
        {
            // 严格按方案的全局层级分趟绘制（下层 → 上层）
            DrawLaneBodies(context, vm);
            DrawLaneHeads(context, vm);
            DrawDashBodies(context, vm);
            DrawTuneMarkers(context, vm);
            DrawMutes(context, vm);
            DrawDashHeads(context, vm);
            DrawDots(context, vm);
            HitEffectPainter.Draw(context, vm.HitEffects); // 打击特效：盖在 Note 之上
            DrawSelectionGlow(context, vm); // 选中提示：只画轮廓外的光晕
            DrawSelectionAnchors(context, vm); // 选中轨道的锚点标记
            DrawLaneLabels(context, vm); // 轨道标号：最后绘制，保证不被 Note/光效遮挡
        }

        // 1600×900 参考框：屏幕坐标绘制，固定 2px，画在最上层
        DrawReferenceFrame(context, camera);
    }

    /// <summary>
    /// 光标锚点滚轮缩放：缩放后光标下的世界点保持不动。
    /// 由 MainWindow 的滚轮隧道处理器在指针悬停于本控件时调用。
    /// </summary>
    /// <param name="position">光标在本控件坐标系中的位置</param>
    /// <param name="sign">滚轮方向：正数放大，负数缩小</param>
    public void ZoomAt(Point position, int sign)
    {
        if (sign == 0 || !EnsureCamera()) return;

        CancelCameraAnimation(); // 用户主动缩放：中断正在进行的复位动画

        double fitZoom = GetFitZoom();
        double newZoom = Math.Clamp(_zoom * Math.Pow(1.1, sign), fitZoom * 0.2, fitZoom * 10);
        if (Math.Abs(newZoom - _zoom) < 1e-6) return;

        double centerX = Bounds.Width / 2;
        double centerY = Bounds.Height / 2;

        // 缩放前光标下的世界坐标（相机无旋转，y 翻转）
        double worldX = (position.X - centerX - _panX) / _zoom;
        double worldY = (centerY + _panY - position.Y) / _zoom;

        _zoom = newZoom;

        // 反算 pan，使同一个世界点缩放后仍落在光标下
        _panX = position.X - centerX - worldX * newZoom;
        _panY = position.Y - centerY + worldY * newZoom;

        InvalidateVisual();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        // 切换 VM 时先退订旧对象，避免残留订阅
        if (_vm != null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.FrameDirty -= OnFrameDirty;
        }

        _vm = DataContext as ViewViewModel;
        if (_vm != null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.FrameDirty += OnFrameDirty;
        }

        InvalidateVisual();
    }

    /// <summary> 播放/拖动进度条时 CurrentTimeMs 每帧变化 → 触发重绘 </summary>
    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewViewModel.CurrentTimeMs))
            InvalidateVisual();
    }

    /// <summary> 谱面编辑后（暂停状态）由 VM 通知重绘 </summary>
    private void OnFrameDirty() => InvalidateVisual();

    // === 相机 ===

    /// <summary> 首次布局后把相机初始化为默认视角（居中、最大内接 16:9 矩形的 80%） </summary>
    private bool EnsureCamera()
    {
        if (_zoom > 0) return true;
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return false;

        _zoom = GetDefaultZoom();
        return true;
    }

    /// <summary> 铺满控件的缩放：1600×900 的最大内接 16:9 矩形适配（框宽/框高分别适配取较小值） </summary>
    private double GetFitZoom() => Math.Min(Bounds.Width / WorldWidth, Bounds.Height / WorldHeight);

    /// <summary> 默认视角缩放：最大内接 16:9 矩形的 80%（宽高同比例缩放，位置由 pan=0 保证居中） </summary>
    private double GetDefaultZoom() => GetFitZoom() * DefaultViewScale;

    /// <summary> 世界坐标 → 屏幕坐标：缩放 + y 翻转 + 居中/平移 </summary>
    private Matrix GetCameraMatrix()
    {
        double centerX = Bounds.Width / 2 + _panX;
        double centerY = Bounds.Height / 2 + _panY;
        return Matrix.CreateScale(_zoom, -_zoom) * Matrix.CreateTranslation(centerX, centerY);
    }

    // === 绘制 ===

    /// <summary>
    /// 轨道身体：从轨道头沿本地 +y（Note 来向）绘制有限长度线段。
    /// 长度来自 LaneState.Length（世界单位，1600×900 坐标系），0/负值按数据原样处理。
    /// </summary>
    private static void DrawLaneBodies(DrawingContext context, ViewViewModel vm)
    {
        var lanes = vm.LaneInfos;
        for (int i = 0; i < lanes.Count; i++)
        {
            LaneDrawInfo lane = lanes[i];
            if (lane.LineOpacity <= 0) continue;

            // 本地 +y 方向旋转到世界坐标：(0,1) → (-sin, cos)
            double rad = lane.Rotation * Math.PI / 180;
            double dx = -Math.Sin(rad) * lane.Length;
            double dy = Math.Cos(rad) * lane.Length;

            using (context.PushOpacity(ClampOpacity(lane.LineOpacity)))
            {
                context.DrawLine(LaneLinePen,
                    new Point(lane.Position.x, lane.Position.y),
                    new Point(lane.Position.x + dx, lane.Position.y + dy));
            }
        }
    }

    /// <summary> 轨道头：白色圆环 + 过圆心、垂直于轨道方向的横线（跟随轨道旋转） </summary>
    private static void DrawLaneHeads(DrawingContext context, ViewViewModel vm)
    {
        var lanes = vm.LaneInfos;
        for (int i = 0; i < lanes.Count; i++)
        {
            LaneDrawInfo lane = lanes[i];
            if (lane.HeadOpacity <= 0) continue;

            using (context.PushOpacity(ClampOpacity(lane.HeadOpacity)))
            using (context.PushTransform(GetLaneMatrix(lane)))
            {
                context.DrawEllipse(null, LaneHeadPen, new Point(0, 0), LaneHeadRadius, LaneHeadRadius);
                context.DrawLine(LaneHeadCrossPen,
                    new Point(-LaneHeadLineHalf, 0), new Point(LaneHeadLineHalf, 0));
            }
        }
    }

    /// <summary> 轨道本地坐标系：原点在轨道头，按轨道旋转 </summary>
    private static Matrix GetLaneMatrix(LaneDrawInfo lane)
    {
        double rad = lane.Rotation * Math.PI / 180;
        return Matrix.CreateRotation(rad) * Matrix.CreateTranslation(lane.Position.x, lane.Position.y);
    }

    /// <summary> 轨道标号：位于轨道头反方向 60 处，永远水平、不随轨道旋转 </summary>
    private void DrawLaneLabels(DrawingContext context, ViewViewModel vm)
    {
        // 用户设置“隐藏标号”开启时整层跳过，不影响轨道本身与 LaneEvent 的 LabelOpacity
        if (vm.IsLaneLabelHidden) return;

        var lanes = vm.LaneInfos;
        for (int i = 0; i < lanes.Count; i++)
        {
            LaneDrawInfo lane = lanes[i];
            if (lane.LabelOpacity <= 0) continue;

            // 本地 (0, -60) 旋转到世界坐标
            double rad = lane.Rotation * Math.PI / 180;
            double wx = lane.Position.x + LaneLabelDistance * Math.Sin(rad);
            double wy = lane.Position.y - LaneLabelDistance * Math.Cos(rad);

            FormattedText text = GetLaneLabel(lane.LaneIndex);
            using (context.PushOpacity(ClampOpacity(lane.LabelOpacity)))
            // 行向量语义：A * B 先应用 A；先做本地 y 翻转，再平移到目标世界坐标
            using (context.PushTransform(
                       Matrix.CreateScale(1, -1) * Matrix.CreateTranslation(wx, wy)))
            {
                // 本地 y 向下 → 世界 y 向上，再经相机 y 翻转后文字保持水平
                context.DrawText(text, new Point(-text.Width / 2, -text.Height / 2));
            }
        }
    }

    /// <summary> Dash 身体：按相邻顶点分段画 20 宽白色矩形（平头画笔等价于矩形） </summary>
    private static void DrawDashBodies(DrawingContext context, ViewViewModel vm)
    {
        var infos = vm.NoteInfos;
        var points = vm.Points;
        for (int n = 0; n < infos.Count; n++)
        {
            NoteDrawInfo info = infos[n];
            if (info.Type != NoteType.Dash || info.Opacity <= 0) continue;

            using (context.PushOpacity(ClampOpacity(info.Opacity)))
            {
                int end = info.PointStart + info.PointCount;
                for (int i = info.PointStart; i < end - 1; i++)
                {
                    Vec2 a = points[i];
                    Vec2 b = points[i + 1];
                    context.DrawLine(DashBodyPen, new Point(a.x, a.y), new Point(b.x, b.y));
                }
            }
        }
    }

    /// <summary> Tune 标记：白圆 d20，画在专用计算输出的移动起点/终点上 </summary>
    private static void DrawTuneMarkers(DrawingContext context, ViewViewModel vm)
    {
        var markers = vm.TuneMarkers;
        for (int i = 0; i < markers.Count; i++)
        {
            TuneMarkerInfo marker = markers[i];
            if (marker.Opacity <= 0) continue;

            using (context.PushOpacity(ClampOpacity(marker.Opacity)))
            {
                context.DrawEllipse(WhiteBrush, null,
                    new Point(marker.Position.x, marker.Position.y), TuneRadius, TuneRadius);
            }
        }
    }

    /// <summary> Mute：白色 28 宽打底 + 黑色 20 宽覆盖；长度取头尾间距与 20 的较大值 </summary>
    private static void DrawMutes(DrawingContext context, ViewViewModel vm)
    {
        var infos = vm.NoteInfos;
        var points = vm.Points;
        for (int n = 0; n < infos.Count; n++)
        {
            NoteDrawInfo info = infos[n];
            if (info.Type != NoteType.Mute || info.Opacity <= 0 || info.PointCount < 2) continue;

            Vec2 head = points[info.PointStart];
            Vec2 tail = points[info.PointStart + info.PointCount - 1];

            double dx = tail.x - head.x;
            double dy = tail.y - head.y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            double drawLength = Math.Max(length, MuteMinLength);

            // 零长度时方向无意义，矩形/正方形旋转与否结果相同
            double angle = length > 0 ? Math.Atan2(dy, dx) : 0;
            double centerX = (head.x + tail.x) / 2;
            double centerY = (head.y + tail.y) / 2;

            using (context.PushOpacity(ClampOpacity(info.Opacity)))
            using (context.PushTransform(
                       Matrix.CreateRotation(angle) * Matrix.CreateTranslation(centerX, centerY)))
            {
                // 白框长度 = 绘制长度 + 8（<20 时正好 28×28）
                context.DrawRectangle(WhiteBrush, null, new Rect(
                    -(drawLength + 8) / 2, -MuteOutlineWidth / 2,
                    drawLength + 8, MuteOutlineWidth));

                // 黑芯长度 = 绘制长度，宽 20
                context.DrawRectangle(BlackBrush, null, new Rect(
                    -drawLength / 2, -MuteFillWidth / 2,
                    drawLength, MuteFillWidth));
            }
        }
    }

    /// <summary> Dash 头部：黑圆 d62 打底 + 白圆 d50 覆盖 </summary>
    private static void DrawDashHeads(DrawingContext context, ViewViewModel vm)
    {
        var infos = vm.NoteInfos;
        var points = vm.Points;
        for (int n = 0; n < infos.Count; n++)
        {
            NoteDrawInfo info = infos[n];
            if (info.Type != NoteType.Dash || info.Opacity <= 0 || info.PointCount == 0) continue;

            Vec2 position = points[info.PointStart];
            using (context.PushOpacity(ClampOpacity(info.Opacity)))
                DrawNoteHead(context, position);
        }
    }

    /// <summary> Dot 头部：与 Dash 头相同 </summary>
    private static void DrawDots(DrawingContext context, ViewViewModel vm)
    {
        var infos = vm.NoteInfos;
        var points = vm.Points;
        for (int n = 0; n < infos.Count; n++)
        {
            NoteDrawInfo info = infos[n];
            if (info.Type != NoteType.Dot || info.Opacity <= 0 || info.PointCount == 0) continue;

            Vec2 position = points[info.PointStart];
            using (context.PushOpacity(ClampOpacity(info.Opacity)))
                DrawNoteHead(context, position);
        }
    }

    /// <summary> Note 头：黑圆 d62 打底 + 白圆 d50 覆盖（叠层实心图形，不用描边） </summary>
    private static void DrawNoteHead(DrawingContext context, Vec2 position)
    {
        var center = new Point(position.x, position.y);
        context.DrawEllipse(BlackBrush, null, center, HeadOutlineRadius, HeadOutlineRadius);
        context.DrawEllipse(WhiteBrush, null, center, HeadFillRadius, HeadFillRadius);
    }

    // === 选中提示光效 ===

    /// <summary>
    /// 选中提示光效（最上层）：被选中的轨道与 Note 都带弱黄色光晕。
    /// 各层光晕只绘制在物体轮廓之外，因此最后绘制也不会遮挡被选中物本身。
    /// </summary>
    private static void DrawSelectionGlow(DrawingContext context, ViewViewModel vm)
    {
        DrawLaneGlow(context, vm);
        DrawNoteGlow(context, vm);
    }

    /// <summary> 选中轨道的锚点标记：黄色圆圈 + 稍微超出圆圈的十字（世界坐标，随缩放变化） </summary>
    private static void DrawSelectionAnchors(DrawingContext context, ViewViewModel vm)
    {
        var anchors = vm.SelectedAnchors;
        for (int i = 0; i < anchors.Count; i++)
        {
            Vec2 anchor = anchors[i];
            var center = new Point(anchor.x, anchor.y);

            context.DrawEllipse(null, AnchorPen, center, AnchorRadius, AnchorRadius);
            context.DrawLine(AnchorPen,
                new Point(center.X - AnchorCrossHalf, center.Y),
                new Point(center.X + AnchorCrossHalf, center.Y));
            context.DrawLine(AnchorPen,
                new Point(center.X, center.Y - AnchorCrossHalf),
                new Point(center.X, center.Y + AnchorCrossHalf));
        }
    }

    /// <summary>
    /// 轨道光效：整条轨道线两侧的光带 + 轨道头圆环外侧的圆形光晕（内部横线不加）。
    /// 选中高亮不随轨道透明度减弱：即使轨道完全隐藏，也按全强度绘制以便定位。
    /// </summary>
    private static void DrawLaneGlow(DrawingContext context, ViewViewModel vm)
    {
        var lanes = vm.LaneInfos;
        for (int i = 0; i < lanes.Count; i++)
        {
            LaneDrawInfo lane = lanes[i];
            if (!lane.IsSelected) continue;

            // 本地 +y（Note 来向）的世界方向及其法线，与 DrawLaneBodies 的旋转约定一致
            double rad = lane.Rotation * Math.PI / 180;
            double dirX = -Math.Sin(rad);
            double dirY = Math.Cos(rad);
            double normalX = Math.Cos(rad);
            double normalY = Math.Sin(rad);

            // 轨道线光带：两侧各画多层，内边缘贴住 4 宽轨道线。
            // 起点沿轨道方向内缩，避免光带端头盖到轨道头
            double startX = lane.Position.x + dirX * GlowLaneHeadClearance;
            double startY = lane.Position.y + dirY * GlowLaneHeadClearance;
            // 光带长度与轨道线一致（LaneState.Length，世界单位）
            double endX = lane.Position.x + dirX * lane.Length;
            double endY = lane.Position.y + dirY * lane.Length;

            for (int layer = 0; layer < GlowPens.Length; layer++)
            {
                double offset = LaneLineHalfWidth + GlowLayerWidths[layer] / 2;
                double ox = normalX * offset;
                double oy = normalY * offset;
                Pen pen = GlowPens[layer];

                context.DrawLine(pen,
                    new Point(startX + ox, startY + oy),
                    new Point(endX + ox, endY + oy));
                context.DrawLine(pen,
                    new Point(startX - ox, startY - oy),
                    new Point(endX - ox, endY - oy));
            }

            // 轨道头光晕：白环 4 宽、半径 25，内边缘从外沿 27 起向外扩散
            var center = new Point(lane.Position.x, lane.Position.y);
            double ringOuter = LaneHeadRadius + 2;

            for (int layer = 0; layer < GlowPens.Length; layer++)
            {
                double radius = ringOuter + GlowLayerWidths[layer] / 2;
                context.DrawEllipse(null, GlowPens[layer], center, radius, radius);
            }
        }
    }

    /// <summary>
    /// Note 光效：头部圆形光晕 + Dash 身体两侧光带 / Mute 白框外侧的矩形光晕。
    /// 选中高亮不随 Note 透明度减弱，完全透明的 Note 仍会显示光晕以便定位。
    /// </summary>
    private static void DrawNoteGlow(DrawingContext context, ViewViewModel vm)
    {
        var infos = vm.NoteInfos;
        var points = vm.Points;
        for (int n = 0; n < infos.Count; n++)
        {
            NoteDrawInfo info = infos[n];
            if (!info.IsSelected) continue;

            switch (info.Type)
            {
                case NoteType.Dot:
                    if (info.PointCount > 0)
                        DrawHeadGlow(context, points[info.PointStart]);
                    break;

                case NoteType.Dash:
                    if (info.PointCount > 0)
                        DrawHeadGlow(context, points[info.PointStart]);
                    DrawDashBodyGlow(context, points, info);
                    break;

                case NoteType.Mute:
                    DrawMuteGlow(context, points, info);
                    break;
            }
        }
    }

    /// <summary> Note 头光晕：内边缘从黑圆外沿（半径 31）开始，只向外扩散，不碰黑圈本身 </summary>
    private static void DrawHeadGlow(DrawingContext context, Vec2 position)
    {
        var center = new Point(position.x, position.y);
        for (int layer = 0; layer < GlowPens.Length; layer++)
        {
            double radius = HeadOutlineRadius + GlowLayerWidths[layer] / 2;
            context.DrawEllipse(null, GlowPens[layer], center, radius, radius);
        }
    }

    /// <summary> Dash 身体光效：每段身体向两侧各偏移画光带，避免光晕盖住白色身体 </summary>
    private static void DrawDashBodyGlow(DrawingContext context, IReadOnlyList<Vec2> points, NoteDrawInfo info)
    {
        int end = info.PointStart + info.PointCount;
        for (int i = info.PointStart; i < end - 1; i++)
        {
            Vec2 a = points[i];
            Vec2 b = points[i + 1];

            double dx = b.x - a.x;
            double dy = b.y - a.y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 1e-6) continue;

            // 本段方向的单位方向与法线（法线垂直于身体走向）
            double ux = dx / length;
            double uy = dy / length;
            double nx = -uy;
            double ny = ux;

            // 第一段从 Note 头部出发：起点沿身体方向内缩，避免光带端头盖住头部圆；
            // 段长不足内缩距离时不画（头部自身光晕仍在）
            double trim = i == info.PointStart ? GlowNoteHeadClearance : 0;
            if (trim >= length) continue;

            double startX = a.x + ux * trim;
            double startY = a.y + uy * trim;

            // 最后一段若尾端有正在显示的 Tune 白圆，光带延伸到白圆外缘，与尾端封口衔接
            double extend = (i == end - 2 && info.TailHasMarker) ? TuneRadius : 0;
            double endX = b.x + ux * extend;
            double endY = b.y + uy * extend;

            for (int layer = 0; layer < GlowPens.Length; layer++)
            {
                double offset = DashBodyHalfWidth + GlowLayerWidths[layer] / 2;
                double ox = nx * offset;
                double oy = ny * offset;
                Pen pen = GlowPens[layer];

                context.DrawLine(pen,
                    new Point(startX + ox, startY + oy), new Point(endX + ox, endY + oy));
                context.DrawLine(pen,
                    new Point(startX - ox, startY - oy), new Point(endX - ox, endY - oy));
            }
        }

        // === 尾端封口 ===
        // 身体末端是平头，两侧光带在尾点截止会留下开口，沿末段方向补一条垂直光带形成 U 形包边。
        // 若尾部正好是正在显示的 Tune 白圆（半径 10），封口从圆的外缘开始，避免盖住白圆。
        if (info.PointCount < 2) return;

        Vec2 headPoint = points[info.PointStart];
        Vec2 tail = points[end - 1];
        Vec2 prev = points[end - 2];

        double tailDx = tail.x - prev.x;
        double tailDy = tail.y - prev.y;
        double tailLength = Math.Sqrt(tailDx * tailDx + tailDy * tailDy);
        if (tailLength < 1e-6) return;

        // 身体总长不足以避开 Note 头部时不封口，避免光带盖住头部圆
        double headDx = tail.x - headPoint.x;
        double headDy = tail.y - headPoint.y;
        if (Math.Sqrt(headDx * headDx + headDy * headDy) < GlowNoteHeadClearance) return;

        double tux = tailDx / tailLength;
        double tuy = tailDy / tailLength;
        double tnx = -tuy;
        double tny = tux;
        double markerOffset = info.TailHasMarker ? TuneRadius : 0;

        for (int layer = 0; layer < GlowPens.Length; layer++)
        {
            double w = GlowLayerWidths[layer];
            // 笔迹内边缘贴住身体末端（或 Tune 白圆外缘）
            double capDistance = markerOffset + w / 2;
            double capX = tail.x + tux * capDistance;
            double capY = tail.y + tuy * capDistance;

            // 横向半长覆盖该层两侧光带的外缘，保证 U 形转角连续
            double halfSpan = DashBodyHalfWidth + w;

            context.DrawLine(GlowPens[layer],
                new Point(capX - tnx * halfSpan, capY - tny * halfSpan),
                new Point(capX + tnx * halfSpan, capY + tny * halfSpan));
        }
    }

    /// <summary> Mute 光效：以 28 宽白框外沿为内边，向外画多层矩形光晕，不盖住白框 </summary>
    private static void DrawMuteGlow(DrawingContext context, IReadOnlyList<Vec2> points, NoteDrawInfo info)
    {
        if (info.PointCount < 2) return;

        Vec2 head = points[info.PointStart];
        Vec2 tail = points[info.PointStart + info.PointCount - 1];

        double dx = tail.x - head.x;
        double dy = tail.y - head.y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        double drawLength = Math.Max(length, MuteMinLength);
        double angle = length > 0 ? Math.Atan2(dy, dx) : 0;
        double centerX = (head.x + tail.x) / 2;
        double centerY = (head.y + tail.y) / 2;

        using (context.PushTransform(
                   Matrix.CreateRotation(angle) * Matrix.CreateTranslation(centerX, centerY)))
        {
            for (int layer = 0; layer < GlowPens.Length; layer++)
            {
                double w = GlowLayerWidths[layer];
                // 路径矩形 = 白框尺寸 + w：笔迹向内收半个线宽后，内边缘正好贴住白框外沿
                double glowLength = drawLength + 8 + w;
                double glowWidth = MuteOutlineWidth + w;
                context.DrawRectangle(null, GlowPens[layer], new Rect(
                    -glowLength / 2, -glowWidth / 2, glowLength, glowWidth));
            }
        }
    }

    /// <summary> 1600×900 参考框：世界固定尺寸投影到屏幕，线宽固定 2px 不随缩放变化 </summary>
    private static void DrawReferenceFrame(DrawingContext context, Matrix camera)
    {
        Point topLeft = camera.Transform(new Point(-WorldWidth / 2, WorldHeight / 2));
        Point bottomRight = camera.Transform(new Point(WorldWidth / 2, -WorldHeight / 2));
        var rect = new Rect(topLeft, new Size(
            bottomRight.X - topLeft.X,
            bottomRight.Y - topLeft.Y));
        context.DrawRectangle(null, ReferenceFramePen, rect);
    }

    // === 输入 ===

    /// <summary> 左键按下：双击复位到默认视角，单击开始平移 </summary>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        // ClickCount 由 Avalonia 统计短时间内同一位置的连续点击次数，≥2 即双击
        if (e.ClickCount >= 2)
        {
            _panning = false;
            e.Pointer.Capture(null);
            StartResetAnimation();
            e.Handled = true;
            return;
        }

        _panning = true;
        _lastPointerPosition = e.GetPosition(this);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    /// <summary> 左键拖动：内容跟随鼠标同向移动；拖动会中断进行中的复位动画 </summary>
    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_panning) return;

        Point position = e.GetPosition(this);
        double dx = position.X - _lastPointerPosition.X;
        double dy = position.Y - _lastPointerPosition.Y;
        if (dx == 0 && dy == 0) return;

        CancelCameraAnimation();
        _panX += dx;
        _panY += dy;
        _lastPointerPosition = position;
        InvalidateVisual();
    }

    /// <summary> 左键松开：结束平移并释放指针捕获 </summary>
    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_panning) return;

        _panning = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    // === 复位动画 ===

    /// <summary>
    /// 启动复位动画：0.2 秒内以二次缓出（先快后慢）把位置和缩放插值到默认视角。
    /// 起点取当前画面（包括动画进行中的中间帧），因此重复双击可平滑接续。
    /// </summary>
    private void StartResetAnimation()
    {
        if (!EnsureCamera()) return;

        _animStartZoom = _zoom;
        _animStartPanX = _panX;
        _animStartPanY = _panY;
        _animTargetZoom = GetDefaultZoom();
        _animTargetPanX = 0;
        _animTargetPanY = 0;

        // 已经在默认视角时无需动画
        if (Math.Abs(_animStartZoom - _animTargetZoom) < 1e-6 &&
            Math.Abs(_animStartPanX) < 1e-6 &&
            Math.Abs(_animStartPanY) < 1e-6)
            return;

        _animationStopwatch = Stopwatch.StartNew();
        _animationTimer.Start();
        InvalidateVisual();
    }

    /// <summary> 定时器回调：按已流逝时间计算缓动进度并更新相机 </summary>
    private void OnAnimationTick(object? sender, EventArgs e)
    {
        if (_animationStopwatch == null)
        {
            _animationTimer.Stop();
            return;
        }

        double t = _animationStopwatch.Elapsed.TotalSeconds / ResetDurationSeconds;

        if (t >= 1)
        {
            // 到达终点：精确吸附到目标值，避免浮点误差残留
            _zoom = _animTargetZoom;
            _panX = _animTargetPanX;
            _panY = _animTargetPanY;
            CancelCameraAnimation();
            InvalidateVisual();
            return;
        }

        // 二次缓出：p = 1 - (1 - t)^2，t 从 0→1 时 p 先快速增长后放缓
        double p = 1 - (1 - t) * (1 - t);
        _zoom = _animStartZoom + (_animTargetZoom - _animStartZoom) * p;
        _panX = _animStartPanX + (_animTargetPanX - _animStartPanX) * p;
        _panY = _animStartPanY + (_animTargetPanY - _animStartPanY) * p;

        InvalidateVisual();
    }

    /// <summary> 中断复位动画：相机停留在当前中间帧，不回跳 </summary>
    private void CancelCameraAnimation()
    {
        _animationTimer.Stop();
        _animationStopwatch = null;
    }

    // === 工具 ===

    /// <summary> 创建画笔（统一平头线帽；线宽在世界坐标中随相机缩放） </summary>
    private static Pen CreatePen(IBrush brush, double thickness)
        => new(brush, thickness, null, PenLineCap.Flat, PenLineJoin.Miter, 10);

    /// <summary> 不透明度夹到 [0,1]，防止动画数据越界导致绘制异常 </summary>
    private static double ClampOpacity(float value) => Math.Clamp(value, 0f, 1f);

    /// <summary> 创建选中光晕各层画笔：同色黄、层宽递增、不透明度递减 </summary>
    private static Pen[] CreateGlowPens()
    {
        var pens = new Pen[GlowLayerWidths.Length];
        for (int i = 0; i < pens.Length; i++)
        {
            byte alpha = (byte)Math.Round(255 * GlowLayerAlphas[i]);
            var brush = new SolidColorBrush(
                Color.FromArgb(alpha, GlowColor.R, GlowColor.G, GlowColor.B));
            pens[i] = CreatePen(brush, GlowLayerWidths[i]);
        }
        return pens;
    }

    /// <summary> 按轨道编号取标号文字，首次访问时创建并缓存 </summary>
    private FormattedText GetLaneLabel(int laneIndex)
    {
        if (_laneLabels.TryGetValue(laneIndex, out var text))
            return text;

        var typeface = new Typeface(FontFamily.Default, FontStyle.Normal,
            FontWeight.Normal, FontStretch.Normal);

        text = new FormattedText(
            laneIndex.ToString(CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            LaneLabelFontSize,
            WhiteBrush);

        _laneLabels[laneIndex] = text;
        return text;
    }
}
