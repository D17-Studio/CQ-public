using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CQ_ChartMaker.Models;
using CQMusicGame.Shared;

namespace CQ_ChartMaker.ViewModels;

public partial class NotesViewModel : ObservableObject
{
    private ChartSheetModel? _chartSheet;
    private UserSettingsModel? _userSettings;

    public ChartSheetModel? ChartSheet
    {
        get => _chartSheet;
        set => _chartSheet = value;
    }

    private TimelineModel? _timeline;
    public TimelineModel? Timeline
    {
        get => _timeline;
        set
        {
            if (_timeline != null) _timeline.TimeChanged -= OnTimeChanged;
            _timeline = value;
            if (_timeline != null) _timeline.TimeChanged += OnTimeChanged;
        }
    }

    /// <summary> 点击选中回调（unit, 是否按住 Ctrl），NotesView 点击后通过此处通知 ChartVM </summary>
    public System.Action<object?, bool>? OnSelectUnit;

    /// <summary> 框选结果回调（框内 unit 列表, 是否追加），由 ChartVM 注入 </summary>
    public System.Action<List<object>, bool>? OnSelectMany;

    /// <summary> 双击轨道空白回调（轨道编号, 是否 Ctrl 追加），由 ChartVM 注入 </summary>
    public System.Action<int, bool>? OnSelectLane;

    /// <summary> 放置 Note 回调，由 ChartVM 注入 </summary>
    public System.Action<NoteUnit>? OnPlaceNote;

    /// <summary> 放置 Tune 回调，由 ChartVM 注入 </summary>
    public System.Action<TuneUnit>? OnPlaceTune;

    /// <summary> 快捷删除回调（鼠标命中的 unit），由 ChartVM 注入 </summary>
    public System.Action<object>? OnDeleteUnitRequested;

    /// <summary> 幽灵放置提交回调（存活 note 的位置表, 是否复制），由 ChartVM 注入 </summary>
    public System.Action<List<NoteGhostPlacement>, bool>? OnCommitGhostPlacement;

    /// <summary> 开始拖拽回调（被抓住的基准 note, 按下点画布 X/Y），由 ChartVM 注入 </summary>
    public System.Action<NoteUnit, double, double>? OnBeginDrag;

    /// <summary>
    /// 是否允许拖拽，由 EditorViewModel 根据用户设置统一下发（默认开启，偏好设置可禁用）。
    /// 关闭时若正在拖拽，立即取消并还原原 note；普通剪贴幽灵不受影响。
    /// </summary>
    [ObservableProperty]
    private bool _isDragMode;

    partial void OnIsDragModeChanged(bool value)
    {
        // CancelGhost 内部同时取消尾部拖拽，关闭拖拽时两种临时拖拽状态一起还原
        if (!value) CancelGhost();
    }

    /// <summary> 当前选中的 Unit，由 ChartVM 设置 </summary>
    private object? _selectedUnit;
    public object? SelectedUnit
    {
        get => _selectedUnit;
        set
        {
            _selectedUnit = value;
            OnPropertyChanged(nameof(CanPlaceTune));
            if (PlacingMode == NotePlacingMode.Tune && !CanPlaceTune)
                PlacingMode = NotePlacingMode.None;
        }
    }

    /// <summary> 当前能否放置 Tune（选中 Dash 或 Tune 时可用） </summary>
    public bool CanPlaceTune =>
        _selectedUnit is NoteUnit { Data.Type: NoteType.Dash } ||
        _selectedUnit is TuneUnit { Parent: not null };

    /// <summary> 当前放置模式 </summary>
    [ObservableProperty]
    private NotePlacingMode _placingMode;

    public UserSettingsModel? UserSettings
    {
        get => _userSettings;
        set
        {
            if (_userSettings != null)
                _userSettings.PropertyChanged -= OnUserSettingChanged;
            _userSettings = value;
            if (_userSettings != null)
            {
                _userSettings.PropertyChanged += OnUserSettingChanged;
                RefreshNotes();
            }
        }
    }

    /// <summary> 是否静音打击音效（持久化到用户设置，仅影响打击音效，不影响 BGM） </summary>
    public bool IsHitSoundMuted
    {
        get => _userSettings?.IsHitSoundMuted ?? false;
        set
        {
            if (_userSettings == null || _userSettings.IsHitSoundMuted == value) return;
            _userSettings.IsHitSoundMuted = value;
            OnPropertyChanged();
        }
    }

    private LaneCatalogModel? _laneCatalog;

    /// <summary> 当前项目的轨道集合（轨道存在性与显隐的唯一数据源） </summary>
    public LaneCatalogModel? LaneCatalog
    {
        get => _laneCatalog;
        set
        {
            if (_laneCatalog != null)
            {
                _laneCatalog.Changed -= OnLaneCatalogChanged;
                _laneCatalog.VisibilityChanged -= OnLaneCatalogChanged;
            }
            _laneCatalog = value;
            if (_laneCatalog != null)
            {
                _laneCatalog.Changed += OnLaneCatalogChanged;
                _laneCatalog.VisibilityChanged += OnLaneCatalogChanged;
            }
            OnPropertyChanged(nameof(VisibleLanes));
            RefreshBeatLines();
        }
    }

    private static readonly List<int> EmptyVisibleLanes = new();

    /// <summary> 可见轨道（升序） </summary>
    public List<int> VisibleLanes => _laneCatalog?.VisibleLanes ?? EmptyVisibleLanes;

    private void OnLaneCatalogChanged()
    {
        OnPropertyChanged(nameof(VisibleLanes));
        RefreshBeatLines();
    }

    public double JudgmentLinePosition
    {
        get => _userSettings?.JudgmentLinePosition ?? 0.75;
        set
        {
            if (_userSettings != null)
                _userSettings.JudgmentLinePosition = value;
            OnPropertyChanged();
        }
    }

    public double LaneWidth => 80.0 * (_userSettings?.EditorZoom ?? 100) / 100.0;
    public double LaneSpacing => 4.0 * (_userSettings?.EditorZoom ?? 100) / 100.0;

    // === 视口高度 ===

    private double _viewHeight;
    public double ViewHeight
    {
        get => _viewHeight;
        set { _viewHeight = value; RefreshBeatLines(); } // 节拍刷新内部同时刷新 Note
    }

    // === 节拍数据（复用，不清空重建 List） ===

    private BeatGridModel? _beatGrid;
    private int _cachedBpmPointCount = -1;
    private readonly List<BeatInfo> _beatInfos = new();
    private readonly List<BeatInfo> _subBeatInfos = new();

    [ObservableProperty]
    private List<BeatLineSegmentItem> _beatLineSegments = new();

    [ObservableProperty]
    private List<BeatLineLabelItem> _beatLineLabels = new();

    [ObservableProperty]
    private List<BeatLineSegmentItem> _subBeatLineSegments = new();

    [ObservableProperty]
    private List<NoteVisualItem> _noteVisualItems = new();

    /// <summary> 重叠提示标记：每个起点与同列其他 Note 视觉重叠的 unit 一个 </summary>
    [ObservableProperty]
    private List<OverlapMarkerItem> _overlapMarkers = new();

    // === 重叠展开选择条 ===

    /// <summary> 本次刷新派生出的重叠组，供悬浮查询（不写回 unit） </summary>
    private readonly Dictionary<object, OverlapGroup> _overlapByUnit = new();

    /// <summary> 展开条打开时从轨道绘制中隐藏的其他 unit；仍参与重叠分组 </summary>
    private readonly HashSet<object> _overlapPanelHiddenUnits = new();

    /// <summary> OpenOverlapPanel 内部刷新时，跳过“外部刷新自动关闭”逻辑 </summary>
    private bool _suppressOverlapPanelAutoClose;

    [ObservableProperty] private bool _isOverlapPanelOpen;
    [ObservableProperty] private List<NoteOverlapPanelColumn> _overlapPanelColumns = new();
    [ObservableProperty] private double _overlapPanelTop;
    [ObservableProperty] private double _overlapPanelHeight;
    [ObservableProperty] private double _overlapPanelColumnWidth;
    [ObservableProperty] private double _overlapPanelSpacing;
    [ObservableProperty] private double _overlapPanelLeft;
    [ObservableProperty] private int _overlapPanelHoverIndex = -1;
    [ObservableProperty] private double _overlapPanelAnchorLeft;
    [ObservableProperty] private double _overlapPanelStripWidth;

    /// <summary> 展开条锚定的 unit（被悬浮的那个） </summary>
    public object? OverlapPanelAnchorUnit { get; private set; }

    /// <summary> 跳转提示：每列（轨道）一个，顺序与 VisibleLanes 一致 </summary>
    public ObservableCollection<LaneJumpHintItem> JumpHints { get; } = new();

    /// <summary> 跳转回调，由 ChartVM 注入（统一处理暂停与时间 clamp） </summary>
    public Action<int>? OnJumpToTime;

    /// <summary> 点击轨道内的悬浮三角：跳转到目标时间 </summary>
    [RelayCommand]
    private void JumpToTime(int timeMs) => OnJumpToTime?.Invoke(timeMs);

    /// <summary> 完全隐藏轨道的区间变暗遮罩矩形（画布坐标，供 View 直接绘制） </summary>
    [ObservableProperty]
    private List<LaneDimRectItem> _laneDimRects = new();

    // Note 头尺寸常量
    private const double HeadStroke = 3;
    private const double RegularHeadHeight = 14;
    private const double RegularInnerHeight = 8;
    private const double MinHeadOuterWidth = 18;
    private const double MuteHeadWidth = 18;
    private const double MuteHeadHeight = 14;
    private const double MuteInnerWidth = 12;
    private const double MuteInnerHeight = 8;
    private const double BodyWidth = 12;
    private const double ViewMarginPx = 10;

    // === 放置逻辑 ===

    /// <summary> Dash/Mute 正在等待第二次点击定长 </summary>
    private bool _pending;

    /// <summary> 第一次点击的时间 </summary>
    private int _pendingTime;

    /// <summary> 第一次点击的轨道 </summary>
    private int _pendingLane;

    /// <summary> 放置模式（Dash/Mute/Tune） </summary>
    private NotePlacingMode _pendingMode;

    /// <summary> Tune 待定时记录的父 Dash（第二次点击仍用它，不依赖那时的选中状态） </summary>
    private NoteUnit? _pendingTuneParent;

    /// <summary> 长按模式：左键已按下并进入拖动定长（松开结束） </summary>
    private bool _holding;

    /// <summary> 当前设置是否为长按放置长条 </summary>
    public bool IsHoldPlaceMode => _userSettings?.LongNotePlaceMode == LongNotePlaceMode.Hold;

    // === 快捷放置（偏好设置） ===

    /// <summary> 快捷拖动放置中：头已吸附确定，身体随鼠标拉伸，松开完成 Dash </summary>
    private bool _quickHolding;

    /// <summary> 快捷拖动的头时间（ms） </summary>
    private int _quickHeadTime;

    /// <summary> 快捷拖动的头轨道编号 </summary>
    private int _quickHeadLane;

    /// <summary> 常驻吸附提示：不处于放置状态时也显示吸附预览（偏好设置） </summary>
    public bool IsSnapPreviewEnabled => _userSettings?.IsSnapPreviewEnabled ?? false;

    /// <summary> 快捷放置：空白处单击放 Dot、长按拖动放 Dash（偏好设置） </summary>
    public bool IsQuickPlaceEnabled => _userSettings?.IsQuickPlaceEnabled ?? false;

