using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CQ_ChartMaker.Models;
using CQ_ChartMaker.Views;

namespace CQ_ChartMaker.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    // 用户设置，构造时加载，贯穿整个生命周期
    private readonly UserSettingsModel _userSettingsModel;

    // 当前打开的项目路径（null 表示未打开项目）
    private string? _projectPath;

    // 子 ViewModel，未打开/新建项目时为 null
    [ObservableProperty]
    private ChartViewModel? _chartVM;

    [ObservableProperty]
    private MusicInfoViewModel? _musicInfoVM;

    [ObservableProperty]
    private object? _currentView;

    [ObservableProperty]
    private bool _hasProject;

    // 激活状态
    [ObservableProperty]
    private bool _isChartViewActive = true;

    [ObservableProperty]
    private bool _isMusicInfoViewActive;

    partial void OnIsChartViewActiveChanged(bool value)
    {
        if (value)
            CurrentView = ChartVM;
    }

    partial void OnIsMusicInfoViewActiveChanged(bool value)
    {
        if (value)
        {
            CurrentView = MusicInfoVM;
            ChartVM?.Pause();
        }
    }

    public MainWindowViewModel()
    {
        _userSettingsModel = UserSettingsService.Load();
    }

    /// <summary>
    /// 新建项目命令
    /// </summary>
    [RelayCommand]
    private async Task NewProject()
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return;

        var vm = new CreateProjectViewModel();
        var view = new CreateProjectView();
        bool confirmed = await view.ShowDialogAsync(mainWindow, vm);
        if (!confirmed) return;

        try
        {
            string projectPath = ProjectService.CreateProject(vm.ProjectPath, vm.ProjectName);
            string chartText = ProjectService.OpenProject(projectPath);
            OnProjectOpened(projectPath, chartText);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"新建项目失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 打开项目命令
    /// </summary>
    [RelayCommand]
    private async Task OpenProject()
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return;

        var topLevel = mainWindow;
        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new Avalonia.Platform.Storage.FolderPickerOpenOptions { Title = "选择项目文件夹" });

        if (folders.Count == 0)
            return; // 用户取消

        string projectPath = folders[0].Path.LocalPath;

        try
        {
            string chartText = ProjectService.OpenProject(projectPath);
            OnProjectOpened(projectPath, chartText);
        }
        catch (FileNotFoundException)
        {
            System.Diagnostics.Debug.WriteLine("所选文件夹不是有效项目（缺少 chart.txt）");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"打开项目失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 保存当前项目
    /// </summary>
    [RelayCommand]
    private void SaveProject()
    {
        if (!HasProject || _projectPath == null || ChartVM == null) return;

        try
        {
            string chartText = ChartVM.SerializeChart();
            ProjectService.SaveProject(_projectPath, chartText);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"保存项目失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 退出时自动保存（由 View 层 Closing 事件调用）
    /// </summary>
    public void SaveOnExit()
    {
        if (!HasProject || _projectPath == null || ChartVM == null) return;

        try
        {
            string chartText = ChartVM.SerializeChart();
            ProjectService.SaveProject(_projectPath, chartText);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"退出自动保存失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 项目打开/新建后的公共初始化逻辑
    /// </summary>
    private void OnProjectOpened(string projectPath, string chartText)
    {
        _projectPath = projectPath;
        ChartVM = new ChartViewModel(chartText, _userSettingsModel);
        MusicInfoVM = new MusicInfoViewModel();
        HasProject = true;
        IsChartViewActive = true;
        CurrentView = ChartVM;
    }

    /// <summary>
    /// 获取主窗口引用的辅助方法
    /// </summary>
    private Avalonia.Controls.Window? GetMainWindow()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        return null;
    }
}
