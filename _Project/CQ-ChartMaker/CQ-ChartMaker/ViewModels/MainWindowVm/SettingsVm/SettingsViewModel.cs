using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CQ_ChartMaker.Models;

namespace CQ_ChartMaker.ViewModels;

/// <summary> 设置窗口的 ViewModel（目前包含快捷键改键） </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly UserSettingsModel _userSettings;

    // 改键工作副本：只有点“应用”才写回 UserSettings，取消则直接丢弃
    private readonly Dictionary<string, string> _workingBindings;

    /// <summary> 当前正在等待按下新键的行（null 表示未处于捕获状态） </summary>
    [ObservableProperty]
    private ShortcutRowViewModel? _capturingRow;

    /// <summary> 可修改的快捷键行 </summary>
    public ObservableCollection<ShortcutRowViewModel> EditableShortcutRows { get; } = new();

    /// <summary> 固定快捷键行（只展示，不可修改） </summary>
    public ObservableCollection<ShortcutRowViewModel> FixedShortcutRows { get; } = new();

    /// <summary> 偏好页“长条放置操作”的下拉选项 </summary>
    private static readonly LongNotePlaceOption[] LongNotePlaceOptionList =
    {
        new(LongNotePlaceMode.TwoClicks, "两次单击"),
        new(LongNotePlaceMode.Hold, "长按")
    };

    /// <summary> 长条放置方式的可选列表（ComboBox 的 ItemsSource） </summary>
    public IReadOnlyList<LongNotePlaceOption> LongNotePlaceOptions => LongNotePlaceOptionList;

    /// <summary> 当前选中的长条放置方式（工作副本，点“应用”才写回 UserSettings） </summary>
    [ObservableProperty]
    private LongNotePlaceOption _selectedLongNotePlaceOption = LongNotePlaceOptionList[0];

    /// <summary> 是否禁用拖拽（工作副本，点“应用”才写回 UserSettings） </summary>
    [ObservableProperty]
    private bool _isDragDisabled;

    /// <summary> 是否常驻显示吸附提示（工作副本，点“应用”才写回 UserSettings） </summary>
    [ObservableProperty]
    private bool _isSnapPreviewEnabled;

    /// <summary> 是否启用快捷放置（工作副本，点“应用”才写回 UserSettings） </summary>
    [ObservableProperty]
    private bool _isQuickPlaceEnabled;

    /// <summary> 是否启用快捷删除（工作副本，点“应用”才写回 UserSettings） </summary>
    [ObservableProperty]
    private bool _isQuickDeleteEnabled;

    /// <summary> Inspector 界面汉化（工作副本，点“应用”才写回；默认不勾选） </summary>
    [ObservableProperty]
    private bool _isInspectorLocalized;

    /// <summary> 缓动曲线汉化（工作副本，独立于界面汉化；默认不勾选） </summary>
    [ObservableProperty]
    private bool _isEaseCurveLocalized;

    /// <summary> 完全隐藏轨道的区间降亮提示（工作副本，点“应用”才写回；默认开启） </summary>
    [ObservableProperty]
    private bool _isHiddenLaneDimEnabled = true;

    /// <summary> 总音量工作副本（0~100，点“应用”才写回 UserSettings） </summary>
    [ObservableProperty]
    private int _masterVolume = 100;

    /// <summary> 音乐音量工作副本（0~100） </summary>
    [ObservableProperty]
    private int _musicVolume = 100;

    /// <summary> 打击音效音量工作副本（0~100） </summary>
    [ObservableProperty]
    private int _hitSoundVolume = 100;

    /// <summary> 打击音效文件路径（工作副本，点“应用”才写回 UserSettings；空 = 不播放） </summary>
    [ObservableProperty]
    private string _hitSoundPath = string.Empty;

    /// <summary> 新建项目默认父目录（工作副本，点“应用”才写回；空 = 不预设） </summary>
    [ObservableProperty]
    private string _defaultProjectPath = string.Empty;

    /// <summary> 由 View 层注入：打开打击音效文件选择器，返回所选路径（用户取消返回 null） </summary>
    public Func<Task<string?>>? BrowseHitSoundFile { get; set; }

    /// <summary> 由 View 层注入：打开文件夹选择器，返回所选目录（用户取消返回 null） </summary>
    public Func<Task<string?>>? BrowseDefaultProjectFolder { get; set; }

    /// <summary> 由 View 层注入：关闭当前设置窗口 </summary>
    public Action? CloseWindow { get; set; }

    /// <summary> 无参构造供 XAML 设计器使用（使用默认键位） </summary>
    public SettingsViewModel() : this(new UserSettingsModel())
    {
    }

    /// <summary> 由 MainWindowViewModel 注入用户设置，键位编辑基于其工作副本 </summary>
    public SettingsViewModel(UserSettingsModel userSettings)
    {
        _userSettings = userSettings;
        _workingBindings = new Dictionary<string, string>(userSettings.KeyBindings);
        // 偏好设置同样用工作副本：当前选中项只在点“应用”时写回
        SelectedLongNotePlaceOption =
            LongNotePlaceOptionList.FirstOrDefault(o => o.Mode == userSettings.LongNotePlaceMode)
            ?? LongNotePlaceOptionList[0];
        IsDragDisabled = userSettings.IsDragDisabled;
        IsSnapPreviewEnabled = userSettings.IsSnapPreviewEnabled;
        IsQuickPlaceEnabled = userSettings.IsQuickPlaceEnabled;
        IsQuickDeleteEnabled = userSettings.IsQuickDeleteEnabled;
        IsInspectorLocalized = userSettings.IsInspectorLocalized;
        IsEaseCurveLocalized = userSettings.IsEaseCurveLocalized;
        IsHiddenLaneDimEnabled = userSettings.IsHiddenLaneDimEnabled;
        MasterVolume = userSettings.MasterVolume;
        MusicVolume = userSettings.MusicVolume;
        HitSoundVolume = userSettings.HitSoundVolume;
        HitSoundPath = userSettings.HitSoundPath;
        DefaultProjectPath = userSettings.DefaultProjectParentPath;
        BuildShortcutRows();
    }

    /// <summary> 按目录顺序创建可改键行和固定提示行 </summary>
    private void BuildShortcutRows()
    {
        foreach (ShortcutDefinition definition in ShortcutCatalog.All.Where(d => d.ShowInSettings))
        {
            var row = new ShortcutRowViewModel(definition)
            {
                GestureText = ShortcutCatalog.FormatGesture(
                    ShortcutCatalog.GetGesture(definition, _workingBindings))
            };

            if (definition.IsEditable)
                EditableShortcutRows.Add(row);
            else
                FixedShortcutRows.Add(row);
        }

        RecomputeConflicts();
    }

    /// <summary> 点击某个可改键行，进入“等待按下新键”状态 </summary>
    [RelayCommand]
    private void BeginCapture(ShortcutRowViewModel row)
    {
        if (!row.IsEditable) return;

        // 切换捕获行前，先把上一行恢复为当前键位显示
        if (CapturingRow is { } previous)
        {
            RefreshRowGesture(previous);
            previous.IsCapturing = false;
        }

        CapturingRow = row;
        row.IsCapturing = true;
        row.GestureText = "按新键…";
    }

    /// <summary> 取消捕获（Esc 或窗口内切换行时） </summary>
    [RelayCommand]
    private void CancelCapture()
    {
        if (CapturingRow is not { } row) return;

        RefreshRowGesture(row);
        row.IsCapturing = false;
        CapturingRow = null;
    }

    /// <summary> 应用捕获到的键位（由 SettingsView 的键盘事件转发） </summary>
    public void ApplyCapture(Key key, KeyModifiers modifiers)
    {
        if (CapturingRow is not { } row || ShortcutCatalog.IsModifierOnly(key))
            return;

        var gesture = new KeyGesture(key, modifiers);
        _workingBindings[row.Definition.Id] = gesture.ToString();

        row.IsCapturing = false;
        CapturingRow = null;
        RefreshRowGesture(row);
        RecomputeConflicts();
    }

    /// <summary> 恢复某一可改键的默认键位（只改工作副本，点“应用”后持久化） </summary>
    [RelayCommand]
    private void ResetShortcut(ShortcutRowViewModel row)
    {
        if (!row.IsEditable) return;

        // 删除自定义项后，GetGesture 会自动回退到 ShortcutCatalog 中的默认键
        _workingBindings.Remove(row.Definition.Id);

        if (CapturingRow == row)
        {
            row.IsCapturing = false;
            CapturingRow = null;
        }

        RefreshRowGesture(row);
        RecomputeConflicts();
    }

    /// <summary> 用工作副本刷新某一行显示的键位文本 </summary>
    private void RefreshRowGesture(ShortcutRowViewModel row)
    {
        row.GestureText = ShortcutCatalog.FormatGesture(
            ShortcutCatalog.GetGesture(row.Definition, _workingBindings));
    }

    /// <summary>
    /// 重新计算所有可改键行的冲突状态。
    /// 与任一其他动作（包含固定键和隐藏的删除/缩放键）手势完全相同时标红，
    /// 但只做提示，不阻止用户应用。
    /// </summary>
    private void RecomputeConflicts()
    {
        var used = new List<(ShortcutAction Action, KeyGesture Gesture)>();
        foreach (ShortcutDefinition definition in ShortcutCatalog.All)
            used.Add((definition.Action, ShortcutCatalog.GetGesture(definition, _workingBindings)));

        foreach (ShortcutRowViewModel row in EditableShortcutRows)
        {
            KeyGesture gesture = ShortcutCatalog.GetGesture(row.Definition, _workingBindings);
            row.HasConflict = used.Any(u =>
                u.Action != row.Definition.Action &&
                u.Gesture.Key == gesture.Key &&
                u.Gesture.KeyModifiers == gesture.KeyModifiers);
        }
    }

    /// <summary> 点击“浏览…”：选择打击音效文件（仅 WAV），结果写入工作副本 </summary>
    [RelayCommand]
    private async Task BrowseHitSound()
    {
        if (BrowseHitSoundFile is null) return;
        var path = await BrowseHitSoundFile();
        if (path is not null)
            HitSoundPath = path;
    }

    /// <summary> 点击“浏览…”：选择新建项目的默认父目录，结果写入工作副本 </summary>
    [RelayCommand]
    private async Task BrowseDefaultProjectPath()
    {
        if (BrowseDefaultProjectFolder is null) return;
        var path = await BrowseDefaultProjectFolder();
        if (path is not null)
            DefaultProjectPath = path;
    }

    /// <summary> 应用设置：把工作副本写回用户设置（自动持久化）后关闭窗口 </summary>
    [RelayCommand]
    private void Apply()
    {
        // 整体替换字典，触发 UserSettingsModel 的属性变更与 settings.json 保存
        _userSettings.KeyBindings = new Dictionary<string, string>(_workingBindings);
        _userSettings.LongNotePlaceMode = SelectedLongNotePlaceOption.Mode;
        _userSettings.IsDragDisabled = IsDragDisabled;
        _userSettings.IsSnapPreviewEnabled = IsSnapPreviewEnabled;
        _userSettings.IsQuickPlaceEnabled = IsQuickPlaceEnabled;
        _userSettings.IsQuickDeleteEnabled = IsQuickDeleteEnabled;
        // 两项语言开关变更会触发 MainWindowViewModel 刷新 Inspector 显示
        _userSettings.IsInspectorLocalized = IsInspectorLocalized;
        _userSettings.IsEaseCurveLocalized = IsEaseCurveLocalized;
        // 完全隐藏轨道提示开关：编辑器会监听该属性立即刷新遮罩
        _userSettings.IsHiddenLaneDimEnabled = IsHiddenLaneDimEnabled;
        // 音量与音效路径变更会通知已打开的谱面窗口即时应用
        _userSettings.MasterVolume = MasterVolume;
        _userSettings.MusicVolume = MusicVolume;
        _userSettings.HitSoundVolume = HitSoundVolume;
        // 空字符串 = 关闭打击音效
        _userSettings.HitSoundPath = HitSoundPath.Trim();
        // 新建项目默认父目录（空 = 未设置，新建时回退到最近使用路径）
        _userSettings.DefaultProjectParentPath = DefaultProjectPath.Trim();
        CloseWindow?.Invoke();
    }

    /// <summary> 取消并关闭窗口（丢弃工作副本，不保存键位改动） </summary>
    [RelayCommand]
    private void Cancel()
    {
        CloseWindow?.Invoke();
    }
}

/// <summary> 设置页下拉选项：实际枚举值 + 中文显示文本 </summary>
public sealed record LongNotePlaceOption(LongNotePlaceMode Mode, string DisplayName);
