using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CQ_ChartMaker.ViewModels;
using System;

namespace CQ_ChartMaker.Views;

public partial class MainWindow : Window
{
    private bool _spacePressed; // 记录空格是否处于按下状态

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnTunnelKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnTunnelKeyUp, RoutingStrategies.Tunnel);
        AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel);
        Closing += OnMainWindowClosing;
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && vm.CurrentView is ChartViewModel chartVm)
        {
            int sign = Math.Sign(e.Delta.Y);
            if (sign == 0) return;

            e.Handled = true;

            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                chartVm.AdjustPps(sign);
            }
            else
            {
                int deltaMs = sign * (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 300 : 100);
                chartVm.ScrollTime(deltaMs);
            }
        }
    }

    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.SaveOnExit();
    }

    private void OnTunnelKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            if (!_spacePressed)
            {
                _spacePressed = true;
                e.Handled = true;
                if (DataContext is MainWindowViewModel vm && vm.CurrentView is ChartViewModel chartVm)
                {
                    chartVm.TogglePlayPause();
                }
            }
            else
            {
                e.Handled = true; // 仍然阻止传递给按钮
            }
        }
    }

    private void OnTunnelKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            _spacePressed = false;
            e.Handled = true;
        }
    }
}