    /// <summary> 快捷删除：右键删除鼠标命中的 unit（偏好设置） </summary>
    public bool IsQuickDeleteEnabled => _userSettings?.IsQuickDeleteEnabled ?? false;

    /// <summary> 切换放置模式时清除待定状态，Tune 不可用时阻止进入 </summary>
    partial void OnPlacingModeChanged(NotePlacingMode value)
    {
        _pending = false;
        _holding = false;
        _pendingTuneParent = null;
        if (value == NotePlacingMode.Tune && !CanPlaceTune)
            PlacingMode = NotePlacingMode.None;
        if (value != NotePlacingMode.None)
        {
            CancelGhost();    // 放置与幽灵互斥：进入任何放置模式都退出幽灵
            CancelRelocate(); // 放置与重定位同样互斥
        }
        UpdatePreview();
    }

    /// <summary>
    /// 在鼠标位置尝试放置 note（两次单击模式）。
    /// Dot 单击直接创建；Dash/Mute/Tune 第一次定点、第二次定长。
    /// 长按模式不走这里，由 BeginHoldPlace / EndHoldPlace 驱动。
    /// </summary>
    public void TryPlace(double canvasX, double canvasY)
    {
        if (_chartSheet == null || _userSettings == null || _viewHeight <= 0) return;

        if (PlacingMode == NotePlacingMode.Dot)
        {
            int snapTime = SnapTimeAt(canvasY);
            if (!PositionCalculator.TryXSnapToLane(
                    canvasX, LaneWidth, LaneSpacing, VisibleLanes, out int lane))
                return;

            var dot = new NoteUnit
            {
                Data = ChartDefaults.CreateNote(NoteType.Dot, lane, snapTime)
            };
            OnPlaceNote?.Invoke(dot);
            RefreshNotes();
            UpdatePreview();
            return;
        }

        // 长按模式由按下/松开事件接管，避免按下时又进入两次单击状态机
        if (IsHoldPlaceMode) return;

        if (!_pending)
            TryBeginPending(canvasX, canvasY);
        else
            CompletePending(canvasY);

        // 点击后放置状态有变（进入待定 / 放置完成回到跟随鼠标），重算预览
        UpdatePreview();
    }

    /// <summary> 长按模式：按下左键，确定长条头并进入拖动状态；返回是否成功进入 </summary>
    public bool BeginHoldPlace(double canvasX, double canvasY)
    {
        if (!IsHoldPlaceMode || _pending ||
            _chartSheet == null || _userSettings == null || _viewHeight <= 0)
            return false;

        // Dot 仍是单击放置，Motion 在另一个 VM 中处理
        if (PlacingMode is not (NotePlacingMode.Dash or NotePlacingMode.Mute or NotePlacingMode.Tune))
            return false;

        if (!TryBeginPending(canvasX, canvasY))
            return false;

        _holding = true;
        UpdatePreview();
        return true;
    }

    /// <summary> 长按模式：松开左键，按当前位置的吸附时间完成长条（未拖动则零长度） </summary>
    public void EndHoldPlace(double canvasY)
    {
        if (!_holding) return;

        _holding = false;
        CompletePending(canvasY);
        UpdatePreview();
    }

    /// <summary>
    /// 快捷放置：空白处单击直接放 Dot（不进入放置模式，复用同一套吸附与创建逻辑）。
    /// 点击点不在轨道内时什么也不做
    /// </summary>
    public void QuickPlaceDot(double canvasX, double canvasY)
    {
        if (!IsQuickPlaceEnabled) return;
        if (_chartSheet == null || _userSettings == null || _viewHeight <= 0) return;
        if (PlacingMode != NotePlacingMode.None || IsGhostActive || IsRelocating) return;

        int snapTime = SnapTimeAt(canvasY);
        if (!PositionCalculator.TryXSnapToLane(
                canvasX, LaneWidth, LaneSpacing, VisibleLanes, out int lane))
            return;

        var dot = new NoteUnit
        {
            Data = ChartDefaults.CreateNote(NoteType.Dot, lane, snapTime)
        };
        OnPlaceNote?.Invoke(dot);
        RefreshNotes();
        UpdatePreview();
    }

    /// <summary> 快捷放置：空白处长按拖动起点（吸附定头），返回是否成功进入拖动定长 </summary>
    public bool BeginQuickPlace(double canvasX, double canvasY)
    {
        if (!IsQuickPlaceEnabled) return false;
        if (_chartSheet == null || _userSettings == null || _viewHeight <= 0) return false;
        if (PlacingMode != NotePlacingMode.None || IsGhostActive || IsRelocating || _pending || _holding)
            return false;

        if (!PositionCalculator.TryXSnapToLane(
                canvasX, LaneWidth, LaneSpacing, VisibleLanes, out int lane))
            return false;

        _quickHolding  = true;
        _quickHeadTime = SnapTimeAt(canvasY);
        _quickHeadLane = lane;
        UpdatePreview();
        return true;
    }

    /// <summary> 快捷放置：松开左键，按当前 Y 的吸附时间完成 Dash（时长可负，与长按放置一致） </summary>
    public void EndQuickPlace(double canvasY)
    {
        if (!_quickHolding) return;

        _quickHolding = false;
        int duration = SnapTimeAt(canvasY) - _quickHeadTime;
        var note = new NoteUnit
        {
            Data = ChartDefaults.CreateNote(NoteType.Dash, _quickHeadLane, _quickHeadTime, duration)
        };
        OnPlaceNote?.Invoke(note);
        RefreshNotes();
        UpdatePreview();
    }

    /// <summary> 取消快捷拖动放置（视图切换等）：丢弃未提交的头，不产生 Note </summary>
    public void CancelQuickPlace()
    {
        if (!_quickHolding) return;
        _quickHolding = false;
        UpdatePreview();
    }

