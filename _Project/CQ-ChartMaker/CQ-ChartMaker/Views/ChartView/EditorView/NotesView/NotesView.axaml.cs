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

public partial class NotesView : UserControl
{
    private bool _isDragging;
    private NotesViewModel? _vm;

    /// <summary> 窗口引用（挂/摘 Shift 键监听用，Detached 时窗口已不可向上查找，需缓存） </summary>
    private TopLevel? _topLevel;

    // === 框选拖拽状态 ===
    private bool _boxPressed;     // 空白处按下了左键（等待判定：点空白还是拖框）
    private bool _boxDragging;    // 已超过拖拽阈值，进入框选
    private bool _boxAppend;      // 按下时按住 Ctrl（追加模式）
    private Point _boxPressPoint; // 按下点（画布坐标）
    private const double BoxDragThreshold = 4; // 超过该像素距离才判定为拖框/拖拽

    // === Note 拖拽状态 ===
    private bool _notePressed;        // 在可拖的 note 头或 Dash/Mute 身体上按下了左键（等待超过阈值）
    private bool _noteDragging;       // 已进入拖拽（VM 幽灵已激活）
    private bool _noteDeferredSelect; // 按下的是已选中项：选中收窄延迟到松手（否则多选无法拖）
    private Point _notePressPoint;    // 按下点（画布坐标，拖拽起点吸附用）
    private NoteUnit? _noteDragAnchor; // 被抓住的基准 note

    // === 尾部拖拽状态（拖动 Dash/Mute 尾部横条改变 duration） ===
    private bool _tailPressed;        // 在尾部横条上按下了左键（等待超过阈值）
    private bool _tailDragging;       // 已进入尾拖（VM 预览已激活）
    private bool _tailDeferredSelect; // 按下的是已选中项：选中收窄延迟到松手
    private Point _tailPressPoint;
    private NoteUnit? _tailAnchor;    // 被拖尾的 note

    // === 长按放置状态 ===
    private bool _placeHolding; // 已按下左键进入长按定长，松开时提交

    // === 快捷放置状态（设置开启时，空白处的单击/拖拽用于放置） ===
    private bool _quickPressed;   // 空白处按下了左键（等待判定：单击放 Dot / 拖动放 Dash）
    private bool _quickDragging;  // 已超过阈值，正在拖拽定长
    private Point _quickPressPoint;

    private IBrush? _beatLineBrush;
    private IBrush? _subBeatLineBrush;
    private IBrush? _laneDimBrush;

    // 悬停光标缓存：避免每次 PointerMoved 都 new Cursor
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
    // 尾部悬停光标：上下双箭头，提示可拖动改变 duration
    private static readonly Cursor VerticalResizeCursor = new(StandardCursorType.SizeNorthSouth);

    // === 重叠展开选择条 ===
    private const int OverlapHoverDelayMs = 300;
    private const int OverlapCloseDelayMs = 0; // 移出展开区域立即关闭

    private DispatcherTimer? _overlapHoverTimer;
    private DispatcherTimer? _overlapCloseTimer;
    private object? _overlapHoverUnit;

    public NotesView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;

