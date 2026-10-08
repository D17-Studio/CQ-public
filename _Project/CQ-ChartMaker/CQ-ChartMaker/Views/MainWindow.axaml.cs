using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CQ_ChartMaker.ViewModels;
using System;
using System.Linq;

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
        {
            vm.SaveOnExit();
            vm.ChartVM?.Dispose(); // 退出时优雅关闭音频设备
        }
    }

    private void OnTunnelKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && vm.CurrentView is ChartViewModel chartVm)
        {
            if (e.Key == Key.Space)
            {
                if (!_spacePressed)
                {
                    _spacePressed = true;
                    e.Handled = true;
                    chartVm.TogglePlayPause();
                }
                else
                {
                    e.Handled = true; // 仍然阻止传递给按钮
                }
            }
            else if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                e.Handled = true;
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                    chartVm.Redo();
                else
                    chartVm.Undo();
            }
            else if (e.Key == Key.M && !IsTextBoxFocused(e))
            {
                // M = 批量移动（幽灵模式）；是否有效由 ChartVM 校验
                e.Handled = true;
                chartVm.BeginMoveOrCopy(isCopy: false);
            }
            else if (e.Key == Key.C && !IsTextBoxFocused(e))
            {
                // C = 批量复制（幽灵模式）
                e.Handled = true;
                chartVm.BeginMoveOrCopy(isCopy: true);
            }
            else if (e.Key == Key.Delete && !IsTextBoxFocused(e))
            {
                e.Handled = true;
                chartVm.DeleteSelected();
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

    /// <summary> 当前焦点是否在 TextBox 内（避免删除键冲突） </summary>
    private static bool IsTextBoxFocused(KeyEventArgs e) => e.Source is TextBox;
}
