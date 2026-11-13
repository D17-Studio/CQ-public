using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CQ_ChartMaker.Models;

public partial class UserSettingsModel : ObservableObject
{
    private bool _suppressSave;

    [ObservableProperty]
    private double _pixelPerSecond = 200.0;

    partial void OnPixelPerSecondChanged(double value)
    {
        if (!_suppressSave)
            UserSettingsService.Save(this);
    }

    /// <summary> 预览全局流速（100~2000，默认 1000） </summary>
    [ObservableProperty]
    private int _globalSpeed = 1000;

    partial void OnGlobalSpeedChanged(int value)
    {
        if (!_suppressSave)
            UserSettingsService.Save(this);
    }

    /// <summary> Editor 缩放百分比（20~120），默认 100 </summary>
    [ObservableProperty]
    private int _editorZoom = 100;

    partial void OnEditorZoomChanged(int value)
    {
        if (!_suppressSave)
            UserSettingsService.Save(this);
    }

    /// <summary> 整体 UI 缩放倍率（0.5~1.5，默认 1.0；1.0 = 100%） </summary>
    [ObservableProperty]
    private double _uiScale = 1.0;

    partial void OnUiScaleChanged(double value)
    {
        if (!_suppressSave)
            UserSettingsService.Save(this);
    }

    /// <summary> 判定线归一化位置（0=顶，1=底），默认 0.75 偏下 </summary>
    [ObservableProperty]
    private double _judgmentLinePosition = 0.75;

    partial void OnJudgmentLinePositionChanged(double value)
    {
        if (!_suppressSave)
            UserSettingsService.Save(this);
    }

    [ObservableProperty]
    private Subdivision _subdivision = Subdivision.x4;

    partial void OnSubdivisionChanged(Subdivision value)
    {
        if (!_suppressSave)
            UserSettingsService.Save(this);
    }

    /// <summary> 上次新建项目使用的父目录（空 = 无记录），用于新建面板的默认保存路径 </summary>
    [ObservableProperty]
    private string _lastProjectParentPath = string.Empty;

    partial void OnLastProjectParentPathChanged(string value)
    {
        if (!_suppressSave)
            UserSettingsService.Save(this);
    }

    /// <summary> 用户指定的新建项目默认父目录（空 = 未设置），设置后优先于最近使用路径 </summary>
    [ObservableProperty]
    private string _defaultProjectParentPath = string.Empty;

    partial void OnDefaultProjectParentPathChanged(string value)
    {
        if (!_suppressSave)
            UserSettingsService.Save(this);
    }

    /// <summary> 上次导入音频时选择的文件夹（空 = 无记录），用于导入音频对话框的默认位置 </summary>
    [ObservableProperty]
    private string _lastAudioFolder = string.Empty;

    partial void OnLastAudioFolderChanged(string value)
    {
        if (!_suppressSave)
            UserSettingsService.Save(this);
    }

    /// <summary> 上次导入曲绘时选择的文件夹（空 = 无记录），用于导入曲绘对话框的默认位置 </summary>
    [ObservableProperty]
    private string _lastCoverFolder = string.Empty;

    partial void OnLastCoverFolderChanged(string value)
    {
        if (!_suppressSave)
            UserSettingsService.Save(this);
    }

    /// <summary>
    /// 用户自定义键位：动作 Id → KeyGesture 字符串（如 "DragMode" → "A"）。
    /// 未出现的动作使用 ShortcutCatalog 中的默认键位。
    /// </summary>
    [ObservableProperty]
    private Dictionary<string, string> _keyBindings = new();

    partial void OnKeyBindingsChanged(Dictionary<string, string> value)
    {
        if (!_suppressSave)
            UserSettingsService.Save(this);
    }

    /// <summary> 长条（Dash/Mute/Tune/Motion）的放置方式，默认保持原有的两次单击 </summary>
    [ObservableProperty]
    private LongNotePlaceMode _longNotePlaceMode = LongNotePlaceMode.TwoClicks;

    partial void OnLongNotePlaceModeChanged(LongNotePlaceMode value)
    {
        if (!_suppressSave)
            UserSettingsService.Save(this);
    }

    /// <summary> 总音量（0~100），作用于 BGM 与打击音效的最终输出 </summary>
    [ObservableProperty]
    private int _masterVolume = 100;

    partial void OnMasterVolumeChanged(int value)
    {
        if (!_suppressSave)
            UserSettingsService.Save(this);
    }

    /// <summary> 音乐（BGM）音量（0~100） </summary>
    [ObservableProperty]
    private int _musicVolume = 100;

    partial void OnMusicVolumeChanged(int value)
    {
        if (!_suppressSave)
            UserSettingsService.Save(this);
    }

    /// <summary> 打击音效音量（0~100） </summary>
    [ObservableProperty]
    private int _hitSoundVolume = 100;

    partial void OnHitSoundVolumeChanged(int value)
    {
        if (!_suppressSave)
            UserSettingsService.Save(this);
    }

    /// <summary> 打击音效文件路径（空 = 不播放打击音效） </summary>
    [ObservableProperty]
    private string _hitSoundPath = string.Empty;

    partial void OnHitSoundPathChanged(string value)
    {
        if (!_suppressSave)
            UserSettingsService.Save(this);
    }

    /// <summary> 是否禁用拖拽（偏好设置，默认 false = 允许在编辑器中按住条目拖动） </summary>
    [ObservableProperty]
    private bool _isDragDisabled;