    /// <summary>
    /// 长条第一次点击/按下：记录头时间和轨道。
    /// Dash/Mute 必须有有效轨道；Tune 的轨道固定为父 Dash 的轨道。
    /// </summary>
    private bool TryBeginPending(double canvasX, double canvasY)
    {
        int snapTime = SnapTimeAt(canvasY);
        bool hasLane = PositionCalculator.TryXSnapToLane(
            canvasX, LaneWidth, LaneSpacing, VisibleLanes, out int lane);

        switch (PlacingMode)
        {
            case NotePlacingMode.Dash:
            case NotePlacingMode.Mute:
                if (!hasLane) return false;
                _pending     = true;
                _pendingTime = snapTime;
                _pendingLane = lane;
                _pendingMode = PlacingMode;
                return true;

            case NotePlacingMode.Tune:
                // Tune 选中 Dash 时直接用；选中 Tune 时取它的 Parent
                NoteUnit? parent = GetTuneParent();
                if (parent == null)
                {
                    CancelPlacement();
                    return false;
                }
                _pending           = true;
                _pendingTime       = snapTime;
                _pendingLane       = parent.Data.LaneIndex;
                _pendingMode       = PlacingMode;
                _pendingTuneParent = parent;
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// 长条第二次点击/松开：头始终是第一次记录的位置，
    /// 长度 = 当前吸附时间 - 头时间（可负，与旧行为一致）
    /// </summary>
    private void CompletePending(double canvasY)
    {
        if (!_pending) return;

        int snapTime = SnapTimeAt(canvasY);

        if (_pendingMode == NotePlacingMode.Tune)
        {
            NoteUnit? parent = _pendingTuneParent;
            if (parent == null)
            {
                CancelPlacement();
                return;
            }
            var tune = new TuneUnit
            {
                Parent = parent,
                Data = ChartDefaults.CreateTune(_pendingTime, parent.Data.LaneIndex, snapTime - _pendingTime)
            };
            OnPlaceTune?.Invoke(tune);
            RefreshNotes();
            _pending = false;
            _pendingTuneParent = null;
            return;
        }

        var note = new NoteUnit
        {
            Data = ChartDefaults.CreateNote(
                _pendingMode == NotePlacingMode.Dash ? NoteType.Dash : NoteType.Mute,
                _pendingLane, _pendingTime, snapTime - _pendingTime)
        };
        OnPlaceNote?.Invoke(note);
        RefreshNotes();
        _pending = false;
    }

    /// <summary> 右键取消放置 </summary>
    public void CancelPlacement()
    {
        _pending = false;
        _holding = false;
        _pendingTuneParent = null;
        PlacingMode = NotePlacingMode.None;
    }

    // === 重定位（Inspector 按钮触发：时间/轨道/持续时间） ===

    /// <summary> 重定位提交回调，由 ChartVM 注入（命令已执行，交给它入撤销池并刷新） </summary>
    public System.Action<IUndoableCommand>? OnCommitRelocate;

    private NoteUnit? _relocateNote;
    private TuneUnit? _relocateTune;
    private RelocateField _relocateField;
    private NoteData _relocateNoteOld;
    private TuneData _relocateTuneOld;

    /// <summary> 预览固定轨道：Note 为原轨道，Tune 为父 Dash 轨道（Tune 渲染跟随父 Dash） </summary>
    private int _relocateLane;

    /// <summary> 是否处于重定位模式 </summary>
    public bool IsRelocating => _relocateNote != null || _relocateTune != null;

    /// <summary> 进入重定位。Note 支持时间/轨道/持续时间；Tune 支持时间/持续时间（轨道跟随父 Dash） </summary>
    public void BeginRelocate(object unit, RelocateField field)
    {
        CancelPlacement();
        CancelGhost();

        _relocateNote = null;
        _relocateTune = null;
        switch (unit)
        {
            case NoteUnit note:
                _relocateNote = note;
                _relocateNoteOld = note.Data;
                _relocateLane = note.Data.LaneIndex;
                break;
            case TuneUnit tune when tune.Parent != null &&
                                     (field == RelocateField.Time || field == RelocateField.Duration):
                _relocateTune = tune;
                _relocateTuneOld = tune.Data;
                _relocateLane = tune.Parent.Data.LaneIndex;
                break;
            default:
                return;
        }

        _relocateField = field;
        UpdatePreview();
    }

    /// <summary> 退出重定位并隐藏预览（右键或选择变化时调用） </summary>
    public void CancelRelocate()
    {
        if (!IsRelocating) return;
        _relocateNote = null;
        _relocateTune = null;
        UpdatePreview();
    }

    /// <summary> 左键应用：按当前鼠标位置生成编辑命令并执行，交给 ChartVM 入撤销池 </summary>
    public void CommitRelocate(double canvasX, double canvasY)
    {
        if (!IsRelocating) return;
        _previewCanvasX = canvasX;
        _previewCanvasY = canvasY;
        _previewMouseOnCanvas = true;

        if (_relocateNote is { } note)
        {
            var oldData = _relocateNoteOld;
            var newData = oldData;

            if (_relocateField == RelocateField.Lane)
            {
                // 轨道外点击：不提交，保持重定位模式
                if (!PositionCalculator.TryXSnapToLane(
                        canvasX, LaneWidth, LaneSpacing, VisibleLanes, out int lane))
                    return;
                newData.LaneIndex = lane;
            }
            else if (_relocateField == RelocateField.Time)
            {
                newData.TargetTime = SnapMouseTime();
            }
            else
            {
                newData.SustainDuration = SnapMouseTime() - oldData.TargetTime; // 与放置一致，可负
            }

            if (Equals(newData, oldData)) { CancelRelocate(); return; }
            note.Data = newData;
            var command = new EditNoteCommand(note, oldData, newData);
            CancelRelocate();
            OnCommitRelocate?.Invoke(command);
            return;
        }

        if (_relocateTune is { } tune)
        {
            var oldData = _relocateTuneOld;
            var newData = oldData;

            if (_relocateField == RelocateField.Time)
                newData.TargetTime = SnapMouseTime();
            else
                newData.Duration = SnapMouseTime() - oldData.TargetTime;

            if (Equals(newData, oldData)) { CancelRelocate(); return; }
            tune.Data = newData;
            var command = new EditTuneCommand(tune, oldData, newData);
            CancelRelocate();
            OnCommitRelocate?.Invoke(command);
        }
    }

    /// <summary>
    /// 生成重定位预览：
    /// 时间 → 头锁定原轨道、Y 跟随鼠标；
    /// 轨道 → 头锁定原时间、X 跟随鼠标；
    /// 持续时间 → 只画 body，终点跟随鼠标（正时长才显示）。
    /// </summary>
    private List<NoteVisualItem> BuildRelocatePreviewItems()
    {
        var items = new List<NoteVisualItem>();
        if (_chartSheet == null || _userSettings == null || _viewHeight <= 0 || !_previewMouseOnCanvas)
            return items;

        int currentTime = _timeline?.TimeMs ?? 0;
        double pps = _userSettings.PixelPerSecond;
        double jlp = _userSettings.JudgmentLinePosition;
        double laneWidth = LaneWidth;
        double laneSpacing = LaneSpacing;

        if (_relocateNote is { } note)
        {
            var oldData = _relocateNoteOld;

            if (_relocateField == RelocateField.Lane)
            {
                if (!PositionCalculator.TryXSnapToLane(
                        _previewCanvasX, laneWidth, laneSpacing, VisibleLanes, out int lane))
                    return items;
                AddHead(items, null!, oldData.TargetTime, lane, ToVisual(oldData.Type), false,
                    laneWidth, laneSpacing, currentTime, pps, jlp);
            }
            else if (_relocateField == RelocateField.Time)
            {
                AddHead(items, null!, SnapMouseTime(), oldData.LaneIndex, ToVisual(oldData.Type), false,
                    laneWidth, laneSpacing, currentTime, pps, jlp);
            }
            else
            {
                int duration = SnapMouseTime() - oldData.TargetTime;
                if (duration > 0 && oldData.Type is NoteType.Dash or NoteType.Mute)
                {
                    var bodyData = new NoteData
                    {
                        TargetTime      = oldData.TargetTime,
                        SustainDuration = duration,
                        LaneIndex       = oldData.LaneIndex
                    };
                    AddBody(items, null!, bodyData, currentTime, pps, jlp, ToVisual(oldData.Type), false,
                        laneWidth, laneSpacing);
                }
            }
            return items;
        }

        if (_relocateTune is { } tune)
        {
            var oldData = _relocateTuneOld;
            if (_relocateField == RelocateField.Time)
            {
                AddHead(items, null!, SnapMouseTime(), _relocateLane, NoteVisualType.Tune, false,
                    laneWidth, laneSpacing, currentTime, pps, jlp);
            }
            else
            {
                int duration = SnapMouseTime() - oldData.TargetTime;
                if (duration > 0)
                {
                    var bodyData = new NoteData
                    {
                        TargetTime      = oldData.TargetTime,
                        SustainDuration = duration,
                        LaneIndex       = _relocateLane
                    };
                    AddBody(items, null!, bodyData, currentTime, pps, jlp, NoteVisualType.Tune, false,
                        laneWidth, laneSpacing);
                }
            }
        }
        return items;
    }

    // === 尾部拖拽（改变 Dash/Mute 的持续时间） ===

    /// <summary> 被拖动尾部的 note（null = 未处于尾拖） </summary>
    private NoteUnit? _tailResizeNote;

    /// <summary> 尾拖开始前的原始数据（松手时用于判断是否有变化） </summary>
    private NoteData _tailResizeOldData;

    /// <summary> 是否处于尾部拖拽（View 据此判定拖拽被中途取消） </summary>
    public bool IsTailResizing => _tailResizeNote != null;

    /// <summary>
    /// 开始拖动尾部：只允许有身体的 Dash/Mute（duration > 0，否则没有尾部）。
    /// 与幽灵/重定位/放置互斥；拖拽期间不改数据，松开时才提交
    /// </summary>
    public bool BeginTailResize(NoteUnit note)
    {
        if (_chartSheet == null || _userSettings == null || _viewHeight <= 0) return false;
        if (!IsDragMode) return false;
        if (note.Data.Type is not (NoteType.Dash or NoteType.Mute)) return false;
        if (note.Data.SustainDuration <= 0) return false;
        if (IsGhostActive || IsRelocating || PlacingMode != NotePlacingMode.None) return false;

        _tailResizeNote = note;
        _tailResizeOldData = note.Data;
        UpdatePreview();
        return true;
    }

    /// <summary>
    /// 松开左键：按当前鼠标 Y 的吸附时间计算新 duration 并提交。
    /// 与 duration 重定位一致，时长可为负（身体与尾部一起消失）；
    /// 命令经 OnCommitRelocate 交给 ChartVM 入撤销池并统一刷新
    /// </summary>
    public void EndTailResize(double canvasY)
    {
        if (_tailResizeNote is not { } note) return;

        var oldData = _tailResizeOldData;
        var newData = oldData;
        newData.SustainDuration = SnapTimeAt(canvasY) - oldData.TargetTime;

        _tailResizeNote = null;
        UpdatePreview();

        if (Equals(newData, oldData)) return;
        note.Data = newData;
        OnCommitRelocate?.Invoke(new EditNoteCommand(note, oldData, newData));
    }

    /// <summary> 取消尾部拖拽（关闭拖拽偏好、切视图等）：不改数据，只隐藏预览 </summary>
    public void CancelTailResize()
    {
        if (_tailResizeNote == null) return;
        _tailResizeNote = null;
        UpdatePreview();
    }

    /// <summary>
    /// 尾拖预览：身体从固定头拉到鼠标吸附时间，尾部贴在身体顶端；
    /// duration ≤ 0 时身体与尾部都不画（与最终渲染一致）。
    /// 原 note 不隐藏，预览以半透明白色叠加显示
    /// </summary>
    private List<NoteVisualItem> BuildTailResizePreviewItems()
    {
        var items = new List<NoteVisualItem>();
        if (_tailResizeNote == null || _chartSheet == null || _userSettings == null ||
            _viewHeight <= 0 || !_previewMouseOnCanvas)
            return items;

        var oldData = _tailResizeOldData;
        int duration = SnapMouseTime() - oldData.TargetTime;
        if (duration <= 0) return items;

        var data = new NoteData
        {
            TargetTime      = oldData.TargetTime,
            SustainDuration = duration,
            LaneIndex       = oldData.LaneIndex
        };

        int currentTime = _timeline?.TimeMs ?? 0;
        double pps = _userSettings.PixelPerSecond;
        double jlp = _userSettings.JudgmentLinePosition;
        var type = ToVisual(oldData.Type);

        AddBody(items, null!, data, currentTime, pps, jlp, type, false, LaneWidth, LaneSpacing);
        AddTail(items, null!, data, currentTime, pps, jlp, type, false, LaneWidth, LaneSpacing);
        return items;
    }

    // === 放置预览 ===

    /// <summary> 最近一次鼠标在 NoteCanvas 中的坐标（吸附计算的输入） </summary>
    private double _previewCanvasX;
    private double _previewCanvasY;

    /// <summary> 鼠标位置是否有效（离开 NoteCanvas 后置 false，待定头仍保留） </summary>
    private bool _previewMouseOnCanvas;

    /// <summary> 当前预览的可视元素，View 统一渲染为半透明白色矩形/三角 </summary>
    [ObservableProperty]
    private List<NoteVisualItem> _previewItems = new();

    /// <summary> 由 View 在 PointerMoved 时调用，更新鼠标画布坐标并重算预览 </summary>
    public void UpdatePreviewAt(double canvasX, double canvasY)
    {
        _previewCanvasX = canvasX;
        _previewCanvasY = canvasY;
        _previewMouseOnCanvas = true;
        UpdatePreview();
    }

    /// <summary> 鼠标离开 NoteCanvas 时调用：隐藏跟随鼠标的部分，待定的固定头保留 </summary>
    public void HidePreview()
    {
        _previewMouseOnCanvas = false;
        UpdatePreview();
    }

    /// <summary> 重算预览。每次都换新 List 实例，靠 ObservableProperty 自动通知 View 重绘 </summary>
    private void UpdatePreview() => PreviewItems = BuildPreviewItems();

    /// <summary>
    /// 生成预览元素（状态机）：
    /// 重定位/幽灵优先；
    /// 快捷放置拖动中 → Dash 头固定在起点、身体跟随鼠标；
    /// 不在放置模式 → 常驻吸附提示（设置开启时画 Dot 头）；
    /// Dash/Mute/Tune 未点击第一次 → 预览头跟随鼠标吸附位置
    ///   （Tune 的轨道固定为父 Dash 轨道，只有时间跟随鼠标）；
    /// Dash/Mute/Tune 已点击第一次 → 头固定在第一次点击处，再追加可拉伸身体；
    /// Tune 的身体即第二次点击要确定的 Duration 段。
    /// 头/body 直接复用 Note 渲染用的 AddHead/AddBody，保证预览与真实 Note 几何完全一致；
    /// 这两个方法只用 SelectionTarget 做点击选中，预览传 null。
    /// </summary>
    private List<NoteVisualItem> BuildPreviewItems()
    {
        var items = new List<NoteVisualItem>();

        // 重定位预览优先（进入时已退出幽灵与放置，这里保证状态互斥）
        if (IsRelocating)
            return BuildRelocatePreviewItems();

        // 尾部拖拽预览（与幽灵/重定位互斥）
        if (_tailResizeNote != null)
            return BuildTailResizePreviewItems();

        // 剪贴/复制幽灵优先于放置预览（两者本互斥，这里双保险）；
        // 布局与提交共用 ComputeGhostLayout，保证所见即所得
        if (_ghostEntries != null)
        {
            if (!_previewMouseOnCanvas || _chartSheet == null || _userSettings == null || _viewHeight <= 0)
                return items;

            var layout = ComputeGhostLayout(_previewCanvasX, _previewCanvasY);
            if (layout == null) return items; // 鼠标不在任何轨道上：幽灵整体隐藏

            int ghostCurrentTime = _timeline?.TimeMs ?? 0;
            double ghostPps = _userSettings.PixelPerSecond;
            double ghostJlp = _userSettings.JudgmentLinePosition;
            double ghostLaneWidth = LaneWidth;
            double ghostLaneSpacing = LaneSpacing;

            foreach (var item in layout)
            {
                // X 直接由（可能越界的）下标外推：越界项自然画到轨道区外，放置时才丢弃
                double laneX = item.Ordinal * (ghostLaneWidth + ghostLaneSpacing);
                AddGhostNote(items, item.Unit.Data, item.Time, laneX,
                    ghostLaneWidth, ghostCurrentTime, ghostPps, ghostJlp);
            }
            return items;
        }

        // 快捷放置拖动中：Dash 头固定、身体拉到鼠标吸附时间（优先于放置模式的早退）
        if (_quickHolding)
        {
            if (_chartSheet == null || _userSettings == null || _viewHeight <= 0)
                return items;

            int quickCurrentTime = _timeline?.TimeMs ?? 0;
            double quickPps = _userSettings.PixelPerSecond;
            double quickJlp = _userSettings.JudgmentLinePosition;
            double quickLaneWidth = LaneWidth;
            double quickLaneSpacing = LaneSpacing;

            AddHead(items, null!, _quickHeadTime, _quickHeadLane, NoteVisualType.Dash, false,
                quickLaneWidth, quickLaneSpacing, quickCurrentTime, quickPps, quickJlp);

            if (!_previewMouseOnCanvas) return items;

            int quickMouseTime = SnapMouseTime();
            if (quickMouseTime > _quickHeadTime)
            {
                var quickData = new NoteData
                {
                    TargetTime      = _quickHeadTime,
                    SustainDuration = quickMouseTime - _quickHeadTime,
                    LaneIndex       = _quickHeadLane
                };
                AddBody(items, null!, quickData, quickCurrentTime, quickPps, quickJlp,
                    NoteVisualType.Dash, false, quickLaneWidth, quickLaneSpacing);
            }
            return items;
        }

        if (_chartSheet == null || _userSettings == null || _viewHeight <= 0)
            return items;

        // 常驻吸附提示：完全空闲时也用 Dot 头显示吸附位置（偏好设置）。
        // 与重定位/幽灵/快捷拖动/放置模式互斥，因此不会与其它预览叠加
        if (PlacingMode == NotePlacingMode.None)
        {
            if (!IsSnapPreviewEnabled || !_previewMouseOnCanvas) return items;

            if (!PositionCalculator.TryXSnapToLane(
                    _previewCanvasX, LaneWidth, LaneSpacing, VisibleLanes, out int snapLane))
                return items;

            AddHead(items, null!, SnapMouseTime(), snapLane, NoteVisualType.Dot, false,
                LaneWidth, LaneSpacing, _timeline?.TimeMs ?? 0,
                _userSettings.PixelPerSecond, _userSettings.JudgmentLinePosition);
            return items;
        }

        int currentTime = _timeline?.TimeMs ?? 0;
        double pps = _userSettings.PixelPerSecond;
        double jlp = _userSettings.JudgmentLinePosition;
        double laneWidth = LaneWidth;
        double laneSpacing = LaneSpacing;

        // 预览头形状：Mute/Tune 用紧凑头尺寸，Dot/Dash 用常规头尺寸
        var headType = PlacingMode == NotePlacingMode.Mute ? NoteVisualType.Mute
                     : PlacingMode == NotePlacingMode.Tune ? NoteVisualType.Tune
                     : NoteVisualType.Dot;

        // Tune 的头始终定位在父 Dash 轨道上（即最终渲染所在轨道），null 表示没有父 Dash。
        // 不能用负数哨兵：负标号轨道是合法数据
        int? tuneLane = PlacingMode == NotePlacingMode.Tune
            ? GetTuneParent()?.Data.LaneIndex
            : null;

        if (_pending)
        {
            // 头固定在第一次点击的位置（_pending 来自 Dash/Mute/Tune 的第一次点击）
            AddHead(items, null!, _pendingTime, _pendingLane, headType, false,
                laneWidth, laneSpacing, currentTime, pps, jlp);

            // 鼠标不在画布上：只保留固定头
            if (!_previewMouseOnCanvas) return items;

            // body：从头拉到鼠标吸附时间（负时长即鼠标在头上方时不生成）
            int mouseTime = SnapMouseTime();
            if (mouseTime > _pendingTime)
            {
                var data = new NoteData
                {
                    TargetTime      = _pendingTime,
                    SustainDuration = mouseTime - _pendingTime,
                    LaneIndex       = _pendingLane
                };
                AddBody(items, null!, data, currentTime, pps, jlp, headType, false,
                    laneWidth, laneSpacing);
            }
            return items;
        }

        // 头跟随鼠标：Dash/Mute 吸附到鼠标所在轨道；Tune 画在父 Dash 轨道上，
        // 只看时间、鼠标 X 不参与，所以不要求落在轨道区内
        if (!_previewMouseOnCanvas) return items;

        int lane;
        if (PlacingMode == NotePlacingMode.Tune)
        {
            if (tuneLane is not { } parentLane) return items;
            lane = parentLane;
        }
        else if (!PositionCalculator.TryXSnapToLane(
                     _previewCanvasX, laneWidth, laneSpacing, VisibleLanes, out lane))
        {
            return items;
        }

        AddHead(items, null!, SnapMouseTime(), lane, headType, false,
            laneWidth, laneSpacing, currentTime, pps, jlp);
        return items;
    }

    /// <summary> 鼠标 Y 坐标 → 吸附到节拍网格的时间 </summary>
    private int SnapMouseTime() => SnapTimeAt(_previewCanvasY);

    /// <summary> 画布 Y 坐标 → 吸附网格的时间（放置预览与幽灵共用） </summary>
    private int SnapTimeAt(double canvasY)
    {
        int currentTime = _timeline?.TimeMs ?? 0;
        int time = PositionCalculator.YToTime((int)canvasY, currentTime,
            _userSettings!.PixelPerSecond, _userSettings.JudgmentLinePosition, _viewHeight);
        return PositionCalculator.TimeSnapToGrid(time,
            _chartSheet!.ChartSheet.GetBpmList(), _userSettings.Subdivision);
    }

    /// <summary> Tune 的父 Dash：选中 Dash 时就是它本身，选中 Tune 时取它的 Parent </summary>
    private NoteUnit? GetTuneParent() => _selectedUnit switch
    {
        NoteUnit { Data.Type: NoteType.Dash } dash => dash,
        TuneUnit tu => tu.Parent,
        _ => null
    };

    // === 批量剪贴/复制幽灵 ===

    /// <summary> 幽灵条目：源 note 引用 + 相对基准的冻结偏移（创建时算一次，之后帧间不变） </summary>
    private sealed class GhostEntry
    {
        public NoteUnit Unit = null!;
        public int TimeOffset;    // 时间偏移（ms）
        public int OrdinalOffset; // 可见轨道下标偏移（隐藏轨道会让视觉相邻的 note 下标差变小）
    }

    /// <summary> 幽灵布局项：渲染与提交共用的判定结果 </summary>
    private sealed class GhostLayoutItem
    {
        public NoteUnit Unit = null!;
        public int Time;      // 幽灵位置的时间
        public int Ordinal;   // 幽灵位置的可见轨道下标（可越界：渲染外推、放置丢弃）
        public int? Lane;     // 对应的轨道编号；null = 越界（当前没有这条轨道）
    }

    /// <summary> 幽灵源快照：null = 未激活。激活后与选中集合解耦（自包含） </summary>
    private List<GhostEntry>? _ghostEntries;

    /// <summary> 左右镜像开关：F 切换，相对偏移取负实现"以基准为对称轴"的镜像 </summary>
    private bool _ghostFlipped;

    /// <summary> 当前幽灵是否为复制模式（false = 剪贴） </summary>
    private bool _ghostIsCopy;

    /// <summary> 当前幽灵是否为拖拽模式（true = 拖拽移动：隐藏原 note、基准可指定） </summary>
    private bool _ghostIsDrag;

    /// <summary> 拖拽期间被隐藏的源 note（RefreshNotes 跳过这些 unit 及其 Tune） </summary>
    private HashSet<NoteUnit>? _dragHiddenUnits;

    /// <summary> 拖拽基准的可见轨道下标（起拖时冻结） </summary>
    private int _dragAnchorOrdinal;

    /// <summary> 起拖时按下点相对基准头中心的像素偏移；时间吸附以头所在位置为准 </summary>
    private double _dragHeadOffsetY;

    /// <summary> 起拖时鼠标所在轨道的可见下标；X 方向用差值移动 </summary>
    private int _dragStartSnapOrdinal;

    /// <summary> 拖拽中是否按住 Shift：时间锁定为起拖原值，只允许水平换轨 </summary>
    private bool _dragTimeLock;

    /// <summary> 拖拽基准的原始时间（Shift 锁定时整组时间的锚点） </summary>
    private int _dragAnchorTime;

    /// <summary>
    /// 实时同步 Shift 锁定状态（View 在鼠标移动与 Shift 按下/松开时调用）。
    /// 锁定中时间取起拖时的原值、仅允许水平移动；状态变化时立即重算预览，
    /// 鼠标不动也能看到时间弹回/放开
    /// </summary>
    public void SetDragTimeLock(bool locked)
    {
        if (_dragTimeLock == locked) return;
        _dragTimeLock = locked;
        if (IsDragActive) UpdatePreview();
    }

    /// <summary> 幽灵模式是否激活（View 据此改变点击行为；ChartVM 据此做同键取消/异键切换） </summary>
    public bool IsGhostActive => _ghostEntries != null;

    /// <summary> 是否处于拖拽幽灵（区别于普通剪贴/复制幽灵） </summary>
    public bool IsDragActive => _ghostEntries != null && _ghostIsDrag;

    /// <summary> 当前幽灵是否为复制模式 </summary>
    public bool GhostIsCopy => _ghostIsCopy;

    /// <summary>
    /// 启动批量剪贴/复制幽灵。传入的 note 必须都有可见轨道（#7 约定：隐藏轨道会先丢选中）。
    /// 当场冻结每个 note 相对基准的时间偏移与可见轨道下标偏移——此后幽灵是"刚性形状"，
    /// 鼠标怎么动、轨道怎么显隐都不重排，保证预览稳定、不发生漂移
    /// </summary>
    public void BeginGhost(List<NoteUnit> units, bool isCopy)
        => BeginGhostCore(units, isCopy, null, false, 0, 0);

    /// <summary>
    /// 启动拖拽幽灵：与剪贴共用同一套幽灵渲染/布局/提交，区别只有两点：
    /// 基准固定为被鼠标抓住的 note（而不是最左下角），并且拖拽期间隐藏源 note
    /// </summary>
    public void BeginDragGhost(List<NoteUnit> units, NoteUnit anchor, double pressX, double pressY)
        => BeginGhostCore(units, false, anchor, true, pressX, pressY);

    /// <summary>
    /// 幽灵公共启动流程：筛选可见轨道 → 冻结相对基准的偏移 → 记录拖拽起点快照。
    /// 拖拽时时间按"基准头所在位置"绝对吸附（Free 下即差值跟随），
    /// 原 note 不在网格上时会被吸到最近网格线；在网格上则起拖不跳
    /// </summary>
    private void BeginGhostCore(List<NoteUnit> units, bool isCopy,
        NoteUnit? anchorOverride, bool isDrag, double pressX, double pressY)
    {
        if (_chartSheet == null || _userSettings == null || _viewHeight <= 0 || units.Count == 0) return;

        CancelRelocate(); // 幽灵与重定位互斥

        // 防御：上一轮拖拽若还处于隐藏状态，先恢复（正常流程不会走到这里）
        if (_dragHiddenUnits != null)
        {
            _dragHiddenUnits = null;
            RefreshNotes();
        }

        // 只收有可见轨道下标的 note（正常不会发生，防御性过滤）
        var visible = new List<NoteUnit>();
        foreach (var unit in units)
            if (VisibleLanes.Contains(unit.Data.LaneIndex)) visible.Add(unit);
        if (visible.Count == 0) return;

        // 基准：拖拽用被抓住的 note；剪贴/复制用最左下角（时间最低优先，并列取轨道编号最小）
        var anchor = anchorOverride != null && visible.Contains(anchorOverride)
            ? anchorOverride!
            : visible[0];
        if (!isDrag || anchor != anchorOverride)
        {
            foreach (var unit in visible)
            {
                var d = unit.Data;
                var a = anchor.Data;
                if (d.TargetTime < a.TargetTime ||
                    (d.TargetTime == a.TargetTime && d.LaneIndex < a.LaneIndex))
                    anchor = unit;
            }
        }

        int anchorOrdinal = VisibleLanes.IndexOf(anchor.Data.LaneIndex);
        var entries = new List<GhostEntry>(visible.Count);
        foreach (var unit in visible)
        {
            entries.Add(new GhostEntry
            {
                Unit = unit,
                TimeOffset = unit.Data.TargetTime - anchor.Data.TargetTime,
                OrdinalOffset = VisibleLanes.IndexOf(unit.Data.LaneIndex) - anchorOrdinal
            });
        }

        _ghostEntries = entries;
        _ghostIsCopy = isCopy;
        _ghostFlipped = false; // 新幽灵从未翻转开始
        _ghostIsDrag = isDrag;
        _dragHiddenUnits = isDrag ? new HashSet<NoteUnit>(visible) : null;

        if (isDrag)
        {
            // 起拖快照：基准轨道下标 + 按下点相对基准头中心的像素偏移。
            // 时间吸附用 SnapTimeAt(鼠标Y + 偏移)，即吸附的是"头所在位置"而非鼠标点：
            // 头当前位置映射回的时间就是原时间，所以在网格上的 note 起拖不跳，
            // 不在网格上的 note 会被吸附到最近的网格线
            _dragAnchorOrdinal = anchorOrdinal;
            _dragHeadOffsetY = PositionCalculator.TimeToY(anchor.Data.TargetTime,
                _timeline?.TimeMs ?? 0, _userSettings.PixelPerSecond,
                _userSettings.JudgmentLinePosition, _viewHeight) - pressY;

            _dragAnchorTime = anchor.Data.TargetTime; // Shift 锁定时间时的整组锚点

            _dragStartSnapOrdinal = PositionCalculator.TryXSnapToLane(
                    pressX, LaneWidth, LaneSpacing, VisibleLanes, out int startLane)
                ? VisibleLanes.IndexOf(startLane)
                : anchorOrdinal;
        }

        PlacingMode = NotePlacingMode.None; // 与放置模式互斥（会自动重算预览）
        if (isDrag) RefreshNotes();          // 隐藏原 note（其 Tune 随 Dash 一起不画）
        UpdatePreview();
    }

    /// <summary>
    /// 取消幽灵模式（右键 / 同键再按 / 切视图 / 撤销删除前调用；未激活时无操作）。
    /// 尾部拖拽同属临时拖拽状态，一并在这里取消，ChartVM 现有的 CancelGhosts 调用点即可覆盖
    /// </summary>
    public void CancelGhost()
    {
        CancelTailResize();

        if (_ghostEntries == null) return;
        _ghostEntries = null;
        _ghostIsDrag = false;

        // 拖拽幽灵取消：恢复被隐藏的源 note（普通剪贴幽灵没有隐藏集合，跳过）
        if (_dragHiddenUnits != null)
        {
            _dragHiddenUnits = null;
            RefreshNotes();
        }
        UpdatePreview(); // 立即隐藏幽灵
    }

    /// <summary> F 键：切换左右镜像（以基准为对称轴，相对偏移取负；再按一次翻回来），未激活时无操作 </summary>
    public void ToggleGhostFlip()
    {
        if (_ghostEntries == null) return;
        _ghostFlipped = !_ghostFlipped;
        UpdatePreview(); // 鼠标不动也要立即重画
    }

    /// <summary>
    /// 左键提交幽灵放置：越界的 note 直接丢弃，其余打包位置表交给 ChartVM 应用。
    /// 剪贴模式放置一次即退出；复制模式保持激活，可连续放置（图章）
    /// </summary>
    public void TryCommitGhost(double canvasX, double canvasY)
    {
        if (_ghostEntries == null) return;

        var layout = ComputeGhostLayout(canvasX, canvasY);
        if (layout == null) return; // 鼠标不在任何轨道上，不提交

        var placements = new List<NoteGhostPlacement>();
        foreach (var item in layout)
        {
            if (item.Lane is not { } lane) continue; // 越界项：当前没有这条轨道，不放置
            placements.Add(new NoteGhostPlacement { Unit = item.Unit, NewTime = item.Time, NewLane = lane });
        }
        if (placements.Count == 0) return;

        OnCommitGhostPlacement?.Invoke(placements, _ghostIsCopy);

        if (!_ghostIsCopy) CancelGhost(); // 剪贴：放一次退出
    }

    /// <summary>
    /// 拖拽松手：位置有效则提交整组移动；无效（拖出轨道区）或原地未动则只还原、不产生命令。
    /// 提交复用剪贴的 OnCommitGhostPlacement（isCopy=false），撤销逻辑完全一致
    /// </summary>
    public void EndDragGhost(double canvasX, double canvasY)
    {
        if (_ghostEntries == null || !_ghostIsDrag) return;

        var layout = ComputeGhostLayout(canvasX, canvasY);
        List<NoteGhostPlacement>? placements = null;
        if (layout != null)
        {
            placements = new List<NoteGhostPlacement>();
            foreach (var item in layout)
            {
                if (item.Lane is not { } lane) continue; // 越界项不移动（留在原位）
                placements.Add(new NoteGhostPlacement { Unit = item.Unit, NewTime = item.Time, NewLane = lane });
            }
            if (placements.Count == 0) placements = null;
        }

        // 先退出幽灵并恢复隐藏，再提交：ChartVM 提交后会统一刷新选中，
        // 若此时仍处于隐藏状态，移动后的 note 会被 RefreshNotes 跳过
        _ghostEntries = null;
        _ghostIsDrag = false;
        _ghostFlipped = false;
        _dragHiddenUnits = null;
        RefreshNotes();
        UpdatePreview();

        if (placements != null)
            OnCommitGhostPlacement?.Invoke(placements, false);
    }

    /// <summary>
    /// 幽灵布局判定（渲染与提交共用的唯一来源，保证所见即所得）：
    /// 剪贴/复制：基准 = 鼠标所在轨道格 + 吸附网格时间；
    /// 拖拽：基准 = 基准头所在位置的绝对吸附时间（Free 下即跟随鼠标差值）；
    /// 其余 note = 基准 + 冻结偏移。鼠标不在任何轨道上返回 null（幽灵隐藏）
    /// </summary>
    private List<GhostLayoutItem>? ComputeGhostLayout(double canvasX, double canvasY)
    {
        if (_ghostEntries == null || _chartSheet == null || _userSettings == null || _viewHeight <= 0)
            return null;

        var lanes = VisibleLanes;
        int anchorOrdinal;
        int anchorTime;

        if (_ghostIsDrag)
        {
            // 拖拽：当前鼠标吸附轨道与原按下轨道的差值，叠加到基准原始下标上
            if (!PositionCalculator.TryXSnapToLane(
                    canvasX, LaneWidth, LaneSpacing, lanes, out int currentLane))
                return null;

            int currentOrdinal = lanes.IndexOf(currentLane);
            anchorOrdinal = _dragAnchorOrdinal + (currentOrdinal - _dragStartSnapOrdinal);

            // 基准被拖出可见轨道：整体隐藏，松手按无效位置取消还原
            if (anchorOrdinal < 0 || anchorOrdinal >= lanes.Count) return null;

            // Shift 按住：时间锁定为起拖原值，只随鼠标横向换轨；
            // 松开后重新跟随鼠标吸附（中途按下/松开都会立即生效）
            anchorTime = _dragTimeLock
                ? _dragAnchorTime
                : SnapTimeAt(canvasY + _dragHeadOffsetY);
        }
        else
        {
            if (!PositionCalculator.TryXSnapToLane(
                    canvasX, LaneWidth, LaneSpacing, lanes, out int anchorLane))
                return null;

            anchorOrdinal = lanes.IndexOf(anchorLane);
            anchorTime = SnapTimeAt(canvasY);
        }

        var result = new List<GhostLayoutItem>(_ghostEntries.Count);
        foreach (var entry in _ghostEntries)
        {
            // 左右镜像：相对偏移取负（基准偏移为 0 恒不动，即对称轴）
            int offset = _ghostFlipped ? -entry.OrdinalOffset : entry.OrdinalOffset;
            int ordinal = anchorOrdinal + offset;
            result.Add(new GhostLayoutItem
            {
                Unit = entry.Unit,
                Time = anchorTime + entry.TimeOffset,
                Ordinal = ordinal,
                Lane = ordinal >= 0 && ordinal < lanes.Count ? lanes[ordinal] : null
            });
        }
        return result;
    }

    /// <summary>
    /// 添加幽灵 note 的可视元素（白影）：几何与 AddHead/AddBody 一致，
    /// 但 X 直接用（可能越界的）下标外推坐标，不走 LaneToX；Tune 不在幽灵里显示
    /// </summary>
    private void AddGhostNote(List<NoteVisualItem> items, NoteData data, int ghostTime, double laneX,
        double laneWidth, int currentTime, double pps, double jlp)
    {
        double headY = PositionCalculator.TimeToY(ghostTime, currentTime, pps, jlp, _viewHeight);

        // 身体（Dash/Mute 且正时长）
        if (data.Type is NoteType.Dash or NoteType.Mute && data.SustainDuration > 0)
        {
            double endY = PositionCalculator.TimeToY(ghostTime + data.SustainDuration, currentTime, pps, jlp, _viewHeight);
            items.Add(new NoteVisualItem
            {
                Type = ToVisual(data.Type),
                IsHead = false,
                X = laneX + (laneWidth - BodyWidth) / 2,
                Y = endY,
                Width = BodyWidth,
                Height = Math.Max(0, headY - endY)
            });
        }

        // 头
        bool isMute = data.Type == NoteType.Mute;
        double headW = isMute ? MuteHeadWidth : Math.Max(MinHeadOuterWidth, laneWidth - 6);
        double headH = isMute ? MuteHeadHeight : RegularHeadHeight;
        items.Add(new NoteVisualItem
        {
            Type = ToVisual(data.Type),
            IsHead = true,
            X = laneX + (laneWidth - headW) / 2,
            Y = headY - headH / 2,
            Width = headW,
            Height = headH
        });
    }

    // === 框选 ===

    /// <summary> 框选锚点的时间：锚在数据坐标上，滚动或改 PPS 后框跟着谱面走 </summary>
    private int _boxAnchorTime;

    /// <summary> 框选锚点的画布 X </summary>
    private double _boxAnchorX;

    /// <summary> 鼠标当前画布位置（框的活动角） </summary>
    private double _boxCurrentX;
    private double _boxCurrentY;

    /// <summary> 是否处于框选状态（View 判定超过拖拽阈值后进入） </summary>
    private bool _boxSelecting;

    /// <summary> Ctrl+框选：结果追加到按下时已有的选中集合（追加合并由 ChartVM 的 SelectMany 完成） </summary>
    private bool _boxAppend;

    /// <summary> 开始框选：把按下点锚定为时间（超出屏幕部分由数据判定天然覆盖） </summary>
    public void BeginBoxSelect(double canvasX, double canvasY, bool append)
    {
        if (_chartSheet == null || _userSettings == null || _viewHeight <= 0) return;

        _boxAnchorTime = PositionCalculator.YToTime((int)canvasY, _timeline?.TimeMs ?? 0,
            _userSettings.PixelPerSecond, _userSettings.JudgmentLinePosition, _viewHeight);
        _boxAnchorX = canvasX;
        _boxCurrentX = canvasX;
        _boxCurrentY = canvasY;
        _boxAppend = append;
        _boxSelecting = true;
        OnPropertyChanged(nameof(BoxSelectRect));
    }

    /// <summary> 拖动中更新活动角：只重算矩形，不做任何选中计算和刷新 </summary>
    public void UpdateBoxSelect(double canvasX, double canvasY)
    {
        if (!_boxSelecting) return;
        _boxCurrentX = canvasX;
        _boxCurrentY = canvasY;
        OnPropertyChanged(nameof(BoxSelectRect));
    }

    /// <summary> 松手：一次性做数据判定，把框内的 unit 整批交给 ChartVM 应用选中 </summary>
    public void EndBoxSelect()
    {
        if (!_boxSelecting) return;
        _boxSelecting = false;
        OnPropertyChanged(nameof(BoxSelectRect)); // 通知 View 隐藏框

        OnSelectMany?.Invoke(CollectBoxSelection(), _boxAppend);
    }

    /// <summary>
    /// 框选判定（纯数据，与图形位置无关）：
    /// 把框换算成"时间区间 × X 区间"，与每个 NoteUnit 的时间跨度和轨道带求交。
    /// Tune 不参与选择（视为 Dash 的数据），所以只遍历 NoteUnitList 里的 Dash/Dot/Mute
    /// </summary>
    private List<object> CollectBoxSelection()
    {
        var hits = new List<object>();
        if (_chartSheet == null || _userSettings == null || _viewHeight <= 0) return hits;

        int currentTime = _timeline?.TimeMs ?? 0;
        double pps = _userSettings.PixelPerSecond;
        double jlp = _userSettings.JudgmentLinePosition;

        int endTime = PositionCalculator.YToTime((int)_boxCurrentY, currentTime, pps, jlp, _viewHeight);
        int timeMin = Math.Min(_boxAnchorTime, endTime);
        int timeMax = Math.Max(_boxAnchorTime, endTime);

        double xMin = Math.Min(_boxAnchorX, _boxCurrentX);
        double xMax = Math.Max(_boxAnchorX, _boxCurrentX);

        var lanes = VisibleLanes;
        double laneWidth = LaneWidth;
        double laneSpacing = LaneSpacing;

        foreach (var unit in _chartSheet.NoteUnitList)
        {
            var data = unit.Data;

            // X 方向：框与该轨道的整条轨道带求交（隐藏轨道不参与）
            int ordinal = lanes.IndexOf(data.LaneIndex);
            if (ordinal < 0) continue;
            double laneX = ordinal * (laneWidth + laneSpacing);
            if (laneX > xMax || laneX + laneWidth < xMin) continue;

            // 时间方向：Dot 是时间点，Dash/Mute 取整段身体（负时长按 0 处理）
            int noteStart = data.TargetTime;
            int noteEnd = data.TargetTime + Math.Max(0, data.SustainDuration);
            if (noteEnd < timeMin || noteStart > timeMax) continue;

            hits.Add(unit);
        }
        return hits;
    }

    /// <summary>
    /// 框选矩形（画布坐标），null 表示不显示。
    /// 只读计算：每次读取都用当前 pps / 时间换算锚点位置，所以滚动或 Ctrl+滚轮改 PPS 时框自动跟着谱面变形
    /// </summary>
    public SelectionRectItem? BoxSelectRect
    {
        get
        {
            if (!_boxSelecting || _userSettings == null || _viewHeight <= 0) return null;

            double anchorY = PositionCalculator.TimeToY(_boxAnchorTime, _timeline?.TimeMs ?? 0,
                _userSettings.PixelPerSecond, _userSettings.JudgmentLinePosition, _viewHeight);
            return new SelectionRectItem
            {
                X = Math.Min(_boxAnchorX, _boxCurrentX),
                Y = Math.Min(anchorY, _boxCurrentY),
                Width = Math.Abs(_boxCurrentX - _boxAnchorX),
                Height = Math.Abs(_boxCurrentY - anchorY)
            };
        }
    }

    // === 刷新 ===

    /// <summary> 对外暴露的刷新入口（Inspector 编辑后调用） </summary>
    public void RefreshNotesPublic() => RefreshNotes();

    // === 时间驱动刷新门控 ===

    private bool _isActive;

    /// <summary>
    /// 是否为当前显示的子视图。时间驱动的刷新只对活动视图执行（不可见视图刷新纯浪费）；
    /// 切回本视图时补一次刷新，保证暂停状态下画面立即恢复正确
    /// </summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;
            _isActive = value;
            if (value) OnTimeChanged(); // 暂停时没有 tick，切回来必须主动补一次
        }
    }

