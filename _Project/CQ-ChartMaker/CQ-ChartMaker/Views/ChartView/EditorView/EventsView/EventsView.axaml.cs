using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CQ_ChartMaker.Models;
using CQ_ChartMaker.ViewModels;

namespace CQ_ChartMaker.Views;

/// <summary>
/// Events 页：轨道 / 节拍线 / 判定线 + 当前类型 LaneEvent 的放置与编辑交互。
/// </summary>
public partial class EventsView : UserControl
{
    private bool _isDragging;
    private EventsViewModel? _vm;

    /// <summary> 窗口引用（挂/摘 Shift 键监听用，Detached 时窗口已不可向上查找，需缓存） </summary>
    private TopLevel? _topLevel;

    // === 框选拖拽状态 ===
    private bool _boxPressed;
    private bool _boxDragging;
    private bool _boxAppend;
    private Point _boxPressPoint;
    private const double BoxDragThreshold = 4;

    // === 事件拖拽状态 ===
    private bool _motionPressed;
    private bool _motionDragging;
    private bool _motionDeferredSelect;
    private Point _motionPressPoint;
    private ILaneEventUnit? _motionDragAnchor;

    // === 起止点把手拖拽状态（白带外侧：上方改 Duration，下方改 StartTime） ===
    private bool _handlePressed;    // 在把手上按下了左键（等待超过阈值）
    private bool _handleDragging;   // 已进入把手拖拽（VM 已隐藏源 unit 并显示预览）
    private Point _handlePressPoint;
    private ILaneEventUnit? _handleAnchor;
    private bool _handleIsEnd;      // true = 上方把手（终点），false = 下方把手（起点）

    // === 长按放置状态 ===
    private bool _placeHolding;

    // === 快捷放置状态（设置开启时，空白处的单击/拖拽用于放置） ===
    private bool _quickPressed;   // 空白处按下了左键（等待判定：单击放零时长事件 / 拖动放有时长事件）
    private bool _quickDragging;  // 已超过阈值，正在拖拽定长
    private Point _quickPressPoint;

    private IBrush? _beatLineBrush;
    private IBrush? _subBeatLineBrush;
    private IBrush? _laneDimBrush;

    // 悬停光标缓存：避免每次 PointerMoved 都 new Cursor
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    // 把手悬停光标：上下双箭头，提示可拖动改变起点/终点
    private static readonly Cursor VerticalResizeCursor = new(StandardCursorType.SizeNorthSouth);

    // === 重叠展开选择条 ===
    private const int OverlapHoverDelayMs = 300;
    private const int OverlapCloseDelayMs = 0; // 移出展开区域立即关闭

    private DispatcherTimer? _overlapHoverTimer;
    private DispatcherTimer? _overlapCloseTimer;
    private object? _overlapHoverUnit;

    public EventsView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;

