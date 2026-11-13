using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CQ_ChartMaker.Models;
using CQMusicGame.Shared;

namespace CQ_ChartMaker.ViewModels;

public partial class EventsViewModel : ObservableObject
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

    /// <summary> 点击选中回调（unit, 是否按住 Ctrl），EventsView 点击后通过此处通知 ChartVM </summary>
    public Action<object?, bool>? OnSelectUnit;

    /// <summary> 框选结果回调（框内 unit 列表, 是否追加），由 ChartVM 注入 </summary>
    public Action<List<object>, bool>? OnSelectMany;

    /// <summary> 双击列空白回调（列序号, 是否 Ctrl 追加），由 ChartVM 注入 </summary>
    public Action<int, bool>? OnSelectLane;

    /// <summary> 放置事件回调，由 ChartVM 注入 </summary>
    public Action<ILaneEventUnit>? OnPlaceEvent;

    /// <summary> 快捷删除请求（鼠标命中的 unit），由 ChartVM 注入 </summary>
    public Action<object>? OnDeleteUnitRequested;

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
                RefreshBeatLines();
            }
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
                _laneCatalog.VisibilityChanged -= OnLaneVisibilityChanged;
            }
            _laneCatalog = value;
            if (_laneCatalog != null)
            {
                _laneCatalog.Changed += OnLaneCatalogChanged;
                _laneCatalog.VisibilityChanged += OnLaneVisibilityChanged;
            }
            OnPropertyChanged(nameof(VisibleLanes));
            RebuildLaneColumns();
            RefreshBeatLines();
        }
    }

    private static readonly List<int> EmptyVisibleLanes = new();

    /// <summary> 可见轨道（升序） </summary>
    public List<int> VisibleLanes => _laneCatalog?.VisibleLanes ?? EmptyVisibleLanes;

    private void OnLaneCatalogChanged()
    {
        OnPropertyChanged(nameof(VisibleLanes));
        RebuildLaneColumns();
        RefreshBeatLines();
    }

    /// <summary> 轨道显隐变化：取消幽灵/待定/重定位，再重建列（选中由 ChartVM 过滤） </summary>
    private void OnLaneVisibilityChanged()
    {
        CancelPendingEdits();
        OnLaneCatalogChanged();
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
        set { _viewHeight = value; RefreshBeatLines(); }
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
    private List<MotionVisualItem> _motionVisualItems = new();

    /// <summary> 重叠提示标记：每个起点与同列其他 event 视觉重叠的 unit 一个 </summary>
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
    [ObservableProperty] private List<MotionOverlapPanelColumn> _overlapPanelColumns = new();
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

    /// <summary> 跳转提示：每列（轨道 / 事件类型 / 平铺列）一个，顺序与 LaneColumns 一致 </summary>
    public ObservableCollection<LaneJumpHintItem> JumpHints { get; } = new();

    /// <summary> 跳转回调，由 ChartVM 注入（统一处理暂停与时间 clamp） </summary>
    public Action<int>? OnJumpToTime;

    /// <summary> 点击轨道内的悬浮三角：跳转到目标时间 </summary>
    [RelayCommand]
    private void JumpToTime(int timeMs) => OnJumpToTime?.Invoke(timeMs);

    /// <summary> 完全隐藏轨道的区间变暗遮罩矩形（画布坐标，供 View 直接绘制） </summary>
    [ObservableProperty]
    private List<LaneDimRectItem> _laneDimRects = new();

    // 预览尺寸常量（与 Dot 头一致）
    private const double RegularHeadHeight = 14;
    private const double MinHeadOuterWidth = 18;
    private const double BodyWidth = 12;                 // 内矩形最小宽度（原身体宽度）
    private const double InnerWidthRatio = 0.8;          // 内宽 ≈ 旧头宽的 80%，随轨道宽缩放
    private const double OutlineThickness = 2;           // 左右描边厚度
    private const double CapThickness = RegularHeadHeight / 4 * 1.5;  // 头尾白带厚度（5.25）
    private const double ViewMarginPx = 10;

    // === 当前事件类型（Events 页 6 个逻辑分页） ===

    /// <summary>
    /// 当前 Events 页正在显示 / 编辑的事件类型。
    /// 顺序与 LaneEventKind 一致，键盘 ←→ 与后续页签 UI 都通过 ChartVM 修改它。
    /// </summary>
    [ObservableProperty]
    private LaneEventKind _currentEventKind = LaneEventKind.Transform;

    partial void OnCurrentEventKindChanged(LaneEventKind value)
    {
        _pending = false;
        _holding = false;
        CancelGhost();
        CancelRelocate();
        RebuildLaneColumns(); // 轨道模式下列几何不随类型变化，但列里存的类型需同步
        UpdatePreview();
        RefreshEvents();
    }

    // === 显示模式（轨道 / 类型 / 平铺） ===

    /// <summary>
    /// 当前显示模式（UI 名称）。轨道 = 列是可见轨道、只显示当前类型；
    /// 类型 = 列是 6 种事件类型、只显示当前轨道；
    /// 平铺 = 列是可见轨道 × 可见类型（先分轨道再分类型）。切换时保留选中，取消临时编辑状态。
    /// </summary>
    [ObservableProperty]
    private LaneDisplayMode _displayMode = LaneDisplayMode.ByType;

    /// <summary> 类型模式下的当前轨道（与显隐是两套独立记忆） </summary>
    [ObservableProperty]
    private int _currentLane;

    /// <summary> 画布平铺列（三种模式共用同一渲染结构，靠列内标签区分轨道/类型） </summary>
    [ObservableProperty]
    private List<LaneColumnItem> _laneColumns = new();

    // === 平铺模式的类型显隐（单独记忆，随谱面重置） ===

    private readonly HashSet<LaneEventKind> _hiddenKinds = new();

    /// <summary> 平铺模式下某类型是否可见 </summary>
    public bool IsKindVisible(LaneEventKind kind) => !_hiddenKinds.Contains(kind);

    /// <summary> 切换类型显隐：清该类型选中、取消临时状态并重建列（仅平铺模式使用） </summary>
    public void SetKindVisible(LaneEventKind kind, bool visible)
    {
        bool changed = visible ? _hiddenKinds.Remove(kind) : _hiddenKinds.Add(kind);
        if (!changed) return;

        CancelPendingEdits();
        RebuildLaneColumns();
        OnKindVisibilityChanged?.Invoke(kind, visible);
        KindVisibilityChanged?.Invoke();
        RefreshBeatLines();
        UpdatePreview();
    }

    /// <summary> 类型显隐变化回调，由 ChartVM 注入（清掉隐藏类型的选中） </summary>
    public Action<LaneEventKind, bool>? OnKindVisibilityChanged;

    /// <summary> 类型显隐变化通知，由 EditorVM 订阅（刷新类型栏按钮状态） </summary>
    public event Action? KindVisibilityChanged;

    /// <summary> 模式切换回调，由 ChartVM 注入（如刷新 Inspector 的轨道重定位按钮） </summary>
    public Action? OnDisplayModeSwitched;

    partial void OnDisplayModeChanged(LaneDisplayMode value)
    {
        // 进入类型模式前校验记忆的当前轨道是否仍存在（可能在轨道模式里被删除）
        if (value == LaneDisplayMode.ByLane && _laneCatalog != null &&
            !_laneCatalog.Contains(CurrentLane) && _laneCatalog.Lanes.Count > 0)
        {
            CurrentLane = _laneCatalog.Lanes[0];
        }

        IsPlacing = false; // 模式切换关闭放置工具，避免带着旧模式状态误放
        CancelPendingEdits();
        RebuildLaneColumns();
        OnDisplayModeSwitched?.Invoke();
        RefreshBeatLines();
        UpdatePreview();
    }

    partial void OnCurrentLaneChanged(int value)
    {
        CancelPendingEdits();
        RebuildLaneColumns(); // 类型模式的列里存了当前轨道，重建以同步
        RefreshBeatLines();
        UpdatePreview();
    }

    /// <summary> 切换显示模式 / 当前类型 / 当前轨道时取消待定放置、幽灵与重定位，选中保留 </summary>
    private void CancelPendingEdits()
    {
        _pending = false;
        _holding = false;
        CancelGhost();
        CancelRelocate();
    }

    // === 列几何（X 坐标在此统一计算，其他模块只读列号） ===

    private readonly List<double> _columnLefts = new();
    private readonly List<double> _columnHitLefts = new();
    private readonly List<double> _columnHitRights = new();
    private readonly List<int> _columnLanes = new();
    private readonly List<LaneEventKind> _columnKinds = new();

    /// <summary> 平铺模式下按枚举顺序列出的可见类型 </summary>
    private List<LaneEventKind> VisibleKindsInOrder()
    {
        var kinds = new List<LaneEventKind>(LaneEventKindInfo.Count);
        for (int i = 0; i < LaneEventKindInfo.Count; i++)
        {
            var kind = (LaneEventKind)i;
            if (IsKindVisible(kind)) kinds.Add(kind);
        }
        return kinds;
    }

    /// <summary>
    /// 重建平铺列几何：三种模式只有列内容和数量不同，坐标统一为 x += 列宽 + 间距。
    /// 轨道=每条可见轨道一列；类型=6 种类型列；平铺=轨道 × 类型（先分轨道再分类型）。
    /// </summary>
    private void RebuildLaneColumns()
    {
        _columnLefts.Clear();
        _columnHitLefts.Clear();
        _columnHitRights.Clear();
        _columnLanes.Clear();
        _columnKinds.Clear();

        double w = LaneWidth;
        double s = LaneSpacing;
        double x = 0;
        var columns = new List<LaneColumnItem>();

        if (DisplayMode == LaneDisplayMode.ByLane)
        {
            for (int i = 0; i < LaneEventKindInfo.Count; i++)
            {
                var kind = (LaneEventKind)i;
                columns.Add(new LaneColumnItem
                {
                    BottomLabel = LaneEventKindInfo.GetLabel(kind),
                    Width = w
                });
                AddColumn(CurrentLane, kind, x);
                x += w + s;
            }
        }
        else if (DisplayMode == LaneDisplayMode.ByType)
        {
            foreach (var lane in VisibleLanes)
            {
                columns.Add(new LaneColumnItem
                {
                    BottomLabel = lane.ToString(),
                    Width = w
                });
                AddColumn(lane, CurrentEventKind, x);
                x += w + s;
            }
        }
        else
        {
            var kinds = VisibleKindsInOrder();
            foreach (var lane in VisibleLanes)
            {
                foreach (var kind in kinds)
                {
                    columns.Add(new LaneColumnItem
                    {
                        TopLabel = lane.ToString(),                            // 上行：轨道号
                        BottomLabel = LaneEventKindInfo.GetLabel(kind),        // 下行：类型名
                        ShowTopLabel = true,
                        Width = w
                    });
                    AddColumn(lane, kind, x);
                    x += w + s;
                }
            }
        }

        LaneColumns = columns;
        BuildHitRanges(w);
    }

    private void AddColumn(int lane, LaneEventKind kind, double x)
    {
        _columnLefts.Add(x);
        _columnLanes.Add(lane);
        _columnKinds.Add(kind);
    }

    /// <summary> 每列的命中范围：列间空隙左右各分一半 </summary>
    private void BuildHitRanges(double w)
    {
        int n = _columnLefts.Count;
        for (int i = 0; i < n; i++)
        {
            double left = _columnLefts[i];
            double gapLeft = i > 0 ? left - (_columnLefts[i - 1] + w) : 0;
            double gapRight = i < n - 1 ? _columnLefts[i + 1] - (left + w) : 0;
            _columnHitLefts.Add(left - gapLeft / 2);
            _columnHitRights.Add(left + w + gapRight / 2);
        }
    }

    /// <summary> 当前模式的平铺列数 </summary>
    public int ColumnCount => _columnLefts.Count;

    /// <summary> 第 column 列的左边缘 X </summary>
    private double GetColumnLeft(int column) =>
        column >= 0 && column < _columnLefts.Count ? _columnLefts[column] : 0;

    /// <summary> 查 (轨道, 类型) 对应的平铺列号；不在当前列集合返回 -1 </summary>
    private int FindColumn(LaneEventKind kind, int laneIndex)
    {
        for (int i = 0; i < _columnLefts.Count; i++)
            if (_columnLanes[i] == laneIndex && _columnKinds[i] == kind)
                return i;
        return -1;
    }

    /// <summary> unit 在当前模式中的列序号；不属于当前界面返回 -1 </summary>
    public int GetColumnOfUnit(ILaneEventUnit unit)
    {
        if (DisplayMode == LaneDisplayMode.ByType)
        {
            if (unit.Kind != CurrentEventKind) return -1;
            return VisibleLanes.IndexOf(unit.LaneIndex);
        }

        if (DisplayMode == LaneDisplayMode.ByLane)
        {
            if (unit.LaneIndex != CurrentLane) return -1;
            return FindColumn(unit.Kind, unit.LaneIndex);
        }

        // 平铺：轨道与类型都必须可见
        if (!IsKindVisible(unit.Kind)) return -1;
        return FindColumn(unit.Kind, unit.LaneIndex);
    }

    /// <summary> unit 是否属于当前界面（复制/剪切/拖拽只对界面内选中项生效） </summary>
    public bool IsUnitInView(ILaneEventUnit unit) => GetColumnOfUnit(unit) >= 0;

    /// <summary> X 坐标 → 列序号（列间/组间空隙按左右各一半归属相邻列） </summary>
    public bool TryGetColumnAtX(double canvasX, out int column)
    {
        for (int i = 0; i < _columnLefts.Count; i++)
        {
            if (canvasX >= _columnHitLefts[i] && canvasX < _columnHitRights[i])
            {
                column = i;
                return true;
            }
        }
        column = -1;
        return false;
    }

    /// <summary> 放置目标 → 列左边缘 X </summary>
    private double GetPlacementX(LaneEventKind kind, int laneIndex,
        double laneWidth, double laneSpacing)
    {
        int column = FindColumn(kind, laneIndex);
        return GetColumnLeft(column);
    }

    /// <summary> 该类型是否带持续时间；Zone / Parent 为瞬时事件 </summary>
    private static bool KindHasDuration(LaneEventKind kind) =>
        kind is LaneEventKind.Transform or LaneEventKind.Opacity
             or LaneEventKind.Length or LaneEventKind.Speed;

    // === 放置逻辑 ===

    [ObservableProperty]
    private bool _isPlacing;

    /// <summary> 正在等待第二次点击定长（第一次点击定头） </summary>
    private bool _pending;

    /// <summary> 第一次点击的时间 </summary>
    private int _pendingTime;

    /// <summary> 第一次点击的轨道 </summary>
    private int _pendingLane;

    /// <summary> 第一次点击决定的事件类型（类型模式下由 X 决定；轨道模式恒为 CurrentEventKind） </summary>
    private LaneEventKind _pendingKind;

    /// <summary> 长按模式：左键已按下并进入拖动定长（松开结束） </summary>
    private bool _holding;

    /// <summary> 当前设置是否为长按放置长条 </summary>
    public bool IsHoldPlaceMode => _userSettings?.LongNotePlaceMode == LongNotePlaceMode.Hold;

    /// <summary> 常驻吸附提示：不处于放置状态时也显示吸附预览（偏好设置） </summary>
    public bool IsSnapPreviewEnabled => _userSettings?.IsSnapPreviewEnabled ?? false;

    /// <summary> 快捷放置：空白处单击放零时长事件、拖拽放有时长事件（偏好设置） </summary>
    public bool IsQuickPlaceEnabled => _userSettings?.IsQuickPlaceEnabled ?? false;

    /// <summary> 快捷删除：右键删除鼠标命中的 unit（偏好设置） </summary>
    public bool IsQuickDeleteEnabled => _userSettings?.IsQuickDeleteEnabled ?? false;

    /// <summary> 切换放置模式时清除待定状态，并立即重算预览 </summary>
    partial void OnIsPlacingChanged(bool value)
    {
        _pending = false;
        _holding = false;
        if (value)
        {
            CancelGhost();
            CancelRelocate();
        }
        UpdatePreview();
    }

    /// <summary>
    /// 在鼠标位置尝试放置事件。
    /// 四类有时长事件沿用两次单击 / 长按；Zone / Parent 单击即放置。
    /// </summary>
    public void TryPlace(double canvasX, double canvasY)
    {
        if (_chartSheet == null || _userSettings == null || _viewHeight <= 0) return;

        // 先按模式解析“放哪个类型、哪条轨道”：轨道=当前类型+鼠标所在轨道；
        // 类型=鼠标所在类型列+当前轨道
        if (!TryResolvePlacementAt(canvasX, out LaneEventKind kind, out int lane)) return;

        if (!KindHasDuration(kind))
        {
            int snapTime = SnapTimeAt(canvasY);
            OnPlaceEvent?.Invoke(CreateEventUnit(kind, lane, snapTime, 0));
            RefreshEvents();
            UpdatePreview();
            return;
        }

        // 长按模式由按下/松开事件接管
        if (IsHoldPlaceMode) return;

        if (!_pending)
            TryBeginPending(canvasX, canvasY);
        else
            CompletePending(canvasY);

        UpdatePreview();
    }

    /// <summary> 根据鼠标 X 解析放置目标（列里已存好该列的轨道与类型） </summary>
    private bool TryResolvePlacementAt(double canvasX, out LaneEventKind kind, out int laneIndex)
    {
        if (TryGetColumnAtX(canvasX, out int column))
        {
            laneIndex = _columnLanes[column];
            kind = _columnKinds[column];
            return true;
        }

        kind = default;
        laneIndex = 0;
        return false;
    }

    /// <summary> X 坐标 → 轨道（平铺模式只吸附到大的轨道组） </summary>
    public bool TryGetLaneAtX(double canvasX, out int laneIndex)
    {
        if (TryGetColumnAtX(canvasX, out int column))
        {
            laneIndex = _columnLanes[column];
            return true;
        }
        laneIndex = 0;
        return false;
    }

    /// <summary> 长按模式：按下左键，确定事件头并进入拖动状态 </summary>
    public bool BeginHoldPlace(double canvasX, double canvasY)
    {
        if (!IsHoldPlaceMode || _pending || !IsPlacing ||
            _chartSheet == null || _userSettings == null || _viewHeight <= 0)
            return false;

        if (!TryResolvePlacementAt(canvasX, out LaneEventKind kind, out _)) return false;
        if (!KindHasDuration(kind)) return false;
        if (!TryBeginPending(canvasX, canvasY)) return false;

        _holding = true;
        UpdatePreview();
        return true;
    }

    /// <summary> 长按模式：松开左键，按当前位置的吸附时间完成事件 </summary>
    public void EndHoldPlace(double canvasY)
    {
        if (!_holding) return;

        _holding = false;
        CompletePending(canvasY);
        UpdatePreview();
    }

    // === 快捷放置（偏好设置：有时长的类型拖拽定长，无时长的类型单击放置） ===

    /// <summary> 快捷拖动放置中：头已吸附确定，身体随鼠标拉伸，松开完成事件 </summary>
    private bool _quickHolding;

    /// <summary> 快捷拖动的头时间（ms） </summary>
    private int _quickHeadTime;

    /// <summary> 快捷拖动的头轨道 </summary>
    private int _quickHeadLane;

    /// <summary> 快捷拖动的事件类型 </summary>
    private LaneEventKind _quickKind;

    /// <summary>
    /// 快捷放置：空白处长按拖动起点（吸附定头），返回是否成功进入拖动定长。
    /// 只有带持续时间的类型支持拖拽；无持续时间类型返回 false，由单击路径放置
    /// </summary>
    public bool BeginQuickPlace(double canvasX, double canvasY)
    {
        if (!IsQuickPlaceEnabled) return false;
        if (_chartSheet == null || _userSettings == null || _viewHeight <= 0) return false;
        if (IsPlacing || IsGhostActive || IsRelocating || _pending || _holding) return false;
        if (!TryResolvePlacementAt(canvasX, out LaneEventKind kind, out int lane)) return false;
        if (!KindHasDuration(kind)) return false;

        _quickHolding  = true;
        _quickHeadTime = SnapTimeAt(canvasY);
        _quickHeadLane = lane;
        _quickKind     = kind;
        UpdatePreview();
        return true;
    }

    /// <summary> 快捷放置：松开左键，按当前 Y 的吸附时间完成事件（时长可负，与长按放置一致） </summary>
    public void EndQuickPlace(double canvasY)
    {
        if (!_quickHolding) return;

        _quickHolding = false;
        int duration = SnapTimeAt(canvasY) - _quickHeadTime;
        OnPlaceEvent?.Invoke(CreateEventUnit(_quickKind, _quickHeadLane, _quickHeadTime, duration));
        RefreshEvents();
        UpdatePreview();
    }

    /// <summary> 快捷放置：空白处单击，按吸附位置放一个零时长事件 </summary>
    public void QuickPlaceAt(double canvasX, double canvasY)
    {
        if (!IsQuickPlaceEnabled) return;
        if (_chartSheet == null || _userSettings == null || _viewHeight <= 0) return;
        if (IsPlacing || IsGhostActive || IsRelocating || _pending || _holding || _quickHolding)
            return;
        if (!TryResolvePlacementAt(canvasX, out LaneEventKind kind, out int lane)) return;

        OnPlaceEvent?.Invoke(CreateEventUnit(kind, lane, SnapTimeAt(canvasY), 0));
        RefreshEvents();
        UpdatePreview();
    }

    /// <summary> 取消快捷拖动放置（视图切换等）：丢弃未提交的头，不产生事件 </summary>
    public void CancelQuickPlace()
    {
        if (!_quickHolding) return;
        _quickHolding = false;
        UpdatePreview();
    }

    /// <summary> 第一次点击/按下：记录头时间和轨道 </summary>
    private bool TryBeginPending(double canvasX, double canvasY)
    {
        if (!TryResolvePlacementAt(canvasX, out LaneEventKind kind, out int lane))
            return false;

        _pending     = true;
        _pendingKind = kind;
        _pendingTime = SnapTimeAt(canvasY);
        _pendingLane = lane;
        return true;
    }

    /// <summary> 第二次点击/松开：头保持第一次记录的位置，长度 = 当前吸附时间 - 头时间 </summary>
    private void CompletePending(double canvasY)
    {
        if (!_pending) return;

        int snapTime = SnapTimeAt(canvasY);
        OnPlaceEvent?.Invoke(CreateEventUnit(
            _pendingKind, _pendingLane, _pendingTime, snapTime - _pendingTime));
        RefreshEvents();
        _pending = false;
    }

    /// <summary> 右键取消放置（同时退出放置模式） </summary>
    public void CancelPlacement()
    {
        _pending = false;
        _holding = false;
        IsPlacing = false;
    }

    /// <summary>
    /// 新建事件的唯一工厂入口：只有用户实际放置时才调用。
    /// Opacity / Length / Speed 的曲线默认 Absolute，From/To 取放置时刻该轨道的当前值，
    /// 空状态下的默认值由 LaneState 查询方法内置返回。
    /// </summary>
    private ILaneEventUnit CreateEventUnit(
        LaneEventKind kind, int laneIndex, int startTime, int duration)
    {
        // 现场构建 LaneState：struct 列表始终同步，ChartSheet.LaneState 是解析快照已过期。
        // 忽略父轨合成，取轨道本地值（新曲线存的就是本地值）。
        LaneState state = _chartSheet!.CreateLaneState(includeParents: false);

        return kind switch
        {
            LaneEventKind.Transform => new TransformEventUnit
                { Data = ChartDefaults.CreateLaneTransform(laneIndex, startTime, duration) },
            // 一条曲线同时作用于四个部位，按 Inspector 的优先级取第一个目标部位（默认全选时为 Head）
            LaneEventKind.Opacity => new OpacityEventUnit
                { Data = ChartDefaults.CreateLaneOpacity(laneIndex, startTime, duration,
                    state.HeadOpacity(laneIndex, startTime)) },
            LaneEventKind.Length => new LengthEventUnit
                { Data = ChartDefaults.CreateLaneLength(laneIndex, startTime, duration,
                    state.Length(laneIndex, startTime)) },
            LaneEventKind.Speed => CreateSpeedUnit(state, laneIndex, startTime, duration),
            LaneEventKind.Zone => new ZoneEventUnit
                { Data = ChartDefaults.CreateLaneZone(laneIndex, startTime) },
            LaneEventKind.Parent => new ParentEventUnit
                { Data = ChartDefaults.CreateLaneParent(laneIndex, startTime) },
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    /// <summary> 新建 Speed 事件：模式与数值都继承当前流速采样，放置后不跳变 </summary>
    private static ILaneEventUnit CreateSpeedUnit(
        LaneState state, int laneIndex, int startTime, int duration)
    {
        LaneSpeedSample speed = state.Speed(laneIndex, startTime);
        return new SpeedEventUnit
        {
            Data = ChartDefaults.CreateLaneSpeed(laneIndex, startTime, duration, speed.Mode, speed.Value)
        };
    }

    // === 重定位（Inspector 按钮触发） ===

    /// <summary> 重定位提交回调，由 ChartVM 注入（命令已执行） </summary>
    public Action<IUndoableCommand>? OnCommitRelocate;

    private ILaneEventUnit? _relocateUnit;
    private object? _relocateOld;
    private RelocateField _relocateField;

    /// <summary> 是否处于重定位模式 </summary>
    public bool IsRelocating => _relocateUnit != null;

    /// <summary> 进入重定位。Zone / Parent 不支持 Duration </summary>
    public void BeginRelocate(object unit, RelocateField field)
    {
        CancelPlacement();
        CancelGhost();

        if (unit is not ILaneEventUnit eventUnit) return;
        if (field == RelocateField.Duration && !eventUnit.HasDuration) return;
        // 类型模式 X 轴是类型列，不存在轨道重定位
        if (field == RelocateField.Lane && DisplayMode == LaneDisplayMode.ByLane) return;

        _relocateUnit = eventUnit;
        _relocateOld = eventUnit.CaptureData();
        _relocateField = field;
        UpdatePreview();
    }

    /// <summary> 退出重定位并隐藏预览 </summary>
    public void CancelRelocate()
    {
        if (_relocateUnit == null) return;
        _relocateUnit = null;
        _relocateOld = null;
        UpdatePreview();
    }

    /// <summary> 左键应用：按当前鼠标位置生成编辑命令并执行，交给 ChartVM 入撤销池 </summary>
    public void CommitRelocate(double canvasX, double canvasY)
    {
        if (_relocateUnit is not { } unit) return;
        _previewCanvasX = canvasX;
        _previewCanvasY = canvasY;
        _previewMouseOnCanvas = true;

        object oldData = _relocateOld!;

        // 类型模式兜底：不允许轨道重定位
        if (_relocateField == RelocateField.Lane && DisplayMode == LaneDisplayMode.ByLane)
        {
            CancelRelocate();
            return;
        }

        if (_relocateField == RelocateField.Lane)
        {
            // 按实际列几何反查轨道：平铺模式一条轨道占多列，不能用列号直接查轨道列表
            if (!TryGetLaneAtX(canvasX, out int lane))
                return;
            unit.LaneIndex = lane;
        }
        else if (_relocateField == RelocateField.Time)
        {
            unit.StartTime = SnapMouseTime();
        }
        else
        {
            if (!unit.HasDuration) { CancelRelocate(); return; }
            unit.Duration = SnapMouseTime() - unit.StartTime;
        }

        object newData = unit.CaptureData();
        if (Equals(newData, oldData)) { CancelRelocate(); return; }

        var command = new EditLaneEventCommand(unit, oldData, newData);
        CancelRelocate();
        OnCommitRelocate?.Invoke(command);
    }

    /// <summary> 生成重定位预览：时间 → 旧头预览；轨道 → 整块矩形；持续时间 → 只画内宽身体 </summary>
    private List<MotionVisualItem> BuildRelocatePreviewItems()
    {
        var items = new List<MotionVisualItem>();
        if (_chartSheet == null || _userSettings == null || _viewHeight <= 0 || !_previewMouseOnCanvas)
            return items;
        if (_relocateUnit is not { } unit) return items;

        int currentTime = _timeline?.TimeMs ?? 0;
        double pps = _userSettings.PixelPerSecond;
        double jlp = _userSettings.JudgmentLinePosition;
        double laneWidth = LaneWidth;
        double laneSpacing = LaneSpacing;

        if (_relocateField == RelocateField.Duration)
        {
            int duration = SnapMouseTime() - unit.StartTime;
            if (duration > 0)
            {
                AddBody(items, null, unit.StartTime, duration,
                    GetPlacementX(unit.Kind, unit.LaneIndex, laneWidth, laneSpacing), false,
                    laneWidth, currentTime, pps, jlp);
            }
            return items;
        }

        int laneIndex = unit.LaneIndex;
        int startTime = unit.StartTime;
        if (_relocateField == RelocateField.Lane)
        {
            // 类型模式禁用轨道重定位（按钮已隐藏，这里兜底）
            if (DisplayMode == LaneDisplayMode.ByLane) return items;
            // 与提交一致：按实际列几何反查轨道，平铺模式吸附到大的轨道组
            if (!TryGetLaneAtX(_previewCanvasX, out int lane))
                return items;
            laneIndex = lane;
        }
        else
        {
            startTime = SnapMouseTime();
        }

        double previewX = GetPlacementX(unit.Kind, laneIndex, laneWidth, laneSpacing);
        if (_relocateField == RelocateField.Lane)
        {
            // 轨道重定位：预览整块矩形（含描边与头尾白带）
            AddOutlinePreview(items, unit.Duration, startTime, previewX, laneWidth,
                currentTime, pps, jlp);
        }
        else
        {
            // starttime 重定位：保持旧头预览
            AddHead(items, null, startTime, previewX, false,
                laneWidth, currentTime, pps, jlp);
        }
        return items;
    }

    // === 起止点把手拖拽（拖动白带外侧区域改变 StartTime / Duration） ===

    /// <summary> 被拖拽把手的事件（null = 未处于把手拖拽） </summary>
    private ILaneEventUnit? _resizeUnit;

    /// <summary> 把手拖拽开始前的原始数据（松手时用于判断是否有变化） </summary>
    private object? _resizeOldData;

    /// <summary> true = 上方把手（改 Duration）；false = 下方把手（改 StartTime，终点不变） </summary>
    private bool _resizeIsEnd;

    /// <summary> 是否处于把手拖拽（View 据此判定拖拽被中途取消） </summary>
    public bool IsHandleResizing => _resizeUnit != null;

    /// <summary>
    /// 开始把手拖拽：只允许有时长的事件（Zone / Parent 返回 false）。
    /// 与幽灵/重定位/放置互斥；拖拽期间不改数据，源 unit 从渲染列表隐藏，松手时才提交
    /// </summary>
    public bool BeginHandleResize(ILaneEventUnit unit, bool isEnd)
    {
        if (_chartSheet == null || _userSettings == null || _viewHeight <= 0) return false;
        if (!IsDragMode) return false;
        if (!unit.HasDuration) return false;
        if (IsGhostActive || IsRelocating || IsPlacing) return false;

        _resizeUnit = unit;
        _resizeOldData = unit.CaptureData();
        _resizeIsEnd = isEnd;
        RefreshEvents(); // 源 unit 隐形，画面只剩预览
        UpdatePreview();
        return true;
    }

    /// <summary>
    /// 松开左键：按当前鼠标 Y 的吸附时间计算新的起点/时长并提交。
    /// 命令经 OnCommitRelocate 交给 ChartVM 入撤销池并统一刷新
    /// </summary>
    public void EndHandleResize(double canvasY)
    {
        if (_resizeUnit is not { } unit) return;

        object oldData = _resizeOldData!;
        ComputeHandleResize(SnapTimeAt(canvasY), out int newStart, out int newDuration);

        _resizeUnit = null;
        _resizeOldData = null;
        RefreshEvents();
        UpdatePreview();

        unit.StartTime = newStart;
        unit.Duration = newDuration;
        object newData = unit.CaptureData();
        if (Equals(newData, oldData)) return;

        OnCommitRelocate?.Invoke(new EditLaneEventCommand(unit, oldData, newData));
    }

    /// <summary> 取消把手拖拽（关闭拖拽偏好、切视图等）：不改数据，只还原显示 </summary>
    public void CancelHandleResize()
    {
        if (_resizeUnit == null) return;
        _resizeUnit = null;
        _resizeOldData = null;
        RefreshEvents();
        UpdatePreview();
    }

    /// <summary>
    /// 把手拖拽的最终时间（预览与提交共用）：
    /// 上方把手固定起点、终点跟随鼠标；下方把手固定终点、起点跟随鼠标。
    /// 两种拖拽都保证 Duration >= 0，越过对面时条塌缩到固定端
    /// </summary>
    private void ComputeHandleResize(int snapTime, out int start, out int duration)
    {
        var unit = _resizeUnit!;
        if (_resizeIsEnd)
        {
            start = unit.StartTime;
            duration = Math.Max(0, snapTime - start);
        }
        else
        {
            int end = unit.StartTime + Math.Max(0, unit.Duration);
            start = Math.Min(snapTime, end);
            duration = end - start;
        }
    }

    /// <summary>
    /// 把手拖拽预览：整块外框（含白带），几何与最终渲染一致。
    /// 源 unit 已在 RefreshEvents 中隐藏，拖拽时只显示这个半透明预览
    /// </summary>
    private List<MotionVisualItem> BuildHandleResizePreviewItems()
    {
        var items = new List<MotionVisualItem>();
        if (_resizeUnit is not { } unit || _chartSheet == null || _userSettings == null ||
            _viewHeight <= 0 || !_previewMouseOnCanvas)
            return items;

        ComputeHandleResize(SnapMouseTime(), out int start, out int duration);

        AddOutlinePreview(items, duration, start,
            GetPlacementX(unit.Kind, unit.LaneIndex, LaneWidth, LaneSpacing),
            LaneWidth, _timeline?.TimeMs ?? 0,
            _userSettings.PixelPerSecond, _userSettings.JudgmentLinePosition);
        return items;
    }

    // === 放置预览 ===

    /// <summary> 最近一次鼠标在 MotionCanvas 中的坐标（吸附计算的输入） </summary>
    private double _previewCanvasX;
    private double _previewCanvasY;

    /// <summary> 鼠标位置是否有效（离开画布后置 false，待定头仍保留） </summary>
    private bool _previewMouseOnCanvas;

    /// <summary> 当前预览的可视元素，View 统一渲染为半透明白色矩形 </summary>
    [ObservableProperty]
    private List<MotionVisualItem> _previewItems = new();

    /// <summary> 由 View 在 PointerMoved 时调用，更新鼠标画布坐标并重算预览 </summary>
    public void UpdatePreviewAt(double canvasX, double canvasY)
    {
        _previewCanvasX = canvasX;
        _previewCanvasY = canvasY;
        _previewMouseOnCanvas = true;
        UpdatePreview();
    }

    /// <summary> 鼠标离开画布时调用：隐藏跟随鼠标的部分，待定的固定头保留 </summary>
    public void HidePreview()
    {
        _previewMouseOnCanvas = false;
        UpdatePreview();
    }

    /// <summary> 重算预览。每次都换新 List 实例，靠 ObservableProperty 自动通知 View 重绘 </summary>
    private void UpdatePreview() => PreviewItems = BuildPreviewItems();

    /// <summary>
    /// 生成预览元素：
    /// 不在放置模式 → 无预览；
    /// 未点击第一次 → 预览头跟随鼠标吸附位置；
    /// 已点击第一次 → 头固定在第一次点击处，鼠标位置再追加 body（负时长不生成）。
    /// </summary>
    private List<MotionVisualItem> BuildPreviewItems()
    {
        var items = new List<MotionVisualItem>();

        if (IsRelocating)
            return BuildRelocatePreviewItems();

        // 起止点把手拖拽预览（与幽灵/重定位互斥）
        if (_resizeUnit != null)
            return BuildHandleResizePreviewItems();

        if (_ghostEntries != null)
        {
            if (!_previewMouseOnCanvas || _chartSheet == null || _userSettings == null || _viewHeight <= 0)
                return items;

            var layout = ComputeGhostLayout(_previewCanvasX, _previewCanvasY);
            if (layout == null) return items;

            int ghostCurrentTime = _timeline?.TimeMs ?? 0;
            double ghostPps = _userSettings.PixelPerSecond;
            double ghostJlp = _userSettings.JudgmentLinePosition;
            double ghostLaneWidth = LaneWidth;

            foreach (var item in layout)
            {
                double laneX = item.X;
                AddOutlinePreview(items, item.Unit.Duration, item.Time, laneX,
                    ghostLaneWidth, ghostCurrentTime, ghostPps, ghostJlp);
            }
            return items;
        }

        if (_chartSheet == null || _userSettings == null || _viewHeight <= 0)
            return items;

        // 快捷拖动中：头固定在起点，身体拉到鼠标吸附时间（优先于放置模式的早退）
        if (_quickHolding)
        {
            int quickCurrentTime = _timeline?.TimeMs ?? 0;
            double quickPps = _userSettings.PixelPerSecond;
            double quickJlp = _userSettings.JudgmentLinePosition;
            double quickLaneWidth = LaneWidth;
            double quickLaneSpacing = LaneSpacing;
            double quickX = GetPlacementX(_quickKind, _quickHeadLane, quickLaneWidth, quickLaneSpacing);

            AddHead(items, null, _quickHeadTime, quickX, false,
                quickLaneWidth, quickCurrentTime, quickPps, quickJlp);

            if (!_previewMouseOnCanvas) return items;

            int quickMouseTime = SnapMouseTime();
            if (quickMouseTime > _quickHeadTime)
            {
                AddBody(items, null, _quickHeadTime, quickMouseTime - _quickHeadTime, quickX, false,
                    quickLaneWidth, quickCurrentTime, quickPps, quickJlp);
            }
            return items;
        }

        // 常驻吸附提示：完全空闲时也用头显示吸附位置（偏好设置）。
        // 与重定位/幽灵/快捷拖动/放置模式互斥，因此不会与其它预览叠加
        if (!IsPlacing)
        {
            if (!IsSnapPreviewEnabled || !_previewMouseOnCanvas) return items;
            if (!TryResolvePlacementAt(_previewCanvasX, out LaneEventKind snapKind, out int snapLane))
                return items;

            AddHead(items, null, SnapMouseTime(),
                GetPlacementX(snapKind, snapLane, LaneWidth, LaneSpacing), false,
                LaneWidth, _timeline?.TimeMs ?? 0,
                _userSettings.PixelPerSecond, _userSettings.JudgmentLinePosition);
            return items;
        }

        int currentTime = _timeline?.TimeMs ?? 0;
        double pps = _userSettings.PixelPerSecond;
        double jlp = _userSettings.JudgmentLinePosition;
        double laneWidth = LaneWidth;
        double laneSpacing = LaneSpacing;

        if (_pending)
        {
            double pendingX = GetPlacementX(_pendingKind, _pendingLane, laneWidth, laneSpacing);
            AddHead(items, null, _pendingTime, pendingX, false,
                laneWidth, currentTime, pps, jlp);

            if (!_previewMouseOnCanvas) return items;

            int mouseTime = SnapMouseTime();
            if (KindHasDuration(_pendingKind) && mouseTime > _pendingTime)
            {
                AddBody(items, null, _pendingTime, mouseTime - _pendingTime, pendingX, false,
                    laneWidth, currentTime, pps, jlp);
            }
            return items;
        }

        if (!_previewMouseOnCanvas) return items;
        if (!TryResolvePlacementAt(_previewCanvasX, out LaneEventKind hoverKind, out int hoverLane))
            return items;

        AddHead(items, null, SnapMouseTime(),
            GetPlacementX(hoverKind, hoverLane, laneWidth, laneSpacing), false,
            laneWidth, currentTime, pps, jlp);
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

    ///// <summary> 将 lane index 转换为 X 坐标（考虑轨道显隐） </summary>
    //private static double LaneToX(int laneIndex, double laneWidth, double laneSpacing, List<int> visibleLanes)
    //{
    //    int ordinal = visibleLanes.IndexOf(laneIndex);
    //    return ordinal >= 0 ? ordinal * (laneWidth + laneSpacing) : 0;
    //}

    // === 批量剪贴/复制幽灵 ===

    private sealed class GhostEntry
    {
        public ILaneEventUnit Unit = null!;
        public int TimeOffset;
        public int OrdinalOffset;
    }

    private sealed class GhostLayoutItem
    {
        public ILaneEventUnit Unit = null!;
        public int Time;
        public double X;
        public int? Lane;
    }

    private List<GhostEntry>? _ghostEntries;
    private bool _ghostFlipped;
    private bool _ghostIsCopy;
    private bool _ghostIsDrag;
    private HashSet<ILaneEventUnit>? _dragHiddenUnits;
    private int _dragAnchorOrdinal;
    private double _dragHeadOffsetY;
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

    /// <summary> 幽灵模式是否激活 </summary>
    public bool IsGhostActive => _ghostEntries != null;

    /// <summary> 当前幽灵是否为复制模式 </summary>
    public bool GhostIsCopy => _ghostIsCopy;

    /// <summary> 是否处于拖拽幽灵 </summary>
    public bool IsDragActive => _ghostEntries != null && _ghostIsDrag;

    /// <summary> 幽灵放置提交回调（位置表, 是否复制），由 ChartVM 注入 </summary>
    public Action<List<LaneEventGhostPlacement>, bool>? OnCommitGhostPlacement;

    /// <summary> 开始拖拽回调（被抓住的基准 Unit, 按下点画布 X/Y），由 ChartVM 注入 </summary>
    public Action<ILaneEventUnit, double, double>? OnBeginDrag;

    /// <summary>
    /// 是否允许拖拽，由 EditorViewModel 根据用户设置统一下发（默认开启，偏好设置可禁用）。
    /// 关闭时若正在拖拽，立即取消并还原原对象。
    /// </summary>
    [ObservableProperty]
    private bool _isDragMode;

    partial void OnIsDragModeChanged(bool value)
    {
        if (value) return;
        if (IsDragActive) CancelGhost();
        CancelHandleResize();
    }

    /// <summary> 启动批量剪贴/复制幽灵 </summary>
    public void BeginGhost(List<ILaneEventUnit> units, bool isCopy)
        => BeginGhostCore(units, isCopy, null, false, 0, 0);

    /// <summary> 启动拖拽幽灵 </summary>
    public void BeginDragGhost(List<ILaneEventUnit> units, ILaneEventUnit anchor, double pressX, double pressY)
        => BeginGhostCore(units, false, anchor, true, pressX, pressY);

    private void BeginGhostCore(List<ILaneEventUnit> units, bool isCopy,
        ILaneEventUnit? anchorOverride, bool isDrag, double pressX, double pressY)
    {
        if (_chartSheet == null || _userSettings == null || _viewHeight <= 0 || units.Count == 0) return;

        CancelRelocate();
        CancelHandleResize();

        if (_dragHiddenUnits != null)
        {
            _dragHiddenUnits = null;
            RefreshEvents();
        }

        // 只对当前界面内的选中项生成幽灵；类型模式允许混合类型
        var visible = new List<ILaneEventUnit>();
        foreach (var unit in units)
            if (IsUnitInView(unit))
                visible.Add(unit);
        if (visible.Count == 0) return;

        var anchor = anchorOverride != null && visible.Contains(anchorOverride)
            ? anchorOverride
            : visible[0];
        if (!isDrag || anchor != anchorOverride)
        {
            foreach (var unit in visible)
            {
                if (unit.StartTime < anchor.StartTime ||
                    (unit.StartTime == anchor.StartTime && unit.LaneIndex < anchor.LaneIndex))
                    anchor = unit;
            }
        }

        // 轨道/平铺模式横向按轨道序号偏移；类型模式类型列固定，不需要横向偏移
        bool horizontal = DisplayMode != LaneDisplayMode.ByLane;
        int anchorOrdinal = horizontal ? VisibleLanes.IndexOf(anchor.LaneIndex) : 0;
        var entries = new List<GhostEntry>(visible.Count);
        foreach (var unit in visible)
        {
            entries.Add(new GhostEntry
            {
                Unit = unit,
                TimeOffset = unit.StartTime - anchor.StartTime,
                OrdinalOffset = horizontal
                    ? VisibleLanes.IndexOf(unit.LaneIndex) - anchorOrdinal
                    : 0
            });
        }

        _ghostEntries = entries;
        _ghostIsCopy = isCopy;
        _ghostFlipped = false;
        _ghostIsDrag = isDrag;
        _dragHiddenUnits = isDrag ? new HashSet<ILaneEventUnit>(visible) : null;

        if (isDrag)
        {
            _dragAnchorOrdinal = anchorOrdinal;
            _dragHeadOffsetY = PositionCalculator.TimeToY(anchor.StartTime,
                _timeline?.TimeMs ?? 0, _userSettings.PixelPerSecond,
                _userSettings.JudgmentLinePosition, _viewHeight) - pressY;

            _dragAnchorTime = anchor.StartTime; // Shift 锁定时间时的整组锚点

            // 类型模式禁止横向拖动；平铺模式吸附到轨道组
            _dragStartSnapOrdinal = horizontal && TryGetLaneAtX(pressX, out int startLane)
                ? Math.Max(0, VisibleLanes.IndexOf(startLane))
                : anchorOrdinal;
        }

        IsPlacing = false;
        if (isDrag) RefreshEvents();
        UpdatePreview();
    }

    /// <summary> 取消幽灵模式（把手拖拽同属临时拖拽状态，一并取消） </summary>
    public void CancelGhost()
    {
        CancelHandleResize();

        if (_ghostEntries == null) return;
        _ghostEntries = null;
        _ghostIsDrag = false;

        if (_dragHiddenUnits != null)
        {
            _dragHiddenUnits = null;
            RefreshEvents();
        }
        UpdatePreview();
    }

    /// <summary> F 键：切换左右镜像 </summary>
    public void ToggleGhostFlip()
    {
        // 类型模式禁止横向移动，镜像无意义
        if (DisplayMode == LaneDisplayMode.ByLane) return;
        if (_ghostEntries == null) return;
        _ghostFlipped = !_ghostFlipped;
        UpdatePreview();
    }

    /// <summary> 左键提交幽灵放置：越界项丢弃 </summary>
    public void TryCommitGhost(double canvasX, double canvasY)
    {
        if (_ghostEntries == null) return;

        var layout = ComputeGhostLayout(canvasX, canvasY);
        if (layout == null) return;

        var placements = new List<LaneEventGhostPlacement>();
        foreach (var item in layout)
        {
            if (item.Lane is not { } lane) continue;
            placements.Add(new LaneEventGhostPlacement { Unit = item.Unit, NewTime = item.Time, NewLane = lane });
        }
        if (placements.Count == 0) return;

        OnCommitGhostPlacement?.Invoke(placements, _ghostIsCopy);

        if (!_ghostIsCopy) CancelGhost();
    }

    /// <summary> 拖拽松手：位置有效则提交整组移动 </summary>
    public void EndDragGhost(double canvasX, double canvasY)
    {
        if (_ghostEntries == null || !_ghostIsDrag) return;

        var layout = ComputeGhostLayout(canvasX, canvasY);
        List<LaneEventGhostPlacement>? placements = null;
        if (layout != null)
        {
            placements = new List<LaneEventGhostPlacement>();
            foreach (var item in layout)
            {
                if (item.Lane is not { } lane) continue;
                placements.Add(new LaneEventGhostPlacement { Unit = item.Unit, NewTime = item.Time, NewLane = lane });
            }
            if (placements.Count == 0) placements = null;
        }

        _ghostEntries = null;
        _ghostIsDrag = false;
        _ghostFlipped = false;
        _dragHiddenUnits = null;
        RefreshEvents();
        UpdatePreview();

        if (placements != null)
            OnCommitGhostPlacement?.Invoke(placements, false);
    }

    private List<GhostLayoutItem>? ComputeGhostLayout(double canvasX, double canvasY)
    {
        if (_ghostEntries == null || _chartSheet == null || _userSettings == null || _viewHeight <= 0)
            return null;

        var lanes = VisibleLanes;
        int anchorOrdinal;
        int anchorTime;

        if (DisplayMode == LaneDisplayMode.ByLane)
        {
            // 类型模式：X 不参与定位，类型列固定，轨道固定为当前轨道。
            // Shift 按住时时间锁定为起拖原值（类型模式本就不能横移，等于完全冻结）
            anchorOrdinal = 0;
            anchorTime = _ghostIsDrag
                ? (_dragTimeLock ? _dragAnchorTime : SnapTimeAt(canvasY + _dragHeadOffsetY))
                : SnapTimeAt(canvasY);
        }
        else
        {
            // 轨道/平铺模式：X 只吸附轨道；平铺模式吸附到大的轨道组，不区分组内类型子列
            if (!TryGetLaneAtX(canvasX, out int hoverLane)) return null;
            int hoverOrdinal = lanes.IndexOf(hoverLane);
            if (hoverOrdinal < 0) return null;

            if (_ghostIsDrag)
            {
                anchorOrdinal = _dragAnchorOrdinal + (hoverOrdinal - _dragStartSnapOrdinal);
                // Shift 按住：时间锁定为起拖原值，只随鼠标横向换轨；
                // 松开后重新跟随鼠标吸附（中途按下/松开都会立即生效）
                anchorTime = _dragTimeLock
                    ? _dragAnchorTime
                    : SnapTimeAt(canvasY + _dragHeadOffsetY);
            }
            else
            {
                anchorOrdinal = hoverOrdinal;
                anchorTime = SnapTimeAt(canvasY);
            }

            if (anchorOrdinal < 0 || anchorOrdinal >= lanes.Count) return null;
        }

        var result = new List<GhostLayoutItem>(_ghostEntries.Count);
        foreach (var entry in _ghostEntries)
        {
            if (DisplayMode == LaneDisplayMode.ByLane)
            {
                result.Add(new GhostLayoutItem
                {
                    Unit = entry.Unit,
                    Time = anchorTime + entry.TimeOffset,
                    X = GetPlacementX(entry.Unit.Kind, CurrentLane, LaneWidth, LaneSpacing),
                    Lane = CurrentLane
                });
                continue;
            }

            int offset = _ghostFlipped ? -entry.OrdinalOffset : entry.OrdinalOffset;
            int ordinal = anchorOrdinal + offset;
            int? lane = ordinal >= 0 && ordinal < lanes.Count ? lanes[ordinal] : null;

            if (lane is not { } targetLane)
            {
                // 轨道模式保留越界幽灵的外推预览；平铺模式组外无法定位子列，直接跳过
                if (DisplayMode == LaneDisplayMode.Free) continue;
                result.Add(new GhostLayoutItem
                {
                    Unit = entry.Unit,
                    Time = anchorTime + entry.TimeOffset,
                    X = ordinal * (LaneWidth + LaneSpacing),
                    Lane = null
                });
                continue;
            }

            result.Add(new GhostLayoutItem
            {
                Unit = entry.Unit,
                Time = anchorTime + entry.TimeOffset,
                X = GetPlacementX(entry.Unit.Kind, targetLane, LaneWidth, LaneSpacing),
                Lane = targetLane
            });
        }
        return result;
    }

    /// <summary>
    /// 添加整块外框预览（幽灵 / 轨道重定位）：白色半透明矩形，尺寸取最大外框
    /// （内宽 + 两条描边，时长高 + 两条白带）。X 直接用可能越界的下标外推。
    /// </summary>
    private void AddOutlinePreview(List<MotionVisualItem> items, int duration, int ghostTime, double laneX,
        double laneWidth, int currentTime, double pps, double jlp)
    {
        double headY = PositionCalculator.TimeToY(ghostTime, currentTime, pps, jlp, _viewHeight);
        double endY = PositionCalculator.TimeToY(
            ghostTime + Math.Max(0, duration), currentTime, pps, jlp, _viewHeight);

        double outerW = GetInnerWidth(laneWidth) + OutlineThickness * 2;
        double outerH = Math.Max(0, headY - endY) + CapThickness * 2;

        items.Add(new MotionVisualItem
        {
            X = laneX + (laneWidth - outerW) / 2,
            Y = endY - CapThickness,
            Width = outerW,
            Height = outerH
        });
    }

    // === 框选 ===

    private int _boxAnchorTime;
    private double _boxAnchorX;
    private double _boxCurrentX;
    private double _boxCurrentY;
    private bool _boxSelecting;
    private bool _boxAppend;

    /// <summary> 开始框选 </summary>
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

    /// <summary> 拖动中更新活动角 </summary>
    public void UpdateBoxSelect(double canvasX, double canvasY)
    {
        if (!_boxSelecting) return;
        _boxCurrentX = canvasX;
        _boxCurrentY = canvasY;
        OnPropertyChanged(nameof(BoxSelectRect));
    }

    /// <summary> 松手：一次性做数据判定，把框内 unit 整批交给 ChartVM </summary>
    public void EndBoxSelect()
    {
        if (!_boxSelecting) return;
        _boxSelecting = false;
        OnPropertyChanged(nameof(BoxSelectRect));

        OnSelectMany?.Invoke(CollectBoxSelection(), _boxAppend);
    }

    /// <summary> 框选判定：只命中当前类型事件 </summary>
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

        double laneWidth = LaneWidth;

        foreach (var unit in _chartSheet.LaneEventUnitList)
        {
            int column = GetColumnOfUnit(unit);
            if (column < 0) continue;

            double laneX = GetColumnLeft(column);
            if (laneX > xMax || laneX + laneWidth < xMin) continue;

            int eventStart = unit.StartTime;
            int eventEnd = unit.StartTime + Math.Max(0, unit.Duration);
            if (eventEnd < timeMin || eventStart > timeMax) continue;

            hits.Add(unit);
        }
        return hits;
    }

    /// <summary> 框选矩形（画布坐标），null 表示不显示 </summary>
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

    /// <summary> 曲线相对值解析用的轨道状态（取轨道本地值）。随数据修改失效，仅存在相对曲线时才构建 </summary>
    private LaneState? _curveState;

    /// <summary> 对外暴露的刷新入口（选中变化、Inspector 编辑等外部触发时调用） </summary>
    public void RefreshPublic()
    {
        _curveState = null; // 事件数据可能已修改：丢弃相对值解析状态，绘制时按需重建
        RefreshBeatLines();
    }

    // === 时间驱动刷新门控 ===

    private bool _isActive;

    /// <summary>
    /// 是否为当前显示的子视图。时间驱动的刷新只对活动视图执行；
    /// 切回本视图时补一次刷新，保证暂停状态下画面立即恢复正确
    /// </summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;
            _isActive = value;
            if (value) OnTimeChanged();
        }
    }

    private void OnTimeChanged()
    {
        if (!_isActive) return;

        RefreshBeatLines();
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
            RebuildLaneColumns(); // 缩放改变列宽，列 X 表需要重算
        }
        else if (name == nameof(UserSettingsModel.LongNotePlaceMode))
        {
            _pending = false;
            _holding = false;
            UpdatePreview();
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
        int columnCount = ColumnCount;

        _beatLineSegments.Clear();
        _beatLineLabels.Clear();
        foreach (var beat in _beatInfos)
        {
            double y = PositionCalculator.TimeToY(beat.TimeMs, currentTime, pps, jlp, _viewHeight);
            for (int ci = 0; ci < columnCount; ci++)
            {
                _beatLineSegments.Add(new BeatLineSegmentItem { X = GetColumnLeft(ci), Y = y, Width = laneWidth });
            }
            _beatLineLabels.Add(new BeatLineLabelItem { Y = y - 7, Label = beat.BeatIndex.ToString() });
        }

        int subDenom = (int)_userSettings.Subdivision;
        _beatGrid.GetSubBeatsInRange(visibleStartMs, visibleEndMs, subDenom, _subBeatInfos);
        _subBeatLineSegments.Clear();
        foreach (var sub in _subBeatInfos)
        {
            double y = PositionCalculator.TimeToY(sub.TimeMs, currentTime, pps, jlp, _viewHeight);
            for (int ci = 0; ci < columnCount; ci++)
            {
                _subBeatLineSegments.Add(new BeatLineSegmentItem { X = GetColumnLeft(ci) + 2, Y = y, Width = laneWidth - 4 });
            }
        }
        OnPropertyChanged(nameof(BeatLineSegments));
        OnPropertyChanged(nameof(BeatLineLabels));
        OnPropertyChanged(nameof(SubBeatLineSegments));

        RefreshLaneDimRects();
        RefreshEvents();
    }

    /// <summary>
    /// 生成"完全隐藏轨道"区间的变暗遮罩矩形（画布坐标）。
    /// 每列按自己所属轨道取区间：轨道模式（列为轨道）按列轨道；类型模式（6 类型列）
    /// 全部属于当前轨道；平铺模式按列的（轨道, 类型）。偏好开关关闭时输出空列表。
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
            double laneWidth = LaneWidth;
            int columnCount = ColumnCount;

            for (int ci = 0; ci < columnCount; ci++)
            {
                var ranges = hidden.GetRanges(_columnLanes[ci]);
                if (ranges.Count == 0) continue;

                double x = GetColumnLeft(ci);
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

    /// <summary> 遍历当前类型的事件 Unit，生成 MotionVisualItems </summary>
    private void RefreshEvents()
    {
        if (_chartSheet == null || _userSettings == null || _viewHeight <= 0) return;

        // 任何外部刷新（播放/滚轮/选择/缩放/模式切换等）都关闭展开条；内部刷新除外
        if (IsOverlapPanelOpen && !_suppressOverlapPanelAutoClose)
            ClearOverlapPanelState();

        int currentTime = _timeline?.TimeMs ?? 0;
        double pps = _userSettings.PixelPerSecond;
        double jlp = _userSettings.JudgmentLinePosition;

        int visibleEndMs   = PositionCalculator.YToTime(-ViewMarginPx, currentTime, pps, jlp, _viewHeight);
        int visibleStartMs = PositionCalculator.YToTime(_viewHeight + ViewMarginPx, currentTime, pps, jlp, _viewHeight);

        double laneWidth = LaneWidth;

        var allItems = new List<MotionVisualItem>();

        // 重叠提示：容差按当前 PPS 从像素折算成毫秒；列号直接复用 GetColumnOfUnit，
        // 三种显示模式的“同列”语义自动正确
        int overlapToleranceMs = OverlapAnalyzer.PxToMs(OverlapAnalyzer.TolerancePx, pps);
        int overlapStartMs = visibleStartMs - overlapToleranceMs;
        int overlapEndMs   = visibleEndMs + overlapToleranceMs;
        var overlapEntries = new List<OverlapEntry>();

        int unitOrder = 0;
        foreach (var unit in _chartSheet.LaneEventUnitList)
        {
            int column = GetColumnOfUnit(unit);
            if (column < 0) continue;
            if (_dragHiddenUnits?.Contains(unit) == true) continue;
            if (unit == _resizeUnit) continue;

            // 先于时间可见性过滤收集：视口外最多容差距离的 unit 也可能与视口内 unit 同组
            if (unit.StartTime >= overlapStartMs && unit.StartTime <= overlapEndMs)
            {
                overlapEntries.Add(new OverlapEntry
                {
                    Unit = unit,
                    Column = column,
                    StartTime = unit.StartTime
                });
            }

            // 展开条里的其他 unit 只从轨道绘制中隐藏，仍参与重叠分组（否则感叹号/展开会失效）
            if (_overlapPanelHiddenUnits.Contains(unit)) continue;

            int eventEnd = unit.StartTime + Math.Max(0, unit.Duration);
            if (eventEnd < visibleStartMs || unit.StartTime > visibleEndMs) continue;

            var item = CreateEventItem(unit, GetColumnLeft(column), unit.IsSelected,
                laneWidth, currentTime, pps, jlp, unitOrder);

            // 高亮与本体作为一个整体紧挨着：startTime 大的单位后画时会一起盖住
            allItems.Add(MakeMotionHighlight(item));
            allItems.Add(item);
            unitOrder++;
        }

        allItems.Sort(CompareVisualItems);

        _motionVisualItems.Clear();
        _motionVisualItems.AddRange(allItems);
        OnPropertyChanged(nameof(MotionVisualItems));

        OverlapMarkers = BuildOverlapMarkers(
            overlapEntries, overlapToleranceMs, laneWidth, currentTime, pps, jlp);

        RefreshJumpHints();
    }

    /// <summary>
    /// 把重叠组展开成绘制标记：每个重叠 unit 一个，水平居中于所在显示列，Y 对齐自己的起始时间。
    /// 组内成员起始时间可能相差几毫秒，所以逐个成员画，而不是每组画一个。
    /// </summary>
    private List<OverlapMarkerItem> BuildOverlapMarkers(
        List<OverlapEntry> entries, int toleranceMs, double laneWidth,
        int currentTime, double pps, double jlp)
    {
        var markers = new List<OverlapMarkerItem>();
        _overlapByUnit.Clear();
        if (entries.Count < 2) return markers;

        foreach (var group in OverlapAnalyzer.Build(entries, toleranceMs))
        {
            foreach (var member in group.Members)
                _overlapByUnit[member.Unit] = group;

            double x = GetColumnLeft(group.Column) + laneWidth / 2;
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
    /// 打开某个重叠 Event 的横向展开条：同组其余 unit 各占一列，按列宽重新生成可视项（含内部曲线）。
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

        var columns = new List<MotionOverlapPanelColumn>();
        double minY = double.MaxValue;
        double maxY = double.MinValue;

        // 高度范围包含被悬浮的 unit 自身（duration 参与）
        foreach (var member in group.Members)
        {
            if (member.Unit is not ILaneEventUnit eu) continue;
            var item = CreateEventItem(eu, 0, false, columnWidth, currentTime, pps, jlp, 0);
            ExpandYBounds(item, ref minY, ref maxY);

            if (!ReferenceEquals(member.Unit, unit))
                columns.Add(new MotionOverlapPanelColumn { Unit = eu, Items = new List<MotionVisualItem> { item } });
        }

        if (columns.Count == 0 || minY > maxY) return false;

        _overlapPanelHiddenUnits.Clear();
        foreach (var column in columns)
            _overlapPanelHiddenUnits.Add(column.Unit);

        OverlapPanelAnchorUnit = unit;
        OverlapPanelAnchorLeft = GetColumnLeft(group.Column);
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
        try { RefreshEvents(); }
        finally { _suppressOverlapPanelAutoClose = false; }
        return true;
    }

    /// <summary> 关闭展开条并恢复被隐藏的 unit </summary>
    public void CloseOverlapPanel()
    {
        if (!IsOverlapPanelOpen) return;
        ClearOverlapPanelState();
        RefreshEvents();
    }

    private void ClearOverlapPanelState()
    {
        IsOverlapPanelOpen = false;
        OverlapPanelColumns = new List<MotionOverlapPanelColumn>();
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

    /// <summary> 把一个事件可视项的 Y 范围并入 minY/maxY </summary>
    private static void ExpandYBounds(MotionVisualItem item, ref double minY, ref double maxY)
    {
        if (item.Y < minY) minY = item.Y;
        if (item.Y + item.Height > maxY) maxY = item.Y + item.Height;
    }

    /// <summary>
    /// 重算每列的跳转提示：画面内无 unit 时给出上/下最近 unit 的时间。
    /// 列号直接复用 GetColumnOfUnit，三种显示模式（轨道 / 类型 / 平铺）语义自动一致。
    /// 可见范围与 RefreshEvents 使用同一套 ViewMarginPx 边距。
    /// </summary>
    private void RefreshJumpHints()
    {
        if (_chartSheet == null || _userSettings == null || _viewHeight <= 0) return;

        int count = ColumnCount;
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

        foreach (var unit in _chartSheet.LaneEventUnitList)
        {
            if (_dragHiddenUnits?.Contains(unit) == true) continue;
            if (unit == _resizeUnit) continue;
            int column = GetColumnOfUnit(unit);
            if (column < 0) continue;

            int start = unit.StartTime;
            int end = start + Math.Max(0, unit.Duration);

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

    /// <summary> 生成高亮副本：比本体大 2px，偏移 -2,-2；排序键跟随本体，保证两者紧挨着 </summary>
    private static MotionVisualItem MakeMotionHighlight(MotionVisualItem src)
    {
        return new MotionVisualItem
        {
            IsHighlight = true,
            X = src.X - 2,
            Y = src.Y - 2,
            Width = src.Width + 4,
            Height = src.Height + 4,
            IsSelected = src.IsSelected,
            SortTime = src.SortTime,
            UnitOrder = src.UnitOrder
        };
    }

    /// <summary> 绘制顺序：startTime 大的后画（盖住小的）；同一时间保持原列表顺序；高亮贴在本体下面 </summary>
    private static int CompareVisualItems(MotionVisualItem a, MotionVisualItem b)
    {
        int c = a.SortTime.CompareTo(b.SortTime);
        if (c != 0) return c;
        c = a.UnitOrder.CompareTo(b.UnitOrder);
        if (c != 0) return c;
        if (a.IsHighlight != b.IsHighlight) return a.IsHighlight ? -1 : 1;
        return 0;
    }

    /// <summary>
    /// 生成一个事件矩形：外框含左右 2px 描边与上下 3.5px 白带，
    /// 灰色内矩形高度 = duration；duration 为 0 时内矩形消失，只剩 7px 白条。
    /// </summary>
    private MotionVisualItem CreateEventItem(ILaneEventUnit unit, double laneX, bool isSelected,
        double laneWidth, int currentTime, double pps, double jlp, int unitOrder)
    {
        double headY = PositionCalculator.TimeToY(unit.StartTime, currentTime, pps, jlp, _viewHeight);
        double endY = PositionCalculator.TimeToY(
            unit.StartTime + Math.Max(0, unit.Duration), currentTime, pps, jlp, _viewHeight);

        double outerW = GetInnerWidth(laneWidth) + OutlineThickness * 2;
        double outerH = Math.Max(0, headY - endY) + CapThickness * 2;

        return new MotionVisualItem
        {
            SelectionTarget = unit,
            X = laneX + (laneWidth - outerW) / 2,
            Y = endY - CapThickness,
            Width = outerW,
            Height = outerH,
            IsSelected = isSelected,
            SortTime = unit.StartTime,
            UnitOrder = unitOrder,
            Curves = BuildCurveVisuals(unit)
        };
    }

    // === 事件内数值曲线 ===

    // 参考范围：左边缘值与右边缘值（中点 = 两者平均）
    private const float XRange = 800f;            // Transform X：-800 ~ 800
    private const float YRange = 450f;            // Transform Y：-450 ~ 450
    private const float RotationRange = 360f;     // Transform 旋转：-360 ~ 360
    private const float LengthMax = 1600f;        // Length：0 ~ 1600
    private const float SpeedFixedMax = 2000f;    // Speed Fixed：0 ~ 2000
    private const float SpeedMultiplierMax = 2f;  // Speed Multiplier：0 ~ 2

    /// <summary> 相对值解析需要读取的轨道通道 </summary>
    private enum CurveChannel { X, Y, Rotation, Opacity, Length, Speed }

    /// <summary>
    /// 生成事件矩形内要显示的数值曲线（0~3 条）。
    /// 返回 null 表示没有可画的曲线：Zone / Parent 无曲线、时长为 0、Transform 三条曲线全为空
    /// </summary>
    private CurveVisualItem[]? BuildCurveVisuals(ILaneEventUnit unit)
    {
        if (unit.Duration <= 0) return null;

        switch (unit)
        {
            case TransformEventUnit t:
            {
                var d = t.Data;
                int count = (d.XCurve.HasValue ? 1 : 0)
                          + (d.YCurve.HasValue ? 1 : 0)
                          + (d.RotationCurve.HasValue ? 1 : 0);
                if (count == 0) return null;

                var curves = new CurveVisualItem[count];
                int i = 0;
                if (d.XCurve is { } x)
                    curves[i++] = MakeCurveVisual(unit, x, CurveChannel.X, -XRange, XRange);
                if (d.YCurve is { } y)
                    curves[i++] = MakeCurveVisual(unit, y, CurveChannel.Y, -YRange, YRange);
                if (d.RotationCurve is { } r)
                    curves[i++] = MakeCurveVisual(
                        unit, r, CurveChannel.Rotation, -RotationRange, RotationRange);
                return curves;
            }

            case OpacityEventUnit o:
                return new[] { MakeCurveVisual(unit, o.Data.ValueCurve, CurveChannel.Opacity, 0f, 1f) };

            case LengthEventUnit l:
                return new[]
                    { MakeCurveVisual(unit, l.Data.ValueCurve, CurveChannel.Length, 0f, LengthMax) };

            case SpeedEventUnit s:
            {
                // 参考范围随流速模式切换：Fixed 是绝对流速，Multiplier 是倍率
                float max = s.Data.SpeedMode == LaneSpeedMode.Fixed
                    ? SpeedFixedMax
                    : SpeedMultiplierMax;
                return new[] { MakeCurveVisual(unit, s.Data.ValueCurve, CurveChannel.Speed, 0f, max) };
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// 单条曲线 → 绘制参数。相对值按游戏 LaneState 的语义解析：
    /// From 相对 = 事件起点处该通道的当前值 + From，To 相对 = 已解析起点 + To
    /// </summary>
    private CurveVisualItem MakeCurveVisual(ILaneEventUnit unit, in ChannelCurve curve,
        CurveChannel channel, float min, float max)
    {
        float start = curve.FromMode == ValueMode.Absolute
            ? curve.From
            : SampleLaneChannel(unit, channel) + curve.From;
        float end = curve.ToMode == ValueMode.Absolute ? curve.To : start + curve.To;

        return new CurveVisualItem
        {
            StartValue = start,
            EndValue = end,
            MinValue = min,
            MaxValue = max,
            Ease = curve.Ease
        };
    }

    /// <summary> 读取轨道在事件起点处的本地通道值（忽略父轨合成，与曲线存储语义一致） </summary>
    private float SampleLaneChannel(ILaneEventUnit unit, CurveChannel channel)
    {
        // 惰性构建：谱面里没有相对曲线时不会走到这里，LaneState 的构建成本为零
        _curveState ??= _chartSheet!.CreateLaneState(includeParents: false);

        int lane = unit.LaneIndex;
        int time = unit.StartTime;
        return channel switch
        {
            CurveChannel.X => _curveState.GetFrame(lane, time).Position.x,
            CurveChannel.Y => _curveState.GetFrame(lane, time).Position.y,
            CurveChannel.Rotation => _curveState.GetFrame(lane, time).Rotation,
            // 一条 Opacity 曲线可作用于多个部位，取 Inspector 约定的第一个目标部位（默认全选时为 Head）
            CurveChannel.Opacity => _curveState.HeadOpacity(lane, time),
            CurveChannel.Length => _curveState.Length(lane, time),
            CurveChannel.Speed => _curveState.Speed(lane, time).Value,
            _ => 0f
        };
    }

    /// <summary> 旧头外宽：放置预览头仍沿用该尺寸 </summary>
    private static double GetHeadWidth(double laneWidth) =>
        Math.Max(MinHeadOuterWidth, laneWidth - 6);

    /// <summary> 内矩形宽度：随轨道宽缩放，最小为原身体宽度 </summary>
    private static double GetInnerWidth(double laneWidth) =>
        Math.Max(BodyWidth, GetHeadWidth(laneWidth) * InnerWidthRatio);

    /// <summary> 添加放置预览头（沿用旧头尺寸） </summary>
    private void AddHead(List<MotionVisualItem> items, object? selectionTarget,
        int startTime, double laneX, bool isSelected,
        double laneWidth, int currentTime, double pps, double jlp)
    {
        double y = PositionCalculator.TimeToY(startTime, currentTime, pps, jlp, _viewHeight);

        double headW = GetHeadWidth(laneWidth);
        double headH = RegularHeadHeight;

        items.Add(new MotionVisualItem
        {
            SelectionTarget = selectionTarget,
            X = laneX + (laneWidth - headW) / 2,
            Y = y - headH / 2,
            Width = headW,
            Height = headH,
            IsSelected = isSelected
        });
    }

    /// <summary> 添加放置预览身体：宽度为内矩形宽，不含描边与白带 </summary>
    private void AddBody(List<MotionVisualItem> items, object? selectionTarget,
        int startTime, int duration, double laneX, bool isSelected,
        double laneWidth, int currentTime, double pps, double jlp)
    {
        double headY = PositionCalculator.TimeToY(startTime, currentTime, pps, jlp, _viewHeight);
        double endY  = PositionCalculator.TimeToY(startTime + duration, currentTime, pps, jlp, _viewHeight);

        double bodyW = GetInnerWidth(laneWidth);

        items.Add(new MotionVisualItem
        {
            SelectionTarget = selectionTarget,
            X = laneX + (laneWidth - bodyW) / 2,
            Y = endY,
            Width = bodyW,
            Height = Math.Max(0, headY - endY),
            IsSelected = isSelected
        });
    }
}
