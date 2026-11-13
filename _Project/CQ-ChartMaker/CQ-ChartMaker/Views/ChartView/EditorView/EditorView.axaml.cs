using Avalonia.Controls;
using Avalonia.Input;
using CQ_ChartMaker.ViewModels;

namespace CQ_ChartMaker.Views;

public partial class EditorView : UserControl
{
    public EditorView()
    {
        InitializeComponent();
    }

    /// <summary> 轨道按钮右键：只有“非默认 + 无数据”的轨道直接删除，其余右键无反应 </summary>
    private void OnLaneButtonRightClick(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control) return;
        if (!e.GetCurrentPoint(control).Properties.IsRightButtonPressed) return;

        if (control.DataContext is LaneBarItem item && item.CanDelete)
            item.DeleteCommand.Execute(null);

        e.Handled = true;
    }
}