    partial void OnIsDragDisabledChanged(bool value)
    {
        if (!_suppressSave)
            UserSettingsService.Save(this);
    }

    /// <summary> 常驻吸附提示：不处于放置状态时也显示吸附预览（偏好设置，默认 false） </summary>
    [ObservableProperty]
    private bool _isSnapPreviewEnabled;

    partial void OnIsSnapPreviewEnabledChanged(bool value)
    {
        if (!_suppressSave)
            UserSettingsService.Save(this);
    }

    /// <summary> 快捷放置：空白处单击放 Dot、长按拖拽放 Dash（偏好设置，默认 false） </summary>
    [ObservableProperty]
    private bool _isQuickPlaceEnabled;

    partial void OnIsQuickPlaceEnabledChanged(bool value)
    {
        if (!_suppressSave)
            UserSettingsService.Save(this);
    }

    /// <summary> 快捷删除：右键直接删除鼠标命中的 Note（偏好设置，默认 false） </summary>
    [ObservableProperty]
    private bool _isQuickDeleteEnabled;

    partial void OnIsQuickDeleteEnabledChanged(bool value)
    {
        if (!_suppressSave)
            UserSettingsService.Save(this);
    }

    /// <summary> Inspector 界面汉化（默认 false = 按结构体属性成员原样显示） </summary>
    [ObservableProperty]
    private bool _isInspectorLocalized;

    partial void OnIsInspectorLocalizedChanged(bool value)
    {
        if (!_suppressSave)
            UserSettingsService.Save(this);
    }

    /// <summary> 缓动曲线汉化（独立开关，默认 false = 保持英文） </summary>
    [ObservableProperty]
    private bool _isEaseCurveLocalized;

    partial void OnIsEaseCurveLocalizedChanged(bool value)
    {
        if (!_suppressSave)
            UserSettingsService.Save(this);
    }

    /// <summary> 是否静音打击音效（仅影响打击音效，不影响 BGM），默认不静音 </summary>
    [ObservableProperty]
    private bool _isHitSoundMuted;

    partial void OnIsHitSoundMutedChanged(bool value)
    {
        if (!_suppressSave)
            UserSettingsService.Save(this);
    }

    /// <summary> 完全隐藏轨道的区间在 Notes/Events 编辑器中降亮提示（偏好设置，默认开启） </summary>
    [ObservableProperty]
    private bool _isHiddenLaneDimEnabled = true;

    partial void OnIsHiddenLaneDimEnabledChanged(bool value)
    {
        if (!_suppressSave)
            UserSettingsService.Save(this);
    }

    /// <summary> 预览画面隐藏轨道标号（偏好设置，默认 false = 显示标号） </summary>
    [ObservableProperty]
    private bool _isLaneLabelHidden;

    partial void OnIsLaneLabelHiddenChanged(bool value)
    {
        if (!_suppressSave)
            UserSettingsService.Save(this);
    }

    /// <summary> Events 页显示模式（轨道/类型/平铺），默认类型模式，重启/换谱面后恢复 </summary>
    [ObservableProperty]
    private LaneDisplayMode _eventsDisplayMode = LaneDisplayMode.ByType;

    partial void OnEventsDisplayModeChanged(LaneDisplayMode value)
    {
        if (!_suppressSave)
            UserSettingsService.Save(this);
    }

    /// <summary>
    /// 从已加载的设置复制数据，不触发 Save
    /// </summary>
    public void ApplyLoaded(UserSettingsModel loaded)
    {
        _suppressSave = true;
        PixelPerSecond = loaded.PixelPerSecond;
        GlobalSpeed = loaded.GlobalSpeed;
        JudgmentLinePosition = loaded.JudgmentLinePosition;
        Subdivision = loaded.Subdivision;
        EditorZoom = loaded.EditorZoom;
        UiScale = loaded.UiScale;
        LastProjectParentPath = loaded.LastProjectParentPath;
        DefaultProjectParentPath = loaded.DefaultProjectParentPath;
        LastAudioFolder = loaded.LastAudioFolder;
        LastCoverFolder = loaded.LastCoverFolder;
        KeyBindings = new Dictionary<string, string>(loaded.KeyBindings);
        LongNotePlaceMode = loaded.LongNotePlaceMode;
        MasterVolume = loaded.MasterVolume;
        MusicVolume = loaded.MusicVolume;
        HitSoundVolume = loaded.HitSoundVolume;
        HitSoundPath = loaded.HitSoundPath;
        IsDragDisabled = loaded.IsDragDisabled;
        IsSnapPreviewEnabled = loaded.IsSnapPreviewEnabled;
        IsQuickPlaceEnabled = loaded.IsQuickPlaceEnabled;
        IsQuickDeleteEnabled = loaded.IsQuickDeleteEnabled;
        IsInspectorLocalized = loaded.IsInspectorLocalized;
        IsEaseCurveLocalized = loaded.IsEaseCurveLocalized;
        IsHitSoundMuted = loaded.IsHitSoundMuted;
        IsHiddenLaneDimEnabled = loaded.IsHiddenLaneDimEnabled;
        IsLaneLabelHidden = loaded.IsLaneLabelHidden;
        EventsDisplayMode = loaded.EventsDisplayMode;
        _suppressSave = false;
    }
}
