using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CQ_ChartMaker.ViewModels;

namespace CQ_ChartMaker.Views;

public partial class InspectorView : UserControl
{
    public InspectorView()
    {
        InitializeComponent();
        LostFocus += OnLostFocus;
    }

    /// <summary> 所在控件失去焦点时提交暂存的编辑命令 </summary>
    private void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is InspectorViewModel vm)
            vm.CommitPendingEdit();
    }
}