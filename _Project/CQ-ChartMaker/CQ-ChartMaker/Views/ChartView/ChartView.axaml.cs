using System;
using Avalonia.Controls;
using CQ_ChartMaker.ViewModels;

namespace CQ_ChartMaker.Views;

public partial class ChartView : UserControl
{
    private Action<TimeSpan>? _renderLoop;

    public ChartView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttached;
    }

    private void OnAttached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        _renderLoop = _ =>
        {
            if (DataContext is ChartViewModel vm)
                vm.OnFrameUpdate();
            topLevel.RequestAnimationFrame(_renderLoop);
        };
        topLevel.RequestAnimationFrame(_renderLoop);
    }
}
