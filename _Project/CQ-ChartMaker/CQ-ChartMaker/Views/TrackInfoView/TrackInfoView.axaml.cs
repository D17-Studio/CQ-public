using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CQ_ChartMaker.ViewModels;

namespace CQ_ChartMaker.Views;

public partial class TrackInfoView : UserControl
{
    public TrackInfoView()
    {
        InitializeComponent();
        LostFocus += OnLostFocus;
        // 隧道阶段监听：先于子控件处理点击，判断是否需要让输入框失焦
        AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
    }

    /// <summary> 失去焦点时提交暂存的编辑（写回 TrackInfo.txt），与 Inspector 的失焦提交一致 </summary>
    private void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TrackInfoViewModel vm)
            vm.CommitPendingEdit();
    }

    /// <summary>
    /// 点击页内除输入框以外的地方时主动清除焦点。
    /// Avalonia 只会在点到可聚焦控件时转移焦点，点空白区域不会让 TextBox 失焦，
    /// 导致失焦提交不会触发；这里在隧道阶段补上这一行为（实现与 Inspector 相同）。
    /// </summary>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        // 点在输入框内部（包括模板里的 TextPresenter）时保持原样，避免打断光标定位
        for (var visual = e.Source as Visual; visual is not null; visual = visual.GetVisualParent())
            if (visual is TextBox)
                return;

        // 仅当前焦点确实在 TextBox 上时清除；焦点为空时 ClearFocus 可能触发焦点范围的“恢复上一次焦点”
        var focusManager = TopLevel.GetTopLevel(this)?.FocusManager;
        if (focusManager?.GetFocusedElement() is TextBox)
            focusManager.ClearFocus();
    }
}
