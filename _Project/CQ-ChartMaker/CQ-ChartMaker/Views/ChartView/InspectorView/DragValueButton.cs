using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace CQ_ChartMaker.Views;

/// <summary>
/// 数值拖拽按钮：按住按钮左右拖拽，按鼠标横向位移修改绑定值。
/// 鼠标到达屏幕边缘时会瞬移到另一端，从而支持无限制拖拽。
/// 需绑定 Value（双向）并设置每像素步长 Step。
/// </summary>
public class DragValueButton : Border
{
    // 可空：多选且数值不一致时来源为 null
    public static readonly StyledProperty<float?> ValueProperty =
        AvaloniaProperty.Register<DragValueButton, float?>(nameof(Value));

    public static readonly StyledProperty<float> StepProperty =
        AvaloniaProperty.Register<DragValueButton, float>(nameof(Step), 0.1f);

    // 可空：null 表示不限制；只约束拖拽，不影响手动输入（输入框保持自由）
    public static readonly StyledProperty<float?> MinValueProperty =
        AvaloniaProperty.Register<DragValueButton, float?>(nameof(MinValue));

    public static readonly StyledProperty<float?> MaxValueProperty =
        AvaloniaProperty.Register<DragValueButton, float?>(nameof(MaxValue));

    /// <summary> 拖拽真正开始时触发（提交之前的暂存编辑，避免撤销快照混入其它修改） </summary>
    public static readonly RoutedEvent<RoutedEventArgs> DragStartedEvent =
        RoutedEvent.Register<DragValueButton, RoutedEventArgs>(nameof(DragStarted), RoutingStrategies.Bubble);

    /// <summary> 一次拖拽调值结束时触发（每次拖拽 = 一条撤销记录） </summary>
    public static readonly RoutedEvent<RoutedEventArgs> DragCompletedEvent =
        RoutedEvent.Register<DragValueButton, RoutedEventArgs>(nameof(DragCompleted), RoutingStrategies.Bubble);

    public event EventHandler<RoutedEventArgs>? DragStarted
    {
        add => AddHandler(DragStartedEvent, value);
        remove => RemoveHandler(DragStartedEvent, value);
    }

    public event EventHandler<RoutedEventArgs>? DragCompleted
    {
        add => AddHandler(DragCompletedEvent, value);
        remove => RemoveHandler(DragCompletedEvent, value);
    }

    /// <summary> 当前数值，双向绑定到 ViewModel 属性 </summary>
    public float? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary> 鼠标每移动 1 像素时数值的变化量 </summary>
    public float Step
    {
        get => GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    /// <summary> 拖拽值下限（null = 不限制；仅约束拖拽，不约束手动输入） </summary>
    public float? MinValue
    {
        get => GetValue(MinValueProperty);
        set => SetValue(MinValueProperty, value);
    }

    /// <summary> 拖拽值上限（null = 不限制；仅约束拖拽，不约束手动输入） </summary>
    public float? MaxValue
    {
        get => GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    /// <summary> 设置鼠标屏幕坐标（物理像素），Windows API </summary>
    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    private int _lastScreenX;
    private int _accumulatedDx;
    private float _startValue;
    private bool _dragging;

    public DragValueButton()
    {
        Focusable = false;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        // 起始值先钳制：越界值（如 opacity 存了 1.5）在拖拽开始的瞬间就回到范围内
        _startValue = Clamp(Value ?? 0f);
        _accumulatedDx = 0;
        _dragging = false;
        _lastScreenX = GetScreenPoint(e)?.X ?? 0;
        // 捕获指针：拖出按钮范围后仍能收到移动事件
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!ReferenceEquals(e.Pointer.Captured, this))
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        var screenPoint = GetScreenPoint(e);
        var screen = screenPoint is null ? null : topLevel?.Screens?.ScreenFromPoint(screenPoint.Value);
        if (screenPoint is null || screen is null)
            return;

        var area = screen.WorkingArea;
        var rawDx = screenPoint.Value.X - _lastScreenX;

        // 坐标跳变（如环绕瞬移）会产生异常大的增量，丢弃该帧
        if (Math.Abs(rawDx) > area.Width / 2)
        {
            _lastScreenX = screenPoint.Value.X;
            return;
        }

        _lastScreenX = screenPoint.Value.X;
        var nextAccumulated = _accumulatedDx + rawDx;

        if (!_dragging)
        {
            // 2px 阈值，避免点击时的轻微抖动误改数值
            if (Math.Abs(nextAccumulated) < 2)
                return;

            _dragging = true;
            // 拖拽开始前先提交之前暂存的编辑（例如刚输入过的其它输入框），
            // 避免本次拖拽的撤销快照把那些修改一起带走
            RaiseEvent(new RoutedEventArgs(DragStartedEvent));
        }

        _accumulatedDx = nextAccumulated;
        Value = Clamp(_startValue + _accumulatedDx * Step);
        e.Handled = true;

        // 到达当前屏幕的工作区边缘时把鼠标瞬移到另一侧，实现无限拖拽
        if (!OperatingSystem.IsWindows())
            return;

        var y = screenPoint.Value.Y;
        if (screenPoint.Value.X <= area.X)
            WarpCursor(area.Right - 2, y);
        else if (screenPoint.Value.X >= area.Right - 1)
            WarpCursor(area.X + 1, y);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
        var wasDragging = _dragging;
        _dragging = false;
        if (wasDragging)
            RaiseEvent(new RoutedEventArgs(DragCompletedEvent));
    }

    /// <summary> 把拖拽值限制在 [MinValue, MaxValue] 内；未设置的边界不限制 </summary>
    private float Clamp(float value)
    {
        if (MinValue is { } min && value < min) value = min;
        if (MaxValue is { } max && value > max) value = max;
        return value;
    }

    /// <summary> 获取指针的物理屏幕坐标（用于跨 DPI/环绕计算） </summary>
    private PixelPoint? GetScreenPoint(PointerEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        return topLevel?.PointToScreen(e.GetPosition(topLevel));
    }

    /// <summary> 瞬移鼠标，并把上一帧坐标同步到瞬移目标，避免下一帧算出大增量 </summary>
    private void WarpCursor(int x, int y)
    {
        if (SetCursorPos(x, y))
            _lastScreenX = x;
    }
}
