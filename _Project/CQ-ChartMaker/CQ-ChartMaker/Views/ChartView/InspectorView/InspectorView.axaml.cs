using System;
using System.ComponentModel;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CQ_ChartMaker.ViewModels;

namespace CQ_ChartMaker.Views;

public partial class InspectorView : UserControl
{
    /// <summary> 当前绑定的 Inspector ViewModel（DataContext 由父级 XAML 注入，需在变化时重新订阅） </summary>
    private InspectorViewModel? _inspectorVm;

    public InspectorView()
    {
        InitializeComponent();
        LostFocus += OnLostFocus;
        GotFocus += OnInputGotFocus;
        // 隧道阶段监听：先于子控件处理点击，判断是否需要让输入框失焦
        AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(DragValueButton.DragStartedEvent, OnDragStarted);
        AddHandler(DragValueButton.DragCompletedEvent, OnDragCompleted);
    }

    /// <summary> DataContext 变化时重新订阅 ViewModel，监听选中集合切换 </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_inspectorVm is not null)
            _inspectorVm.PropertyChanged -= OnInspectorVmPropertyChanged;

        _inspectorVm = DataContext as InspectorViewModel;

        if (_inspectorVm is not null)
            _inspectorVm.PropertyChanged += OnInspectorVmPropertyChanged;
    }

    /// <summary> 切换选中的 unit 时，让正在编辑的输入框失焦（触发提交与非法值还原） </summary>
    private void OnInspectorVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(InspectorViewModel.SelectedUnits))
            return;

        var focusManager = TopLevel.GetTopLevel(this)?.FocusManager;
        if (focusManager?.GetFocusedElement() is TextBox textBox &&
            textBox.FindAncestorOfType<InspectorView>() == this)
            focusManager.ClearFocus();
    }

    /// <summary> 输入框获得焦点时全选内容，方便直接输入覆盖旧值 </summary>
    private void OnInputGotFocus(object? sender, GotFocusEventArgs e)
    {
        if (e.Source is not TextBox textBox || !textBox.Classes.Contains("PropertyInput"))
            return;

        // GotFocus 在 TextBox 处理鼠标按下之前触发，若立即 SelectAll 会被随后的光标定位覆盖，
        // 因此延后到本次输入事件处理完再全选
        Dispatcher.UIThread.Post(textBox.SelectAll, DispatcherPriority.Input);
    }

    /// <summary>
    /// 点击 Inspector 内除输入框以外的地方时主动清除焦点。
    /// Avalonia 只会在点到可聚焦控件时转移焦点，点空白区域或普通按钮不会让 TextBox 失焦，
    /// 导致失焦提交/非法值还原不会触发；这里在隧道阶段补上这一行为。
    /// </summary>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        // 点在输入框内部（包括模板里的 TextPresenter）时保持原样，避免打断光标定位与聚焦全选
        for (var visual = e.Source as Visual; visual is not null; visual = visual.GetVisualParent())
            if (visual is TextBox)
                return;

        // 仅当前焦点确实在 TextBox 上时清除；焦点为空时 ClearFocus 可能触发焦点范围的“恢复上一次焦点”
        var focusManager = TopLevel.GetTopLevel(this)?.FocusManager;
        if (focusManager?.GetFocusedElement() is TextBox)
            focusManager.ClearFocus();
    }

    /// <summary> 失去焦点时提交暂存编辑；若输入框里是非法值，则恢复为数据源中的值 </summary>
    private void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is InspectorViewModel vm)
            vm.CommitPendingEdit();

        if (e.Source is not TextBox textBox || !DataValidationErrors.GetHasErrors(textBox))
            return;

        var expression = BindingOperations.GetBindingExpressionBase(textBox, TextBox.TextProperty);
        if (expression is null)
            return;

        // 1. 重新读取绑定源：取到数据里的旧值并清除错误状态（红色边框消失）。
        //    Avalonia 在“源值没变”时不会重写目标，所以仅这一步输入框文字不会恢复
        expression.UpdateTarget();

        // 2. 取出绑定表达式当前的有效值（已转换成目标类型 string）并写回 TextBox。
        //    GetValue 在 Avalonia 运行时程序集里是 public，但引用程序集里被改成了 internal，
        //    无法直接调用，只能反射。
        //    SetCurrentValue 只改当前值、不会破坏绑定；此时错误已清除且写入值与源值一致，
        //    TwoWay 绑定不会再去调用 ViewModel 的 setter（避免产生多余的撤销记录）
        var getValue = expression.GetType().GetMethod(
            "GetValue",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            Type.EmptyTypes,
            null);

        if (getValue is not null)
        {
            var value = getValue.Invoke(expression, null);
            if (value != AvaloniaProperty.UnsetValue)
                textBox.SetCurrentValue(TextBox.TextProperty, value);
        }
    }

    /// <summary> 拖拽开始前提交已有暂存，避免本次拖拽的撤销快照包含之前的修改 </summary>
    private void OnDragStarted(object? sender, RoutedEventArgs e)
    {
        if (DataContext is InspectorViewModel vm)
            vm.CommitPendingEdit();
    }

    /// <summary> 拖拽调值结束时提交，保证每次拖拽只产生一条撤销记录 </summary>
    private void OnDragCompleted(object? sender, RoutedEventArgs e)
    {
        if (DataContext is InspectorViewModel vm)
            vm.CommitPendingEdit();
    }
}