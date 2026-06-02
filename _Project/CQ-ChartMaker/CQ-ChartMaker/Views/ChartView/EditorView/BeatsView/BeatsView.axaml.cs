using Avalonia.Controls;
using CQ_ChartMaker.ViewModels;

namespace CQ_ChartMaker.Views;

public partial class BeatsView : UserControl
{
    public BeatsView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttached;
    }

    private void OnAttached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is BeatsViewModel vm)
            vm.LoadInitialBpm();
    }
}
