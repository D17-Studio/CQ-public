using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CQ_ChartMaker.Models;
using CQ_ChartMaker.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CQ_ChartMaker.Views;

public partial class MainWindow : Window
{
    // 已按下的按键集合：用于忽略长按产生的重复 KeyDown（KeyUp 时移除）
    private readonly HashSet<Key> _pressedKeys = new();

    // 鼠标离开下拉按钮/下拉框后的延迟关闭计时器（与菜单行为一致）
    private DispatcherTimer? _chartDropdownCloseTimer;

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnTunnelKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnTunnelKeyUp, RoutingStrategies.Tunnel);
        AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnTunnelPointerPressed, RoutingStrategies.Tunnel);
        Closing += OnMainWindowClosing;

        // 监听输入框显隐：进入重命名态时自动聚焦并全选
        ChartNameTextBox.PropertyChanged += OnChartNameTextBoxPropertyChanged;

        // Popup 在独立的视觉根中，PlacementTarget 用代码指定，避免 XAML 元素名绑定不生效
        ChartListPopup.PlacementTarget = ChartPopupAnchor;
    }

    /// <summary>
    /// 谱面重命名态下点击编辑区之外的任何位置：自动取消编辑（保留原文件名）
    /// </summary>
    private void OnTunnelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        // 重命名编辑态：点击编辑区（输入框+确认按钮）之外自动取消
        if (vm.IsChartNameEditing)
        {
            if (e.Source is Visual editSource
                && IsInsideVisual(editSource, ChartNameEditArea))
                return;

            vm.CancelChartNameEditCommand.Execute(null);
            return;
        }

        // 下拉列表打开时：只有点击箭头按钮/弹层内容以外的地方才关闭
        if (vm.IsChartListOpen && e.Source is Visual listSource)
        {
            bool inToggle = IsInsideVisual(listSource, ChartListToggleButton);
            bool inPopup = IsInsideVisual(listSource, ChartListPopupContent);
            if (!inToggle && !inPopup)
                vm.IsChartListOpen = false;
        }
    }

    /// <summary> 从命中控件沿可视树向上查找，判断是否位于指定控件内 </summary>
    private static bool IsInsideVisual(Visual source, Control? target)
    {
        if (target == null) return false;

        for (Visual? current = source; current != null; current = current.GetVisualParent())
        {
            if (current == target) return true;
        }
        return false;
    }

    /// <summary> 输入框由隐藏变可见（开始重命名）时聚焦并全选文本 </summary>
    private void OnChartNameTextBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Visual.IsVisibleProperty || !ChartNameTextBox.IsVisible) return;

        ChartNameTextBox.Focus();
        ChartNameTextBox.SelectAll();
    }

    /// <summary> 下拉列表项点击：直接调用 VM 的切换命令（避免 Popup 模板内的相对绑定失效） </summary>
    private void OnChartListItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ChartFileEntry entry }
            && DataContext is MainWindowViewModel vm)
        {
            vm.SelectChartCommand.Execute(entry.FileName);
        }
    }

    /// <summary> 鼠标进入下拉按钮或下拉框：取消待执行的关闭 </summary>
    private void OnChartDropdownPointerEntered(object? sender, PointerEventArgs e)
        => _chartDropdownCloseTimer?.Stop();

    /// <summary> 鼠标离开下拉按钮或下拉框：延迟关闭，留出从按钮移入下拉框的时间 </summary>
    private void OnChartDropdownPointerExited(object? sender, PointerEventArgs e)
    {
        if (DataContext is not MainWindowViewModel { IsChartListOpen: true }) return;

        _chartDropdownCloseTimer ??= CreateChartDropdownCloseTimer();
        _chartDropdownCloseTimer.Stop();
        _chartDropdownCloseTimer.Start();
    }

    private DispatcherTimer CreateChartDropdownCloseTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            // 指针仍在按钮或下拉框内（例如从按钮移入下拉框）时不关闭
            if (ChartListToggleButton.IsPointerOver || ChartListPopupContent.IsPointerOver) return;

            if (DataContext is MainWindowViewModel vm)
                vm.IsChartListOpen = false;
        };
        return timer;
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        int sign = Math.Sign(e.Delta.Y);
        if (sign == 0) return;

        // 指针悬停在游戏预览画布上：滚轮用于预览缩放（忽略 Ctrl/Shift），不驱动时间轴
        if (e.Source is GameCanvas gameCanvas)
        {
            e.Handled = true;
            gameCanvas.ZoomAt(e.GetPosition(gameCanvas), sign);
            return;
        }

        // 时间轴/PPS 滚轮仅在 Editor 面板内生效（含普通、Shift、Ctrl 三种）
        // 面板外不标记 Handled，滚轮交还给控件自身处理（如 Inspector 的 ScrollViewer 滚动）
        if (!IsPointerInEditor(e.Source)) return;

        if (DataContext is MainWindowViewModel vm && vm.CurrentView is ChartViewModel chartVm)
        {
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

    /// <summary>
    /// 判断滚轮事件是否发生在 Editor 面板内。
    /// 从命中控件沿可视树向上查找，包含自身（Avalonia 的 FindAncestorOfType 可视化树遍历 API）。
    /// </summary>
    private static bool IsPointerInEditor(object? source)
        => source is Visual visual && visual.FindAncestorOfType<EditorView>(includeSelf: true) is not null;

    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.SaveOnExit();
            vm.Dispose(); // 释放谱面 VM 与项目级音频设备
        }
    }

    private void OnTunnelKeyDown(object? sender, KeyEventArgs e)
    {
        // 文本输入状态下不拦截任何快捷键：
        // 让 TextBox 自己处理字符输入、Ctrl+Z / Ctrl+C 等编辑操作
        if (IsTextInputFocused()) return;
        if (DataContext is not MainWindowViewModel vm) return;

        // 长按同一键时系统会连续产生 KeyDown，这里只触发第一次
        if (!_pressedKeys.Add(e.Key)) return;

        // ===== 临时测试：K/L 键切换 LaneEvent 类型页（已由 EditorView 类型栏替代，停用） =====
        // // ===== 临时测试：K/L 键切换 LaneEvent 类型页，后续接入 6 页签 UI 后删除 =====
        // // 切页功能本身（ChartVM.SwitchLaneEventPage）是必要功能，页签 UI 也调用它。
        // // 不用 ←→ 是因为方向键会被底部进度条等控件消费
        // if (vm.ChartVM is { } chart && vm.CurrentView is ChartViewModel &&
        //     chart.EditorVM.IsEventsSelected &&
        //     e.KeyModifiers == KeyModifiers.None && !IsArrowKeyConsumerFocused())
        // {
        //     if (e.Key == Key.K)
        //     {
        //         chart.SwitchLaneEventPage(-1);
        //         e.Handled = true;
        //         return;
        //     }
        //     if (e.Key == Key.L)
        //     {
        //         chart.SwitchLaneEventPage(1);
        //         e.Handled = true;
        //         return;
        //     }
        // }

        if (vm.Shortcuts.TryResolve(e.Key, e.KeyModifiers, out ShortcutAction action))
        {
            vm.ExecuteShortcut(action);
            e.Handled = true;
        }
    }

    private void OnTunnelKeyUp(object? sender, KeyEventArgs e)
    {
        // 松开后允许该键下一次按下重新触发
        _pressedKeys.Remove(e.Key);
    }

    /// <summary> 当前焦点是否在 TextBox（含 TextBox 内部模板元素）内 </summary>
    private bool IsTextInputFocused()
    {
        var focused = FocusManager?.GetFocusedElement();
        return focused is Visual visual
            && (visual is TextBox || visual.FindAncestorOfType<TextBox>() != null);
    }

    ///// <summary>
    ///// 临时测试辅助：焦点是否在会消费方向键的控件内（ComboBox / Slider / ListBox）。
    ///// 接入 6 页签 UI 后随 K/L 切页分支一起删除。
    ///// </summary>
    //private bool IsArrowKeyConsumerFocused()
    //{
    //    var focused = FocusManager?.GetFocusedElement();
    //    return focused is Visual visual
    //        && (visual is ComboBox or Slider or ListBox or ListBoxItem
    //            || visual.FindAncestorOfType<ComboBox>() != null
    //            || visual.FindAncestorOfType<Slider>() != null
    //            || visual.FindAncestorOfType<ListBox>() != null);
    //}
}
