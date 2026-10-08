using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using CQ_ChartMaker.ViewModels;

namespace CQ_ChartMaker.Views;

public partial class LanesView : UserControl
{
    private bool _isDragging;
    private LanesViewModel? _vm;

    // === 框选拖拽状态 ===
    private bool _boxPressed;     // 空白处按下了左键（等待判定：点空白还是拖框）
    private bool _boxDragging;    // 已超过拖拽阈值，进入框选
    private bool _boxAppend;      // 按下时按住 Ctrl（追加模式）
    private Point _boxPressPoint; // 按下点（画布坐标）
    private const double BoxDragThreshold = 4; // 超过该像素距离才判定为拖框

    private IBrush? _beatLineBrush;
    private IBrush? _subBeatLineBrush;
    private IBrush? _motionBrush, _nodeStrokeBrush, _highlightBrush;

    public LanesView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttached;
    }

    private void OnAttached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _beatLineBrush = (IBrush?)Resources["BeatLineBrush"];
        _subBeatLineBrush = (IBrush?)Resources["SubBeatLineBrush"];
        _motionBrush = (IBrush?)Resources["MotionBrush"];
        _nodeStrokeBrush = (IBrush?)Resources["NodeStrokeBrush"];
        _highlightBrush = new SolidColorBrush(Color.FromRgb(0, 174, 255)); // HSB(199,100%,100%)

        if (DataContext is LanesViewModel vm)
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

        MotionCanvas.PointerPressed  += OnMotionCanvasPressed;
        MotionCanvas.PointerReleased += OnMotionCanvasReleased;

        // 放置模式下全局右键取消（Tunnel 保证在子控件之前捕获）
        MotionCanvas.AddHandler(PointerPressedEvent, OnMotionCanvasTunnelPlacing, RoutingStrategies.Tunnel);

        // 放置工具栏按钮
        MotionButton.Click += OnPlacingButtonClick;

        // 预览：外层 Grid 捕获鼠标坐标，换算到 MotionCanvas 坐标系
        RootGrid.PointerMoved += OnRootGridPointerMoved;
        RootGrid.PointerExited += OnRootGridPointerExited;

        JudgmentTriangle.PointerPressed += OnTrianglePressed;
        JudgmentTriangle.PointerMoved += OnTriangleMoved;
        JudgmentTriangle.PointerReleased += OnTriangleReleased;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LanesViewModel.JudgmentLinePosition))
            PositionJudgmentLine();
        else if (e.PropertyName == nameof(LanesViewModel.BeatLineSegments) ||
                 e.PropertyName == nameof(LanesViewModel.BeatLineLabels))
            RenderBeatLines();
        else if (e.PropertyName == nameof(LanesViewModel.SubBeatLineSegments))
            RenderSubBeatLines();
        else if (e.PropertyName == nameof(LanesViewModel.MotionVisualItems))
            RenderMotions();
        else if (e.PropertyName == nameof(LanesViewModel.PreviewItems))
            RenderPreview();
        else if (e.PropertyName == nameof(LanesViewModel.BoxSelectRect))
            RenderBoxSelect();
        else if (e.PropertyName == nameof(LanesViewModel.IsPlacing))
            UpdatePlacingButtons();
    }

    // === LaneMotion 显示 / 放置 ===

    /// <summary> 放置按钮点击：切换或退出放置模式 </summary>
    private void OnPlacingButtonClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null || sender is not ToggleButton btn) return;

        // ToggleButton 内部已切换 IsChecked，若变为选中则进入放置模式，否则退出
        _vm.IsPlacing = btn.IsChecked == true;
    }

    /// <summary> 根据 VM 的 IsPlacing 同步按钮的 IsChecked 状态 </summary>
    private void UpdatePlacingButtons()
    {
        if (_vm == null) return;
        MotionButton.IsChecked = _vm.IsPlacing;
    }

    /// <summary> 点击 Motion 节点 → 选中（Ctrl 加选/减选），点击空白 → 取消选中 </summary>
    private void OnMotionPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is Control ctrl && ctrl.Tag != null)
        {
            _vm.OnSelectUnit?.Invoke(ctrl.Tag, e.KeyModifiers.HasFlag(KeyModifiers.Control));
            e.Handled = true;
        }
    }

    /// <summary> 点击 MotionCanvas 空白区域：先记录起点，松手时再判定是"点空白清选中"还是"拖框框选" </summary>
    private void OnMotionCanvasPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm == null || e.Handled) return; // 点在节点上的按下已被子控件处理
        if (!e.GetCurrentPoint(MotionCanvas).Properties.IsLeftButtonPressed) return;

        _boxPressed    = true;
        _boxDragging   = false;
        _boxAppend     = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        _boxPressPoint = e.GetPosition(MotionCanvas);
    }

    /// <summary> 松开左键：已进入框选则应用框选结果；未超过阈值则按"点击空白"处理 </summary>
    private void OnMotionCanvasReleased(object? sender, PointerReleasedEventArgs e)
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

    /// <summary> 放置/幽灵模式下拦截左右键（Tunnel，先于子控件，阻止误触选中）：幽灵模式优先 </summary>
    private void OnMotionCanvasTunnelPlacing(object? sender, PointerPressedEventArgs e)
    {
        if (_vm == null) return;
        bool ghost = _vm.IsGhostActive;
        if (!ghost && !_vm.IsPlacing) return;

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
            var pos = e.GetPosition(MotionCanvas); // MotionCanvas 画布坐标
            if (ghost) _vm.TryCommitGhost(pos.X, pos.Y);
            else _vm.TryPlace(pos.X, pos.Y);
            e.Handled = true;
        }
    }

    /// <summary> 遍历 MotionVisualItems 渲染所有可视元素 </summary>
    private void RenderMotions()
    {
        if (_vm == null) return;

        var items = _vm.MotionVisualItems;
        MotionCanvas.Children.Clear();

        foreach (var item in items)
        {
            if (item.IsHighlight)
                MotionCanvas.Children.Add(BuildMotionHighlight(item));
            else if (item.IsHead)
                MotionCanvas.Children.Add(BuildMotionHead(item));
            else
                MotionCanvas.Children.Add(BuildMotionBody(item));
        }

        RenderBoxSelect(); // 滚动/缩放等刷新时，框选矩形也要跟着重定位
    }

    /// <summary> 遍历 PreviewItems 渲染放置预览（半透明白色矩形，不参与命中测试） </summary>
    private void RenderPreview()
    {
        if (_vm == null) return;

        PreviewCanvas.Children.Clear();
        foreach (var item in _vm.PreviewItems)
        {
            var border = new Border
            {
                Width = item.Width,
                Height = item.Height,
                Background = Brushes.White,
                Opacity = 0.4,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(border, item.X);
            Canvas.SetTop(border, item.Y);
            PreviewCanvas.Children.Add(border);
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

    /// <summary> 构建 Motion 头（白色外框 + 浅灰内芯，尺寸同 Dot） </summary>
    private Border BuildMotionHead(MotionVisualItem item)
    {
        double innerW = item.Width - 6;
        double innerH = 8;

        var outer = new Border
        {
            Width = item.Width,
            Height = item.Height,
            Background = _nodeStrokeBrush, // 白色外框
            BorderThickness = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
            Tag = item.SelectionTarget,
            [Canvas.LeftProperty] = item.X,
            [Canvas.TopProperty] = item.Y
        };
        outer.PointerPressed += OnMotionPressed;

        var inner = new Border
        {
            Width = innerW,
            Height = innerH,
            Background = _motionBrush,
            IsHitTestVisible = false
        };

        var canvas = new Canvas();
        canvas.Children.Add(inner);
        Canvas.SetLeft(inner, (item.Width - innerW) / 2);
        Canvas.SetTop(inner, (item.Height - innerH) / 2);
        outer.Child = canvas;

        return outer;
    }

    /// <summary> 构建 Motion 身体（纯色填充长条，宽度同 Dash） </summary>
    private Border BuildMotionBody(MotionVisualItem item)
    {
        var body = new Border
        {
            Width = item.Width,
            Height = item.Height,
            Background = _motionBrush,
            BorderThickness = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
            Tag = item.SelectionTarget,
            [Canvas.LeftProperty] = item.X,
            [Canvas.TopProperty] = item.Y
        };
        body.PointerPressed += OnMotionPressed;
        return body;
    }

    /// <summary> 构建高亮矩形（比本体大 2px，蓝色底色） </summary>
    private Border BuildMotionHighlight(MotionVisualItem item)
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
        var pos = e.GetPosition(MotionCanvas);

        // 空白按下后的拖动：超过阈值进入框选，之后每次移动只更新矩形（不做选中计算）
        if (_boxPressed)
        {
            if (!_boxDragging && Distance(pos, _boxPressPoint) > BoxDragThreshold)
            {
                _boxDragging = true;
                e.Pointer.Capture(MotionCanvas); // 捕获指针：拖出控件范围也能继续收到移动/松开事件
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

    private void OnRootGridPointerExited(object? sender, PointerEventArgs e)
    {
        _vm?.HidePreview();
    }
}
