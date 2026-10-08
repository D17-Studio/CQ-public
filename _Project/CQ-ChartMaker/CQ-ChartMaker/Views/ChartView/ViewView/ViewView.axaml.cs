using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using CQ_ChartMaker.ViewModels;

namespace CQ_ChartMaker.Views;

public partial class ViewView : UserControl
{
    public ViewView()
    {
        InitializeComponent();

        // 按下进度条（拖拽把手或点击轨道）时暂停播放：
        // 避免拖拽改位置与播放推进同时修改时间导致位置失控
        // Tunnel 策略让本处理器先于 Slider 内部逻辑触发
        TimeSlider.AddHandler(PointerPressedEvent, OnSliderPointerPressed, RoutingStrategies.Tunnel);

        // 松开进度条时：若按下前处于播放中，则从拖拽后的位置自动恢复播放
        // （拖拽时 Thumb 会捕获指针，指针移出控件后松开同样能收到此事件）
        TimeSlider.AddHandler(PointerReleasedEvent, OnSliderPointerReleased, RoutingStrategies.Tunnel);
    }

    private void OnSliderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // DataContext 由父级绑定注入，事件触发时才读取（构造时尚未赋值）
        (DataContext as ViewViewModel)?.PauseOnDrag();
    }

    private void OnSliderPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        (DataContext as ViewViewModel)?.ResumeAfterDrag();
    }
}