        // 展开条点击用 Tunnel：先于下层选中/拖拽处理，且覆盖画布之外的空白区域
        AddHandler(PointerPressedEvent, OnOverlapPanelTunnelPressed, RoutingStrategies.Tunnel);
        // 指针移动挂在整视图上：展开条可能超出画布范围
        PointerMoved += OnOverlapPanelPointerMoved;
        PointerExited += OnOverlapPanelPointerExited;
    }

    private void OnAttached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _beatLineBrush    = (IBrush?)Resources["BeatLineBrush"];
        _subBeatLineBrush = (IBrush?)Resources["SubBeatLineBrush"];
        _laneDimBrush     = (IBrush?)Resources["LaneDimBrush"];

        if (DataContext is NotesViewModel vm)
        {
            _vm = vm;
            vm.PropertyChanged += OnVmPropertyChanged;
            UpdatePlacingButtons(); // 初始同步放置模式按钮
            RenderLaneDimRects();   // 初始渲染变暗遮罩

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

        NoteCanvas.PointerPressed  += OnNoteCanvasPressed;
        NoteCanvas.PointerReleased += OnNoteCanvasReleased;
        NoteCanvas.PointerExited   += OnNoteCanvasPointerExited;

        // 放置模式下全局右键取消（Tunnel 保证在子控件之前捕获）
        NoteCanvas.AddHandler(PointerPressedEvent, OnNoteCanvasTunnelPlacing, RoutingStrategies.Tunnel);

        // 预览：外层 Grid 捕获鼠标坐标，换算到 NoteCanvas 坐标系
        RootGrid.PointerMoved += OnRootGridPointerMoved;

        // 拖拽中按/松 Shift 锁定时间：窗口级 Tunnel 捕获按键（只观察，不吞事件）。
        // 挂在窗口而不是本控件，是因为拖拽时焦点可能落在工具栏等控件上
        _topLevel = TopLevel.GetTopLevel(this);
        _topLevel?.AddHandler(KeyDownEvent, OnTopLevelKeyDown, RoutingStrategies.Tunnel);
        _topLevel?.AddHandler(KeyUpEvent, OnTopLevelKeyUp, RoutingStrategies.Tunnel);

        JudgmentTriangle.PointerPressed += OnTrianglePressed;
        JudgmentTriangle.PointerMoved += OnTriangleMoved;
        JudgmentTriangle.PointerReleased += OnTriangleReleased;

        // 放置工具栏按钮
        DtButton.Click += OnPlacingButtonClick;
        DsButton.Click += OnPlacingButtonClick;
        MButton.Click  += OnPlacingButtonClick;
        TButton.Click  += OnPlacingButtonClick;
    }

    /// <summary> 移出视觉树时退订 VM：切走子视图后旧 View 不再收到刷新事件，避免后台空转与内存泄漏 </summary>
    private void OnDetached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        // 键监听挂在窗口上：离开时必须手动摘掉，否则旧 View 会被窗口持有。
        // RemoveHandler 只按事件与委托匹配，注册时的 Tunnel 策略无需重复指定
        _topLevel?.RemoveHandler(KeyDownEvent, OnTopLevelKeyDown);
        _topLevel?.RemoveHandler(KeyUpEvent, OnTopLevelKeyUp);
        _topLevel = null;

        _placeHolding = false; // 长按中途切走视图：本地状态复位（VM 的放置模式由子视图切换逻辑取消）
        _quickPressed = false;
        _quickDragging = false;
        _vm?.CancelQuickPlace(); // 快捷拖动中途切走视图：丢弃未提交的头
        _tailPressed = false;
        _tailDragging = false;
        _tailDeferredSelect = false;
        _tailAnchor = null;
        _vm?.CancelTailResize(); // 尾拖中途切走视图：丢弃预览，不改数据
        StopOverlapHoverTimer();
        CancelOverlapCloseTimer();
        _vm?.CloseOverlapPanel(); // 切走视图：恢复被展开条隐藏的 unit
        LaneScroll.ScrollChanged -= OnLaneScrollChanged;
        LaneScroll.SizeChanged -= OnLaneScrollSizeChanged;
        LaneHScroll.ValueChanged -= OnLaneHScrollChanged;
        if (_vm == null) return;
        _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = null;
    }

    // === 水平滚动条同步（滚轮归时间轴使用，横向滚动改为底部拖拽条） ===

    private bool _syncingScroll;

    private void OnLaneScrollChanged(object? sender, ScrollChangedEventArgs e) => UpdateHorizontalScrollBar();

    private void OnLaneScrollSizeChanged(object? sender, SizeChangedEventArgs e) => UpdateHorizontalScrollBar();

    private void OnLaneHScrollChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
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

    /// <summary> 放置按钮点击：切换或退出放置模式 </summary>
    private void OnPlacingButtonClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null || sender is not ToggleButton btn) return;

        // 先捕获点击后的状态：VM 同步按钮状态时可能改写 IsChecked
        bool isChecked = btn.IsChecked == true;

        var mode = NotePlacingMode.None;
        if (btn == DtButton) mode = NotePlacingMode.Dot;
        else if (btn == DsButton) mode = NotePlacingMode.Dash;
        else if (btn == MButton) mode = NotePlacingMode.Mute;
        else if (btn == TButton) mode = NotePlacingMode.Tune;

        // ToggleButton 内部已切换 IsChecked，若变为选中则设为对应模式，否则退出
        _vm.PlacingMode = isChecked ? mode : NotePlacingMode.None;
    }

    /// <summary> 根据 VM 的 PlacingMode 同步四个按钮的 IsChecked 状态 </summary>
    private void UpdatePlacingButtons()
    {
        if (_vm == null) return;
        DtButton.IsChecked = _vm.PlacingMode == NotePlacingMode.Dot;
        DsButton.IsChecked = _vm.PlacingMode == NotePlacingMode.Dash;
        MButton.IsChecked  = _vm.PlacingMode == NotePlacingMode.Mute;
        TButton.IsChecked  = _vm.PlacingMode == NotePlacingMode.Tune;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NotesViewModel.JudgmentLinePosition))
            PositionJudgmentLine();
        else if (e.PropertyName == nameof(NotesViewModel.BeatLineSegments) ||
                 e.PropertyName == nameof(NotesViewModel.BeatLineLabels))
            RenderBeatLines();
        else if (e.PropertyName == nameof(NotesViewModel.SubBeatLineSegments))
            RenderSubBeatLines();
        else if (e.PropertyName == nameof(NotesViewModel.LaneDimRects))
            RenderLaneDimRects();
        else if (e.PropertyName == nameof(NotesViewModel.NoteVisualItems))
        {
            NoteLayer.InvalidateVisual();
            RenderBoxSelect(); // 滚动/缩放等刷新时，框选矩形也要跟着重定位
        }
        else if (e.PropertyName == nameof(NotesViewModel.PreviewItems) ||
                 e.PropertyName == nameof(NotesViewModel.OverlapMarkers))
            NoteLayer.InvalidateVisual();
        else if (e.PropertyName == nameof(NotesViewModel.IsOverlapPanelOpen))
        {
            if (_vm?.IsOverlapPanelOpen != true)
            {
                StopOverlapHoverTimer();
                CancelOverlapCloseTimer();
            }
            NoteLayer.InvalidateVisual();
        }
        else if (e.PropertyName == nameof(NotesViewModel.OverlapPanelHoverIndex) ||
                 e.PropertyName == nameof(NotesViewModel.OverlapPanelLeft))
            NoteLayer.InvalidateVisual();
        else if (e.PropertyName == nameof(NotesViewModel.BoxSelectRect))
            RenderBoxSelect();
        else if (e.PropertyName == nameof(NotesViewModel.PlacingMode))
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

    /// <summary> 放置/幽灵模式下拦截左右键（Tunnel，先于命中测试与框选，阻止误触选中）：幽灵模式优先 </summary>
    private void OnNoteCanvasTunnelPlacing(object? sender, PointerPressedEventArgs e)
    {
        if (_vm == null || e.Handled) return;

        // 拖拽进行中：忽略所有按键（含右键），拖拽只由左键松开结束
        if (_noteDragging || _quickDragging || _tailDragging)
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
                var relPos = e.GetPosition(NoteCanvas);
                _vm.CommitRelocate(relPos.X, relPos.Y);
            }
            e.Handled = true;
            return;
        }

        bool ghost = _vm.IsGhostActive;
        if (!ghost && _vm.PlacingMode == NotePlacingMode.None)
        {
            // 快捷删除：普通状态右键命中 Note/Tune 时删除该 unit（与当前选中无关）。
            // 右键空白不改变选中
            if (_vm.IsQuickDeleteEnabled && props.IsRightButtonPressed)
            {
                if (NoteLayer.HitTestItem(e.GetPosition(NoteCanvas)) is { } hit)
                {
                    _vm.OnDeleteUnitRequested?.Invoke(hit.SelectionTarget);
                    e.Handled = true;
                }
            }
            return;
        }
        if (props.IsRightButtonPressed)
        {
            // 右键取消：幽灵 / 放置（长按过程中同时复位本地捕获状态）
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
            var pos = e.GetPosition(NoteCanvas); // NoteCanvas 画布坐标
            if (ghost)
            {
                _vm.TryCommitGhost(pos.X, pos.Y);
            }
            else if (_vm.IsHoldPlaceMode &&
                     _vm.PlacingMode is NotePlacingMode.Dash or NotePlacingMode.Mute or NotePlacingMode.Tune)
            {
                // 长按模式：按下定头，拖动由 PointerMoved 更新预览，松开时提交。
                // 捕获指针，保证拖出画布范围后仍能收到松开事件
                if (_vm.BeginHoldPlace(pos.X, pos.Y))
                {
                    _placeHolding = true;
                    e.Pointer.Capture(NoteCanvas);
                }
            }
            else
            {
                _vm.TryPlace(pos.X, pos.Y);
            }
            e.Handled = true;
        }
    }

    /// <summary>
    /// 左键按下：先做 Note 命中测试（自绘层没有子控件，命中改为数学判定）；
    /// 命中则选中并结束事件，未命中才记录框选起点。
    /// 拖拽模式下命中可拖的头或 Dash/Mute 身体时进入拖拽候选；若命中的是已选中项，
    /// 选中收窄延迟到松手（拖动则保持整组选中，不拖才收窄为单选）。
    /// </summary>
    private void OnNoteCanvasPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm == null || e.Handled) return; // 放置/幽灵模式的 Tunnel 处理器已处理
        if (!e.GetCurrentPoint(NoteCanvas).Properties.IsLeftButtonPressed) return;

        var pos = e.GetPosition(NoteCanvas);
        var hit = NoteLayer.HitTestItem(pos);
        if (hit is { } item)
        {
            bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

            // 尾部横条：拖拽模式下按住可把尾部拖到新时间以改变 duration。
            // 与普通 note 拖拽一致：未选中则立即选中；已选中则延迟收窄（多选时不破坏整组选中）
            if (item.IsTail && _vm.IsDragMode && !ctrl &&
                item.SelectionTarget is NoteUnit tailAnchor)
            {
                bool tailDefer = item.IsSelected;
                if (!tailDefer)
                    _vm.OnSelectUnit?.Invoke(tailAnchor, false);

                _tailPressed        = true;
                _tailDragging       = false;
                _tailDeferredSelect = tailDefer;
                _tailPressPoint     = pos;
                _tailAnchor         = tailAnchor;
                e.Handled = true;
                return;
            }

            // 可起拖：拖拽模式 + 未按 Ctrl + 不是 Tune，且命中头或 Dash/Mute 身体
            // （Tune 依附父 Dash，不能独立拖动；身体起拖时 VM 仍以所属头的位置做时间吸附）
            bool canDrag = _vm.IsDragMode && !ctrl && item.Type != NoteVisualType.Tune
                           && (item.IsHead || item.Type is NoteVisualType.Dash or NoteVisualType.Mute);

            // 已选中项不立刻收窄：否则多选拖拽在按下瞬间就退化成单选
            bool defer = canDrag && item.IsSelected;
            if (!defer)
                _vm.OnSelectUnit?.Invoke(item.SelectionTarget, ctrl);

            if (canDrag && item.SelectionTarget is NoteUnit anchor)
            {
                _notePressed        = true;
                _noteDragging       = false;
                _noteDeferredSelect = defer;
                _notePressPoint     = pos;
                _noteDragAnchor     = anchor;
            }

            e.Handled = true;
            return;
        }

        // 快捷放置：空白处左键交给快捷放置状态机（单击放 Dot、拖动放 Dash）。
        // 开启后空白的单击取消选中、双击选轨、长按框选全部禁用；
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

        // 双击轨道空白：选中该轨道上的全部 Note（Ctrl 追加）。
        // 命中 unit 的双击不进入这里，仍按原来的单击/拖拽逻辑处理
        if (e.ClickCount == 2)
        {
            if (PositionCalculator.TryXSnapToLane(
                    pos.X, _vm.LaneWidth, _vm.LaneSpacing, _vm.VisibleLanes, out int lane))
            {
                _vm.OnSelectLane?.Invoke(lane, e.KeyModifiers.HasFlag(KeyModifiers.Control));
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
    /// 松开左键：拖拽 → 提交/取消；按下可拖项但未拖动 → 需要时收窄为单选；
    /// 否则走原有框选/点空白逻辑。
    /// </summary>
    private void OnNoteCanvasReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_vm == null) return;

        // 快捷放置：拖动结束提交 Dash；未拖动则按单击放 Dot
        if (_quickDragging)
        {
            var pos = e.GetPosition(NoteCanvas);
            _quickDragging = false;
            _quickPressed  = false;
            e.Pointer.Capture(null);
            _vm.EndQuickPlace(pos.Y);
            return;
        }
        if (_quickPressed)
        {
            _quickPressed = false;
            _vm.QuickPlaceDot(_quickPressPoint.X, _quickPressPoint.Y);
            return;
        }

        // 长按放置：松开即定长提交，先于拖拽/框选逻辑处理
        if (_placeHolding)
        {
            var pos = e.GetPosition(NoteCanvas);
            _placeHolding = false;
            e.Pointer.Capture(null);
            _vm.EndHoldPlace(pos.Y);
            return;
        }

        // 尾拖松手：按当前位置的吸附时间提交新的 duration
        if (_tailDragging)
        {
            var pos = e.GetPosition(NoteCanvas);
            _tailDragging       = false;
            _tailPressed        = false;
            _tailDeferredSelect = false;
            _tailAnchor         = null;
            e.Pointer.Capture(null);
            _vm.EndTailResize(pos.Y);
            return;
        }

        // 在尾部按了一下但没拖动：已选中项此时才收窄为单选
        if (_tailPressed)
        {
            _tailPressed        = false;
            bool tailCollapse   = _tailDeferredSelect;
            var  tailAnchor     = _tailAnchor;
            _tailDeferredSelect = false;
            _tailAnchor         = null;
            if (tailCollapse && tailAnchor != null)
                _vm.OnSelectUnit?.Invoke(tailAnchor, false);
            return;
        }

        // 拖拽松手：有效位置提交移动，无效位置（轨道外）由 VM 取消并还原
        if (_noteDragging)
        {
            var pos = e.GetPosition(NoteCanvas);
            _noteDragging       = false;
            _notePressed        = false;
            _noteDeferredSelect = false;
            _noteDragAnchor     = null;
            e.Pointer.Capture(null);
            // 以松手瞬间的按键状态为准，兜底漏掉的键盘事件
            _vm.SetDragTimeLock(e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            _vm.EndDragGhost(pos.X, pos.Y);
            return;
        }

        // 在可拖项上按了一下但没拖动：已选中项此时才收窄为单选（与原单击行为一致）
        if (_notePressed)
        {
            _notePressed        = false;
            bool collapse       = _noteDeferredSelect;
            var anchor          = _noteDragAnchor;
            _noteDeferredSelect = false;
            _noteDragAnchor     = null;
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

    /// <summary> 渲染完全隐藏区间的变暗遮罩：按轨道列宽铺底色半透明矩形（复用控件，不每帧新建） </summary>
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
        // 线宽最多覆盖轨道内容宽度，轨道少时不延伸到右侧空白
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
        var pos = e.GetPosition(NoteCanvas);

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
                NoteCanvas.Cursor = HandCursor;
                return;
            }
            _vm.SetOverlapPanelHover(-1);
        }

        // Note 拖拽：超过阈值后启动幽灵；VM 中途取消（如模式关闭）时同步复位本地状态
        if (_notePressed)
        {
            if (_noteDragging && !_vm.IsDragActive)
            {
                _noteDragging       = false;
                _notePressed        = false;
                _noteDeferredSelect = false;
                _noteDragAnchor     = null;
                e.Pointer.Capture(null);
            }
            else if (!_noteDragging && Distance(pos, _notePressPoint) > BoxDragThreshold)
            {
                _vm.OnBeginDrag?.Invoke(_noteDragAnchor!, _notePressPoint.X, _notePressPoint.Y);
                if (_vm.IsDragActive)
                {
                    _noteDragging = true;
                    e.Pointer.Capture(NoteCanvas); // 捕获指针：拖出控件范围也能继续收到移动/松开
                }
                else
                {
                    // 无法起拖（选中集合异常等）：本次按下不再重试；延迟的选中按单击处理
                    _notePressed = false;
                    if (_noteDeferredSelect)
                        _vm.OnSelectUnit?.Invoke(_noteDragAnchor, false);
                    _noteDeferredSelect = false;
                    _noteDragAnchor = null;
                }
            }
        }

        // 尾部拖拽：超过阈值后进入尾拖；VM 中途取消（如设置关闭拖拽）时同步复位本地状态
        if (_tailPressed)
        {
            if (_tailDragging && !_vm.IsTailResizing)
            {
                _tailDragging       = false;
                _tailPressed        = false;
                _tailDeferredSelect = false;
                _tailAnchor         = null;
                e.Pointer.Capture(null);
            }
            else if (!_tailDragging && Distance(pos, _tailPressPoint) > BoxDragThreshold)
            {
                if (_vm.BeginTailResize(_tailAnchor!))
                {
                    _tailDragging = true;
                    e.Pointer.Capture(NoteCanvas); // 捕获指针：拖出控件范围也能继续收到移动/松开
                }
                else
                {
                    // 无法起拖（数据已变化等）：本次按下不再重试，延迟的选中按单击处理
                    _tailPressed = false;
                    if (_tailDeferredSelect)
                        _vm.OnSelectUnit?.Invoke(_tailAnchor, false);
                    _tailDeferredSelect = false;
                    _tailAnchor = null;
                }
            }
        }

        // 快捷放置：空白处按住拖动超过阈值 → 进入拖拽定长（头为按下点吸附位置）
        if (_quickPressed)
        {
            if (!_quickDragging && Distance(pos, _quickPressPoint) > BoxDragThreshold)
            {
                if (_vm.BeginQuickPlace(_quickPressPoint.X, _quickPressPoint.Y))
                {
                    _quickDragging = true;
                    e.Pointer.Capture(NoteCanvas); // 捕获指针：拖出控件范围也能继续收到移动/松开
                }
                else
                {
                    // 起点不在轨道内等：本次手势不做任何事（不退回框选）
                    _quickPressed = false;
                }
            }
        }

        // 空白按下后的拖动：超过阈值进入框选，之后每次移动只更新矩形（不做选中计算）
        if (_boxPressed)
        {
            if (!_boxDragging && Distance(pos, _boxPressPoint) > BoxDragThreshold)
            {
                _boxDragging = true;
                e.Pointer.Capture(NoteCanvas); // 捕获指针：拖出控件范围也能继续收到移动/松开事件
                _vm.BeginBoxSelect(_boxPressPoint.X, _boxPressPoint.Y, _boxAppend);
            }
            if (_boxDragging)
                _vm.UpdateBoxSelect(pos.X, pos.Y);
        }

        // 悬停光标：自绘层没有子控件可设 Cursor，统一在这里按命中结果切换。
        // 预览状态（放置/幽灵/重定位）下点击不会选中，不显示手型以免误导。
        // 尾部（拖拽开启时）与尾拖进行中显示上下箭头，提示可改变 duration
        bool previewActive = _vm.IsGhostActive
                             || _vm.IsRelocating
                             || _vm.PlacingMode != NotePlacingMode.None;
        NoteVisualItem? hoverHit = previewActive ? null : NoteLayer.HitTestItem(pos);
        NoteCanvas.Cursor = _tailDragging || (hoverHit?.IsTail == true && _vm.IsDragMode)
            ? VerticalResizeCursor
            : hoverHit != null ? HandCursor : null;

        // 展开条打开时由整视图级 PointerMoved 管理存续；这里只负责未展开时的悬浮计时
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

    /// <summary> 悬浮重叠 unit 达到延迟后展开其余同组 unit </summary>
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

        double rightLeft = anchorLeft + _vm.LaneWidth; // 从该列右端开始
        if (rightLeft + stripWidth - offsetX <= viewportWidth)
        {
            _vm.OverlapPanelLeft = rightLeft;
            return;
        }

        double leftLeft = anchorLeft - stripWidth;
        _vm.OverlapPanelLeft = leftLeft - offsetX >= 0 ? leftLeft : rightLeft;
    }

    /// <summary> 点是否落在展开条整体范围内（含列间距） </summary>
    private bool IsInOverlapPanelStrip(Point pos)
    {
        if (_vm == null || !_vm.IsOverlapPanelOpen) return false;
        double x = pos.X - _vm.OverlapPanelLeft;
        return x >= 0 && x <= _vm.OverlapPanelStripWidth &&
               pos.Y >= _vm.OverlapPanelTop &&
               pos.Y <= _vm.OverlapPanelTop + _vm.OverlapPanelHeight;
    }

    /// <summary> 点所在的展开列序号；落在列间距或范围外返回 -1 </summary>
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

    /// <summary> 整视图级指针移动：展开区域 = 展开条 + 源轨道列在展开条纵向范围内的部分 </summary>
    private void OnOverlapPanelPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_vm == null || !_vm.IsOverlapPanelOpen) return;
        var pos = e.GetPosition(NoteCanvas);
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

    /// <summary> 整视图级 Tunnel 点击：整列背景都是点击区域，先于下层选中/拖拽 </summary>
    private void OnOverlapPanelTunnelPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm == null || !_vm.IsOverlapPanelOpen) return;
        var pos = e.GetPosition(NoteCanvas);
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

    private void OnNoteCanvasPointerExited(object? sender, PointerEventArgs e)
    {
        _vm?.HidePreview();
        StopOverlapHoverTimer();
    }
}
