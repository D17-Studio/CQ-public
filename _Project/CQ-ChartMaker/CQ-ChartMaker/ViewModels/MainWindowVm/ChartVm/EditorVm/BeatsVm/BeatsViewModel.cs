using System;
using System.Collections.Generic;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
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

    /// <summary> 放置 BPM 回调，由 ChartVM 注入 </summary>
    public System.Action<BpmUnit>? OnPlaceBpm;

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
        if (value) CancelGhost(); // 放置与幽灵互斥：进入放置模式退出幽灵
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

        // X 不在轨道范围内不放置（-4 表示轨道外）
        int lane = PositionCalculator.XSnapToLane(canvasX, LaneWidth, LaneSpacing, VisibleLanes);
        if (lane == -4) return;

        // 反推时间 → 吸附网格
        int time = PositionCalculator.YToTime((int)canvasY, currentTime, pps, jlp, _viewHeight);
        var bpmList = _chartSheet.ChartSheet.GetBpmList();
        int snapTime = PositionCalculator.TimeSnapToGrid(time, bpmList, _userSettings.Subdivision);

        // 新节点初始 BPM 为 0（合法值），由用户随后在 Inspector 中编辑
        var bpm = new BpmUnit
        {
            Data = new BpmData { StartTime = snapTime, Bpm = 0 }
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

        // 移动/复制幽灵优先于放置预览（BPM 没有轨道概念，只按时间平移）
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

        if (!IsPlacing || _chartSheet == null || _userSettings == null || _viewHeight <= 0)
            return items;
        if (!_previewMouseOnCanvas) return items;

        // 鼠标不在轨道横向范围内时不显示（借用轨道吸附做范围判断，-4 表示在轨道外）
        if (PositionCalculator.XSnapToLane(_previewCanvasX, LaneWidth, LaneSpacing, VisibleLanes) == -4)
            return items;

        int currentTime = _timeline?.TimeMs ?? 0;
        double snapY = PositionCalculator.TimeToY(SnapMouseTime(), currentTime,
            _userSettings.PixelPerSecond, _userSettings.JudgmentLinePosition, _viewHeight);

        double headW = Math.Max(MinHeadOuterWidth, LaneWidth - 6);
        double headH = RegularHeadHeight;

        items.Add(new BpmVisualItem
        {
            X      = (LaneWidth - headW) / 2,  // 单条轨道内固定居中
            Y      = snapY - headH / 2,        // 以时间线为中心
            Width  = headW,
            Height = headH
        });
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

    // === 批量移动/复制幽灵 ===

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

    /// <summary> 当前幽灵是否为复制模式（false = 移动） </summary>
    private bool _ghostIsCopy;

    /// <summary> 幽灵模式是否激活（View 据此改变点击行为；ChartVM 据此做同键取消/异键切换） </summary>
    public bool IsGhostActive => _ghostEntries != null;

    /// <summary> 当前幽灵是否为复制模式 </summary>
    public bool GhostIsCopy => _ghostIsCopy;

    /// <summary> 幽灵放置提交回调（位置表, 是否复制），由 ChartVM 注入 </summary>
    public System.Action<List<BpmGhostPlacement>, bool>? OnCommitGhostPlacement;

    /// <summary>
    /// 启动批量移动/复制幽灵：BPM 没有轨道概念，只冻结每个节点相对基准（时间最低）的时间偏移
    /// </summary>
    public void BeginGhost(List<BpmUnit> units, bool isCopy)
    {
        if (_chartSheet == null || _userSettings == null || _viewHeight <= 0 || units.Count == 0) return;

        // 基准 = 时间最低的节点
        var anchor = units[0];
        foreach (var unit in units)
            if (unit.Data.StartTime < anchor.Data.StartTime) anchor = unit;

        var entries = new List<GhostEntry>(units.Count);
        foreach (var unit in units)
            entries.Add(new GhostEntry { Unit = unit, TimeOffset = unit.Data.StartTime - anchor.Data.StartTime });

        _ghostEntries = entries;
        _ghostIsCopy = isCopy;
        IsPlacing = false; // 与放置模式互斥
        UpdatePreview();
    }

    /// <summary> 取消幽灵模式（右键 / 同键再按 / 切视图 / 撤销删除前调用；未激活时无操作） </summary>
    public void CancelGhost()
    {
        if (_ghostEntries == null) return;
        _ghostEntries = null;
        UpdatePreview(); // 立即隐藏幽灵
    }

    /// <summary>
    /// 左键提交幽灵放置：BPM 没有越界概念，全部节点都会放置。
    /// 移动模式放置一次即退出；复制模式保持激活，可连续放置（图章）
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

        if (!_ghostIsCopy) CancelGhost(); // 移动：放一次退出
    }

    /// <summary>
    /// 幽灵布局判定（渲染与提交共用的唯一来源）：基准 = 吸附网格的鼠标时间；其余 = 基准 + 冻结偏移。
    /// 鼠标 X 不在唯一轨道条内返回 null（幽灵隐藏，与放置预览的范围判断一致）
    /// </summary>
    private List<GhostLayoutItem>? ComputeGhostLayout(double canvasX, double canvasY)
    {
        if (_ghostEntries == null || _chartSheet == null || _userSettings == null || _viewHeight <= 0)
            return null;

        // X 不在轨道横向范围内时不显示（借用轨道吸附做范围判断，-4 表示在轨道外）
        if (PositionCalculator.XSnapToLane(canvasX, LaneWidth, LaneSpacing, VisibleLanes) == -4)
            return null;

        int anchorTime = SnapTimeAt(canvasY);
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

    private void OnTimeChanged()
    {
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

        int currentTime = _timeline?.TimeMs ?? 0;
        double pps = _userSettings.PixelPerSecond;
        double jlp = _userSettings.JudgmentLinePosition;

        int visibleEndMs   = PositionCalculator.YToTime(-ViewMarginPx, currentTime, pps, jlp, _viewHeight);
        int visibleStartMs = PositionCalculator.YToTime(_viewHeight + ViewMarginPx, currentTime, pps, jlp, _viewHeight);

        double laneWidth = LaneWidth;
        double headW = Math.Max(MinHeadOuterWidth, laneWidth - 6);
        double headH = RegularHeadHeight;

        _bpmVisualItems.Clear();
        foreach (var unit in _chartSheet.BpmUnitList)
        {
            int time = unit.Data.StartTime;
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
