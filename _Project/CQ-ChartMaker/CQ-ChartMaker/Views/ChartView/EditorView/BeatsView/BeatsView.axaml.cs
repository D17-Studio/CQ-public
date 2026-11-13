using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CQ_ChartMaker.ViewModels;
using CQ_ChartMaker.Models;

namespace CQ_ChartMaker.Views;

public partial class BeatsView : UserControl
{
    private bool _isDragging;
    private BeatsViewModel? _vm;

    // === 框选拖拽状态 ===
    private bool _boxPressed;     // 空白处按下了左键（等待判定：点空白还是拖框）
    private bool _boxDragging;    // 已超过拖拽阈值，进入框选
    private bool _boxAppend;      // 按下时按住 Ctrl（追加模式）
    private Point _boxPressPoint; // 按下点（画布坐标）
    private const double BoxDragThreshold = 4; // 超过该像素距离才判定为拖框/拖拽

    // === BPM 拖拽状态 ===
    private bool _bpmPressed;        // 在 BPM 节点上按下了左键（等待超过阈值）
    private bool _bpmDragging;       // 已进入拖拽（VM 幽灵已激活）
    private bool _bpmDeferredSelect; // 按下的是已选中项：选中收窄延迟到松手
    private Point _bpmPressPoint;    // 按下点（画布坐标，拖拽起点吸附用）
    private BpmUnit? _bpmDragAnchor; // 被抓住的基准 BPM

    private IBrush? _beatLineBrush;
    private IBrush? _subBeatLineBrush;

    // 悬停光标缓存：避免每次 PointerMoved 都 new Cursor
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    // === 重叠展开选择条 ===
    private const int OverlapHoverDelayMs = 300;
    private const int OverlapCloseDelayMs = 0; // 移出展开区域立即关闭

    private DispatcherTimer? _overlapHoverTimer;
    private DispatcherTimer? _overlapCloseTimer;
    private object? _overlapHoverUnit;

    public BeatsView()
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

        if (DataContext is BeatsViewModel vm)
        {
            _vm = vm;
            vm.PropertyChanged += OnVmPropertyChanged;
            UpdatePlacingButtons(); // 初始同步放置按钮状态

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

        BpmNodeCanvas.PointerPressed  += OnBpmNodeCanvasPressed;
        BpmNodeCanvas.PointerReleased += OnBpmNodeCanvasReleased;

        // 放置模式下全局右键取消（Tunnel 保证在子控件之前捕获）
        BpmNodeCanvas.AddHandler(PointerPressedEvent, OnBpmNodeCanvasTunnelPlacing, RoutingStrategies.Tunnel);

        // 预览：BpmNodeCanvas 捕获鼠标坐标（单条轨道，不做轨道吸附，仅判断是否在轨道范围内）
        BpmNodeCanvas.PointerMoved += OnBpmNodeCanvasPointerMoved;
        BpmNodeCanvas.PointerExited += OnBpmNodeCanvasPointerExited;

        // 放置工具栏按钮
        BpmButton.Click += OnPlacingButtonClick;

        JudgmentTriangle.PointerPressed += OnTrianglePressed;
        JudgmentTriangle.PointerMoved += OnTriangleMoved;
        JudgmentTriangle.PointerReleased += OnTriangleReleased;
    }