        // 展开条点击用 Tunnel：先于下层选中/拖拽处理，且覆盖画布之外的空白区域
        AddHandler(PointerPressedEvent, OnOverlapPanelTunnelPressed, RoutingStrategies.Tunnel);
        PointerMoved += OnOverlapPanelPointerMoved;
        PointerExited += OnOverlapPanelPointerExited;
    }

    private void OnAttached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _beatLineBrush = (IBrush?)Resources["BeatLineBrush"];
        _subBeatLineBrush = (IBrush?)Resources["SubBeatLineBrush"];
        _laneDimBrush = (IBrush?)Resources["LaneDimBrush"];

        if (DataContext is EventsViewModel vm)
        {
            _vm = vm;
            vm.PropertyChanged += OnVmPropertyChanged;
            UpdatePlacingButtons();
            RenderLaneDimRects(); // 初始渲染变暗遮罩

            LaneContainer.EffectiveViewportChanged += (_, _) =>
            {
                PositionJudgmentLine();
                UpdateViewHeight();
            };
            JudgmentCanvas.EffectiveViewportChanged += (_, _) =>
            {
                PositionJudgmentLine();
                UpdateViewHeight();
            };

            LaneScroll.ScrollChanged += OnLaneScrollChanged;
            LaneScroll.SizeChanged += OnLaneScrollSizeChanged;
            LaneHScroll.ValueChanged += OnLaneHScrollChanged;
        }

        MotionCanvas.PointerPressed  += OnMotionCanvasPressed;
        MotionCanvas.PointerReleased += OnMotionCanvasReleased;
        MotionCanvas.PointerExited   += OnMotionCanvasPointerExited;

        // 放置模式下全局右键取消（Tunnel 保证在子控件之前捕获）
        MotionCanvas.AddHandler(PointerPressedEvent, OnMotionCanvasTunnelPlacing, RoutingStrategies.Tunnel);

        // 预览：外层 Grid 捕获鼠标坐标，换算到 MotionCanvas 坐标系
        RootGrid.PointerMoved += OnRootGridPointerMoved;

        // 拖拽中按/松 Shift 锁定时间：窗口级 Tunnel 捕获按键（只观察，不吞事件）。
        // 挂在窗口而不是本控件，是因为拖拽时焦点可能落在工具栏等控件上
        _topLevel = TopLevel.GetTopLevel(this);
        _topLevel?.AddHandler(KeyDownEvent, OnTopLevelKeyDown, RoutingStrategies.Tunnel);
        _topLevel?.AddHandler(KeyUpEvent, OnTopLevelKeyUp, RoutingStrategies.Tunnel);

        JudgmentTriangle.PointerPressed  += OnTrianglePressed;
        JudgmentTriangle.PointerMoved    += OnTriangleMoved;
        JudgmentTriangle.PointerReleased += OnTriangleReleased;

        MotionButton.Click += OnPlacingButtonClick;
    }

    /// <summary> 移出视觉树时退订 VM：切走子视图后旧 View 不再收到刷新事件 </summary>
    private void OnDetached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        // 键监听挂在窗口上：离开时必须手动摘掉，否则旧 View 会被窗口持有。
        // RemoveHandler 只按事件与委托匹配，注册时的 Tunnel 策略无需重复指定
        _topLevel?.RemoveHandler(KeyDownEvent, OnTopLevelKeyDown);
        _topLevel?.RemoveHandler(KeyUpEvent, OnTopLevelKeyUp);
        _topLevel = null;

        _placeHolding = false;
        _quickPressed = false;
        _quickDragging = false;
        _handlePressed = false;
        _handleDragging = false;
        _handleAnchor = null;
        _vm?.CancelQuickPlace();     // 快捷拖动中途切走视图：丢弃未提交的头
        _vm?.CancelHandleResize();   // 把手拖拽中途切走视图：还原被隐藏的 unit
        StopOverlapHoverTimer();
        CancelOverlapCloseTimer();
        _vm?.CloseOverlapPanel();    // 切走视图：恢复被展开条隐藏的 unit
        LaneScroll.ScrollChanged -= OnLaneScrollChanged;
        LaneScroll.SizeChanged -= OnLaneScrollSizeChanged;
        LaneHScroll.ValueChanged -= OnLaneHScrollChanged;
        if (_vm == null) return;
        _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = null;
    }

    // === 水平滚动条同步 ===

    private bool _syncingScroll;

    private void OnLaneScrollChanged(object? sender, ScrollChangedEventArgs e) => UpdateHorizontalScrollBar();

    private void OnLaneScrollSizeChanged(object? sender, SizeChangedEventArgs e) => UpdateHorizontalScrollBar();

    private void OnLaneHScrollChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncingScroll) return;
        if (_vm?.IsOverlapPanelOpen == true) _vm.CloseOverlapPanel(); // 横向滚动后锚点失效
        _syncingScroll = true;
        LaneScroll.Offset = new Vector(e.NewValue, LaneScroll.Offset.Y);
        _syncingScroll = false;
    }

    private void UpdateHorizontalScrollBar()
    {
        if (_syncingScroll) return;
        _syncingScroll = true;
        double max = Math.Max(0, LaneScroll.Extent.Width - LaneScroll.Viewport.Width);
        LaneHScroll.Maximum = max;
        LaneHScroll.ViewportSize = LaneScroll.Viewport.Width;
        LaneHScroll.Value = Math.Clamp(LaneScroll.Offset.X, 0, max);
        _syncingScroll = false;
    }

    // === 放置工具栏 ===

    private void OnPlacingButtonClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null || sender is not ToggleButton btn) return;

        bool isChecked = btn.IsChecked == true;

        _vm.IsPlacing = isChecked;
    }

    private void UpdatePlacingButtons()
    {
        if (_vm == null) return;
        MotionButton.IsChecked = _vm.IsPlacing;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EventsViewModel.JudgmentLinePosition))
            PositionJudgmentLine();
        else if (e.PropertyName == nameof(EventsViewModel.BeatLineSegments) ||
                 e.PropertyName == nameof(EventsViewModel.BeatLineLabels))
            RenderBeatLines();
        else if (e.PropertyName == nameof(EventsViewModel.SubBeatLineSegments))
            RenderSubBeatLines();
        else if (e.PropertyName == nameof(EventsViewModel.LaneDimRects))
            RenderLaneDimRects();
        else if (e.PropertyName == nameof(EventsViewModel.MotionVisualItems))
        {
            MotionLayer.InvalidateVisual();
            RenderBoxSelect();
        }
        else if (e.PropertyName == nameof(EventsViewModel.PreviewItems) ||
                 e.PropertyName == nameof(EventsViewModel.OverlapMarkers))
            MotionLayer.InvalidateVisual();
        else if (e.PropertyName == nameof(EventsViewModel.IsOverlapPanelOpen))
        {
            if (_vm?.IsOverlapPanelOpen != true)
            {
                StopOverlapHoverTimer();
                CancelOverlapCloseTimer();
            }
            MotionLayer.InvalidateVisual();
        }
        else if (e.PropertyName == nameof(EventsViewModel.OverlapPanelHoverIndex) ||
                 e.PropertyName == nameof(EventsViewModel.OverlapPanelLeft))
            MotionLayer.InvalidateVisual();
        else if (e.PropertyName == nameof(EventsViewModel.BoxSelectRect))
            RenderBoxSelect();
        else if (e.PropertyName == nameof(EventsViewModel.IsPlacing))
            UpdatePlacingButtons();
    }

    // === 拖拽 Shift 锁定（按下/松开时立即通知 VM，鼠标不动也生效） ===

    /// <summary> Shift 按下：若正在拖拽则时间立即锁定为起拖原值；非拖拽状态下无可见效果 </summary>
    private void OnTopLevelKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.LeftShift or Key.RightShift)
            _vm?.SetDragTimeLock(true);
    }

    /// <summary> Shift 松开：时间恢复跟随鼠标。用事件修饰键判断，另一侧 Shift 仍按住时保持锁定 </summary>
    private void OnTopLevelKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.LeftShift or Key.RightShift)
            _vm?.SetDragTimeLock(e.KeyModifiers.HasFlag(KeyModifiers.Shift));
    }

    /// <summary> 放置/幽灵/重定位模式下拦截左右键 </summary>
    private void OnMotionCanvasTunnelPlacing(object? sender, PointerPressedEventArgs e)
    {
        if (_vm == null || e.Handled) return;

        // 拖拽进行中：忽略所有按键（含右键），拖拽只由左键松开结束
        if (_motionDragging || _quickDragging || _handleDragging)
        {
            e.Handled = true;
            return;
        }

        var props = e.GetCurrentPoint(this).Properties;

        if (_vm.IsRelocating)
        {
            if (props.IsRightButtonPressed)
                _vm.CancelRelocate();
            else if (props.IsLeftButtonPressed)
            {
                var relPos = e.GetPosition(MotionCanvas);
                _vm.CommitRelocate(relPos.X, relPos.Y);
            }
            e.Handled = true;
            return;
        }

        bool ghost = _vm.IsGhostActive;
        if (!ghost && !_vm.IsPlacing)
        {
            // 快捷删除：普通状态右键命中事件时删除该 unit（与当前选中无关）。
            // 右键空白不改变选中
            if (_vm.IsQuickDeleteEnabled && props.IsRightButtonPressed)
            {
                if (MotionLayer.HitTestItem(e.GetPosition(MotionCanvas)) is { } hit)
                {
                    _vm.OnDeleteUnitRequested?.Invoke(hit.SelectionTarget!);
                    e.Handled = true;
                }
            }
            return;
        }

        if (props.IsRightButtonPressed)
        {
            if (ghost)
            {
                _vm.CancelGhost();
            }
            else
            {
                _vm.CancelPlacement();
                if (_placeHolding)
                {
                    _placeHolding = false;
                    e.Pointer.Capture(null);
                }
            }
            e.Handled = true;
        }
        else if (props.IsLeftButtonPressed)
        {
            var pos = e.GetPosition(MotionCanvas);
            if (ghost)
            {
                _vm.TryCommitGhost(pos.X, pos.Y);
            }
            else if (_vm.IsHoldPlaceMode)
            {
                // 长按模式只对有时长类型有效；Zone/Parent 会返回 false，退回单击放置
                if (_vm.BeginHoldPlace(pos.X, pos.Y))
                {
                    _placeHolding = true;
                    e.Pointer.Capture(MotionCanvas);
                }
                else
                {
                    _vm.TryPlace(pos.X, pos.Y);
                }
            }
            else
            {
                _vm.TryPlace(pos.X, pos.Y);
            }
            e.Handled = true;
        }
    }

    /// <summary> 左键按下：命中测试 → 选中 / 拖拽候选；否则框选起点 </summary>
    private void OnMotionCanvasPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm == null || e.Handled) return;
        if (!e.GetCurrentPoint(MotionCanvas).Properties.IsLeftButtonPressed) return;

        var pos = e.GetPosition(MotionCanvas);
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        // 白带外侧的把手：只用于拖拽起点/终点，不属于点击选中区域；
        // 按下只记录候选，不改变选中，超过阈值后才真正进入把手拖拽
        var hit = MotionLayer.HitTestItem(pos, _vm.IsDragMode && !ctrl, out MotionHitKind hitKind);
        if (hit is { } hitItem && hitKind != MotionHitKind.Body)
        {
            _handlePressed    = true;
            _handleDragging   = false;
            _handlePressPoint = pos;
            _handleAnchor     = hitItem.SelectionTarget as ILaneEventUnit;
            _handleIsEnd      = hitKind == MotionHitKind.EndHandle;
            e.Handled = true;
            return;
        }

        if (hit is { } item)
        {
            bool canDrag = _vm.IsDragMode && !ctrl;

            bool defer = canDrag && item.IsSelected;
            if (!defer)
                _vm.OnSelectUnit?.Invoke(item.SelectionTarget, ctrl);

            if (canDrag && item.SelectionTarget is ILaneEventUnit anchor)
            {
                _motionPressed        = true;
                _motionDragging       = false;
                _motionDeferredSelect = defer;
                _motionPressPoint     = pos;
                _motionDragAnchor     = anchor;
            }

            e.Handled = true;
            return;
        }

        // 快捷放置：空白处左键交给快捷放置状态机（单击放零时长事件、拖拽放有时长事件）。
        // 开启后空白的单击取消选中、双击选列、框选全部禁用；
        // Ctrl+左键空白不放置也不改变选中（保留复选习惯）
        if (_vm.IsQuickPlaceEnabled)
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                _quickPressed    = true;
                _quickDragging   = false;
                _quickPressPoint = pos;
            }
            e.Handled = true;
            return;
        }

        if (e.ClickCount == 2)
        {
            // 双击空白：把命中的列序号交给 ChartVM（轨道=轨道列；类型=类型列）
            if (_vm.TryGetColumnAtX(pos.X, out int column))
            {
                _vm.OnSelectLane?.Invoke(column, e.KeyModifiers.HasFlag(KeyModifiers.Control));
                e.Handled = true;
                return;
            }
        }

        _boxPressed    = true;
        _boxDragging   = false;
        _boxAppend     = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        _boxPressPoint = pos;
    }

    /// <summary> 松开左键：拖拽提交 / 延迟选中收窄 / 框选提交 </summary>
    private void OnMotionCanvasReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_vm == null) return;

        // 快捷放置：拖动结束提交有时长事件；未拖动则按单击放零时长事件
        if (_quickDragging)
        {
            var pos = e.GetPosition(MotionCanvas);
            _quickDragging = false;
            _quickPressed  = false;
            e.Pointer.Capture(null);
            _vm.EndQuickPlace(pos.Y);
            return;
        }
        if (_quickPressed)
        {
            _quickPressed = false;
            _vm.QuickPlaceAt(_quickPressPoint.X, _quickPressPoint.Y);
            return;
        }

        if (_placeHolding)
        {
            var pos = e.GetPosition(MotionCanvas);
            _placeHolding = false;
            e.Pointer.Capture(null);
            _vm.EndHoldPlace(pos.Y);
            return;
        }

        // 把手拖拽松手：按当前位置的吸附时间提交新的起点/时长
        if (_handleDragging)
        {
            var pos = e.GetPosition(MotionCanvas);
            _handleDragging = false;
            _handlePressed  = false;
            _handleAnchor   = null;
            e.Pointer.Capture(null);
            _vm.EndHandleResize(pos.Y);
            return;
        }
        if (_handlePressed)
        {
            // 按下把手但未拖动：不改变选中，也不修改数据
            _handlePressed = false;
            _handleAnchor  = null;
            return;
        }

        if (_motionDragging)
        {
            var pos = e.GetPosition(MotionCanvas);
            _motionDragging       = false;
            _motionPressed        = false;
            _motionDeferredSelect = false;
            _motionDragAnchor     = null;
            e.Pointer.Capture(null);
            // 以松手瞬间的按键状态为准，兜底漏掉的键盘事件
            _vm.SetDragTimeLock(e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            _vm.EndDragGhost(pos.X, pos.Y);
            return;
        }

        if (_motionPressed)
        {
            _motionPressed        = false;
            bool collapse         = _motionDeferredSelect;
            var anchor            = _motionDragAnchor;
            _motionDeferredSelect = false;
            _motionDragAnchor     = null;
            if (collapse && anchor != null)
                _vm.OnSelectUnit?.Invoke(anchor, false);
            return;
        }

        if (!_boxPressed) return;

        _boxPressed = false;
        if (_boxDragging)
        {
            _boxDragging = false;
            e.Pointer.Capture(null);
            _vm.EndBoxSelect();
        }
        else if (!_boxAppend)
        {
            _vm.OnSelectUnit?.Invoke(null, false);
        }
    }

    /// <summary> 渲染框选矩形 </summary>
    private void RenderBoxSelect()
    {
        if (_vm?.BoxSelectRect is not { } rect)
        {
            BoxSelectBorder.IsVisible = false;
            return;
        }
        BoxSelectBorder.IsVisible = true;
        BoxSelectBorder.Width = rect.Width;
        BoxSelectBorder.Height = rect.Height;
        Canvas.SetLeft(BoxSelectBorder, rect.X);
        Canvas.SetTop(BoxSelectBorder, rect.Y);
    }

    private void RenderBeatLines()
    {
        if (_vm == null || _beatLineBrush == null) return;

        var segments = _vm.BeatLineSegments;
        while (BeatLineCanvas.Children.Count > segments.Count)
            BeatLineCanvas.Children.RemoveAt(BeatLineCanvas.Children.Count - 1);
        while (BeatLineCanvas.Children.Count < segments.Count)
        {
            BeatLineCanvas.Children.Add(new Border
            {
                Background = _beatLineBrush,
                Height = 1,
            });
        }
        for (int i = 0; i < segments.Count; i++)
        {
            var b = (Border)BeatLineCanvas.Children[i];
            var seg = segments[i];
            b.Width = seg.Width;
            Canvas.SetLeft(b, seg.X);
            Canvas.SetTop(b, seg.Y);
        }

        var labels = _vm.BeatLineLabels;
        while (BeatLabelCanvas.Children.Count > labels.Count)
            BeatLabelCanvas.Children.RemoveAt(BeatLabelCanvas.Children.Count - 1);
        while (BeatLabelCanvas.Children.Count < labels.Count)
        {
            BeatLabelCanvas.Children.Add(new TextBlock
            {
                Foreground = _beatLineBrush,
                FontSize = 10,
                TextAlignment = TextAlignment.Right,
                Width = 36,
            });
        }
        for (int i = 0; i < labels.Count; i++)
        {
            var tb = (TextBlock)BeatLabelCanvas.Children[i];
            tb.Text = labels[i].Label;
            Canvas.SetTop(tb, labels[i].Y);
        }
    }

    private void RenderSubBeatLines()
    {
        if (_vm == null || _subBeatLineBrush == null) return;

        var segments = _vm.SubBeatLineSegments;
        while (SubBeatLineCanvas.Children.Count > segments.Count)
            SubBeatLineCanvas.Children.RemoveAt(SubBeatLineCanvas.Children.Count - 1);
        while (SubBeatLineCanvas.Children.Count < segments.Count)
        {
            SubBeatLineCanvas.Children.Add(new Border
            {
                Background = _subBeatLineBrush,
                Height = 1,
            });
        }
        for (int i = 0; i < segments.Count; i++)
        {
            var b = (Border)SubBeatLineCanvas.Children[i];
            var seg = segments[i];
            b.Width = seg.Width;
            Canvas.SetLeft(b, seg.X);
            Canvas.SetTop(b, seg.Y);
        }
    }

    /// <summary> 渲染完全隐藏区间的变暗遮罩：按列宽铺底色半透明矩形（复用控件，不每帧新建） </summary>
    private void RenderLaneDimRects()
    {
        if (_vm == null || _laneDimBrush == null) return;

        var rects = _vm.LaneDimRects;
        while (LaneDimCanvas.Children.Count > rects.Count)
            LaneDimCanvas.Children.RemoveAt(LaneDimCanvas.Children.Count - 1);
        while (LaneDimCanvas.Children.Count < rects.Count)
        {
            LaneDimCanvas.Children.Add(new Border
            {
                Background = _laneDimBrush,
                IsHitTestVisible = false,
            });
        }
        for (int i = 0; i < rects.Count; i++)
        {
            var b = (Border)LaneDimCanvas.Children[i];
            var r = rects[i];
            b.Width = r.Width;
            b.Height = r.Height;
            Canvas.SetLeft(b, r.X);
            Canvas.SetTop(b, r.Y);
        }
    }

    private void UpdateViewHeight()
    {
        if (_vm != null && LaneScroll.Bounds.Height > 0)
            _vm.ViewHeight = LaneScroll.Bounds.Height;
    }

    private void PositionJudgmentLine()
    {
        if (_vm == null) return;
        double h = LaneScroll.Bounds.Height;
        if (h <= 0) return;

        const double laneAreaLeft = 40;
        double y = Math.Clamp(_vm.JudgmentLinePosition * h, 0, h);
        Canvas.SetTop(JudgmentTriangle, y - 7);
        Canvas.SetLeft(JudgmentTriangle, laneAreaLeft - 14);
        Canvas.SetTop(JudgmentLine, y - 1);
        Canvas.SetLeft(JudgmentLine, laneAreaLeft - 4);
        JudgmentLine.Width = Math.Max(0, Math.Min(
            JudgmentCanvas.Bounds.Width - (laneAreaLeft - 4),
            LaneContainer.Bounds.Width + 4));
    }

    private void OnTrianglePressed(object? sender, PointerPressedEventArgs e)
    {
        _isDragging = true;
        e.Pointer.Capture(JudgmentTriangle);
        e.Handled = true;
    }

    private void OnTriangleMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging) return;
        var pos = e.GetPosition(JudgmentCanvas);
        double h = LaneScroll.Bounds.Height;
        if (h <= 0) return;

        double y = Math.Clamp(pos.Y, 0, h);
        Canvas.SetTop(JudgmentTriangle, y - 7);
        Canvas.SetTop(JudgmentLine, y - 1);
    }

    private void OnTriangleReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        e.Pointer.Capture(null);

        if (_vm != null)
        {
            var pos = e.GetPosition(JudgmentCanvas);
            double h = JudgmentCanvas.Bounds.Height;
            if (h > 0)
            {
                double normalized = Math.Clamp(pos.Y / h, 0, 1);
                _vm.JudgmentLinePosition = normalized;
            }
        }
    }

    private void OnRootGridPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_vm == null) return;
        var pos = e.GetPosition(MotionCanvas);

        // 实时同步 Shift 状态（放在起拖判定之前）：起拖前按住即全程锁定；
        // 键盘事件丢失（如失焦）时这里也能在下次移动自动纠正
        _vm.SetDragTimeLock(e.KeyModifiers.HasFlag(KeyModifiers.Shift));

        // 展开条优先：在条上时只更新列悬浮，不做下层命中
        if (_vm.IsOverlapPanelOpen)
        {
            if (IsInOverlapPanelStrip(pos))
            {
                CancelOverlapCloseTimer();
                _vm.SetOverlapPanelHover(GetOverlapPanelColumnAt(pos));
                MotionCanvas.Cursor = HandCursor;
                return;
            }
            _vm.SetOverlapPanelHover(-1);
        }

        if (_motionPressed)
        {
            if (_motionDragging && !_vm.IsDragActive)
            {
                _motionDragging       = false;
                _motionPressed        = false;
                _motionDeferredSelect = false;
                _motionDragAnchor     = null;
                e.Pointer.Capture(null);
            }
            else if (!_motionDragging && Distance(pos, _motionPressPoint) > BoxDragThreshold)
            {
                _vm.OnBeginDrag?.Invoke(_motionDragAnchor!, _motionPressPoint.X, _motionPressPoint.Y);
                if (_vm.IsDragActive)
                {
                    _motionDragging = true;
                    e.Pointer.Capture(MotionCanvas);
                }
                else
                {
                    _motionPressed = false;
                    if (_motionDeferredSelect)
                        _vm.OnSelectUnit?.Invoke(_motionDragAnchor, false);
                    _motionDeferredSelect = false;
                    _motionDragAnchor = null;
                }
            }
        }

        // 把手拖拽：与普通拖拽一样超过阈值才启动，避免误触
        if (_handlePressed)
        {
            if (_handleDragging && !_vm.IsHandleResizing)
            {
                // VM 中途取消了（如设置关闭拖拽）：复位把手拖拽状态
                _handleDragging = false;
                _handlePressed  = false;
                _handleAnchor   = null;
                e.Pointer.Capture(null);
            }
            else if (!_handleDragging && Distance(pos, _handlePressPoint) > BoxDragThreshold)
            {
                if (_handleAnchor is { } anchor && _vm.BeginHandleResize(anchor, _handleIsEnd))
                {
                    _handleDragging = true;
                    e.Pointer.Capture(MotionCanvas);
                }
                else
                {
                    _handlePressed = false;
                    _handleAnchor  = null;
                }
            }
        }

        // 快捷放置：空白处按住拖动超过阈值 → 进入拖拽定长（仅有时长的类型）。
        // 无时长类型保持单击行为：松开时在按下点放置零时长事件
        if (_quickPressed)
        {
            if (!_quickDragging && Distance(pos, _quickPressPoint) > BoxDragThreshold)
            {
                if (_vm.BeginQuickPlace(_quickPressPoint.X, _quickPressPoint.Y))
                {
                    _quickDragging = true;
                    e.Pointer.Capture(MotionCanvas); // 捕获指针：拖出控件范围也能继续收到移动/松开
                }
            }
        }

        if (_boxPressed)
        {
            if (!_boxDragging && Distance(pos, _boxPressPoint) > BoxDragThreshold)
            {
                _boxDragging = true;
                e.Pointer.Capture(MotionCanvas);
                _vm.BeginBoxSelect(_boxPressPoint.X, _boxPressPoint.Y, _boxAppend);
            }
            if (_boxDragging)
                _vm.UpdateBoxSelect(pos.X, pos.Y);
        }

        // 光标：把手拖拽中恒为上下箭头；悬停白带外侧把手时也显示上下箭头；
        // 其余命中身体时保持手型。放置/幽灵/重定位期间不提示把手
        bool handleEnabled = _vm.IsDragMode
                             && !_vm.IsGhostActive && !_vm.IsRelocating && !_vm.IsPlacing;
        var hoverHit = MotionLayer.HitTestItem(pos, handleEnabled, out MotionHitKind hoverKind);
        if (_handleDragging || (handleEnabled && hoverHit != null && hoverKind != MotionHitKind.Body))
            MotionCanvas.Cursor = VerticalResizeCursor;
        else
            MotionCanvas.Cursor = hoverHit != null ? HandCursor : null;

        // 展开条打开时由整视图级 PointerMoved 管理存续；这里只负责未展开时的悬浮计时
        bool previewActive = _vm.IsGhostActive || _vm.IsRelocating || _vm.IsPlacing;
        if (!_vm.IsOverlapPanelOpen)
        {
            if (!previewActive && hoverHit?.SelectionTarget is { } hoverTarget &&
                 _vm.TryGetOverlapGroup(hoverTarget, out _))
            {
                if (!ReferenceEquals(_overlapHoverUnit, hoverTarget))
                    StartOverlapHoverTimer(hoverTarget);
            }
            else
            {
                StopOverlapHoverTimer();
            }
        }

        _vm.UpdatePreviewAt(pos.X, pos.Y);
    }

    // === 重叠展开选择条：计时 / 定位 / 命中 ===

    private void StartOverlapHoverTimer(object unit)
    {
        _overlapHoverUnit = unit;
        _overlapHoverTimer ??= CreateTimer(OverlapHoverDelayMs, OnOverlapHoverTick);
        _overlapHoverTimer.Stop();
        _overlapHoverTimer.Start();
    }

    private void StopOverlapHoverTimer()
    {
        _overlapHoverTimer?.Stop();
        _overlapHoverUnit = null;
    }

    private void OnOverlapHoverTick(object? sender, EventArgs e)
    {
        _overlapHoverTimer?.Stop();
        var unit = _overlapHoverUnit;
        _overlapHoverUnit = null;
        if (_vm == null || unit == null) return;
        if (!_vm.OpenOverlapPanel(unit)) return;
        PositionOverlapPanel();
    }

    private void StartOverlapCloseTimer()
    {
        _overlapCloseTimer ??= CreateTimer(OverlapCloseDelayMs, OnOverlapCloseTick);
        _overlapCloseTimer.Stop();
        _overlapCloseTimer.Start();
    }

    private void CancelOverlapCloseTimer() => _overlapCloseTimer?.Stop();

    private void OnOverlapCloseTick(object? sender, EventArgs e)
    {
        _overlapCloseTimer?.Stop();
        _vm?.CloseOverlapPanel();
    }

    private static DispatcherTimer CreateTimer(int intervalMs, EventHandler handler)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(intervalMs) };
        timer.Tick += handler;
        return timer;
    }

    /// <summary> 展开条横向放置：优先右侧，放不下改左侧，两侧都不够时仍放右侧并允许裁切 </summary>
    private void PositionOverlapPanel()
    {
        if (_vm == null || !_vm.IsOverlapPanelOpen || LaneScroll.Viewport.Width <= 0) return;

        double anchorLeft = _vm.OverlapPanelAnchorLeft;
        double stripWidth = _vm.OverlapPanelStripWidth;
        double offsetX = LaneScroll.Offset.X;
        double viewportWidth = LaneScroll.Viewport.Width;

        double rightLeft = anchorLeft + _vm.LaneWidth;
        if (rightLeft + stripWidth - offsetX <= viewportWidth)
        {
            _vm.OverlapPanelLeft = rightLeft;
            return;
        }

        double leftLeft = anchorLeft - stripWidth;
        _vm.OverlapPanelLeft = leftLeft - offsetX >= 0 ? leftLeft : rightLeft;
    }

    private bool IsInOverlapPanelStrip(Point pos)
    {
        if (_vm == null || !_vm.IsOverlapPanelOpen) return false;
        double x = pos.X - _vm.OverlapPanelLeft;
        return x >= 0 && x <= _vm.OverlapPanelStripWidth &&
               pos.Y >= _vm.OverlapPanelTop &&
               pos.Y <= _vm.OverlapPanelTop + _vm.OverlapPanelHeight;
    }

    private int GetOverlapPanelColumnAt(Point pos)
    {
        if (_vm == null || !IsInOverlapPanelStrip(pos)) return -1;

        double x = pos.X - _vm.OverlapPanelLeft;
        double stride = _vm.OverlapPanelColumnWidth + _vm.OverlapPanelSpacing;
        if (stride <= 0) return -1;

        int index = (int)(x / stride);
        if (index < 0 || index >= _vm.OverlapPanelColumns.Count) return -1;
        double inColumn = x - index * stride;
        return inColumn <= _vm.OverlapPanelColumnWidth ? index : -1;
    }

    private void OnOverlapPanelPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_vm == null || !_vm.IsOverlapPanelOpen) return;
        var pos = e.GetPosition(MotionCanvas);
        if (IsInOverlapPanelKeepZone(pos))
        {
            CancelOverlapCloseTimer();
            _vm.SetOverlapPanelHover(GetOverlapPanelColumnAt(pos)); // 不在列上时为 -1
        }
        else
        {
            _vm.SetOverlapPanelHover(-1);
            StartOverlapCloseTimer(); // 0ms：移出展开区域立即消失
        }
    }

    /// <summary> 展开条 + 源轨道列的纵向带；保证从 unit 横向移入展开条时不会断连 </summary>
    private bool IsInOverlapPanelKeepZone(Point pos)
    {
        if (_vm == null || !_vm.IsOverlapPanelOpen) return false;
        if (IsInOverlapPanelStrip(pos)) return true;

        double x = pos.X - _vm.OverlapPanelAnchorLeft;
        return x >= 0 && x <= _vm.LaneWidth &&
               pos.Y >= _vm.OverlapPanelTop &&
               pos.Y <= _vm.OverlapPanelTop + _vm.OverlapPanelHeight;
    }

    /// <summary> 指针离开整个视图：立即关闭展开条 </summary>
    private void OnOverlapPanelPointerExited(object? sender, PointerEventArgs e)
    {
        StopOverlapHoverTimer();
        if (_vm?.IsOverlapPanelOpen == true)
            StartOverlapCloseTimer();
    }

    private void OnOverlapPanelTunnelPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm == null || !_vm.IsOverlapPanelOpen) return;
        var pos = e.GetPosition(MotionCanvas);
        if (!IsInOverlapPanelStrip(pos)) return;

        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsLeftButtonPressed)
        {
            int index = GetOverlapPanelColumnAt(pos);
            if (index >= 0) _vm.SelectOverlapPanelColumn(index);
        }
        else if (props.IsRightButtonPressed)
        {
            _vm.CloseOverlapPanel();
        }
        e.Handled = true;
    }

    private static double Distance(Point a, Point b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private void OnMotionCanvasPointerExited(object? sender, PointerEventArgs e)
    {
        _vm?.HidePreview();
        StopOverlapHoverTimer();
    }
}