    private void OnTimeChanged()
    {
        if (!_isActive) return;

        RefreshBeatLines(); // 内部已包含 RefreshNotes，避免每帧重复计算
        UpdatePreview();
    }

    private void OnUserSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        var name = e.PropertyName;
        if (name == nameof(UserSettingsModel.JudgmentLinePosition))
            OnPropertyChanged(nameof(JudgmentLinePosition));
        else if (name == nameof(UserSettingsModel.EditorZoom))
        {
            OnPropertyChanged(nameof(LaneWidth));
            OnPropertyChanged(nameof(LaneSpacing));
        }
        else if (name == nameof(UserSettingsModel.LongNotePlaceMode))
        {
            // 切换放置方式时丢弃未完成的待定头，避免两套状态机混用
            _pending = false;
            _holding = false;
            _pendingTuneParent = null;
            UpdatePreview();
        }
        else if (name == nameof(UserSettingsModel.IsHitSoundMuted))
        {
            // 设置变化（如从其它入口修改）时同步按钮绑定值
            OnPropertyChanged(nameof(IsHitSoundMuted));
        }
        else if (name == nameof(UserSettingsModel.IsSnapPreviewEnabled))
        {
            // 空闲状态下开关常驻预览：鼠标不动也要立即显示/隐藏
            UpdatePreview();
        }

