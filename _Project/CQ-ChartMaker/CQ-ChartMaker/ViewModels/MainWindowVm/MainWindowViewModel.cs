using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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

    // 最近项目列表
    [ObservableProperty]
    private ObservableCollection<RecentProjectEntry> _recentProjects = new();

    // 是否有最近项目（用于 UI 空状态）
    [ObservableProperty]
    private bool _hasRecentProjects;

    // 无项目时是否显示欢迎页（HasProject 取反，方便 XAML 绑定）
    [ObservableProperty]
    private bool _showWelcome = true;

    partial void OnHasProjectChanged(bool value)
    {
        ShowWelcome = !value;
    }

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
        RecentProjects.CollectionChanged += (_, _) => HasRecentProjects = RecentProjects.Count > 0;
        LoadRecentProjects();
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
    /// 导入音频命令：选择 WAV 文件加载到当前项目，并把路径记录到项目文件夹
    /// </summary>
    [RelayCommand]
    private async Task ImportAudio()
    {
        if (!HasProject || _projectPath == null || ChartVM == null) return;

        var mainWindow = GetMainWindow();
        if (mainWindow == null) return;

        // 文件选择对话框：Avalonia 的 StorageProvider 提供系统原生选择器
        // 只过滤 WAV：当前音频方案（WaveFileReader）仅支持 WAV 格式
        var files = await mainWindow.StorageProvider.OpenFilePickerAsync(
            new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "选择音频文件",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("WAV 音频") { Patterns = new[] { "*.wav" } }
                }
            });

        if (files.Count == 0) return; // 用户取消

        string audioPath = files[0].Path.LocalPath;

        // 加载成功才记录路径：记录一个加载不了的路径没有意义
        if (ChartVM.LoadBgm(audioPath))
            ProjectService.SaveAudioPath(_projectPath, audioPath);
    }

    /// <summary>
    /// 从最近项目列表中打开项目（点击列表项时调用）
    /// </summary>
    /// <param name="projectPath">项目文件夹路径</param>
    [RelayCommand]
    private async Task OpenRecentProject(string projectPath)
    {
        if (!Directory.Exists(projectPath))
        {
            // 项目文件夹已不存在，从列表中移除
            var entry = RecentProjects.FirstOrDefault(e => e.Path == projectPath);
            if (entry != null) RecentProjects.Remove(entry);
            return;
        }

        try
        {
            string chartText = ProjectService.OpenProject(projectPath);
            OnProjectOpened(projectPath, chartText);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"打开最近项目失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 从最近项目列表中删除指定项目
    /// </summary>
    [RelayCommand]
    private void DeleteRecentProject(RecentProjectEntry entry)
    {
        RecentProjects.Remove(entry);
        ProjectService.RemoveRecentProject(entry.Path);
    }

    /// <summary>
    /// 清空最近项目列表
    /// </summary>
    [RelayCommand]
    private void ClearRecentProjects()
    {
        RecentProjects.Clear();
    }

    /// <summary>
    /// 从 ProjectService 加载最近项目列表到 RecentProjects
    /// </summary>
    private void LoadRecentProjects()
    {
        var entries = ProjectService.LoadRecentProjects();
        RecentProjects.Clear();
        foreach (var entry in entries)
            RecentProjects.Add(entry);
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
        ChartVM?.Dispose(); // 打开新项目时释放上一个项目的音频设备
        ChartVM = new ChartViewModel(chartText, _userSettingsModel);

        // 自动加载项目记录的音频（audioPath.txt）；无记录则进入无音频模式
        // 目前仅 Debug 提示，后续可改为 UI 弹窗提示“暂无音频”/“音频路径不可用”
        string? audioPath = ProjectService.LoadAudioPath(projectPath);
        if (audioPath == null)
            System.Diagnostics.Debug.WriteLine("[OnProjectOpened] 项目暂无音频记录");
        else
            ChartVM.LoadBgm(audioPath);

        MusicInfoVM = new MusicInfoViewModel();
        HasProject = true;
        IsChartViewActive = true;
        CurrentView = ChartVM;

        // 记录到最近项目列表并刷新
        ProjectService.RecordRecentProject(projectPath);
        LoadRecentProjects();
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
