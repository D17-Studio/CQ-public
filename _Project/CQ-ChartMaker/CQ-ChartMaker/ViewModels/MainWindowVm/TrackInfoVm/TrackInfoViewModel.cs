using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CQ_ChartMaker.Models;
using CQMusicGame.Shared;

namespace CQ_ChartMaker.ViewModels;

/// <summary>
/// 曲目信息页 ViewModel：编辑并即时保存 TrackInfo.txt，
/// 同时把 MusicPath 指向的 WAV 作为制谱音频加载。
/// </summary>
public partial class TrackInfoViewModel : ViewModelBase
{
    private readonly string _projectPath;

    // 初始化/刷新文件列表期间为 true，避免联动保存和重复加载音频
    private bool _suppressSave;

    // 文本编辑暂存标记：输入过程中只更新属性、不写文件，失焦提交时统一保存
    // （与 Inspector 的“暂存命令 + 失焦提交”模式一致）
    private bool _pendingSave;

    // 起始偏移文本变化后，等提交时再重新解析音频，避免每键入一个字符都重载整曲
    private bool _pendingStartOffsetApply;

    private ChartViewModel? _chartVM;

    /// <summary> 当前谱面 VM；切换谱面时由 MainWindowViewModel 更新 </summary>
    public ChartViewModel? ChartVM
    {
        get => _chartVM;
        set
        {
            if (ReferenceEquals(_chartVM, value)) return;

            // 订阅/退订音频状态，用于驱动起始偏移下方的提示
            if (_chartVM != null) _chartVM.PropertyChanged -= OnChartVMPropertyChanged;
            _chartVM = value;
            if (_chartVM != null) _chartVM.PropertyChanged += OnChartVMPropertyChanged;

            OnPropertyChanged(nameof(HasAudioShorterThan1Ms));
            OnPropertyChanged(nameof(HasPreviewOutOfAudioRange));
        }
    }