    /// <summary> 移出视觉树时退订 VM：切走子视图后旧 View 不再收到刷新事件，避免后台空转与内存泄漏 </summary>
    private void OnDetached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        StopOverlapHoverTimer();
        CancelOverlapCloseTimer();
        _vm?.CloseOverlapPanel(); // 切走视图：恢复被展开条隐藏的节点
        LaneScroll.ScrollChanged -= OnLaneScrollChanged;
        LaneScroll.SizeChanged -= OnLaneScrollSizeChanged;
        LaneHScroll.ValueChanged -= OnLaneHScrollChanged;
        if (_vm == null) return;
        _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = null;
    }

    // === 水平滚动条同步（单轨时 Maximum 为 0，滑块满宽，仅保持与其它页一致） ===

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

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BeatsViewModel.JudgmentLinePosition))
            PositionJudgmentLine();
        else if (e.PropertyName == nameof(BeatsViewModel.BeatLineSegments) ||
                 e.PropertyName == nameof(BeatsViewModel.BeatLineLabels))
            RenderBeatLines();
        else if (e.PropertyName == nameof(BeatsViewModel.SubBeatLineSegments))
            RenderSubBeatLines();
        else if (e.PropertyName == nameof(BeatsViewModel.BpmVisualItems))
        {
            BpmLayer.InvalidateVisual();
            RenderBoxSelect(); // 滚动/缩放等刷新时，框选矩形也要跟着重定位
        }
        else if (e.PropertyName == nameof(BeatsViewModel.PreviewItems) ||
                 e.PropertyName == nameof(BeatsViewModel.OverlapMarkers))
            BpmLayer.InvalidateVisual();
        else if (e.PropertyName == nameof(BeatsViewModel.IsOverlapPanelOpen))
        {
            if (_vm?.IsOverlapPanelOpen != true)
            {
                StopOverlapHoverTimer();
                CancelOverlapCloseTimer();
            }
            BpmLayer.InvalidateVisual();
        }
        else if (e.PropertyName == nameof(BeatsViewModel.OverlapPanelHoverIndex) ||
                 e.PropertyName == nameof(BeatsViewModel.OverlapPanelLeft))
            BpmLayer.InvalidateVisual();
        else if (e.PropertyName == nameof(BeatsViewModel.BoxSelectRect))
            RenderBoxSelect();
        else if (e.PropertyName == nameof(BeatsViewModel.IsPlacing))
            UpdatePlacingButtons();
    }

    /// <summary>
    /// 左键按下：先做 BPM 节点命中测试（自绘层没有子控件，命中改为数学判定）；
    /// 命中则选中并结束事件，未命中才记录框选起点。
    /// 拖拽模式下节点整体可拖；已选中项延迟到松手再收窄（支持多选拖拽）。
    /// </summary>
    private void OnBpmNodeCanvasPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm == null || e.Handled) return; // 放置/幽灵模式的 Tunnel 处理器已处理
        if (!e.GetCurrentPoint(BpmNodeCanvas).Properties.IsLeftButtonPressed) return;

        var pos = e.GetPosition(BpmNodeCanvas);
        var hit = BpmLayer.HitTestItem(pos);
        if (hit is { } item)
        {
            bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

            // BPM 节点没有头/身体之分，整个节点都可起拖
            bool canDrag = _vm.IsDragMode && !ctrl;

            bool defer = canDrag && item.IsSelected;
            if (!defer)
                _vm.OnSelectUnit?.Invoke(item.SelectionTarget, ctrl);

            if (canDrag && item.SelectionTarget is BpmUnit anchor)
            {
                _bpmPressed        = true;
                _bpmDragging       = false;
                _bpmDeferredSelect = defer;
                _bpmPressPoint     = pos;
                _bpmDragAnchor     = anchor;
            }

            e.Handled = true;
            return;
        }

        // 快捷放置：空白处左键单击直接放 BPM（点按语义）。
        // 开启后空白的单击取消选中、双击全选、框选全部禁用；
        // Ctrl+左键空白不放置也不改变选中（保留复选习惯）
        if (_vm.IsQuickPlaceEnabled)
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
                _vm.QuickPlaceBpm(pos.X, pos.Y);
            e.Handled = true;
            return;
        }

        // 双击轨道空白：全选所有 BPM（Ctrl 追加）。Beats 只有一条轨道，命中节点时不触发
        if (e.ClickCount == 2)
        {
            if (PositionCalculator.TryXSnapToLane(
                    pos.X, _vm.LaneWidth, _vm.LaneSpacing, _vm.VisibleLanes, out _))
            {
                _vm.OnSelectAll?.Invoke(e.KeyModifiers.HasFlag(KeyModifiers.Control));
                e.Handled = true;
                return;
            }
        }

        _boxPressed    = true;
        _boxDragging   = false;
        _boxAppend     = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        _boxPressPoint = pos;
    }

    /// <summary>
    /// 松开左键：拖拽 → 提交/取消；按下节点但未拖动 → 需要时收窄为单选；
    /// 否则走原有框选/点空白逻辑。
    /// </summary>
    private void OnBpmNodeCanvasReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_vm == null) return;

        if (_bpmDragging)
        {
            var pos = e.GetPosition(BpmNodeCanvas);
            _bpmDragging       = false;
            _bpmPressed        = false;
            _bpmDeferredSelect = false;
            _bpmDragAnchor     = null;
            e.Pointer.Capture(null);
            _vm.EndDragGhost(pos.X, pos.Y);
            return;
        }

        if (_bpmPressed)
        {
            _bpmPressed        = false;
            bool collapse      = _bpmDeferredSelect;
            var anchor         = _bpmDragAnchor;
            _bpmDeferredSelect = false;
            _bpmDragAnchor     = null;
            if (collapse && anchor != null)
                _vm.OnSelectUnit?.Invoke(anchor, false);
            return;
        }

        if (!_boxPressed) return;

        _boxPressed = false;
        if (_boxDragging)
        {
            _boxDragging = false;
            e.Pointer.Capture(null); // 释放框选时捕获的指针
            _vm.EndBoxSelect();
        }
        else if (!_boxAppend) // Ctrl+点空白不改变选中
        {
            _vm.OnSelectUnit?.Invoke(null, false);
        }
    }

    private void RenderBeatLines()
    {
        if (_vm == null || _beatLineBrush == null) return;

        var segments = _vm.BeatLineSegments;
        // 线段复用
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
        // 标签复用
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

    /// <summary> 渲染框选矩形（半透明蓝底 + 1px 白描边），无框选时隐藏 </summary>
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

    private void UpdateViewHeight()
    {
        // 用横向滚动视口的高度：底部拖拽条占用的那一行不参与时间坐标映射
        if (_vm != null && LaneScroll.Bounds.Height > 0)
            _vm.ViewHeight = LaneScroll.Bounds.Height;
    }

    private void PositionJudgmentLine()
    {
        if (_vm == null) return;
        double h = LaneScroll.Bounds.Height;
        if (h <= 0) return;

        const double laneAreaLeft = 40; // 左侧节拍编号列宽度
        double y = Math.Clamp(_vm.JudgmentLinePosition * h, 0, h);
        Canvas.SetTop(JudgmentTriangle, y - 7);
        Canvas.SetLeft(JudgmentTriangle, laneAreaLeft - 14);
        Canvas.SetTop(JudgmentLine, y - 1);
        Canvas.SetLeft(JudgmentLine, laneAreaLeft - 4);
        // 线宽最多覆盖轨道内容宽度，单轨时不会延伸到右侧空白
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

    private void OnBpmNodeCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_vm == null) return;
        var pos = e.GetPosition(BpmNodeCanvas);

        // 展开条优先：在条上时只更新列悬浮，不做下层命中
        if (_vm.IsOverlapPanelOpen)
        {
            if (IsInOverlapPanelStrip(pos))
            {
                CancelOverlapCloseTimer();
                _vm.SetOverlapPanelHover(GetOverlapPanelColumnAt(pos));
                BpmNodeCanvas.Cursor = HandCursor;
                return;
            }
            _vm.SetOverlapPanelHover(-1);
        }

        // BPM 拖拽：超过阈值后启动幽灵；VM 中途取消（如模式关闭）时同步复位本地状态
        if (_bpmPressed)
        {
            if (_bpmDragging && !_vm.IsDragActive)
            {
                _bpmDragging       = false;
                _bpmPressed        = false;
                _bpmDeferredSelect = false;
                _bpmDragAnchor     = null;
                e.Pointer.Capture(null);
            }
            else if (!_bpmDragging && Distance(pos, _bpmPressPoint) > BoxDragThreshold)
            {
                _vm.OnBeginDrag?.Invoke(_bpmDragAnchor!, _bpmPressPoint.X, _bpmPressPoint.Y);
                if (_vm.IsDragActive)
                {
                    _bpmDragging = true;
                    e.Pointer.Capture(BpmNodeCanvas);
                }
                else
                {
                    _bpmPressed = false;
                    if (_bpmDeferredSelect)
                        _vm.OnSelectUnit?.Invoke(_bpmDragAnchor, false);
                    _bpmDeferredSelect = false;
                    _bpmDragAnchor = null;
                }
            }
        }

        // 空白按下后的拖动：超过阈值进入框选，之后每次移动只更新矩形（不做选中计算）
        if (_boxPressed)
        {
            if (!_boxDragging && Distance(pos, _boxPressPoint) > BoxDragThreshold)
            {
                _boxDragging = true;
                e.Pointer.Capture(BpmNodeCanvas); // 捕获指针：拖出控件范围也能继续收到移动/松开事件
                _vm.BeginBoxSelect(_boxPressPoint.X, _boxPressPoint.Y, _boxAppend);
            }
            if (_boxDragging)
                _vm.UpdateBoxSelect(pos.X, pos.Y);
        }

        // 悬停光标：自绘层没有子控件可设 Cursor，统一在这里按命中结果切换
        var hoverHit = BpmLayer.HitTestItem(pos);
        BpmNodeCanvas.Cursor = hoverHit != null ? HandCursor : null;

        // 展开条打开时由整视图级 PointerMoved 管理存续；这里只负责未展开时的悬浮计时
        bool previewActive = _vm.IsPlacing || _vm.IsGhostActive || _vm.IsRelocating;
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

        // 单次调用传给 VM，避免分两次 set 触发两遍重算
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
        var pos = e.GetPosition(BpmNodeCanvas);
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
        var pos = e.GetPosition(BpmNodeCanvas);
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

    /// <summary> 两点像素距离（框选拖拽阈值判定用） </summary>
    private static double Distance(Point a, Point b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private void OnBpmNodeCanvasPointerExited(object? sender, PointerEventArgs e)
    {
        _vm?.HidePreview();
        StopOverlapHoverTimer();
    }

    /// <summary> 放置按钮点击：切换或退出放置模式 </summary>
    private void OnPlacingButtonClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null || sender is not ToggleButton btn) return;

        // 先捕获点击后的状态：VM 同步按钮状态时可能改写 IsChecked
        bool isChecked = btn.IsChecked == true;

        // ToggleButton 内部已切换 IsChecked，若变为选中则进入放置模式，否则退出
        _vm.IsPlacing = isChecked;
    }

    /// <summary> 根据 VM 的 IsPlacing 同步按钮的 IsChecked 状态 </summary>
    private void UpdatePlacingButtons()
    {
        if (_vm == null) return;
        BpmButton.IsChecked = _vm.IsPlacing;
    }

    /// <summary> 放置/幽灵模式下拦截左右键（Tunnel，先于子控件，阻止误触选中）：幽灵模式优先 </summary>
    private void OnBpmNodeCanvasTunnelPlacing(object? sender, PointerPressedEventArgs e)
    {
        if (_vm == null || e.Handled) return;

        // 拖拽进行中：忽略所有按键（含右键），拖拽只由左键松开结束
        if (_bpmDragging)
        {
            e.Handled = true;
            return;
        }

        var props = e.GetCurrentPoint(this).Properties;

        // 重定位模式优先：左键应用，右键取消
        if (_vm.IsRelocating)
        {
            if (props.IsRightButtonPressed)
                _vm.CancelRelocate();
            else if (props.IsLeftButtonPressed)
            {
                var relPos = e.GetPosition(BpmNodeCanvas);
                _vm.CommitRelocate(relPos.X, relPos.Y);
            }
            e.Handled = true;
            return;
        }

        bool ghost = _vm.IsGhostActive;
        if (!ghost && !_vm.IsPlacing)
        {
            // 快捷删除：普通状态右键命中 BPM 时删除该 unit（与当前选中无关）。
            // 右键空白不改变选中
            if (_vm.IsQuickDeleteEnabled && props.IsRightButtonPressed)
            {
                if (BpmLayer.HitTestItem(e.GetPosition(BpmNodeCanvas)) is { } hit)
                {
                    _vm.OnDeleteUnitRequested?.Invoke(hit.SelectionTarget!);
                    e.Handled = true;
                }
            }
            return;
        }

        if (props.IsRightButtonPressed)
        {
            // 右键取消：幽灵 / 放置
            if (ghost) _vm.CancelGhost();
            else _vm.CancelPlacement();
            e.Handled = true;
        }
        else if (props.IsLeftButtonPressed)
        {
            var pos = e.GetPosition(BpmNodeCanvas); // BpmNodeCanvas 画布坐标
            if (ghost) _vm.TryCommitGhost(pos.X, pos.Y);
            else _vm.TryPlace(pos.X, pos.Y);
            e.Handled = true;
        }
    }
}