        if (name == nameof(UserSettingsModel.PixelPerSecond) ||
            name == nameof(UserSettingsModel.JudgmentLinePosition) ||
            name == nameof(UserSettingsModel.EditorZoom) ||
            name == nameof(UserSettingsModel.Subdivision) ||
            name == nameof(UserSettingsModel.IsHiddenLaneDimEnabled))
        {
            RefreshBeatLines();
        }
    }

    private void EnsureBeatGrid()
    {
        if (ChartSheet == null) return;
        var points = ChartSheet.ChartSheet.GetBpmList();
        if (points.Count != _cachedBpmPointCount)
        {
            _beatGrid = new BeatGridModel(points);
            _cachedBpmPointCount = points.Count;
        }
    }

    private void RefreshBeatLines()
    {
        if (ChartSheet == null || _userSettings == null || _viewHeight <= 0) return;

        EnsureBeatGrid();
        if (_beatGrid == null) return;

        int currentTime = _timeline?.TimeMs ?? 0;
        double pps = _userSettings.PixelPerSecond;
        double jlp = _userSettings.JudgmentLinePosition;

        int visibleEndMs = PositionCalculator.YToTime(0, currentTime, pps, jlp, _viewHeight);
        int visibleStartMs = PositionCalculator.YToTime(_viewHeight, currentTime, pps, jlp, _viewHeight);

        _beatGrid.GetBeatsInRange(visibleStartMs, visibleEndMs, _beatInfos);

        double laneWidth = LaneWidth;
        double laneSpacing = LaneSpacing;
        var lanes = VisibleLanes;

        _beatLineSegments.Clear();
        _beatLineLabels.Clear();
        foreach (var beat in _beatInfos)
        {
            double y = PositionCalculator.TimeToY(beat.TimeMs, currentTime, pps, jlp, _viewHeight);
            double x = 0;
            for (int li = 0; li < lanes.Count; li++)
            {
                _beatLineSegments.Add(new BeatLineSegmentItem { X = x, Y = y, Width = laneWidth });
                x += laneWidth + laneSpacing;
            }
            _beatLineLabels.Add(new BeatLineLabelItem { Y = y - 7, Label = beat.BeatIndex.ToString() });
        }

        int subDenom = (int)_userSettings.Subdivision;
        _beatGrid.GetSubBeatsInRange(visibleStartMs, visibleEndMs, subDenom, _subBeatInfos);
        _subBeatLineSegments.Clear();
        foreach (var sub in _subBeatInfos)
        {
            double y = PositionCalculator.TimeToY(sub.TimeMs, currentTime, pps, jlp, _viewHeight);
            double x = 0;
            for (int li = 0; li < lanes.Count; li++)
            {
                _subBeatLineSegments.Add(new BeatLineSegmentItem { X = x + 2, Y = y, Width = laneWidth - 4 });
                x += laneWidth + laneSpacing;
            }
        }
        OnPropertyChanged(nameof(BeatLineSegments));
        OnPropertyChanged(nameof(BeatLineLabels));
        OnPropertyChanged(nameof(SubBeatLineSegments));
        RefreshNotes();
    }

    /// <summary> 遍历 NoteUnitList 生成 NoteVisualItems，过滤不可见 note 并按 z-order 排列 </summary>
    private void RefreshNotes()
    {
        if (_chartSheet == null || _userSettings == null || _viewHeight <= 0) return;

        // 任何外部刷新（播放/滚轮/选择/缩放等）都关闭展开条；OpenOverlapPanel 内部的刷新除外
        if (IsOverlapPanelOpen && !_suppressOverlapPanelAutoClose)
            ClearOverlapPanelState();

        int currentTime = _timeline?.TimeMs ?? 0;
        double pps = _userSettings.PixelPerSecond;
        double jlp = _userSettings.JudgmentLinePosition;

        int visibleEndMs   = PositionCalculator.YToTime(-ViewMarginPx, currentTime, pps, jlp, _viewHeight);
        int visibleStartMs = PositionCalculator.YToTime(_viewHeight + ViewMarginPx, currentTime, pps, jlp, _viewHeight);

        var visibleLanes = VisibleLanes;
        double laneWidth = LaneWidth;
        double laneSpacing = LaneSpacing;

        var allItems = new List<NoteVisualItem>();

        // 重叠提示：容差按当前 PPS 从像素折算成毫秒；只收集视口附近的 Note 本体（Tune 不参与）
        int overlapToleranceMs = OverlapAnalyzer.PxToMs(OverlapAnalyzer.TolerancePx, pps);
        int overlapStartMs = visibleStartMs - overlapToleranceMs;
        int overlapEndMs   = visibleEndMs + overlapToleranceMs;
        var overlapEntries = new List<OverlapEntry>();

        // 第一遍遍历：收集 Dash 可见性（Tune 判断需要）
        var dashVisible = new HashSet<NoteUnit>();
        foreach (var unit in _chartSheet.NoteUnitList)
        {
            if (unit.Data.Type != NoteType.Dash) continue;
            if (_dragHiddenUnits?.Contains(unit) == true) continue; // 拖拽中被隐藏
            if (_overlapPanelHiddenUnits.Contains(unit)) continue;  // 已移到展开条
            if (!visibleLanes.Contains(unit.Data.LaneIndex)) continue;
            int noteEnd = unit.Data.SustainDuration > 0
                ? unit.Data.TargetTime + unit.Data.SustainDuration
                : unit.Data.TargetTime;
            if (noteEnd < visibleStartMs || unit.Data.TargetTime > visibleEndMs) continue;
            dashVisible.Add(unit);
        }

        // 第二遍遍历：生成 NoteVisualItem
        foreach (var unit in _chartSheet.NoteUnitList)
        {
            if (_dragHiddenUnits?.Contains(unit) == true) continue; // 拖拽中被隐藏（Tune 随父 Dash 一起跳过）
            var data = unit.Data;
            if (!visibleLanes.Contains(data.LaneIndex)) continue;

            // 只比较 Note 本体；Tune 属于 Dash 的属性，不参与重叠判定
            if (data.TargetTime >= overlapStartMs && data.TargetTime <= overlapEndMs)
            {
                overlapEntries.Add(new OverlapEntry
                {
                    Unit = unit,
                    Column = visibleLanes.IndexOf(data.LaneIndex),
                    StartTime = data.TargetTime
                });
            }

            // 展开条里的其他 unit 只从轨道绘制中隐藏，仍参与重叠分组（否则感叹号/展开会失效）
            if (_overlapPanelHiddenUnits.Contains(unit)) continue;

            switch (data.Type)
            {
                case NoteType.Dot:
                    if (data.TargetTime < visibleStartMs || data.TargetTime > visibleEndMs) continue;
                    AddHead(allItems, unit, data.TargetTime, data.LaneIndex, ToVisual(data.Type), unit.IsSelected, laneWidth, laneSpacing, currentTime, pps, jlp);
                    break;

                case NoteType.Dash:
                    if (!dashVisible.Contains(unit)) continue;
                    if (data.SustainDuration > 0)
                    {
                        AddBody(allItems, unit, data, currentTime, pps, jlp, ToVisual(data.Type), unit.IsSelected, laneWidth, laneSpacing);
                        AddTail(allItems, unit, data, currentTime, pps, jlp, ToVisual(data.Type), unit.IsSelected, laneWidth, laneSpacing);
                    }
                    AddHead(allItems, unit, data.TargetTime, data.LaneIndex, ToVisual(data.Type), unit.IsSelected, laneWidth, laneSpacing, currentTime, pps, jlp);
                    if (unit.TuneList != null)
                    {
                        foreach (var tu in unit.TuneList)
                        {
                            // 时间可见性按整段判断（与 Mute 一致）：负时长按 0 处理
                            int tuneEnd = tu.Data.Duration > 0
                                ? tu.Data.TargetTime + tu.Data.Duration
                                : tu.Data.TargetTime;
                            if (tuneEnd < visibleStartMs || tu.Data.TargetTime > visibleEndMs) continue;

                            // Duration > 0 时画身体，复用 Mute 的身体绘制（Tune 的 body 在父 Dash 轨道上）
                            if (tu.Data.Duration > 0)
                            {
                                var tuneBody = new NoteData
                                {
                                    TargetTime      = tu.Data.TargetTime,
                                    SustainDuration = tu.Data.Duration,
                                    LaneIndex       = data.LaneIndex
                                };
                                AddBody(allItems, tu, tuneBody, currentTime, pps, jlp, NoteVisualType.Tune, tu.IsSelected, laneWidth, laneSpacing);
                            }
                            AddHead(allItems, tu, tu.Data.TargetTime, data.LaneIndex, NoteVisualType.Tune, tu.IsSelected, laneWidth, laneSpacing, currentTime, pps, jlp);
                        }
                    }
                    break;

                case NoteType.Mute:
                    {
                        int muteEnd = data.SustainDuration > 0
                            ? data.TargetTime + data.SustainDuration
                            : data.TargetTime;
                        if (muteEnd < visibleStartMs || data.TargetTime > visibleEndMs) continue;
                        if (data.SustainDuration > 0)
                        {
                            AddBody(allItems, unit, data, currentTime, pps, jlp, ToVisual(data.Type), unit.IsSelected, laneWidth, laneSpacing);
                            AddTail(allItems, unit, data, currentTime, pps, jlp, ToVisual(data.Type), unit.IsSelected, laneWidth, laneSpacing);
                        }
                        AddHead(allItems, unit, data.TargetTime, data.LaneIndex, ToVisual(data.Type), unit.IsSelected, laneWidth, laneSpacing, currentTime, pps, jlp);
                    }
                    break;
            }
        }

        // 展开：每个原始 item 生成高亮副本（更底层）+ 本体
        var expanded = new List<NoteVisualItem>();
        foreach (var src in allItems)
        {
            expanded.Add(MakeHighlight(src));
            expanded.Add(src);
        }

        expanded.Sort((a, b) => GetZOrder(a).CompareTo(GetZOrder(b)));

        _noteVisualItems.Clear();
        _noteVisualItems.AddRange(expanded);
        OnPropertyChanged(nameof(NoteVisualItems));

        OverlapMarkers = BuildOverlapMarkers(
            overlapEntries, overlapToleranceMs, laneWidth, laneSpacing, currentTime, pps, jlp);

        // 遮罩与跳转提示只依赖轨道布局与时间映射，跟随 Note 刷新一起更新
        RefreshLaneDimRects();
        RefreshJumpHints();
    }

    /// <summary>
    /// 把重叠组展开成绘制标记：每个重叠 unit 一个，水平居中于所在轨道，Y 对齐自己的起始时间。
    /// 组内成员起始时间可能相差几毫秒，所以逐个成员画，而不是每组画一个。
    /// </summary>
    private List<OverlapMarkerItem> BuildOverlapMarkers(
        List<OverlapEntry> entries, int toleranceMs,
        double laneWidth, double laneSpacing,
        int currentTime, double pps, double jlp)
    {
        var markers = new List<OverlapMarkerItem>();
        _overlapByUnit.Clear();
        if (entries.Count < 2) return markers;

        foreach (var group in OverlapAnalyzer.Build(entries, toleranceMs))
        {
            foreach (var member in group.Members)
                _overlapByUnit[member.Unit] = group;

            double x = group.Column * (laneWidth + laneSpacing) + laneWidth / 2;
            foreach (var member in group.Members)
            {
                if (_overlapPanelHiddenUnits.Contains(member.Unit)) continue;
                markers.Add(new OverlapMarkerItem
                {
                    X = x,
                    Y = PositionCalculator.TimeToY(member.StartTime, currentTime, pps, jlp, _viewHeight)
                });
            }
        }
        return markers;
    }

    /// <summary> 查询 unit 所在的重叠组；没有可展开的其他 unit 时返回 false </summary>
    public bool TryGetOverlapGroup(object unit, out OverlapGroup group)
    {
        if (_overlapByUnit.TryGetValue(unit, out var found))
        {
            group = found;
            return true;
        }
        group = null!;
        return false;
    }

    /// <summary>
    /// 打开某个重叠 Note 的横向展开条：同组其余 unit 各占一列，按列宽重新生成可视项。
    /// 列内 unit 的 Y 保持原时间位置；打开期间其余 unit 从轨道绘制中隐藏。
    /// </summary>
    public bool OpenOverlapPanel(object unit)
    {
        if (_chartSheet == null || _userSettings == null || _viewHeight <= 0) return false;
        if (!_overlapByUnit.TryGetValue(unit, out var group)) return false;

        int currentTime = _timeline?.TimeMs ?? 0;
        double pps = _userSettings.PixelPerSecond;
        double jlp = _userSettings.JudgmentLinePosition;
        double columnWidth = LaneWidth * OverlapAnalyzer.PanelColumnWidthFactor;
        double spacing = 0; // 列之间不留缝，靠每列上下边缘的浅灰细线区分

        var columns = new List<NoteOverlapPanelColumn>();
        double minY = double.MaxValue;
        double maxY = double.MinValue;

        // 高度范围包含被悬浮的 unit 自身（duration 参与）
        foreach (var member in group.Members)
        {
            if (member.Unit is not NoteUnit nu) continue;
            var items = BuildNotePanelItems(nu, columnWidth, currentTime, pps, jlp);
            ExpandYBounds(items, ref minY, ref maxY);

            if (!ReferenceEquals(member.Unit, unit))
                columns.Add(new NoteOverlapPanelColumn { Unit = nu, Items = items });
        }

        if (columns.Count == 0 || minY > maxY) return false;

        _overlapPanelHiddenUnits.Clear();
        foreach (var column in columns)
            _overlapPanelHiddenUnits.Add(column.Unit);

        OverlapPanelAnchorUnit = unit;
        OverlapPanelAnchorLeft = group.Column * (LaneWidth + LaneSpacing);
        OverlapPanelColumnWidth = columnWidth;
        OverlapPanelSpacing = spacing;
        OverlapPanelColumns = columns;
        OverlapPanelTop = minY - OverlapAnalyzer.PanelPaddingPx;
        OverlapPanelHeight = maxY - minY + OverlapAnalyzer.PanelPaddingPx * 2;
        OverlapPanelStripWidth = columns.Count * columnWidth + (columns.Count - 1) * spacing;
        OverlapPanelLeft = 0;
        OverlapPanelHoverIndex = -1;
        IsOverlapPanelOpen = true;

        _suppressOverlapPanelAutoClose = true;
        try { RefreshNotes(); }
        finally { _suppressOverlapPanelAutoClose = false; }
        return true;
    }

    /// <summary> 关闭展开条并恢复被隐藏的 unit </summary>
    public void CloseOverlapPanel()
    {
        if (!IsOverlapPanelOpen) return;
        ClearOverlapPanelState();
        RefreshNotes();
    }

    private void ClearOverlapPanelState()
    {
        IsOverlapPanelOpen = false;
        OverlapPanelColumns = new List<NoteOverlapPanelColumn>();
        OverlapPanelHoverIndex = -1;
        OverlapPanelAnchorUnit = null;
        _overlapPanelHiddenUnits.Clear();
    }

    /// <summary> 设置展开条当前悬浮的列；-1 = 不在任何列上 </summary>
    public void SetOverlapPanelHover(int index)
    {
        if (OverlapPanelHoverIndex == index) return;
        OverlapPanelHoverIndex = index;
    }

    /// <summary> 点击展开条某一列：选中该列 unit 并关闭 </summary>
    public void SelectOverlapPanelColumn(int index)
    {
        if (index < 0 || index >= OverlapPanelColumns.Count) return;
        object unit = OverlapPanelColumns[index].Unit;
        CloseOverlapPanel();
        OnSelectUnit?.Invoke(unit, false);
    }

    /// <summary> 为展开条的一列生成 Note 可视项：X 为列内坐标，Y 与原轨道一致 </summary>
    private List<NoteVisualItem> BuildNotePanelItems(NoteUnit unit, double columnWidth,
        int currentTime, double pps, double jlp)
    {
        var items = new List<NoteVisualItem>();
        var data = unit.Data;
        double headY = PositionCalculator.TimeToY(data.TargetTime, currentTime, pps, jlp, _viewHeight);

        if (data.SustainDuration > 0 && data.Type is NoteType.Dash or NoteType.Mute)
        {
            double endY = PositionCalculator.TimeToY(
                data.TargetTime + data.SustainDuration, currentTime, pps, jlp, _viewHeight);
            double bodyX = (columnWidth - BodyWidth) / 2;

            items.Add(new NoteVisualItem
            {
                SelectionTarget = unit,
                Type = ToVisual(data.Type),
                X = bodyX,
                Y = endY,
                Width = BodyWidth,
                Height = System.Math.Max(0, headY - endY)
            });
            items.Add(new NoteVisualItem
            {
                SelectionTarget = unit,
                Type = ToVisual(data.Type),
                IsTail = true,
                X = bodyX,
                Y = endY - HeadStroke,
                Width = BodyWidth,
                Height = HeadStroke
            });
        }

        bool isCompact = data.Type == NoteType.Mute;
        double headW = isCompact ? MuteHeadWidth : System.Math.Max(MinHeadOuterWidth, columnWidth - 6);
        double headH = isCompact ? MuteHeadHeight : RegularHeadHeight;
        items.Add(new NoteVisualItem
        {
            SelectionTarget = unit,
            Type = ToVisual(data.Type),
            IsHead = true,
            X = (columnWidth - headW) / 2,
            Y = headY - headH / 2,
            Width = headW,
            Height = headH
        });
        return items;
    }

    /// <summary> 把一组可视项的 Y 范围并入 minY/maxY </summary>
    private static void ExpandYBounds(List<NoteVisualItem> items, ref double minY, ref double maxY)
    {
        foreach (var item in items)
        {
            if (item.Y < minY) minY = item.Y;
            if (item.Y + item.Height > maxY) maxY = item.Y + item.Height;
        }
    }

    /// <summary>
    /// 重算每条轨道的跳转提示：画面内无 unit 时给出上/下最近 unit 的时间。
    /// 可见范围与 RefreshNotes 使用同一套 ViewMarginPx 边距，保证"画面内是否有内容"与渲染一致。
    /// 时间方向：Y 越小时间越大，因此 top=画面顶部（更晚），bottom=画面底部（更早）。
    /// </summary>
    private void RefreshJumpHints()
    {
        if (_chartSheet == null || _userSettings == null || _viewHeight <= 0) return;

        var lanes = VisibleLanes;
        int count = lanes.Count;
        LaneJumpHintItem.SyncCount(JumpHints, count);

        int currentTime = _timeline?.TimeMs ?? 0;
        double pps = _userSettings.PixelPerSecond;
        double jlp = _userSettings.JudgmentLinePosition;

        int top = PositionCalculator.YToTime(-ViewMarginPx, currentTime, pps, jlp, _viewHeight);
        int bottom = PositionCalculator.YToTime(_viewHeight + ViewMarginPx, currentTime, pps, jlp, _viewHeight);

        var hasContent = new bool[count];
        // 哨兵：向上取最小值用 int.MaxValue，向下取最大值用 int.MinValue。
        // 不能用 -1：负时间 unit 也是合法候选（点击后由跳转逻辑 clamp 到 0）
        var upTime = new int[count];
        var downTime = new int[count];
        for (int i = 0; i < count; i++)
        {
            upTime[i] = int.MaxValue;
            downTime[i] = int.MinValue;
        }

        foreach (var unit in _chartSheet.NoteUnitList)
        {
            if (_dragHiddenUnits?.Contains(unit) == true) continue;
            int column = lanes.IndexOf(unit.Data.LaneIndex);
            if (column < 0) continue;

            // 长条按整段 [头, 尾] 参与判断，只有整段都在画面外才作为跳转候选
            int start = unit.Data.TargetTime;
            int end = start + Math.Max(0, unit.Data.SustainDuration);

            if (start <= top && end >= bottom)
                hasContent[column] = true;
            else if (start > top)
            {
                // 向上跳：取头部离画面顶部最近的 unit，目标为它的头部
                if (start < upTime[column])
                    upTime[column] = start;
            }
            else if (end < bottom)
            {
                // 向下跳：取尾部离画面底部最近的 unit，目标为它的尾部
                if (end > downTime[column])
                    downTime[column] = end;
            }
        }

        for (int i = 0; i < count; i++)
        {
            var hint = JumpHints[i];
            hint.ShowUp = !hasContent[i] && upTime[i] != int.MaxValue;
            hint.UpTimeMs = upTime[i];
            hint.ShowDown = !hasContent[i] && downTime[i] != int.MinValue;
            hint.DownTimeMs = downTime[i];
        }
    }

    /// <summary>
    /// 生成"完全隐藏轨道"区间的变暗遮罩矩形（画布坐标）。
    /// 区间由 ChartSheetModel.LaneHiddenRanges 提供，这里只做时间 → Y、轨道 → X 的映射；
    /// 偏好开关关闭时输出空列表。
    /// </summary>
    private void RefreshLaneDimRects()
    {
        var rects = new List<LaneDimRectItem>();
        var hidden = _chartSheet?.LaneHiddenRanges;
        if ((_userSettings?.IsHiddenLaneDimEnabled ?? true) &&
            hidden is { HasAny: true } && _userSettings != null && _viewHeight > 0)
        {
            int currentTime = _timeline?.TimeMs ?? 0;
            double pps = _userSettings.PixelPerSecond;
            double jlp = _userSettings.JudgmentLinePosition;
            var lanes = VisibleLanes;
            double laneWidth = LaneWidth;
            double laneSpacing = LaneSpacing;

            for (int i = 0; i < lanes.Count; i++)
            {
                var ranges = hidden.GetRanges(lanes[i]);
                if (ranges.Count == 0) continue;

                double x = i * (laneWidth + laneSpacing);
                foreach (var range in ranges)
                {
                    // 时间越大 Y 越小：区间起点在下方、终点在上方
                    double top = PositionCalculator.TimeToY(
                        range.EndTime, currentTime, pps, jlp, _viewHeight);
                    double bottom = PositionCalculator.TimeToY(
                        range.StartTime, currentTime, pps, jlp, _viewHeight);
                    top = Math.Max(0, top);
                    bottom = Math.Min(_viewHeight, bottom);
                    if (bottom <= top) continue;

                    rects.Add(new LaneDimRectItem
                    {
                        X = x,
                        Y = top,
                        Width = laneWidth,
                        Height = bottom - top
                    });
                }
            }
        }
        LaneDimRects = rects;
    }

    /// <summary> 生成高亮副本：比本体大 2px，偏移 -2,-2 </summary>
    private static NoteVisualItem MakeHighlight(NoteVisualItem src)
    {
        return new NoteVisualItem
        {
            Type = src.Type,
            IsHead = src.IsHead,
            IsTail = src.IsTail,
            IsHighlight = true,
            X = src.X - 2,
            Y = src.Y - 2,
            Width = src.Width + 4,
            Height = src.Height + 4,
            IsSelected = src.IsSelected
        };
    }

    /// <summary> Shared 数据类型 → 制谱器渲染类型（Tune 不在 NoteData.Type 中） </summary>
    private static NoteVisualType ToVisual(NoteType type) => type switch
    {
        NoteType.Dash => NoteVisualType.Dash,
        NoteType.Mute => NoteVisualType.Mute,
        _ => NoteVisualType.Dot
    };

    /// <summary> 添加 head NoteVisualItem（Dot/Dash/Mute/Tune 通用） </summary>
    private void AddHead(List<NoteVisualItem> items, object target, int timeMs, int laneIndex,
        NoteVisualType type, bool isSelected, double laneWidth, double laneSpacing,
        int currentTime, double pps, double jlp)
    {
        double y = PositionCalculator.TimeToY(timeMs, currentTime, pps, jlp, _viewHeight);
        double laneX = LaneToX(laneIndex, laneWidth, laneSpacing, VisibleLanes);

        // Tune 与 Mute 使用同款紧凑头型；方向只存在于数据中，不再影响形状
        bool isCompact = type is NoteVisualType.Mute or NoteVisualType.Tune;
        double headW = isCompact ? MuteHeadWidth : Math.Max(MinHeadOuterWidth, laneWidth - 6);
        double headH = isCompact ? MuteHeadHeight : RegularHeadHeight;

        items.Add(new NoteVisualItem
        {
            SelectionTarget = target,
            Type = type,
            IsHead = true,
            X = laneX + (laneWidth - headW) / 2,
            Y = y - headH / 2,
            Width = headW,
            Height = headH,
            IsSelected = isSelected
        });
    }

    /// <summary> 添加 body NoteVisualItem（Dash/Mute/Tune）；target 统一作为点击选中目标 </summary>
    private void AddBody(List<NoteVisualItem> items, object target, NoteData data,
        int currentTime, double pps, double jlp, NoteVisualType type, bool isSelected,
        double laneWidth, double laneSpacing)
    {
        double headY = PositionCalculator.TimeToY(data.TargetTime, currentTime, pps, jlp, _viewHeight);
        double endY  = PositionCalculator.TimeToY(data.TargetTime + data.SustainDuration, currentTime, pps, jlp, _viewHeight);
        double laneX = LaneToX(data.LaneIndex, laneWidth, laneSpacing, VisibleLanes);

        items.Add(new NoteVisualItem
        {
            SelectionTarget = target,
            Type = type,
            IsHead = false,
            X = laneX + (laneWidth - BodyWidth) / 2,
            Y = endY,
            Width = BodyWidth,
            Height = Math.Max(0, headY - endY),
            IsSelected = isSelected
        });
    }

    /// <summary>
    /// 添加尾部结束点横条（Dash/Mute 专用）：白色矩形，宽度与身体一致，
    /// 底端贴在身体顶端，高度等于头的描边宽度。独立成项以参与命中与选中高亮
    /// </summary>
    private void AddTail(List<NoteVisualItem> items, object target, NoteData data,
        int currentTime, double pps, double jlp, NoteVisualType type, bool isSelected,
        double laneWidth, double laneSpacing)
    {
        double endY = PositionCalculator.TimeToY(data.TargetTime + data.SustainDuration, currentTime, pps, jlp, _viewHeight);
        double laneX = LaneToX(data.LaneIndex, laneWidth, laneSpacing, VisibleLanes);

        items.Add(new NoteVisualItem
        {
            SelectionTarget = target,
            Type = type,
            IsHead = false,
            IsTail = true,
            X = laneX + (laneWidth - BodyWidth) / 2,
            Y = endY - HeadStroke,   // 画布 Y 向下：底端贴身体顶端（endY）
            Width = BodyWidth,
            Height = HeadStroke,
            IsSelected = isSelected
        });
    }

    /// <summary> 将 lane index 转换为 X 坐标（考虑轨道显隐） </summary>
    private static double LaneToX(int laneIndex, double laneWidth, double laneSpacing, List<int> visibleLanes)
    {
        int ordinal = visibleLanes.IndexOf(laneIndex);
        return ordinal >= 0 ? ordinal * (laneWidth + laneSpacing) : 0;
    }

    /// <summary>
    /// 返回最终显示顺序权重，越小越底层（先绘制）。
    /// 顺序：Dash身体高亮→Dash尾高亮→Dash身体→Dash尾→
    ///       Tune身体高亮→Tune身体→Mute身体高亮→Mute尾高亮→Mute身体→Mute尾→
    ///       Dash头高亮→Dash头→Dot头高亮→Dot头→Tune头高亮→Tune头→Mute头高亮→Mute头。
    /// 尾部本体压在身体之上、所有头之下（duration 极短时头优先命中）；
    /// 尾部高亮压在本体之下，避免外扩的蓝色盖进身体
    /// </summary>
    private static int GetZOrder(NoteVisualItem item)
    {
        if (item.IsTail)
        {
            // 尾部高亮必须在身体本体之下：高亮比尾部外扩 2px，会压到身体顶部，
            // 放到下面才能只露出两侧与顶部的蓝色描边
            return item.Type switch
            {
                NoteVisualType.Mute => item.IsHighlight ? 45 : 52,
                _                   => item.IsHighlight ? 5 : 12 // Dash tail
            };
        }

        if (!item.IsHead) // body
        {
            return item.Type switch
            {
                NoteVisualType.Mute => item.IsHighlight ? 40 : 50,
                NoteVisualType.Tune => item.IsHighlight ? 20 : 30,
                _                    => item.IsHighlight ? 0 : 10 // Dash body
            };
        }

        // head
        return item.Type switch
        {
            NoteVisualType.Dash => item.IsHighlight ? 60 : 70,
            NoteVisualType.Dot  => item.IsHighlight ? 80 : 90,
            NoteVisualType.Tune => item.IsHighlight ? 100 : 110,
            NoteVisualType.Mute => item.IsHighlight ? 120 : 130,
            _ => 0
        };
    }

}
