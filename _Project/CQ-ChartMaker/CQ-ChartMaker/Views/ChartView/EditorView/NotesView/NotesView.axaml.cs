using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using CQ_ChartMaker.ViewModels;
using CQ_ChartMaker.Models;
using CQMusicGame.Shared;

namespace CQ_ChartMaker.Views;

public partial class NotesView : UserControl
{
    private bool _isDragging;
    private NotesViewModel? _vm;

    // === 框选拖拽状态 ===
    private bool _boxPressed;     // 空白处按下了左键（等待判定：点空白还是拖框）
    private bool _boxDragging;    // 已超过拖拽阈值，进入框选
    private bool _boxAppend;      // 按下时按住 Ctrl（追加模式）
    private Point _boxPressPoint; // 按下点（画布坐标）
    private const double BoxDragThreshold = 4; // 超过该像素距离才判定为拖框

    private IBrush? _beatLineBrush;
    private IBrush? _subBeatLineBrush;
    private IBrush? _dotBrush, _dashBrush, _muteBrush, _tuneBrush, _strokeBrush, _highlightBrush;

    public NotesView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttached;
    }

    private void OnAttached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _beatLineBrush    = (IBrush?)Resources["BeatLineBrush"];
        _subBeatLineBrush = (IBrush?)Resources["SubBeatLineBrush"];
        _dotBrush         = (IBrush?)Resources["DotBrush"];
        _dashBrush        = (IBrush?)Resources["DashBrush"];
        _muteBrush        = (IBrush?)Resources["MuteBrush"];
        _tuneBrush        = (IBrush?)Resources["TuneBrush"];
        _strokeBrush      = (IBrush?)Resources["NoteStrokeBrush"];
        _highlightBrush   = new SolidColorBrush(Color.FromRgb(0, 174, 255)); // HSB(199,100%,100%)

        if (DataContext is NotesViewModel vm)
        {
            _vm = vm;
            vm.PropertyChanged += OnVmPropertyChanged;

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
        }

        NoteCanvas.PointerPressed  += OnNoteCanvasPressed;
        NoteCanvas.PointerReleased += OnNoteCanvasReleased;
        NoteCanvas.PointerExited   += OnNoteCanvasPointerExited;

        // 放置模式下全局右键取消（Tunnel 保证在子控件之前捕获）
        NoteCanvas.AddHandler(PointerPressedEvent, OnNoteCanvasTunnelPlacing, RoutingStrategies.Tunnel);

        // 预览：外层 Grid 捕获鼠标坐标，换算到 NoteCanvas 坐标系
        RootGrid.PointerMoved += OnRootGridPointerMoved;

        JudgmentTriangle.PointerPressed += OnTrianglePressed;
        JudgmentTriangle.PointerMoved += OnTriangleMoved;
        JudgmentTriangle.PointerReleased += OnTriangleReleased;

        // 放置工具栏按钮
        DtButton.Click += OnPlacingButtonClick;
        DsButton.Click += OnPlacingButtonClick;
        MButton.Click  += OnPlacingButtonClick;
        TButton.Click  += OnPlacingButtonClick;
    }

    // === 放置工具栏 ===

    /// <summary> 放置按钮点击：切换或退出放置模式 </summary>
    private void OnPlacingButtonClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null || sender is not ToggleButton btn) return;

        var mode = NotePlacingMode.None;
        if (btn == DtButton) mode = NotePlacingMode.Dot;
        else if (btn == DsButton) mode = NotePlacingMode.Dash;
        else if (btn == MButton) mode = NotePlacingMode.Mute;
        else if (btn == TButton) mode = NotePlacingMode.Tune;

        // ToggleButton 内部已切换 IsChecked，若变为选中则设为对应模式，否则退出
        _vm.PlacingMode = btn.IsChecked == true ? mode : NotePlacingMode.None;
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
        else if (e.PropertyName == nameof(NotesViewModel.NoteVisualItems))
            RenderNotes();
        else if (e.PropertyName == nameof(NotesViewModel.PreviewItems))
            RenderPreview();
        else if (e.PropertyName == nameof(NotesViewModel.BoxSelectRect))
            RenderBoxSelect();
        else if (e.PropertyName == nameof(NotesViewModel.PlacingMode))
            UpdatePlacingButtons();
    }

    /// <summary> 点击 Note 控件 → 选中（Ctrl 加选/减选），点击空白 → 取消选中 </summary>
    private void OnNotePressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is Control ctrl && ctrl.Tag != null)
        {
            _vm.OnSelectUnit?.Invoke(ctrl.Tag, e.KeyModifiers.HasFlag(KeyModifiers.Control));
            e.Handled = true;
        }
    }

    /// <summary> 放置/幽灵模式下拦截左右键（Tunnel，先于子控件，阻止误触选中）：幽灵模式优先 </summary>
    private void OnNoteCanvasTunnelPlacing(object? sender, PointerPressedEventArgs e)
    {
        if (_vm == null) return;
        bool ghost = _vm.IsGhostActive;
        if (!ghost && _vm.PlacingMode == NotePlacingMode.None) return;

        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsRightButtonPressed)
        {
            // 右键取消：幽灵 / 放置
            if (ghost) _vm.CancelGhost();
            else _vm.CancelPlacement();
            e.Handled = true;
        }
        else if (props.IsLeftButtonPressed)
        {
            var pos = e.GetPosition(NoteCanvas); // NoteCanvas 画布坐标
            if (ghost) _vm.TryCommitGhost(pos.X, pos.Y);
            else _vm.TryPlace(pos.X, pos.Y);
            e.Handled = true;
        }
    }

    /// <summary> 点击 NoteCanvas 空白区域：先记录起点，松手时再判定是"点空白清选中"还是"拖框框选" </summary>
    private void OnNoteCanvasPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm == null || e.Handled) return; // 点在 note 上的按下已被子控件处理
        if (!e.GetCurrentPoint(NoteCanvas).Properties.IsLeftButtonPressed) return;

        _boxPressed    = true;
        _boxDragging   = false;
        _boxAppend     = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        _boxPressPoint = e.GetPosition(NoteCanvas);
    }

    /// <summary> 松开左键：已进入框选则应用框选结果；未超过阈值则按"点击空白"处理 </summary>
    private void OnNoteCanvasReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_vm == null || !_boxPressed) return;

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

    /// <summary>
    /// 遍历 NoteVisualItems 渲染所有可视元素。每个 item 对应一个控件，
    /// 列表已按绘制顺序排列（先底层后上层），不再依赖 ZIndex 分层。
    /// </summary>
    private void RenderNotes()
    {
        if (_vm == null) return;

        var items = _vm.NoteVisualItems;

        NoteCanvas.Children.Clear();

        foreach (var item in items)
        {
            Control ctrl;
            if (item.IsTriangle)
                ctrl = BuildTriangle(item);
            else if (item.IsHighlight)
                ctrl = BuildHighlight(item);
            else if (item.IsHead)
                ctrl = BuildHead(item);
            else
                ctrl = BuildBody(item);
            NoteCanvas.Children.Add(ctrl);
        }

        RenderBoxSelect(); // 滚动/缩放等刷新时，框选矩形也要跟着重定位
    }

    /// <summary>
    /// 遍历 PreviewItems 渲染放置预览：矩形用 Border，三角用 Polygon（按 Direction 选左右点位，
    /// 与 BuildTriangle 非高亮的形状一致），统一白色 + 40% 不透明度，不参与命中测试。
    /// </summary>
    private void RenderPreview()
    {
        if (_vm == null) return;

        PreviewCanvas.Children.Clear();
        foreach (var item in _vm.PreviewItems)
        {
            Control ctrl;
            if (item.IsTriangle)
            {
                ctrl = new Polygon
                {
                    Width = item.Width,
                    Height = item.Height,
                    Stretch = Stretch.None,
                    IsHitTestVisible = false,
                    Fill = Brushes.White,
                    Opacity = 0.4,
                    Points = item.Direction == TuneDirection.Right
                        ? new List<Point> { new(5, 0), new(5, 20), new(20, 10) }
                        : new List<Point> { new(15, 0), new(15, 20), new(0, 10) }
                };
            }
            else
            {
                ctrl = new Border
                {
                    Width = item.Width,
                    Height = item.Height,
                    Background = Brushes.White,
                    Opacity = 0.4,
                    IsHitTestVisible = false
                };
            }
            Canvas.SetLeft(ctrl, item.X);
            Canvas.SetTop(ctrl, item.Y);
            PreviewCanvas.Children.Add(ctrl);
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

    /// <summary> 构建 Tune 三角（或高亮三角） </summary>
    private Polygon BuildTriangle(NoteVisualItem item)
    {
        double offset = item.IsHighlight ? 2 : 0;
        var tri = new Polygon
        {
            Width = item.Width,
            Height = item.Height,
            Stretch = Stretch.None,
            IsHitTestVisible = false,
            Fill = item.IsHighlight ? null : _tuneBrush,
            Stroke = item.IsHighlight
                ? (item.IsSelected ? _highlightBrush : null)
                : _strokeBrush,
            StrokeThickness = item.IsHighlight ? 7 : 3,
            Points = item.Direction == TuneDirection.Right
                ? new List<Point> { new(5 + offset, offset), new(5 + offset, 20 + offset), new(20 + offset, 10 + offset) }
                : new List<Point> { new(15 + offset, offset), new(15 + offset, 20 + offset), new(offset, 10 + offset) }
        };
        Canvas.SetLeft(tri, item.X);
        Canvas.SetTop(tri, item.Y);
        return tri;
    }

    /// <summary> 构建高亮矩形（比 note 大 2px，蓝色底色） </summary>
    private Border BuildHighlight(NoteVisualItem item)
    {
        return new Border
        {
            Width = item.Width,
            Height = item.Height,
            Background = item.IsSelected ? _highlightBrush : Brushes.Transparent,
            BorderThickness = new Thickness(0),
            IsHitTestVisible = false,
            [Canvas.LeftProperty] = item.X,
            [Canvas.TopProperty] = item.Y
        };
    }

    /// <summary> 构建 Note 头（白色外框 + 彩色内填） </summary>
    private Border BuildHead(NoteVisualItem item)
    {
        bool isMute = item.Type == NoteType.Mute;
        double innerW = isMute ? 12 : item.Width - 6;
        double innerH = 8;

        var outer = new Border
        {
            Width = item.Width,
            Height = item.Height,
            Background = _strokeBrush, // 白色外框
            BorderThickness = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
            Tag = item.SelectionTarget,
            [Canvas.LeftProperty] = item.X,
            [Canvas.TopProperty] = item.Y
        };
        outer.PointerPressed += OnNotePressed;

        var inner = new Border
        {
            Width = innerW,
            Height = innerH,
            Background = GetNoteBrush(item.Type),
            IsHitTestVisible = false
        };

        var canvas = new Canvas();
        canvas.Children.Add(inner);
        Canvas.SetLeft(inner, (item.Width - innerW) / 2);
        Canvas.SetTop(inner, (item.Height - innerH) / 2);
        outer.Child = canvas;

        return outer;
    }

    /// <summary> 构建 Note 身体（纯色填充） </summary>
    private Border BuildBody(NoteVisualItem item)
    {
        var body = new Border
        {
            Width = item.Width,
            Height = item.Height,
            Background = GetNoteBrush(item.Type),
            BorderThickness = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
            Tag = item.SelectionTarget,
            [Canvas.LeftProperty] = item.X,
            [Canvas.TopProperty] = item.Y
        };
        body.PointerPressed += OnNotePressed;
        return body;
    }

    /// <summary> 根据 NoteType 获取对应颜色 Brush </summary>
    private IBrush? GetNoteBrush(NoteType type) => type switch
    {
        NoteType.Dot  => _dotBrush,
        NoteType.Dash => _dashBrush,
        NoteType.Mute => _muteBrush,
        NoteType.Tune => _tuneBrush,
        _ => null
    };

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

    private void UpdateViewHeight()
    {
        if (_vm != null && RootGrid.Bounds.Height > 0)
            _vm.ViewHeight = RootGrid.Bounds.Height;
    }

    private void PositionJudgmentLine()
    {
        if (_vm == null) return;
        double h = JudgmentCanvas.Bounds.Height;
        if (h <= 0) return;

        JudgmentCanvas.Width = LaneContainer.Bounds.Width;

        double y = Math.Clamp(_vm.JudgmentLinePosition * h, 0, h);
        Canvas.SetTop(JudgmentTriangle, y - 7);
        Canvas.SetLeft(JudgmentTriangle, -14);
        Canvas.SetTop(JudgmentLine, y - 1);
        Canvas.SetLeft(JudgmentLine, -4);
        JudgmentLine.Width = Math.Max(0, JudgmentCanvas.Bounds.Width + 4);
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
        double h = JudgmentCanvas.Bounds.Height;
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

        // 单次调用传给 VM，避免分两次 set 触发两遍重算
        _vm.UpdatePreviewAt(pos.X, pos.Y);
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
    }
}
