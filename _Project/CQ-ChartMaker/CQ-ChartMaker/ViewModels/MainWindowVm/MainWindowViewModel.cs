using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CQ_ChartMaker.Common;
using CQ_ChartMaker.Models;
using CQ_ChartMaker.Views;
using CQMusicGame.Shared;

namespace CQ_ChartMaker.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    // 用户设置，构造时加载，贯穿整个生命周期
    private readonly UserSettingsModel _userSettingsModel;

    // 整体 UI 缩放范围（50%~150%）
    private const double UiScaleMin = 0.5;
    private const double UiScaleMax = 1.5;

    /// <summary> 用户设置（View 层绑定整体 UI 缩放等设置，修改后自动持久化） </summary>
    public UserSettingsModel UserSettings => _userSettingsModel;

    /// <summary> 快捷键运行时管理器（集中按键分发使用） </summary>
    public ShortcutManagerModel Shortcuts { get; }

    // 当前打开的项目路径（null 表示未打开项目）
    private string? _projectPath;

    // 项目级共享音频：同一项目内所有谱面共用，切换谱面时无需重建 WASAPI 管线
    private AudioModel? _audio;

    // 当前谱面文件名（charts 下的 xxx.txt）；只在切换成功后更新
    private string? _activeChartFile;

    // 切换/刷新谱面列表期间为 true，避免 SelectedItem 变化触发保存和加载
    private bool _isSwitchingChart;

    // 删除谱面后切换到下一份时跳过“保存旧谱面”，否则会把刚删除的文件重新写回来
    private bool _skipNextChartSave;

    // 子 ViewModel，未打开/新建项目时为 null
    [ObservableProperty]
    private ChartViewModel? _chartVM;

    [ObservableProperty]
    private TrackInfoViewModel? _trackInfoVM;

    [ObservableProperty]
    private object? _currentView;

    [ObservableProperty]
    private bool _hasProject;

    /// <summary> 当前打开项目的名称（顶栏右对齐显示，打开/切换项目时更新） </summary>
    [ObservableProperty]
    private string _projectName = string.Empty;

    // 激活状态
    [ObservableProperty]
    private bool _isChartViewActive = true;

    [ObservableProperty]
    private bool _isTrackInfoViewActive;

    // 谱面选择状态
    /// <summary> 当前项目的谱面文件列表（下拉框数据源，含当前项高亮状态） </summary>
    public ObservableCollection<ChartFileEntry> ChartFiles { get; } = new();

    /// <summary> 下拉框选中项（文件全名，变化时统一保存旧谱面并加载新谱面） </summary>
    [ObservableProperty]
    private string? _currentChartFile;

    /// <summary> 谱面下拉列表是否展开 </summary>
    [ObservableProperty]
    private bool _isChartListOpen;

    /// <summary> 是否处于谱面重命名编辑态 </summary>
    [ObservableProperty]
    private bool _isChartNameEditing;

    /// <summary> 重命名输入框文本 </summary>
    [ObservableProperty]
    private string _chartNameInput = string.Empty;

    /// <summary> 输入框是否显示错误（红框） </summary>
    [ObservableProperty]
    private bool _hasChartNameError;

    /// <summary> 当前谱面显示名（去掉 .txt） </summary>
    public string CurrentChartDisplayName => CurrentChartFile == null
        ? string.Empty
        : Path.GetFileNameWithoutExtension(CurrentChartFile);

    // 最近项目列表
    [ObservableProperty]
    private ObservableCollection<RecentProjectEntry> _recentProjects = new();

    // 是否有最近项目（用于 UI 空状态）
    [ObservableProperty]
    private bool _hasRecentProjects;

    // 搜索框文本（输入时实时过滤最近项目列表）
    [ObservableProperty]
    private string _recentProjectSearchText = string.Empty;

    /// <summary> 按搜索文本过滤后的最近项目列表（欢迎页列表绑定这个集合） </summary>
    public ObservableCollection<RecentProjectEntry> FilteredRecentProjects { get; } = new();

    /// <summary> 过滤结果是否非空（控制最近项目列表显隐） </summary>
    public bool HasFilteredRecentProjects => FilteredRecentProjects.Count > 0;

    /// <summary> 有最近项目但搜索无匹配（控制“未找到匹配的项目”提示显隐） </summary>
    public bool ShowNoSearchResults => HasRecentProjects && !HasFilteredRecentProjects;

    // 搜索文本变化时重建过滤列表（[ObservableProperty] 生成的钩子方法）
    partial void OnRecentProjectSearchTextChanged(string value)
    {
        RefreshFilteredRecentProjects();
    }

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
        {
            // 离开曲目信息页：输入框可能仍有未失焦的编辑，先提交再切换视图
            TrackInfoVM?.CommitPendingEdit();
            CurrentView = ChartVM;
        }
    }

    partial void OnIsTrackInfoViewActiveChanged(bool value)
    {
        if (value)
        {
            // 切到曲目信息页时取消未提交的谱面重命名
            if (IsChartNameEditing) CancelChartNameEdit();

            // 期间可能新增/删除了谱面文件，下拉框需要重新扫描
            TrackInfoVM?.RefreshFiles();
            CurrentView = TrackInfoVM;
            ChartVM?.Pause();
        }
    }

    /// <summary> 下拉框选中项变化：保存旧谱面 → 加载新谱面 → 重建编辑视图 </summary>
    partial void OnCurrentChartFileChanged(string? value)
    {
        OnPropertyChanged(nameof(CurrentChartDisplayName));

        if (_isSwitchingChart || value == null) return;
        if (string.Equals(value, _activeChartFile, StringComparison.OrdinalIgnoreCase)) return;
        // ChartVM 允许为空：打开项目时若还没有活动谱面，第一次选择就是“从无到有”
        if (_projectPath == null || _audio == null) return;

        string? oldFile = _activeChartFile;

        // 先保存旧谱面，再加载新谱面；删除谱面后的切换跳过保存，避免重建刚删除的文件
        if (_skipNextChartSave)
            _skipNextChartSave = false;
        else
            SaveCurrentChart();

        string chartText;
        try
        {
            chartText = ProjectService.LoadChart(_projectPath, value);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[切换谱面] 加载失败: {ex.Message}");
            // 加载失败：选中项回滚到旧谱面，避免显示与实际不一致
            _isSwitchingChart = true;
            CurrentChartFile = oldFile;
            _isSwitchingChart = false;
            return;
        }

        // 先构造新 VM（构造函数内完成严格解析）；解析失败时保持当前谱面不变。
        // 用户当前约定：非法文件“暂时没反应”，只写调试输出，不弹提示。
        ChartViewModel newChartVm;
        try
        {
            newChartVm = new ChartViewModel(chartText, _userSettingsModel, _audio);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[切换谱面] 解析失败: {ex.Message}");
            _isSwitchingChart = true;
            CurrentChartFile = oldFile;
            _isSwitchingChart = false;
            return;
        }

        // 解析成功后才拆旧视图，保证新的 ChartView 子树是全新实例
        CurrentView = null;
        ChartVM?.Dispose();
        ChartVM = newChartVm;

        // 曲目信息页的音频操作要作用在新谱面 VM 上
        if (TrackInfoVM != null)
            TrackInfoVM.ChartVM = ChartVM;

        _activeChartFile = value;

        // 更新下拉列表的当前项高亮
        foreach (var entry in ChartFiles)
            entry.IsCurrent = string.Equals(entry.FileName, value, StringComparison.OrdinalIgnoreCase);

        // 切换谱面的额外行为：用户切谱就是想去编辑新谱面，直接进入制谱界面
        IsTrackInfoViewActive = false;
        IsChartViewActive = true;
        CurrentView = ChartVM;

        IsChartListOpen = false;
    }

    /// <summary> 改名输入变化时实时校验（红框提示） </summary>
    partial void OnChartNameInputChanged(string value)
        => HasChartNameError = IsChartNameInvalid(value);

    public MainWindowViewModel()
    {
        _userSettingsModel = UserSettingsService.Load();
        // 防御：settings.json 被手改/损坏时，把缩放限制回允许范围
        _userSettingsModel.UiScale = Math.Clamp(_userSettingsModel.UiScale, UiScaleMin, UiScaleMax);

        // 启动时按已保存的偏好应用 Inspector 语言（默认两项都不汉化）
        InspectorLocalization.Apply(_userSettingsModel);

        Shortcuts = new ShortcutManagerModel(_userSettingsModel);

        // 缩放值变化时刷新顶栏百分比文本（按钮和快捷键都会触发）
        _userSettingsModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UserSettingsModel.UiScale))
                OnPropertyChanged(nameof(UiZoomText));

            // Inspector 语言开关：更新全局动态资源，并刷新代码拼接的文案与下拉选项
            if (e.PropertyName is nameof(UserSettingsModel.IsInspectorLocalized)
                or nameof(UserSettingsModel.IsEaseCurveLocalized))
            {
                InspectorLocalization.Apply(_userSettingsModel);
                ChartVM?.InspectorVM.RefreshLocalization();
            }
        };

        // 最近项目集合变化时：刷新空状态标记，同时重建过滤列表（增删/重新加载都走这里）
        RecentProjects.CollectionChanged += (_, _) =>
        {
            HasRecentProjects = RecentProjects.Count > 0;
            RefreshFilteredRecentProjects();
        };
        LoadRecentProjects();
    }

    /// <summary>
    /// 调整整体 UI 缩放：direction = -1 缩小 / +1 放大，步进 10%，限制在 50%~150%
    /// </summary>
    public void AdjustUiScale(int direction)
    {
        double next = Math.Round(_userSettingsModel.UiScale + direction * 0.1, 1);
        _userSettingsModel.UiScale = Math.Clamp(next, UiScaleMin, UiScaleMax);
    }

    /// <summary> 执行快捷键动作：集中转发到各 ViewModel 的现有公开入口 </summary>
    public void ExecuteShortcut(ShortcutAction action)
    {
        // UI 缩放不依赖项目，任何时候都可以触发
        switch (action)
        {
            case ShortcutAction.UiZoomIn:
                AdjustUiScale(1);
                return;
            case ShortcutAction.UiZoomOut:
                AdjustUiScale(-1);
                return;
        }

        // 其余快捷键只在谱面编辑视图内生效
        if (ChartVM is not { } chart || CurrentView is not ChartViewModel)
            return;

        EditorViewModel editor = chart.EditorVM;
        switch (action)
        {
            case ShortcutAction.TogglePlayPause: chart.TogglePlayPause(); break;
            case ShortcutAction.Undo: chart.Undo(); break;
            case ShortcutAction.Redo: chart.Redo(); break;
            case ShortcutAction.Delete: chart.DeleteSelected(); break;
            case ShortcutAction.Copy: chart.BeginMoveOrCopy(isCopy: true); break;
            case ShortcutAction.Cut: chart.BeginMoveOrCopy(isCopy: false); break;
            case ShortcutAction.SelectAll: chart.SelectAllVisible(); break;
            case ShortcutAction.ToggleGhostFlip: chart.ToggleGhostFlip(); break;
            case ShortcutAction.Save: SaveProjectCommand.Execute(null); break;
            case ShortcutAction.ToggleHitSoundMute:
                chart.UserSettingsModel.IsHitSoundMuted = !chart.UserSettingsModel.IsHitSoundMuted;
                break;
            case ShortcutAction.PlaceDot: ToggleNotePlacing(editor, NotePlacingMode.Dot); break;
            case ShortcutAction.PlaceDash: ToggleNotePlacing(editor, NotePlacingMode.Dash); break;
            case ShortcutAction.PlaceMute: ToggleNotePlacing(editor, NotePlacingMode.Mute); break;
            case ShortcutAction.PlaceTune: ToggleNotePlacing(editor, NotePlacingMode.Tune); break;
            case ShortcutAction.PlaceMotionBpm: ToggleMotionOrBpmPlacing(editor); break;
        }
    }

    /// <summary> 放置 Note：仅 Notes 子视图生效，同键再按一次退出放置（拖拽开关不受影响） </summary>
    private static void ToggleNotePlacing(EditorViewModel editor, NotePlacingMode mode)
    {
        if (!editor.IsNotesSelected) return;

        editor.NotesVM.PlacingMode = editor.NotesVM.PlacingMode == mode
            ? NotePlacingMode.None
            : mode;
    }

    /// <summary> 切换 Motion/BPM 放置：仅当前 Events/Beats 子视图生效（拖拽开关不受影响） </summary>
    private static void ToggleMotionOrBpmPlacing(EditorViewModel editor)
    {
        if (editor.IsEventsSelected)
        {
            editor.EventsVm.IsPlacing = !editor.EventsVm.IsPlacing;
        }
        else if (editor.IsBeatsSelected)
        {
            editor.BeatsVM.IsPlacing = !editor.BeatsVM.IsPlacing;
        }
    }

    /// <summary> 顶栏显示的 UI 缩放百分比（如 "100%"） </summary>
    public string UiZoomText => $"{_userSettingsModel.UiScale * 100:0}%";

    [RelayCommand]
    private void UiZoomIn() => AdjustUiScale(1);

    [RelayCommand]
    private void UiZoomOut() => AdjustUiScale(-1);

    /// <summary>
    /// 打开设置窗口（模态对话框，关闭后返回原界面）
    /// </summary>
    [RelayCommand]
    private async Task OpenSettings()
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return;

        // 弹出模态窗口前先暂停时间轴，避免后台继续播放
        ChartVM?.Pause();

        var vm = new SettingsViewModel(_userSettingsModel);
        var view = new SettingsView();
        await view.ShowDialogAsync(mainWindow, vm);
    }

    /// <summary>
    /// 打开更新日志窗口（模态对话框，关闭后返回原界面）
    /// </summary>
    [RelayCommand]
    private async Task OpenChangelog()
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return;

        // 弹出模态窗口前先暂停时间轴，避免后台继续播放
        ChartVM?.Pause();

        var vm = new ChangelogViewModel();
        var view = new ChangelogView();
        await view.ShowDialogAsync(mainWindow, vm);
    }

    /// <summary>
    /// 新建项目命令
    /// </summary>
    [RelayCommand]
    private async Task NewProject()
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return;

        // 弹出模态窗口前先暂停时间轴，避免后台继续播放
        ChartVM?.Pause();

        var vm = new CreateProjectViewModel();
        // 默认保存路径：用户在设置中指定的固定默认路径优先；
        // 未设置时回退到上次新建使用的父目录，项目名按该目录自动编号
        string defaultParent = string.IsNullOrWhiteSpace(_userSettingsModel.DefaultProjectParentPath)
            ? _userSettingsModel.LastProjectParentPath
            : _userSettingsModel.DefaultProjectParentPath;
        vm.Initialize(defaultParent);
        var view = new CreateProjectView();
        bool confirmed = await view.ShowDialogAsync(mainWindow, vm);
        if (!confirmed) return;

        try
        {
            string projectPath = ProjectService.CreateProject(vm.ProjectPath, vm.ProjectName);
            // 创建成功后记住本次的父目录，供下次新建作为默认路径
            _userSettingsModel.LastProjectParentPath = vm.ProjectPath;
            OnProjectOpened(projectPath);
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
        var options = new FolderPickerOpenOptions { Title = "选择项目文件夹" };

        // 起始位置 = 上一次打开项目的上一级文件夹（未打开项目时取最近项目列表第一条）
        string? lastProjectPath = _projectPath ?? RecentProjects.FirstOrDefault()?.Path;
        if (!string.IsNullOrEmpty(lastProjectPath))
        {
            // 路径末尾可能带分隔符，先去掉再取父目录，否则会取到项目文件夹本身
            string normalized = lastProjectPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string? parentPath = Path.GetDirectoryName(normalized);
            if (!string.IsNullOrEmpty(parentPath))
            {
                // TryGetFolderFromPathAsync 把本地路径转为选择器可用的 IStorageFolder
                options.SuggestedStartLocation =
                    await topLevel.StorageProvider.TryGetFolderFromPathAsync(parentPath);
            }
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(options);

        if (folders.Count == 0)
            return; // 用户取消

        string projectPath = folders[0].Path.LocalPath;

        try
        {
            if (!ProjectService.IsValidProject(projectPath))
            {
                System.Diagnostics.Debug.WriteLine("所选文件夹不是有效项目（缺少 charts 文件夹）");
                return;
            }

            OnProjectOpened(projectPath);
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
    private void SaveProject() => SaveCurrentChart();

    /// <summary>
    /// 在系统文件管理器中打开当前项目文件夹
    /// </summary>
    [RelayCommand]
    private void OpenProjectFolder()
    {
        if (_projectPath == null) return;

        try
        {
            // UseShellExecute = true：把路径交给系统的默认处理程序（文件夹 → 资源管理器）
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _projectPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[打开项目文件夹] 失败: {ex.Message}");
        }
    }

    /// <summary> 展开/收起谱面下拉列表 </summary>
    [RelayCommand]
    private void ToggleChartList()
    {
        if (IsChartNameEditing) return;
        IsChartListOpen = !IsChartListOpen;
    }

    /// <summary> 从下拉列表选择谱面：先关闭列表，再切换（点当前谱面也会正常收起） </summary>
    [RelayCommand]
    private void SelectChart(string fileName)
    {
        IsChartListOpen = false;
        CurrentChartFile = fileName;
    }

    /// <summary> 新建谱面：立即以默认名（chartN）创建并切换，然后进入重命名编辑态 </summary>
    [RelayCommand]
    private void NewChart()
    {
        if (_projectPath == null || ChartVM == null) return;

        string defaultName = ProjectService.GetDefaultChartName(_projectPath);
        try
        {
            string fileName = ProjectService.CreateChart(_projectPath, defaultName);
            RefreshChartFiles();
            CurrentChartFile = fileName; // 触发保存旧谱面并加载新谱面
            BeginRenameChart();          // 新谱面直接进入命名状态
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[新建谱面] 失败: {ex.Message}");
        }
    }

    /// <summary> 进入当前谱面的重命名编辑态（输入框由 View 自动聚焦并全选） </summary>
    [RelayCommand]
    private void BeginRenameChart()
    {
        if (_projectPath == null || _activeChartFile == null) return;

        IsChartListOpen = false;
        ChartNameInput = Path.GetFileNameWithoutExtension(_activeChartFile);
        HasChartNameError = false;
        IsChartNameEditing = true;
    }

    /// <summary>
    /// 确认重命名：名称合法且与当前不同时执行文件改名，然后退出编辑态；
    /// 名称非法时直接退出编辑态，保留原名（不落盘）
    /// </summary>
    [RelayCommand]
    private void ConfirmChartName()
    {
        if (!IsChartNameEditing) return;

        string newName = NormalizeChartName(ChartNameInput);
        bool sameName = string.Equals(newName, CurrentChartDisplayName, StringComparison.OrdinalIgnoreCase);

        if (!HasChartNameError && newName.Length > 0 && !sameName)
        {
            try
            {
                string newFile = ProjectService.RenameChart(_projectPath!, _activeChartFile!, newName);
                _activeChartFile = newFile;

                // 刷新列表并同步选中项；_isSwitchingChart 防止被当成切谱
                _isSwitchingChart = true;
                RefreshChartFiles();
                CurrentChartFile = newFile;
                _isSwitchingChart = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[重命名谱面] 失败: {ex.Message}");
            }
        }

        IsChartNameEditing = false;
        HasChartNameError = false;
    }

    /// <summary> 取消重命名（点击别处/切换页面时自动调用），保留原文件名 </summary>
    [RelayCommand]
    private void CancelChartNameEdit()
    {
        IsChartNameEditing = false;
        HasChartNameError = false;
    }

    /// <summary>
    /// 删除当前谱面：默认谱面直接删除；有改动内容时弹窗确认。
    /// 删除后切到剩余的第一份谱面，全部删完则自动补一份默认谱面
    /// </summary>
    [RelayCommand]
    private async Task DeleteChart()
    {
        if (_projectPath == null || _activeChartFile == null || ChartVM == null) return;

        // 非默认内容先确认，避免误删已编辑的谱面
        if (!ChartVM.IsDefaultChart())
        {
            var mainWindow = GetMainWindow();
            if (mainWindow == null) return;

            var dialog = new DeleteChartConfirmView();
            bool confirmed = await dialog.ShowDialogAsync(mainWindow,
                $"{CurrentChartDisplayName}有改动内容。确认删除吗？");
            if (!confirmed) return;
        }

        string deletedFile = _activeChartFile;
        try
        {
            ChartVM.Pause();
            File.Delete(Path.Combine(ProjectService.GetChartsDirectory(_projectPath), deletedFile));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[删除谱面] 失败: {ex.Message}");
            return;
        }

        // 删除后刷新列表；若已没有谱面，补一份默认谱面保证编辑器可用
        RefreshChartFiles();
        if (ChartFiles.Count == 0)
        {
            ProjectService.CreateChart(_projectPath, ProjectService.GetDefaultChartName(_projectPath));
            RefreshChartFiles();
        }

        if (ChartFiles.Count == 0) return;

        // 清空当前谱面状态：即使新建的默认谱面与被删文件同名，也能触发一次真正的重新加载
        _isSwitchingChart = true;
        CurrentChartFile = null;
        _activeChartFile = null;
        _isSwitchingChart = false;

        // 切换到剩余第一份；跳过保存，避免把刚删除的旧谱面写回磁盘
        _skipNextChartSave = true;
        CurrentChartFile = ChartFiles[0].FileName;
    }

    /// <summary>
    /// 返回欢迎界面：自动保存当前项目后，卸载项目视图与音频设备
    /// </summary>
    [RelayCommand]
    private void ReturnToWelcome()
    {
        if (!HasProject) return;

        // 与关窗逻辑一致：先静默保存，避免丢失修改
        SaveProject();
        TrackInfoVM?.CommitPendingEdit(); // 曲目信息页未失焦的输入也要落盘

        // 先置空 CurrentView，让 ContentControl 卸载旧 View 子树（退订事件、停止渲染循环）
        CurrentView = null;
        HasProject = false;

        ChartVM?.Dispose(); // 解除事件订阅
        ChartVM = null;
        _audio?.Dispose();  // 释放项目级音频管线
        _audio = null;
        TrackInfoVM = null;
        _projectPath = null;
        ProjectName = string.Empty;

        // 清空谱面选择状态，下次打开项目重新加载
        ChartFiles.Clear();
        _activeChartFile = null;
        _isSwitchingChart = true;
        CurrentChartFile = null;
        _isSwitchingChart = false;
        IsChartNameEditing = false;
        HasChartNameError = false;
        IsChartListOpen = false;
    }

    /// <summary> 退出时释放谱面 VM 与项目级音频（由 MainWindow 关闭事件调用） </summary>
    public void Dispose()
    {
        ChartVM?.Dispose();
        ChartVM = null;
        _audio?.Dispose();
        _audio = null;
    }

    /// <summary>
    /// 曲绘/音频导入按钮共用的文件选择器：返回用户选择的完整路径，并记住所在目录。
    /// isCover 决定读写哪个目录记录，使曲绘和乐曲各自记住上次打开的文件夹。
    /// </summary>
    private async Task<string?> PickAssetFileAsync(string title, FilePickerFileType fileType, bool isCover)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return null;

        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { fileType }
        };

        string lastFolder = isCover
            ? _userSettingsModel.LastCoverFolder
            : _userSettingsModel.LastAudioFolder;

        // 起始位置 = 上次导入同类资源的文件夹（无记录时使用系统默认位置）
        if (!string.IsNullOrWhiteSpace(lastFolder))
        {
            options.SuggestedStartLocation =
                await mainWindow.StorageProvider.TryGetFolderFromPathAsync(lastFolder);
        }

        var files = await mainWindow.StorageProvider.OpenFilePickerAsync(options);
        if (files.Count == 0) return null; // 用户取消

        string path = files[0].Path.LocalPath;
        string folder = Path.GetDirectoryName(path) ?? string.Empty;

        if (isCover)
            _userSettingsModel.LastCoverFolder = folder;
        else
            _userSettingsModel.LastAudioFolder = folder;

        return path;
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
            if (!ProjectService.IsValidProject(projectPath))
            {
                System.Diagnostics.Debug.WriteLine("最近项目缺少 charts 文件夹，无法打开");
                return;
            }

            OnProjectOpened(projectPath);
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
    /// 根据搜索框文本重建过滤后的最近项目列表：
    /// 搜索词为空时显示全部；否则按项目名/路径不区分大小写匹配。
    /// </summary>
    private void RefreshFilteredRecentProjects()
    {
        FilteredRecentProjects.Clear();

        string keyword = RecentProjectSearchText.Trim();
        var source = string.IsNullOrEmpty(keyword)
            ? RecentProjects
            : RecentProjects.Where(e =>
                e.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                e.Path.Contains(keyword, StringComparison.OrdinalIgnoreCase));

        foreach (var entry in source)
            FilteredRecentProjects.Add(entry);

        // 两个派生属性依赖过滤结果，手动通知 UI 刷新显隐
        OnPropertyChanged(nameof(HasFilteredRecentProjects));
        OnPropertyChanged(nameof(ShowNoSearchResults));
    }

    /// <summary>
    /// 退出时自动保存（由 View 层 Closing 事件调用）
    /// </summary>
    public void SaveOnExit()
    {
        // 曲目信息页未失焦的输入先落盘；退出时只写文件，不再重新解析音频
        TrackInfoVM?.CommitPendingEdit(applyStartOffset: false);
        SaveCurrentChart();
    }

    /// <summary>
    /// 项目打开/新建后的公共初始化逻辑
    /// </summary>
    private void OnProjectOpened(string projectPath)
    {
        _projectPath = projectPath;

        // 顶栏显示项目名：项目名即项目文件夹名（路径可能带末尾分隔符，先去掉再取）
        ProjectName = Path.GetFileName(
            projectPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        IsChartListOpen = false;
        IsChartNameEditing = false;
        HasChartNameError = false;

        // 旧项目的曲目信息页若有未提交编辑，先落盘（旧 ChartVM 即将释放，不再重解析音频）
        TrackInfoVM?.CommitPendingEdit(applyStartOffset: false);

        // 先拆掉旧视图：置空 CurrentView，强制 ContentControl 移除旧的 ChartView 子树
        // （旧 View 随之下树：NotesView 等退订 VM、ChartView 停止渲染循环），
        // 之后再赋新 ChartVM，保证 EditorView/NotesView 是全新实例而不是复用旧的
        CurrentView = null;

        ChartVM?.Dispose(); // 旧谱面 VM 解除事件订阅（共享音频不随 VM 释放）
        _audio?.Dispose();  // 释放上一个项目的音频管线
        _audio = CreateProjectAudio();

        // 读取谱面列表；按文件名顺序依次尝试解析，打开第一份合法谱面。
        // 非法谱面（旧 DSL、空白文本、损坏 JSON）会抛 ChartException，跳过但仍保留在下拉列表里。
        var charts = ProjectService.ListCharts(projectPath);

        ChartViewModel? newChartVm = null;
        string? activeFile = null;
        foreach (var file in charts)
        {
            try
            {
                string text = ProjectService.LoadChart(projectPath, file);
                newChartVm = new ChartViewModel(text, _userSettingsModel, _audio);
                activeFile = file;
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OnProjectOpened] 跳过非法谱面 {file}: {ex.Message}");
            }
        }

        // 没有任何合法谱面（含空目录）：按命名规则新建一份最小 JSON 框架谱面。
        // 例如 charts 下只有非法的 chart1.txt 时，会创建合法的 chart2.txt。
        if (newChartVm == null)
        {
            string fileName = ProjectService.CreateChart(projectPath, ProjectService.GetDefaultChartName(projectPath));
            charts = ProjectService.ListCharts(projectPath);
            newChartVm = new ChartViewModel(ProjectService.LoadChart(projectPath, fileName),
                                            _userSettingsModel, _audio);
            activeFile = fileName;
        }

        _activeChartFile = activeFile;
        _isSwitchingChart = true;
        ChartFiles.Clear();
        foreach (var file in charts)
        {
            ChartFiles.Add(new ChartFileEntry(
                file, string.Equals(file, _activeChartFile, StringComparison.OrdinalIgnoreCase)));
        }
        CurrentChartFile = _activeChartFile;
        _isSwitchingChart = false;

        ChartVM = newChartVm;

        // 读取曲目信息（文件不存在时自动创建空白模板）；MusicPath 即制谱音频
        var trackInfo = ProjectService.LoadTrackInfo(projectPath);
        TrackInfoVM = new TrackInfoViewModel(projectPath, trackInfo)
        {
            ChartVM = ChartVM,
            PickCoverFile = () => PickAssetFileAsync(
                "选择曲绘",
                new FilePickerFileType("图片") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg" } },
                isCover: true),
            PickMusicFile = () => PickAssetFileAsync(
                "选择音频",
                new FilePickerFileType("WAV 音频") { Patterns = new[] { "*.wav" } },
                isCover: false)
        };

        // 起始偏移必须在加载音频前设置：LoadBgm 会直接按偏移裁剪/补齐音频数据
        ChartVM.SetMusicStartOffset(trackInfo.MusicStartOffset);

        if (string.IsNullOrWhiteSpace(trackInfo.MusicPath))
        {
            System.Diagnostics.Debug.WriteLine("[OnProjectOpened] 项目未设置 MusicPath");
        }
        else
        {
            string audioPath = Path.Combine(projectPath, trackInfo.MusicPath);
            ChartVM.LoadBgm(audioPath); // 加载失败时保持无音频模式（时间轴默认长度）
        }
        HasProject = true;
        IsTrackInfoViewActive = false; // 主标签状态复位：打开项目后固定回到制谱视图
        IsChartViewActive = true;
        CurrentView = ChartVM;

        // 记录到最近项目列表并刷新
        ProjectService.RecordRecentProject(projectPath);
        LoadRecentProjects();
    }

    /// <summary>
    /// 创建项目级音频管线并按用户设置初始化。
    /// 设备、混音器和整曲缓存由项目内所有谱面共用，切换谱面时不再重建。
    /// </summary>
    private AudioModel CreateProjectAudio()
    {
        var audio = new AudioModel();
        // 按全局设置加载打击音效（路径为空 = 不播放）并应用三路音量（0~100）
        audio.LoadHitSound(_userSettingsModel.HitSoundPath);
        audio.SetMasterVolume(_userSettingsModel.MasterVolume);
        audio.SetMusicVolume(_userSettingsModel.MusicVolume);
        audio.SetHitSoundVolume(_userSettingsModel.HitSoundVolume);
        audio.SetHitSoundMuted(_userSettingsModel.IsHitSoundMuted);
        return audio;
    }

    /// <summary> 保存当前谱面到磁盘（无项目/无谱面时静默跳过） </summary>
    private void SaveCurrentChart()
    {
        if (_projectPath == null || _activeChartFile == null || ChartVM == null) return;

        try
        {
            ProjectService.SaveChart(_projectPath, _activeChartFile, ChartVM.SerializeChart());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[保存谱面] 失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 重新读取 charts 文件列表。清空集合会让 ListBox 把 SelectedItem 置空并回写，
    /// 因此恢复选中项时用 _isSwitchingChart 屏蔽切谱逻辑。
    /// </summary>
    private void RefreshChartFiles()
    {
        string? selected = _activeChartFile;
        ChartFiles.Clear();
        if (_projectPath == null) return;

        foreach (var file in ProjectService.ListCharts(_projectPath))
        {
            ChartFiles.Add(new ChartFileEntry(
                file, string.Equals(file, selected, StringComparison.OrdinalIgnoreCase)));
        }

        if (selected != null
            && ChartFiles.Any(e => string.Equals(e.FileName, selected, StringComparison.OrdinalIgnoreCase)))
        {
            _isSwitchingChart = true;
            CurrentChartFile = selected;
            _isSwitchingChart = false;
        }
    }

    /// <summary> 校验谱面名：空、含非法字符、与其他谱面重名都视为无效 </summary>
    private bool IsChartNameInvalid(string input)
    {
        string name = NormalizeChartName(input);
        if (name.Length == 0) return true;
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return true;

        // 与当前谱面同名不算重名（可直接确认退出编辑态）
        string fileName = name + ".txt";
        if (string.Equals(fileName, _activeChartFile, StringComparison.OrdinalIgnoreCase))
            return false;

        return _projectPath != null
            && File.Exists(Path.Combine(ProjectService.GetChartsDirectory(_projectPath), fileName));
    }

    /// <summary> 去掉首尾空白，并去掉用户可能误输入的 .txt 后缀 </summary>
    private static string NormalizeChartName(string input)
    {
        string name = input.Trim();
        if (name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            name = name[..^4].Trim();
        return name;
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