    private void OnChartVMPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChartViewModel.IsAudioShorterThanOneMs))
            OnPropertyChanged(nameof(HasAudioShorterThan1Ms));
        else if (e.PropertyName == nameof(ChartViewModel.AudioDurationMs))
            OnPropertyChanged(nameof(HasPreviewOutOfAudioRange));
    }

    /// <summary> 处理后音频不足 1ms（已替换为 1ms 空白音频） </summary>
    public bool HasAudioShorterThan1Ms => ChartVM?.IsAudioShorterThanOneMs ?? false;

    /// <summary> 预览起点或终点超出 [0, 音频总时长]；无音频时不提示 </summary>
    public bool HasPreviewOutOfAudioRange
    {
        get
        {
            int duration = ChartVM?.AudioDurationMs ?? 0;
            if (duration <= 0) return false;

            return PreviewStart < 0 || PreviewEnd < 0
                || PreviewStart > duration || PreviewEnd > duration;
        }
    }

    /// <summary> 预览起点大于终点 </summary>
    public bool HasPreviewInvalidRange => PreviewStart > PreviewEnd;

    /// <summary> 曲绘文件选择器（由 MainWindowViewModel 注入） </summary>
    public Func<Task<string?>>? PickCoverFile { get; set; }

    /// <summary> 音频文件选择器（由 MainWindowViewModel 注入） </summary>
    public Func<Task<string?>>? PickMusicFile { get; set; }

    /// <summary> 项目根目录下的图片文件名 </summary>
    public ObservableCollection<string> CoverFiles { get; } = new();

    /// <summary> 项目根目录下的 WAV 文件名 </summary>
    public ObservableCollection<string> MusicFiles { get; } = new();

    /// <summary> 曲绘可选项多于一个时，界面提示清理多余资源 </summary>
    public bool HasMultipleCoverFiles => CoverFiles.Count > 1;

    /// <summary> 音频可选项多于一个时，界面提示清理多余资源 </summary>
    public bool HasMultipleMusicFiles => MusicFiles.Count > 1;

    /// <summary> 是否存在重复的难度编号 </summary>
    public bool HasDuplicateDifficultyIds =>
        Charts.GroupBy(c => c.DifficultyId).Any(g => g.Count() > 1);

    /// <summary> 是否有多条谱面行选中了同一个谱面文件 </summary>
    public bool HasDuplicateChartFiles =>
        Charts.Where(c => !string.IsNullOrEmpty(c.ChartFileName))
              .GroupBy(c => c.ChartFileName)
              .Any(g => g.Count() > 1);

    /// <summary> charts 目录中是否存在未被任何谱面行使用的文件 </summary>
    public bool HasUnusedChartFiles =>
        ChartFiles.Any(file => Charts.All(c =>
            !string.Equals(c.ChartFileName, file, StringComparison.OrdinalIgnoreCase)));

    /// <summary> 任意一条谱面提示需要显示 </summary>
    public bool HasAnyChartHint =>
        HasDuplicateDifficultyIds || HasDuplicateChartFiles || HasUnusedChartFiles;

    /// <summary> 项目 charts 目录下的谱面文件名 </summary>
    public ObservableCollection<string> ChartFiles { get; } = new();

    /// <summary> 已添加的谱面行 </summary>
    public ObservableCollection<TrackChartRowViewModel> Charts { get; } = new();

    [ObservableProperty]
    private string _musicTitle = string.Empty;

    [ObservableProperty]
    private string _musicCoverPath = string.Empty;

    [ObservableProperty]
    private string _musicPath = string.Empty;

    [ObservableProperty]
    private string _musicStartOffsetText = "0";

    /// <summary> 当前输入的起始偏移（毫秒），非法输入按 0 处理 </summary>
    public int MusicStartOffset => int.TryParse(MusicStartOffsetText, out int value) ? value : 0;

    [ObservableProperty]
    private string _previewStartText = "0";

    /// <summary> 当前输入的预览片段起点（毫秒），非法输入按 0 处理 </summary>
    public int PreviewStart => int.TryParse(PreviewStartText, out int value) ? value : 0;

    [ObservableProperty]
    private string _previewEndText = "0";

    /// <summary> 当前输入的预览片段终点（毫秒），非法输入按 0 处理 </summary>
    public int PreviewEnd => int.TryParse(PreviewEndText, out int value) ? value : 0;

    [ObservableProperty]
    private string _previewBpmText = "0";

    /// <summary> 当前输入的预览 BPM，非法输入按 0 处理 </summary>
    public float PreviewBpm => float.TryParse(PreviewBpmText, out float value) ? value : 0f;

    [ObservableProperty]
    private string _musicArtist = string.Empty;

    [ObservableProperty]
    private string _musicAlbum = string.Empty;

    [ObservableProperty]
    private string _musicCoverArtist = string.Empty;

    /// <summary> 曲绘预览图；未选择曲绘或加载失败时为 null </summary>
    [ObservableProperty]
    private Bitmap? _coverPreview;

    /// <summary> 曲绘原始像素尺寸文本（如 "1024 × 1024"） </summary>
    [ObservableProperty]
    private string _coverSizeText = string.Empty;

    public TrackInfoViewModel(string projectPath, TrackInfo info)
    {
        _projectPath = projectPath;
        LoadFrom(info);
        RefreshFiles();
    }

    // 文本输入只暂存，失焦提交时才写文件（见 CommitPendingEdit）

    partial void OnMusicTitleChanged(string value) => MarkPendingSave();

    partial void OnMusicStartOffsetTextChanged(string value)
    {
        if (_suppressSave) return;

        // 起始偏移的音频重新解析（补静音/裁剪）也推迟到提交时执行，
        // 否则每键入一个字符都会整曲重载
        MarkPendingSave();
        _pendingStartOffsetApply = true;
    }

    partial void OnMusicArtistChanged(string value) => MarkPendingSave();
    partial void OnMusicAlbumChanged(string value) => MarkPendingSave();
    partial void OnMusicCoverArtistChanged(string value) => MarkPendingSave();

    // 预览片段：提示状态仍随输入即时刷新，文件写入暂存到失焦提交
    partial void OnPreviewStartTextChanged(string value)
    {
        MarkPendingSave();
        OnPropertyChanged(nameof(HasPreviewInvalidRange));
        OnPropertyChanged(nameof(HasPreviewOutOfAudioRange));
    }

    partial void OnPreviewEndTextChanged(string value)
    {
        MarkPendingSave();
        OnPropertyChanged(nameof(HasPreviewInvalidRange));
        OnPropertyChanged(nameof(HasPreviewOutOfAudioRange));
    }

    // 预览 BPM 只作信息记录，不参与校验
    partial void OnPreviewBpmTextChanged(string value) => MarkPendingSave();

    partial void OnMusicCoverPathChanged(string value)
    {
        Save();
        UpdateCoverPreview();
    }

    partial void OnMusicPathChanged(string value)
    {
        if (_suppressSave) return;
        Save();
        LoadMusic(value);
    }

    /// <summary>
    /// 重新扫描项目根目录与 charts 目录（导入文件、切换谱面后调用）
    /// </summary>
    public void RefreshFiles()
    {
        string cover = MusicCoverPath;
        string music = MusicPath;
        string[] chartSelections = Charts.Select(c => c.ChartFileName).ToArray();

        _suppressSave = true;

        UpdateCollection(CoverFiles, ProjectService.ListCoverFiles(_projectPath));
        UpdateCollection(MusicFiles, ProjectService.ListMusicFiles(_projectPath));
        UpdateCollection(ChartFiles, ProjectService.ListCharts(_projectPath));

        // 重建 ItemsSource 时 ComboBox 可能把 SelectedItem 清空，这里恢复原值
        MusicCoverPath = cover;
        MusicPath = music;
        for (int i = 0; i < Charts.Count && i < chartSelections.Length; i++)
            Charts[i].ChartFileName = chartSelections[i];

        _suppressSave = false;

        // 文件列表变化后刷新资源提示与谱面提示
        OnPropertyChanged(nameof(HasMultipleCoverFiles));
        OnPropertyChanged(nameof(HasMultipleMusicFiles));
        NotifyChartHintState();
    }

    [RelayCommand]
    private async Task ImportCover()
    {
        if (PickCoverFile == null) return;

        string? source = await PickCoverFile();
        if (string.IsNullOrEmpty(source)) return;

        string? fileName = ProjectService.ImportFileToProject(_projectPath, source);
        if (fileName == null) return;

        RefreshFiles();

        // 导入的文件名可能与 TrackInfo.txt 里残留的旧路径完全相同（旧文件被删除后重新导入同名文件）。
        // 此时属性值不变，setter 不会触发 OnMusicCoverPathChanged 和 OnPropertyChanged，
        // 预览和下拉框都不会刷新，这里手动补发
        if (string.Equals(MusicCoverPath, fileName, StringComparison.Ordinal))
        {
            UpdateCoverPreview();
            OnPropertyChanged(nameof(MusicCoverPath));
        }
        else
        {
            MusicCoverPath = fileName;
        }
    }

    [RelayCommand]
    private async Task ImportMusic()
    {
        if (PickMusicFile == null) return;

        string? source = await PickMusicFile();
        if (string.IsNullOrEmpty(source)) return;

        string? fileName = ProjectService.ImportFileToProject(_projectPath, source);
        if (fileName == null) return;

        RefreshFiles();

        // 同 ImportCover：残留路径与导入文件同名时值不变，需要手动补发加载与通知
        if (string.Equals(MusicPath, fileName, StringComparison.Ordinal))
        {
            LoadMusic(fileName);
            OnPropertyChanged(nameof(MusicPath));
        }
        else
        {
            MusicPath = fileName;
        }
    }

    [RelayCommand]
    private void AddChart()
    {
        // 默认选第一个未使用的难度编号；编号允许重复，全部用过时回到 0
        int difficultyId = Enumerable.Range(0, 4)
            .FirstOrDefault(d => Charts.All(c => c.DifficultyId != d));

        AddRow(difficultyId, 0, ChartFiles.FirstOrDefault() ?? string.Empty, string.Empty);
        Save();
        NotifyChartHintState();
    }

    [RelayCommand]
    private void DeleteChart(TrackChartRowViewModel row)
    {
        if (row == null) return;

        row.PropertyChanged -= OnRowPropertyChanged;
        Charts.Remove(row);
        Save();
        NotifyChartHintState();
    }

    private void AddRow(int difficultyId, int chartConstant, string chartFileName, string chartArtist)
    {
        var row = new TrackChartRowViewModel
        {
            DifficultyIdText = difficultyId.ToString(),
            ChartConstantText = chartConstant.ToString(),
            ChartFileName = chartFileName,
            ChartArtist = chartArtist
        };
        row.PropertyChanged += OnRowPropertyChanged;
        Charts.Add(row);
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 谱面文件是下拉点选，立即保存；其余文本编辑与左栏一致，暂存到失焦提交
        if (e.PropertyName == nameof(TrackChartRowViewModel.ChartFileName))
            Save();
        else
            MarkPendingSave();

        NotifyChartHintState();
    }

    /// <summary> 谱面行数据变化后，刷新三条谱面提示的显示状态 </summary>
    private void NotifyChartHintState()
    {
        OnPropertyChanged(nameof(HasDuplicateDifficultyIds));
        OnPropertyChanged(nameof(HasDuplicateChartFiles));
        OnPropertyChanged(nameof(HasUnusedChartFiles));
        OnPropertyChanged(nameof(HasAnyChartHint));
    }

    /// <summary>
    /// 根据当前 MusicCoverPath 重新加载曲绘预览与尺寸文本。
    /// 路径为空、文件不存在或图片无法解码时都清空预览。
    /// </summary>
    private void UpdateCoverPreview()
    {
        CoverPreview = null;
        CoverSizeText = string.Empty;

        if (string.IsNullOrWhiteSpace(MusicCoverPath))
            return;

        string fullPath = Path.Combine(_projectPath, MusicCoverPath);
        try
        {
            if (!File.Exists(fullPath))
                return;

            // Avalonia 的 Bitmap 会一次性解码到内存，不长期占用源文件
            var bitmap = new Bitmap(fullPath);
            CoverPreview = bitmap;
            CoverSizeText = $"{bitmap.PixelSize.Width} × {bitmap.PixelSize.Height}";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CoverPreview] 加载失败: {ex.Message}");
        }
    }

    private void LoadFrom(TrackInfo info)
    {
        _suppressSave = true;

        MusicTitle = info.MusicTitle ?? string.Empty;
        MusicCoverPath = info.MusicCoverPath ?? string.Empty;
        MusicPath = info.MusicPath ?? string.Empty;
        MusicStartOffsetText = info.MusicStartOffset.ToString();
        PreviewStartText = info.PreviewStart.ToString();
        PreviewEndText = info.PreviewEnd.ToString();
        PreviewBpmText = info.PreviewBpm.ToString();
        MusicArtist = info.MusicArtist ?? string.Empty;
        MusicAlbum = info.MusicAlbum ?? string.Empty;
        MusicCoverArtist = info.MusicCoverArtist ?? string.Empty;

        Charts.Clear();
        if (info.Charts != null)
        {
            foreach (var chart in info.Charts.OrderBy(c => c.DifficultyId))
                AddRow(chart.DifficultyId, chart.ChartConstant,
                    GetChartFileName(chart.ChartPath), chart.ChartArtist ?? string.Empty);
        }

        _suppressSave = false;
    }

    /// <summary>
    /// 暂存文本编辑：只打标记不立即写文件，等输入框失焦时由 CommitPendingEdit 统一保存
    /// </summary>
    private void MarkPendingSave()
    {
        if (_suppressSave) return;
        _pendingSave = true;
    }

    /// <summary>
    /// 提交暂存的编辑（输入框或页面失焦时由 View 调用）：
    /// 写回 TrackInfo.txt，并按新的起始偏移重新解析音频
    /// </summary>
    /// <param name="applyStartOffset">退出程序时传 false，只写文件、不做无意义的音频重载</param>
    public void CommitPendingEdit(bool applyStartOffset = true)
    {
        if (_pendingSave)
            Save(); // Save 内部会清掉暂存标记

        if (!_pendingStartOffsetApply) return;
        _pendingStartOffsetApply = false;

        if (applyStartOffset)
            ChartVM?.SetMusicStartOffset(MusicStartOffset);
    }

    private void Save()
    {
        if (_suppressSave) return;

        // 本次写入基于所有属性的当前值，已包含全部暂存文本
        _pendingSave = false;

        try
        {
            ProjectService.SaveTrackInfo(_projectPath, BuildTrackInfo());
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SaveTrackInfo] 失败: {ex.Message}");
        }
    }

    private TrackInfo BuildTrackInfo()
    {
        return new TrackInfo
        {
            MusicTitle = MusicTitle,
            MusicCoverPath = MusicCoverPath,
            MusicPath = MusicPath,
            MusicStartOffset = int.TryParse(MusicStartOffsetText, out int offset) ? offset : 0,
            PreviewStart = PreviewStart,
            PreviewEnd = PreviewEnd,
            PreviewBpm = PreviewBpm,
            MusicArtist = MusicArtist,
            MusicAlbum = MusicAlbum,
            MusicCoverArtist = MusicCoverArtist,
            Charts = Charts.Count == 0
                ? null
                : Charts.Select(row => new ChartInfo
                {
                    DifficultyId = row.DifficultyId,
                    ChartConstant = row.ChartConstant,
                    ChartPath = string.IsNullOrWhiteSpace(row.ChartFileName)
                        ? string.Empty
                        : "charts/" + row.ChartFileName,
                    ChartArtist = row.ChartArtist
                }).ToArray()
        };
    }

    private void LoadMusic(string? musicPath)
    {
        if (ChartVM == null) return;

        // 空路径或加载失败都退回无音频模式
        if (string.IsNullOrWhiteSpace(musicPath))
        {
            ChartVM.ClearBgm();
            return;
        }

        string fullPath = Path.Combine(_projectPath, musicPath);
        if (!ChartVM.LoadBgm(fullPath))
            ChartVM.ClearBgm();
    }

    /// <summary> TrackInfo 中存的是 charts/xxx.txt，界面下拉框只显示文件名 </summary>
    private static string GetChartFileName(string? chartPath)
    {
        if (string.IsNullOrWhiteSpace(chartPath))
            return string.Empty;

        string normalized = chartPath.Replace('\\', '/');
        int slash = normalized.LastIndexOf('/');
        return slash >= 0 ? normalized.Substring(slash + 1) : normalized;
    }

    /// <summary>
    /// 只做差异更新（删除多余、追加缺失），不整体清空集合。
    /// 整体 Clear 会让绑定该集合的 ComboBox 把 SelectedItem 置空并回写源属性，
    /// 导致切换面板后已选文件被清掉。
    /// </summary>
    private static void UpdateCollection(ObservableCollection<string> target, System.Collections.Generic.List<string> values)
    {
        for (int i = target.Count - 1; i >= 0; i--)
        {
            if (!values.Contains(target[i]))
                target.RemoveAt(i);
        }

        foreach (string value in values)
        {
            if (!target.Contains(value))
                target.Add(value);
        }
    }
}

/// <summary>
/// 曲目信息页的一条谱面行
/// </summary>
public partial class TrackChartRowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _difficultyIdText = "0";

    [ObservableProperty]
    private string _chartConstantText = "0";

    private string _chartFileName = string.Empty;

    /// <summary>
    /// 选中的谱面文件名。
    /// ComboBox 在视图销毁或 ItemsSource 刷新时会回写 null/空串，
    /// 这里忽略这类空值，避免切换面板后已选文件被清空。
    /// </summary>
    public string ChartFileName
    {
        get => _chartFileName;
        set
        {
            if (string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(_chartFileName))
                return;

            SetProperty(ref _chartFileName, value);
        }
    }

    [ObservableProperty]
    private string _chartArtist = string.Empty;

    /// <summary> 难度编号；允许重复和任意整数，非法输入按 0 处理 </summary>
    public int DifficultyId
        => int.TryParse(DifficultyIdText, out int value) ? value : 0;

    /// <summary> 定数；非法输入按 0 处理 </summary>
    public int ChartConstant
        => int.TryParse(ChartConstantText, out int value) ? value : 0;
}
