using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CQ_ChartMaker.Models;
using CQMusicGame.Shared;

namespace CQ_ChartMaker.ViewModels;

public partial class BeatsViewModel : ObservableObject
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

    /// <summary> 点击选中回调（unit, 是否按住 Ctrl），BeatsView 点击节点后通过此处通知 ChartVM </summary>
    public System.Action<object?, bool>? OnSelectUnit;

    /// <summary> 框选结果回调（框内 unit 列表, 是否追加），由 ChartVM 注入 </summary>
    public System.Action<List<object>, bool>? OnSelectMany;

    /// <summary> 双击轨道空白全选 BPM 回调（是否 Ctrl 追加），由 ChartVM 注入 </summary>
    public System.Action<bool>? OnSelectAll;

    /// <summary> 放置 BPM 回调，由 ChartVM 注入 </summary>
    public System.Action<BpmUnit>? OnPlaceBpm;

    /// <summary> 快捷删除请求（鼠标命中的 unit），由 ChartVM 注入 </summary>
    public System.Action<object>? OnDeleteUnitRequested;

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

    /// <summary> 唯一一条轨道，固定下标 0，不跟随 UserSettings 的轨道显隐 </summary>
    [ObservableProperty]
    private List<int> _visibleLanes = new() { 0 };

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
    private List<BpmVisualItem> _bpmVisualItems = new();

    /// <summary> 重叠提示标记：每个起点与其他 BPM 视觉重叠的 unit 一个 </summary>
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
    [ObservableProperty] private List<BpmOverlapPanelColumn> _overlapPanelColumns = new();
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

    /// <summary> 跳转提示：单列 BPM 面板固定一个 </summary>
    public ObservableCollection<LaneJumpHintItem> JumpHints { get; } = new();

    /// <summary> 跳转回调，由 ChartVM 注入（统一处理暂停与时间 clamp） </summary>
    public Action<int>? OnJumpToTime;

    /// <summary> 点击轨道内的悬浮三角：跳转到目标时间 </summary>
    [RelayCommand]
    private void JumpToTime(int timeMs) => OnJumpToTime?.Invoke(timeMs);

    // BPM 节点尺寸常量（与 Dot 头一致）
    private const double RegularHeadHeight = 14;
    private const double MinHeadOuterWidth = 18;
    private const double ViewMarginPx = 10;

    // === 放置逻辑 ===

    /// <summary> 当前是否处于放置模式（右侧工具栏 + 按钮控制） </summary>
    [ObservableProperty]
    private bool _isPlacing;

    /// <summary> 切换放置模式时重算预览（进入/退出立即显示/隐藏） </summary>
    partial void OnIsPlacingChanged(bool value)
    {
        if (value)
        {
            CancelGhost();    // 放置与幽灵互斥：进入放置模式退出幽灵
            CancelRelocate(); // 放置与重定位同样互斥
        }
        UpdatePreview();
    }

    /// <summary>
    /// 在鼠标位置尝试放置 BPM 节点。X 需在单条轨道范围内，时间吸附网格。
    /// </summary>
    public void TryPlace(double canvasX, double canvasY)
    {
        if (_chartSheet == null || _userSettings == null || _viewHeight <= 0) return;

        int currentTime = _timeline?.TimeMs ?? 0;
        double pps = _userSettings.PixelPerSecond;
        double jlp = _userSettings.JudgmentLinePosition;

        // X 不在轨道范围内不放置
        if (!PositionCalculator.TryXSnapToLane(canvasX, LaneWidth, LaneSpacing, VisibleLanes, out _))
            return;

        // 反推时间 → 吸附网格
        int time = PositionCalculator.YToTime((int)canvasY, currentTime, pps, jlp, _viewHeight);
        var bpmList = _chartSheet.ChartSheet.GetBpmList();
        int snapTime = PositionCalculator.TimeSnapToGrid(time, bpmList, _userSettings.Subdivision);

        // 新节点的 BPM 初值由 ChartDefaults 统一规定，由用户随后在 Inspector 中编辑
        var bpm = new BpmUnit
        {
            Data = ChartDefaults.CreateBpm(snapTime)
        };
        OnPlaceBpm?.Invoke(bpm);
        RefreshBeatLines();

        // 放置后预览继续跟随鼠标（同 Dot 单击放置）
        UpdatePreview();
    }

    /// <summary> 右键取消放置 </summary>
    public void CancelPlacement()
    {
        IsPlacing = false;
    }

    /// <summary> 常驻吸附提示：不处于放置状态时也显示吸附预览（偏好设置） </summary>
    public bool IsSnapPreviewEnabled => _userSettings?.IsSnapPreviewEnabled ?? false;

    /// <summary> 快捷放置：空白处单击直接放 BPM（偏好设置） </summary>
    public bool IsQuickPlaceEnabled => _userSettings?.IsQuickPlaceEnabled ?? false;

    /// <summary> 快捷删除：右键删除鼠标命中的 unit（偏好设置） </summary>
    public bool IsQuickDeleteEnabled => _userSettings?.IsQuickDeleteEnabled ?? false;

    /// <summary> 快捷放置：空白处单击，按吸附位置放一个 BPM 节点 </summary>
    public void QuickPlaceBpm(double canvasX, double canvasY)
    {
        if (!IsQuickPlaceEnabled) return;
        if (_chartSheet == null || _userSettings == null || _viewHeight <= 0) return;
        if (IsPlacing || IsGhostActive || IsRelocating) return;

        if (!PositionCalculator.TryXSnapToLane(
                canvasX, LaneWidth, LaneSpacing, VisibleLanes, out _))
            return;

        var bpm = new BpmUnit
        {
            Data = ChartDefaults.CreateBpm(SnapTimeAt(canvasY))
        };
        OnPlaceBpm?.Invoke(bpm);
        RefreshBeatLines();
        UpdatePreview();
    }

    // === 重定位（Inspector 按钮触发：只支持时间） ===

    /// <summary> 重定位提交回调，由 ChartVM 注入（命令已执行） </summary>
    public System.Action<IUndoableCommand>? OnCommitRelocate;

    private BpmUnit? _relocateBpm;
    private BpmData _relocateOld;

    /// <summary> 是否处于重定位模式 </summary>
    public bool IsRelocating => _relocateBpm != null;

    /// <summary> 进入重定位（BPM 只支持时间，X 固定） </summary>
    public void BeginRelocate(object unit, RelocateField field)
    {
        CancelPlacement();
        CancelGhost();

        if (unit is not BpmUnit bpm || field != RelocateField.Time) return;
        _relocateBpm = bpm;
        _relocateOld = bpm.Data;
        UpdatePreview();
    }

    /// <summary> 退出重定位并隐藏预览 </summary>
    public void CancelRelocate()
    {
        if (_relocateBpm == null) return;
        _relocateBpm = null;
        UpdatePreview();
    }

    /// <summary> 左键应用：按鼠标时间生成编辑命令并执行，交给 ChartVM 入撤销池 </summary>
    public void CommitRelocate(double canvasX, double canvasY)
    {
        if (_relocateBpm is not { } bpm) return;
        _previewCanvasX = canvasX;
        _previewCanvasY = canvasY;
        _previewMouseOnCanvas = true;

        var oldData = _relocateOld;
        var newData = oldData;
        newData.StartTime = SnapMouseTime();

        if (Equals(newData, oldData)) { CancelRelocate(); return; }
        bpm.Data = newData;
        var command = new EditBpmCommand(bpm, oldData, newData);
        CancelRelocate();
        OnCommitRelocate?.Invoke(command);
    }

    /// <summary> 生成重定位预览：X 固定居中，Y 跟随鼠标吸附时间 </summary>
    private List<BpmVisualItem> BuildRelocatePreviewItems()
    {
        var items = new List<BpmVisualItem>();
        if (_chartSheet == null || _userSettings == null || _viewHeight <= 0 || !_previewMouseOnCanvas)
            return items;

        int currentTime = _timeline?.TimeMs ?? 0;
        double snapY = PositionCalculator.TimeToY(SnapMouseTime(), currentTime,
            _userSettings.PixelPerSecond, _userSettings.JudgmentLinePosition, _viewHeight);

        double headW = Math.Max(MinHeadOuterWidth, LaneWidth - 6);
        double headH = RegularHeadHeight;

        items.Add(new BpmVisualItem
        {
            X      = (LaneWidth - headW) / 2, // 单轨内固定居中
            Y      = snapY - headH / 2,
            Width  = headW,
            Height = headH
        });
        return items;
    }

    // === 放置预览 ===

    /// <summary> 最近一次鼠标在 BpmNodeCanvas 中的坐标（吸附计算的输入） </summary>
    private double _previewCanvasX;
    private double _previewCanvasY;

    /// <summary> 鼠标位置是否有效（离开画布后置 false） </summary>
    private bool _previewMouseOnCanvas;

    /// <summary> 当前预览的可视元素，View 统一渲染为半透明白色矩形 </summary>
    [ObservableProperty]
    private List<BpmVisualItem> _previewItems = new();

    /// <summary> 由 View 在 PointerMoved 时调用，更新鼠标画布坐标并重算预览 </summary>
    public void UpdatePreviewAt(double canvasX, double canvasY)
    {
        _previewCanvasX = canvasX;
        _previewCanvasY = canvasY;
        _previewMouseOnCanvas = true;
        UpdatePreview();
    }

    /// <summary> 鼠标离开画布时调用，隐藏预览 </summary>
    public void HidePreview()
    {
        _previewMouseOnCanvas = false;
        UpdatePreview();
    }

    /// <summary> 重算预览。每次都换新 List 实例，靠 ObservableProperty 自动通知 View 重绘 </summary>
    private void UpdatePreview() => PreviewItems = BuildPreviewItems();

    /// <summary>
    /// 生成预览元素（同 Dot，单击放置）：
    /// 不在放置模式或鼠标不在画布 → 无预览；
    /// 否则在鼠标吸附时间处生成一个节点头（单条轨道内固定居中，几何同 RefreshBpmNodes）。
    /// </summary>
    private List<BpmVisualItem> BuildPreviewItems()
    {
        var items = new List<BpmVisualItem>();

        // 重定位预览优先（进入时已退出幽灵与放置）
        if (IsRelocating)
            return BuildRelocatePreviewItems();

        // 剪贴/复制幽灵优先于放置预览（BPM 没有轨道概念，只按时间平移）
        if (_ghostEntries != null)
        {
            if (!_previewMouseOnCanvas || _chartSheet == null || _userSettings == null || _viewHeight <= 0)
                return items;

            var layout = ComputeGhostLayout(_previewCanvasX, _previewCanvasY);
            if (layout == null) return items; // 鼠标不在轨道范围内：幽灵隐藏

            int ghostCurrentTime = _timeline?.TimeMs ?? 0;
            double ghostPps = _userSettings.PixelPerSecond;
            double ghostJlp = _userSettings.JudgmentLinePosition;
            double ghostLaneWidth = LaneWidth;

            foreach (var item in layout)
                AddGhostBpm(items, item.Time, ghostLaneWidth, ghostCurrentTime, ghostPps, ghostJlp);
            return items;
        }

        if (_chartSheet == null || _userSettings == null || _viewHeight <= 0)
            return items;

        // 常驻吸附提示：完全空闲时也显示吸附位置的节点预览（偏好设置）
        if (!IsPlacing)
        {
            if (!IsSnapPreviewEnabled || !_previewMouseOnCanvas) return items;

            // 鼠标不在轨道横向范围内时不显示（借用轨道吸附做范围判断）
            if (!PositionCalculator.TryXSnapToLane(
                    _previewCanvasX, LaneWidth, LaneSpacing, VisibleLanes, out _))
                return items;

            AddPreviewNode(items);
            return items;
        }

        if (!_previewMouseOnCanvas) return items;

        // 鼠标不在轨道横向范围内时不显示（借用轨道吸附做范围判断）
        if (!PositionCalculator.TryXSnapToLane(_previewCanvasX, LaneWidth, LaneSpacing, VisibleLanes, out _))
            return items;

        AddPreviewNode(items);
        return items;
    }

    /// <summary> 在鼠标吸附时间处追加一个预览节点（单条轨道内固定居中，几何同 RefreshBpmNodes） </summary>
    private void AddPreviewNode(List<BpmVisualItem> items)
    {
        int currentTime = _timeline?.TimeMs ?? 0;
        double snapY = PositionCalculator.TimeToY(SnapMouseTime(), currentTime,
            _userSettings!.PixelPerSecond, _userSettings.JudgmentLinePosition, _viewHeight);

        double headW = Math.Max(MinHeadOuterWidth, LaneWidth - 6);
        double headH = RegularHeadHeight;

        items.Add(new BpmVisualItem
        {
            X      = (LaneWidth - headW) / 2,  // 单条轨道内固定居中
            Y      = snapY - headH / 2,        // 以时间线为中心
            Width  = headW,
            Height = headH
        });
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

    // === 批量剪贴/复制幽灵 ===

    /// <summary> 幽灵条目：源 BPM 引用 + 相对基准的时间偏移（BPM 没有轨道概念） </summary>
    private sealed class GhostEntry
    {
        public BpmUnit Unit = null!;
        public int TimeOffset; // 时间偏移（ms）
    }

    /// <summary> 幽灵布局项：渲染与提交共用的判定结果（只含时间） </summary>
    private sealed class GhostLayoutItem
    {
        public BpmUnit Unit = null!;
        public int Time;
    }

    /// <summary> 幽灵源快照：null = 未激活。激活后与选中集合解耦（自包含） </summary>
    private List<GhostEntry>? _ghostEntries;

    /// <summary> 当前幽灵是否为复制模式（false = 剪贴） </summary>
    private bool _ghostIsCopy;

    /// <summary> 当前幽灵是否为拖拽模式（true = 拖拽移动：隐藏原 BPM、基准可指定） </summary>
    private bool _ghostIsDrag;

    /// <summary> 拖拽期间被隐藏的源 BPM（RefreshBpmNodes 跳过这些 unit） </summary>
    private HashSet<BpmUnit>? _dragHiddenUnits;

    /// <summary> 起拖时按下点相对基准节点中心的像素偏移；时间吸附以节点所在位置为准 </summary>
    private double _dragHeadOffsetY;

    /// <summary> 幽灵模式是否激活（View 据此改变点击行为；ChartVM 据此做同键取消/异键切换） </summary>
    public bool IsGhostActive => _ghostEntries != null;

    /// <summary> 当前幽灵是否为复制模式 </summary>
    public bool GhostIsCopy => _ghostIsCopy;

    /// <summary> 是否处于拖拽幽灵（区别于普通剪贴/复制幽灵） </summary>
    public bool IsDragActive => _ghostEntries != null && _ghostIsDrag;

    /// <summary> 幽灵放置提交回调（位置表, 是否复制），由 ChartVM 注入 </summary>
    public System.Action<List<BpmGhostPlacement>, bool>? OnCommitGhostPlacement;

    /// <summary> 开始拖拽回调（被抓住的基准 BPM, 按下点画布 X/Y），由 ChartVM 注入 </summary>
    public System.Action<BpmUnit, double, double>? OnBeginDrag;

    /// <summary>
    /// 是否允许拖拽，由 EditorViewModel 根据用户设置统一下发（默认开启，偏好设置可禁用）。
    /// 关闭时若正在拖拽，立即取消并还原原 BPM；普通剪贴幽灵不受影响。
    /// </summary>
    [ObservableProperty]
    private bool _isDragMode;

    partial void OnIsDragModeChanged(bool value)
    {
        if (!value && IsDragActive) CancelGhost();
    }

    /// <summary>
    /// 启动批量剪贴/复制幽灵：BPM 没有轨道概念，只冻结每个节点相对基准（时间最低）的时间偏移
    /// </summary>
    public void BeginGhost(List<BpmUnit> units, bool isCopy)
        => BeginGhostCore(units, isCopy, null, false, 0, 0);

    /// <summary>
    /// 启动拖拽幽灵：基准固定为被抓住的 BPM 节点，拖拽期间隐藏源节点
    /// </summary>
    public void BeginDragGhost(List<BpmUnit> units, BpmUnit anchor, double pressX, double pressY)
        => BeginGhostCore(units, false, anchor, true, pressX, pressY);

    /// <summary>
    /// 幽灵公共启动流程：冻结相对基准的时间偏移 → 记录拖拽起点快照。
    /// 拖拽时时间按"基准节点所在位置"绝对吸附（Free 下即差值跟随），
    /// 原节点不在网格上时会被吸到最近网格线；在网格上则起拖不跳
    /// </summary>
    private void BeginGhostCore(List<BpmUnit> units, bool isCopy,
        BpmUnit? anchorOverride, bool isDrag, double pressX, double pressY)
    {
        if (_chartSheet == null || _userSettings == null || _viewHeight <= 0 || units.Count == 0) return;

        CancelRelocate(); // 幽灵与重定位互斥

        // 防御：上一轮拖拽若还处于隐藏状态，先恢复（正常流程不会走到这里）
        if (_dragHiddenUnits != null)
        {
            _dragHiddenUnits = null;
            RefreshBpmNodes();
        }

        // 基准：拖拽用被抓住的节点；剪贴/复制用时间最低的节点
        var anchor = anchorOverride != null && units.Contains(anchorOverride)
            ? anchorOverride!
            : units[0];
        if (!isDrag || anchor != anchorOverride)
        {
            foreach (var unit in units)
                if (unit.Data.StartTime < anchor.Data.StartTime) anchor = unit;
        }

        var entries = new List<GhostEntry>(units.Count);
        foreach (var unit in units)
            entries.Add(new GhostEntry { Unit = unit, TimeOffset = unit.Data.StartTime - anchor.Data.StartTime });

        _ghostEntries = entries;
        _ghostIsCopy = isCopy;
        _ghostIsDrag = isDrag;
        _dragHiddenUnits = isDrag ? new HashSet<BpmUnit>(units) : null;

        if (isDrag)
        {
            // 起拖快照：按下点相对基准节点中心的像素偏移。
            // 时间吸附用 SnapTimeAt(鼠标Y + 偏移)，即吸附的是"节点所在位置"而非鼠标点：
            // 节点当前位置映射回的时间就是原时间，所以在网格上的节点起拖不跳，
            // 不在网格上的节点会被吸附到最近的网格线
            _dragHeadOffsetY = PositionCalculator.TimeToY(anchor.Data.StartTime,
                _timeline?.TimeMs ?? 0, _userSettings.PixelPerSecond,
                _userSettings.JudgmentLinePosition, _viewHeight) - pressY;
        }

        IsPlacing = false; // 与放置模式互斥
        if (isDrag) RefreshBpmNodes(); // 隐藏原 BPM
        UpdatePreview();
    }

    /// <summary> 取消幽灵模式（右键 / 同键再按 / 切视图 / 撤销删除前调用；未激活时无操作） </summary>
    public void CancelGhost()
    {
        if (_ghostEntries == null) return;
        _ghostEntries = null;
        _ghostIsDrag = false;

        // 拖拽幽灵取消：恢复被隐藏的源 BPM（普通剪贴幽灵没有隐藏集合，跳过）
        if (_dragHiddenUnits != null)
        {
            _dragHiddenUnits = null;
            RefreshBpmNodes();
        }
        UpdatePreview(); // 立即隐藏幽灵
    }

    /// <summary>
    /// 左键提交幽灵放置：BPM 没有越界概念，全部节点都会放置。
    /// 剪贴模式放置一次即退出；复制模式保持激活，可连续放置（图章）
    /// </summary>
    public void TryCommitGhost(double canvasX, double canvasY)
    {
        if (_ghostEntries == null) return;

        var layout = ComputeGhostLayout(canvasX, canvasY);
        if (layout == null) return; // 鼠标不在轨道范围内，不提交

        var placements = new List<BpmGhostPlacement>(layout.Count);
        foreach (var item in layout)
            placements.Add(new BpmGhostPlacement { Unit = item.Unit, NewTime = item.Time });

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
        List<BpmGhostPlacement>? placements = null;
        if (layout != null)
        {
            placements = new List<BpmGhostPlacement>(layout.Count);
            foreach (var item in layout)
                placements.Add(new BpmGhostPlacement { Unit = item.Unit, NewTime = item.Time });
            if (placements.Count == 0) placements = null;
        }

        // 先退出幽灵并恢复隐藏，再提交：ChartVM 提交后会统一刷新选中，
        // 若此时仍处于隐藏状态，移动后的 BPM 会被 RefreshBpmNodes 跳过
        _ghostEntries = null;
        _ghostIsDrag = false;
        _dragHiddenUnits = null;
        RefreshBpmNodes();
        UpdatePreview();

        if (placements != null)
            OnCommitGhostPlacement?.Invoke(placements, false);
    }

    /// <summary>
    /// 幽灵布局判定（渲染与提交共用的唯一来源）：
    /// 剪贴/复制：基准 = 吸附网格的鼠标时间；拖拽：基准 = 基准节点所在位置的绝对吸附时间；
    /// 其余 = 基准 + 冻结偏移。鼠标 X 不在唯一轨道条内返回 null（幽灵隐藏）
    /// </summary>
    private List<GhostLayoutItem>? ComputeGhostLayout(double canvasX, double canvasY)
    {
        if (_ghostEntries == null || _chartSheet == null || _userSettings == null || _viewHeight <= 0)
            return null;

        // X 不在轨道横向范围内时不显示（借用轨道吸附做范围判断）
        if (!PositionCalculator.TryXSnapToLane(canvasX, LaneWidth, LaneSpacing, VisibleLanes, out _))
            return null;

        int anchorTime = _ghostIsDrag
            ? SnapTimeAt(canvasY + _dragHeadOffsetY)
            : SnapTimeAt(canvasY);
        var result = new List<GhostLayoutItem>(_ghostEntries.Count);
        foreach (var entry in _ghostEntries)
            result.Add(new GhostLayoutItem { Unit = entry.Unit, Time = anchorTime + entry.TimeOffset });
        return result;
    }

    /// <summary> 添加幽灵 BPM 节点的可视元素（白影）：几何与 RefreshBpmNodes 一致（单条轨道内居中） </summary>
    private void AddGhostBpm(List<BpmVisualItem> items, int ghostTime, double laneWidth,
        int currentTime, double pps, double jlp)
    {
        double y = PositionCalculator.TimeToY(ghostTime, currentTime, pps, jlp, _viewHeight);
        double headW = Math.Max(MinHeadOuterWidth, laneWidth - 6);
        double headH = RegularHeadHeight;
        items.Add(new BpmVisualItem
        {
            X = (laneWidth - headW) / 2, // 单条轨道内水平居中
            Y = y - headH / 2,           // 以时间线为中心
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
    /// 框选判定（纯数据，与图形位置无关）：BPM 节点只占一个时间点，
    /// 把框换算成"时间区间 × X 区间"后与节点时间、唯一轨道带求交
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

        // 单条轨道固定排列在最左，框的 X 区间需与它相交
        if (0 > xMax || LaneWidth < xMin) return hits;

        foreach (var unit in _chartSheet.BpmUnitList)
        {
            int time = unit.Data.StartTime;
            if (time < timeMin || time > timeMax) continue;
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

    /// <summary> 对外暴露的刷新入口（选中变化等外部触发时调用，含节拍线 + 节点） </summary>
    public void RefreshPublic() => RefreshBeatLines();

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
        }
        else if (name == nameof(UserSettingsModel.IsSnapPreviewEnabled))
        {
            // 空闲状态下开关常驻预览：鼠标不动也要立即显示/隐藏
            UpdatePreview();
        }

        if (name == nameof(UserSettingsModel.PixelPerSecond) ||
            name == nameof(UserSettingsModel.JudgmentLinePosition) ||
            name == nameof(UserSettingsModel.EditorZoom) ||
            name == nameof(UserSettingsModel.Subdivision))
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
        RefreshBpmNodes();
    }

    /// <summary> 遍历 BpmUnitList 生成 BpmVisualItem，过滤不可见节点 </summary>
    private void RefreshBpmNodes()
    {
        if (_chartSheet == null || _userSettings == null || _viewHeight <= 0) return;

        // 任何外部刷新（播放/滚轮/选择/缩放等）都关闭展开条；内部刷新除外
        if (IsOverlapPanelOpen && !_suppressOverlapPanelAutoClose)
            ClearOverlapPanelState();

        int currentTime = _timeline?.TimeMs ?? 0;
        double pps = _userSettings.PixelPerSecond;
        double jlp = _userSettings.JudgmentLinePosition;

        int visibleEndMs   = PositionCalculator.YToTime(-ViewMarginPx, currentTime, pps, jlp, _viewHeight);
        int visibleStartMs = PositionCalculator.YToTime(_viewHeight + ViewMarginPx, currentTime, pps, jlp, _viewHeight);

        double laneWidth = LaneWidth;
        double headW = Math.Max(MinHeadOuterWidth, laneWidth - 6);
        double headH = RegularHeadHeight;

        // 重叠提示：单列，容差按当前 PPS 从像素折算成毫秒
        int overlapToleranceMs = OverlapAnalyzer.PxToMs(OverlapAnalyzer.TolerancePx, pps);
        int overlapStartMs = visibleStartMs - overlapToleranceMs;
        int overlapEndMs   = visibleEndMs + overlapToleranceMs;
        var overlapEntries = new List<OverlapEntry>();

        _bpmVisualItems.Clear();
        foreach (var unit in _chartSheet.BpmUnitList)
        {
            if (_dragHiddenUnits?.Contains(unit) == true) continue; // 拖拽中被隐藏
            int time = unit.Data.StartTime;

            // 先于时间可见性过滤收集：视口外最多容差距离的 unit 也可能与视口内 unit 同组
            if (time >= overlapStartMs && time <= overlapEndMs)
            {
                overlapEntries.Add(new OverlapEntry
                {
                    Unit = unit,
                    Column = 0,
                    StartTime = time
                });
            }

            // 展开条里的其他 unit 只从轨道绘制中隐藏，仍参与重叠分组
            if (_overlapPanelHiddenUnits.Contains(unit)) continue;

            if (time < visibleStartMs || time > visibleEndMs) continue;

            double y = PositionCalculator.TimeToY(time, currentTime, pps, jlp, _viewHeight);
            var node = new BpmVisualItem
            {
                SelectionTarget = unit,
                X      = (laneWidth - headW) / 2, // 单条轨道内水平居中
                Y      = y - headH / 2,           // 以时间线为中心
                Width  = headW,
                Height = headH,
                IsSelected = unit.IsSelected
            };
            // 高亮副本先加（更底层），本体后加（上层），与 note 的叠放顺序一致
            _bpmVisualItems.Add(MakeBpmHighlight(node));
            _bpmVisualItems.Add(node);
        }
        OnPropertyChanged(nameof(BpmVisualItems));

        OverlapMarkers = BuildOverlapMarkers(
            overlapEntries, overlapToleranceMs, laneWidth, currentTime, pps, jlp);

        RefreshJumpHints();
    }

    /// <summary>
    /// 把重叠组展开成绘制标记：每个重叠 unit 一个，水平居中于单条轨道，Y 对齐自己的起始时间。
    /// 组内成员起始时间可能相差几毫秒，所以逐个成员画，而不是每组画一个。
    /// </summary>
    private List<OverlapMarkerItem> BuildOverlapMarkers(
        List<OverlapEntry> entries, int toleranceMs, double laneWidth,
        int currentTime, double pps, double jlp)
    {
        var markers = new List<OverlapMarkerItem>();
        _overlapByUnit.Clear();
        if (entries.Count < 2) return markers;

        double x = laneWidth / 2;
        foreach (var group in OverlapAnalyzer.Build(entries, toleranceMs))
        {
            foreach (var member in group.Members)
                _overlapByUnit[member.Unit] = group;

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
    /// 打开某个重叠 BPM 的横向展开条：同组其余 unit 各占一列，按列宽重新生成节点。
    /// 列内节点 Y 保持原时间位置；打开期间其余节点从轨道绘制中隐藏。
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

        var columns = new List<BpmOverlapPanelColumn>();
        double minY = double.MaxValue;
        double maxY = double.MinValue;

        foreach (var member in group.Members)
        {
            if (member.Unit is not BpmUnit bu) continue;
            var item = BuildBpmPanelItem(bu, columnWidth, currentTime, pps, jlp);
            if (item.Y < minY) minY = item.Y;
            if (item.Y + item.Height > maxY) maxY = item.Y + item.Height;

            if (!ReferenceEquals(member.Unit, unit))
                columns.Add(new BpmOverlapPanelColumn { Unit = bu, Items = new List<BpmVisualItem> { item } });
        }

        if (columns.Count == 0 || minY > maxY) return false;

        _overlapPanelHiddenUnits.Clear();
        foreach (var column in columns)
            _overlapPanelHiddenUnits.Add(column.Unit);

        OverlapPanelAnchorUnit = unit;
        OverlapPanelAnchorLeft = 0;
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
        try { RefreshBpmNodes(); }
        finally { _suppressOverlapPanelAutoClose = false; }
        return true;
    }

    /// <summary> 关闭展开条并恢复被隐藏的节点 </summary>
    public void CloseOverlapPanel()
    {
        if (!IsOverlapPanelOpen) return;
        ClearOverlapPanelState();
        RefreshBpmNodes();
    }

    private void ClearOverlapPanelState()
    {
        IsOverlapPanelOpen = false;
        OverlapPanelColumns = new List<BpmOverlapPanelColumn>();
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

    /// <summary> 生成展开条一列里的 BPM 节点（X 为列内坐标，Y 与原轨道一致） </summary>
    private BpmVisualItem BuildBpmPanelItem(BpmUnit unit, double columnWidth,
        int currentTime, double pps, double jlp)
    {
        double headW = System.Math.Max(MinHeadOuterWidth, columnWidth - 6);
        double headH = RegularHeadHeight;
        double y = PositionCalculator.TimeToY(unit.Data.StartTime, currentTime, pps, jlp, _viewHeight);
        return new BpmVisualItem
        {
            SelectionTarget = unit,
            X = (columnWidth - headW) / 2,
            Y = y - headH / 2,
            Width = headW,
            Height = headH
        };
    }

    /// <summary>
    /// 重算 BPM 面板的跳转提示：画面内无节点时给出上/下最近节点的时间。
    /// 可见范围与 RefreshBpmNodes 使用同一套 ViewMarginPx 边距。
    /// </summary>
    private void RefreshJumpHints()
    {
        if (_chartSheet == null || _userSettings == null || _viewHeight <= 0) return;

        LaneJumpHintItem.SyncCount(JumpHints, 1);

        int currentTime = _timeline?.TimeMs ?? 0;
        double pps = _userSettings.PixelPerSecond;
        double jlp = _userSettings.JudgmentLinePosition;

        int top = PositionCalculator.YToTime(-ViewMarginPx, currentTime, pps, jlp, _viewHeight);
        int bottom = PositionCalculator.YToTime(_viewHeight + ViewMarginPx, currentTime, pps, jlp, _viewHeight);

        bool hasContent = false;
        // 哨兵：向上取最小值用 int.MaxValue，向下取最大值用 int.MinValue。
        // 不能用 -1：负时间节点也是合法候选（点击后由跳转逻辑 clamp 到 0）
        int upTime = int.MaxValue;
        int downTime = int.MinValue;

        foreach (var unit in _chartSheet.BpmUnitList)
        {
            if (_dragHiddenUnits?.Contains(unit) == true) continue;
            int time = unit.Data.StartTime;

            if (time >= bottom && time <= top)
                hasContent = true;
            else if (time > top)
            {
                // 向上跳：取离画面顶部最近的节点
                if (time < upTime)
                    upTime = time;
            }
            else if (time < bottom)
            {
                // 向下跳：节点没有长度，目标即节点自身时间
                if (time > downTime)
                    downTime = time;
            }
        }

        var hint = JumpHints[0];
        hint.ShowUp = !hasContent && upTime != int.MaxValue;
        hint.UpTimeMs = upTime;
        hint.ShowDown = !hasContent && downTime != int.MinValue;
        hint.DownTimeMs = downTime;
    }

    /// <summary> 生成高亮副本：比本体大 2px，偏移 -2,-2 </summary>
    private static BpmVisualItem MakeBpmHighlight(BpmVisualItem src)
    {
        return new BpmVisualItem
        {
            IsHighlight = true,
            X = src.X - 2,
            Y = src.Y - 2,
            Width = src.Width + 4,
            Height = src.Height + 4,
            IsSelected = src.IsSelected
        };
    }
}
