using System;
using System.Collections.Generic;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CQ_ChartMaker.Models;
using CQMusicGame.Shared;

namespace CQ_ChartMaker.ViewModels;

public partial class LanesViewModel : ObservableObject
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

    /// <summary> 点击选中回调（unit, 是否按住 Ctrl），LanesView 点击后通过此处通知 ChartVM </summary>
    public System.Action<object?, bool>? OnSelectUnit;

    /// <summary> 框选结果回调（框内 unit 列表, 是否追加），由 ChartVM 注入 </summary>
    public System.Action<List<object>, bool>? OnSelectMany;

    /// <summary> 放置 LaneMotion 回调，由 ChartVM 注入 </summary>
    public System.Action<MotionUnit>? OnPlaceMotion;

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
                RefreshVisibleLanes();
                RefreshBeatLines();
            }
        }
    }

    [ObservableProperty]
    private List<int> _visibleLanes = new() { -3, -2, -1, 0, 1, 2, 3 };

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

    // 预览尺寸常量（与 Dot 头一致）
    private const double RegularHeadHeight = 14;
    private const double MinHeadOuterWidth = 18;
    private const double BodyWidth = 12;
    private const double ViewMarginPx = 10;

    // === 放置逻辑 ===

    /// <summary> 当前是否处于放置模式（右侧工具栏 + 按钮控制） </summary>
    [ObservableProperty]
    private bool _isPlacing;

    /// <summary> LaneMotion 正在等待第二次点击定长（第一次点击定头） </summary>
    private bool _pending;

    /// <summary> 第一次点击的时间 </summary>
    private int _pendingTime;

    /// <summary> 第一次点击的轨道 </summary>
    private int _pendingLane;

    /// <summary> 切换放置模式时清除待定状态，并立即重算预览（进入/退出直接显示/隐藏） </summary>
    partial void OnIsPlacingChanged(bool value)
    {
        _pending = false;
        if (value) CancelGhost(); // 放置与幽灵互斥：进入放置模式退出幽灵
        UpdatePreview();
    }

    /// <summary>
    /// 在鼠标位置尝试放置 LaneMotion。第一次点击定头，第二次点击定长度（同 Dash）。
    /// </summary>
    public void TryPlace(double canvasX, double canvasY)
    {
        if (_chartSheet == null || _userSettings == null || _viewHeight <= 0) return;

        int currentTime = _timeline?.TimeMs ?? 0;
        double pps = _userSettings.PixelPerSecond;
        double jlp = _userSettings.JudgmentLinePosition;

        // 反推时间 → 吸附网格
        int time = PositionCalculator.YToTime((int)canvasY, currentTime, pps, jlp, _viewHeight);
        var bpmList = _chartSheet.ChartSheet.GetBpmList();
        int snapTime = PositionCalculator.TimeSnapToGrid(time, bpmList, _userSettings.Subdivision);

        // 吸附轨道（第二次定长不需要轨道，走内部判断）
        int lane = PositionCalculator.XSnapToLane(canvasX, LaneWidth, LaneSpacing, VisibleLanes);

        if (!_pending)
        {
            // 第一次点击：记录头位置
            if (lane == -4) return;
            _pending     = true;
            _pendingTime = snapTime;
            _pendingLane = lane;
        }
        else
        {
            // 第二次点击：头始终是第一次点的位置，Duration = 二减一（可负）
            var motion = new MotionUnit
            {
                Data = new LaneMotionData
                {
                    StartTime = _pendingTime,
                    LaneIndex = _pendingLane,
                    Duration  = snapTime - _pendingTime
                }
            };
            OnPlaceMotion?.Invoke(motion);
            RefreshMotions();
            _pending = false;
        }

        // 点击后放置状态有变（进入待定 / 放置完成回到跟随鼠标），重算预览
        UpdatePreview();
    }

    /// <summary> 右键取消放置 </summary>
    public void CancelPlacement()
    {
        _pending = false;
        IsPlacing = false;
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
    /// 生成预览元素（与 Dash 相同的两段式）：
    /// 不在放置模式 → 无预览；
    /// 未点击第一次 → 预览头跟随鼠标吸附位置；
    /// 已点击第一次 → 头固定在第一次点击处，鼠标位置再追加 body（负时长不生成）。
    /// 头/body 复用 Motion 渲染用的 AddMotionHead/AddMotionBody：几何数据装在一个临时
    /// MotionUnit 中传入（预览不参与选中，SelectionTarget 没有实际用途）。
    /// </summary>
    private List<MotionVisualItem> BuildPreviewItems()
    {
        var items = new List<MotionVisualItem>();

        // 移动/复制幽灵优先于放置预览（两者本互斥，这里双保险）；
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
                AddGhostMotion(items, item.Unit.Data, item.Time, laneX,
                    ghostLaneWidth, ghostCurrentTime, ghostPps, ghostJlp);
            }
            return items;
        }

        if (!IsPlacing || _chartSheet == null || _userSettings == null || _viewHeight <= 0)
            return items;

        int currentTime = _timeline?.TimeMs ?? 0;
        double pps = _userSettings.PixelPerSecond;
        double jlp = _userSettings.JudgmentLinePosition;
        double laneWidth = LaneWidth;
        double laneSpacing = LaneSpacing;

        if (_pending)
        {
            // 头固定在第一次点击的位置；鼠标在画布上且往下拉（正时长）才生成 body
            int mouseTime = SnapMouseTime();
            bool showBody = _previewMouseOnCanvas && mouseTime > _pendingTime;
            var preview = new MotionUnit
            {
                Data = new LaneMotionData
                {
                    StartTime = _pendingTime,
                    LaneIndex = _pendingLane,
                    Duration  = showBody ? mouseTime - _pendingTime : 0
                }
            };
            if (showBody)
                AddMotionBody(items, preview, laneWidth, laneSpacing, currentTime, pps, jlp);
            AddMotionHead(items, preview, laneWidth, laneSpacing, currentTime, pps, jlp);
            return items;
        }

        // 未点击第一次：头跟随鼠标
        if (!_previewMouseOnCanvas) return items;
        int lane = PositionCalculator.XSnapToLane(_previewCanvasX, laneWidth, laneSpacing, VisibleLanes);
        if (lane == -4) return items;

        var hover = new MotionUnit
        {
            Data = new LaneMotionData { StartTime = SnapMouseTime(), LaneIndex = lane }
        };
        AddMotionHead(items, hover, laneWidth, laneSpacing, currentTime, pps, jlp);
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

    /// <summary> 将 lane index 转换为 X 坐标（考虑轨道显隐） </summary>
    private static double LaneToX(int laneIndex, double laneWidth, double laneSpacing, List<int> visibleLanes)
    {
        int ordinal = visibleLanes.IndexOf(laneIndex);
        return ordinal >= 0 ? ordinal * (laneWidth + laneSpacing) : 0;
    }

    // === 批量移动/复制幽灵 ===

    /// <summary> 幽灵条目：源 motion 引用 + 相对基准的冻结偏移（创建时算一次，之后帧间不变） </summary>
    private sealed class GhostEntry
    {
        public MotionUnit Unit = null!;
        public int TimeOffset;    // 时间偏移（ms）
        public int OrdinalOffset; // 可见轨道下标偏移
    }

    /// <summary> 幽灵布局项：渲染与提交共用的判定结果 </summary>
    private sealed class GhostLayoutItem
    {
        public MotionUnit Unit = null!;
        public int Time;      // 幽灵位置的时间
        public int Ordinal;   // 幽灵位置的可见轨道下标（可越界：渲染外推、放置丢弃）
        public int? Lane;     // 对应的轨道编号；null = 越界（当前没有这条轨道）
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
    public System.Action<List<MotionGhostPlacement>, bool>? OnCommitGhostPlacement;

    /// <summary>
    /// 启动批量移动/复制幽灵。传入的 motion 必须都有可见轨道（#7 约定：隐藏轨道会先丢选中）。
    /// 当场冻结每个 motion 相对基准的时间偏移与可见轨道下标偏移——此后幽灵是"刚性形状"，
    /// 鼠标怎么动、轨道怎么显隐都不重排
    /// </summary>
    public void BeginGhost(List<MotionUnit> units, bool isCopy)
    {
        if (_chartSheet == null || _userSettings == null || _viewHeight <= 0 || units.Count == 0) return;

        // 只收有可见轨道下标的 motion（正常不会发生，防御性过滤）
        var visible = new List<MotionUnit>();
        foreach (var unit in units)
            if (VisibleLanes.Contains(unit.Data.LaneIndex)) visible.Add(unit);
        if (visible.Count == 0) return;

        // 基准 = 最左下角：时间最低优先，并列取轨道编号最小
        var anchor = visible[0];
        foreach (var unit in visible)
        {
            var d = unit.Data;
            var a = anchor.Data;
            if (d.StartTime < a.StartTime ||
                (d.StartTime == a.StartTime && d.LaneIndex < a.LaneIndex))
                anchor = unit;
        }

        int anchorOrdinal = VisibleLanes.IndexOf(anchor.Data.LaneIndex);
        var entries = new List<GhostEntry>(visible.Count);
        foreach (var unit in visible)
        {
            entries.Add(new GhostEntry
            {
                Unit = unit,
                TimeOffset = unit.Data.StartTime - anchor.Data.StartTime,
                OrdinalOffset = VisibleLanes.IndexOf(unit.Data.LaneIndex) - anchorOrdinal
            });
        }

        _ghostEntries = entries;
        _ghostIsCopy = isCopy;
        IsPlacing = false; // 与放置模式互斥（会自动重算预览）
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
    /// 左键提交幽灵放置：越界的 motion 直接丢弃，其余打包位置表交给 ChartVM 应用。
    /// 移动模式放置一次即退出；复制模式保持激活，可连续放置（图章）
    /// </summary>
    public void TryCommitGhost(double canvasX, double canvasY)
    {
        if (_ghostEntries == null) return;

        var layout = ComputeGhostLayout(canvasX, canvasY);
        if (layout == null) return; // 鼠标不在任何轨道上，不提交

        var placements = new List<MotionGhostPlacement>();
        foreach (var item in layout)
        {
            if (item.Lane is not { } lane) continue; // 越界项：当前没有这条轨道，不放置
            placements.Add(new MotionGhostPlacement { Unit = item.Unit, NewTime = item.Time, NewLane = lane });
        }
        if (placements.Count == 0) return;

        OnCommitGhostPlacement?.Invoke(placements, _ghostIsCopy);

        if (!_ghostIsCopy) CancelGhost(); // 移动：放一次退出
    }

    /// <summary>
    /// 幽灵布局判定（渲染与提交共用的唯一来源，保证所见即所得）：
    /// 基准 = 鼠标所在轨道格 + 吸附网格时间；其余 = 基准 + 冻结偏移；
    /// 下标 → 轨道编号按"当前可见轨道列表"实时翻译。鼠标不在任何轨道上返回 null（幽灵隐藏）
    /// </summary>
    private List<GhostLayoutItem>? ComputeGhostLayout(double canvasX, double canvasY)
    {
        if (_ghostEntries == null || _chartSheet == null || _userSettings == null || _viewHeight <= 0)
            return null;

        var lanes = VisibleLanes;
        int anchorLane = PositionCalculator.XSnapToLane(canvasX, LaneWidth, LaneSpacing, lanes);
        if (anchorLane == -4) return null;

        int anchorOrdinal = lanes.IndexOf(anchorLane);
        int anchorTime = SnapTimeAt(canvasY);

        var result = new List<GhostLayoutItem>(_ghostEntries.Count);
        foreach (var entry in _ghostEntries)
        {
            int ordinal = anchorOrdinal + entry.OrdinalOffset;
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
    /// 添加幽灵 motion 的可视元素（白影）：几何与 AddMotionHead/AddMotionBody 一致，
    /// 但 X 直接用（可能越界的）下标外推坐标，不走 LaneToX
    /// </summary>
    private void AddGhostMotion(List<MotionVisualItem> items, LaneMotionData data, int ghostTime, double laneX,
        double laneWidth, int currentTime, double pps, double jlp)
    {
        double headY = PositionCalculator.TimeToY(ghostTime, currentTime, pps, jlp, _viewHeight);

        // 身体（正时长）
        if (data.Duration > 0)
        {
            double endY = PositionCalculator.TimeToY(ghostTime + data.Duration, currentTime, pps, jlp, _viewHeight);
            items.Add(new MotionVisualItem
            {
                IsHead = false,
                X = laneX + (laneWidth - BodyWidth) / 2,
                Y = endY,
                Width = BodyWidth,
                Height = Math.Max(0, headY - endY)
            });
        }

        // 头
        double headW = Math.Max(MinHeadOuterWidth, laneWidth - 6);
        double headH = RegularHeadHeight;
        items.Add(new MotionVisualItem
        {
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
    /// 把框换算成"时间区间 × X 区间"，与每个 MotionUnit 的时间跨度和轨道带求交
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

        foreach (var unit in _chartSheet.MotionUnitList)
        {
            var data = unit.Data;

            // X 方向：框与该轨道的整条轨道带求交（隐藏轨道不参与）
            int ordinal = lanes.IndexOf(data.LaneIndex);
            if (ordinal < 0) continue;
            double laneX = ordinal * (laneWidth + laneSpacing);
            if (laneX > xMax || laneX + laneWidth < xMin) continue;

            // 时间方向：取整段时长（负时长按 0 处理，即时间点）
            int motionStart = data.StartTime;
            int motionEnd = data.StartTime + Math.Max(0, data.Duration);
            if (motionEnd < timeMin || motionStart > timeMax) continue;

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

    /// <summary> 对外暴露的刷新入口（选中变化、Inspector 编辑等外部触发时调用） </summary>
    public void RefreshPublic() => RefreshBeatLines();

    private void OnTimeChanged()
    {
        RefreshBeatLines();
        UpdatePreview();
    }

    private void OnUserSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        var name = e.PropertyName;
        if (name == nameof(UserSettingsModel.LaneVisibilityMask))
            RefreshVisibleLanes();
        else if (name == nameof(UserSettingsModel.JudgmentLinePosition))
            OnPropertyChanged(nameof(JudgmentLinePosition));
        else if (name == nameof(UserSettingsModel.EditorZoom))
        {
            OnPropertyChanged(nameof(LaneWidth));
            OnPropertyChanged(nameof(LaneSpacing));
        }

        if (name == nameof(UserSettingsModel.PixelPerSecond) ||
            name == nameof(UserSettingsModel.JudgmentLinePosition) ||
            name == nameof(UserSettingsModel.LaneVisibilityMask) ||
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
        RefreshMotions();
    }

    /// <summary> 遍历 MotionUnitList 生成 MotionVisualItems，过滤不可见轨道和时间区间，按 z-order 排列 </summary>
    private void RefreshMotions()
    {
        if (_chartSheet == null || _userSettings == null || _viewHeight <= 0) return;

        int currentTime = _timeline?.TimeMs ?? 0;
        double pps = _userSettings.PixelPerSecond;
        double jlp = _userSettings.JudgmentLinePosition;

        int visibleEndMs   = PositionCalculator.YToTime(-ViewMarginPx, currentTime, pps, jlp, _viewHeight);
        int visibleStartMs = PositionCalculator.YToTime(_viewHeight + ViewMarginPx, currentTime, pps, jlp, _viewHeight);

        var visibleLanes = VisibleLanes;
        double laneWidth = LaneWidth;
        double laneSpacing = LaneSpacing;

        var allItems = new List<MotionVisualItem>();

        foreach (var unit in _chartSheet.MotionUnitList)
        {
            var data = unit.Data;
            if (!visibleLanes.Contains(data.LaneIndex)) continue;

            int motionEnd = data.Duration > 0
                ? data.StartTime + data.Duration
                : data.StartTime;
            if (motionEnd < visibleStartMs || data.StartTime > visibleEndMs) continue;

            if (data.Duration > 0)
                AddMotionBody(allItems, unit, laneWidth, laneSpacing, currentTime, pps, jlp);
            AddMotionHead(allItems, unit, laneWidth, laneSpacing, currentTime, pps, jlp);
        }

        // 展开：每个原始 item 生成高亮副本 + 本体
        var expanded = new List<MotionVisualItem>();
        foreach (var src in allItems)
        {
            expanded.Add(MakeMotionHighlight(src));
            expanded.Add(src);
        }

        // 身体先画（底层）、头后画（上层）
        expanded.Sort((a, b) => GetZOrder(a).CompareTo(GetZOrder(b)));

        _motionVisualItems.Clear();
        _motionVisualItems.AddRange(expanded);
        OnPropertyChanged(nameof(MotionVisualItems));
    }

    /// <summary> 生成高亮副本：比本体大 2px，偏移 -2,-2 </summary>
    private static MotionVisualItem MakeMotionHighlight(MotionVisualItem src)
    {
        return new MotionVisualItem
        {
            IsHead = src.IsHead,
            IsHighlight = true,
            X = src.X - 2,
            Y = src.Y - 2,
            Width = src.Width + 4,
            Height = src.Height + 4,
            IsSelected = src.IsSelected
        };
    }

    /// <summary> 添加 head MotionVisualItem（白色外框 + 灰色内芯，尺寸同 Dot） </summary>
    private void AddMotionHead(List<MotionVisualItem> items, MotionUnit unit,
        double laneWidth, double laneSpacing, int currentTime, double pps, double jlp)
    {
        var data = unit.Data;
        double y = PositionCalculator.TimeToY(data.StartTime, currentTime, pps, jlp, _viewHeight);
        double laneX = LaneToX(data.LaneIndex, laneWidth, laneSpacing, VisibleLanes);

        double headW = Math.Max(MinHeadOuterWidth, laneWidth - 6);
        double headH = RegularHeadHeight;

        items.Add(new MotionVisualItem
        {
            SelectionTarget = unit,
            IsHead = true,
            X = laneX + (laneWidth - headW) / 2,
            Y = y - headH / 2,
            Width = headW,
            Height = headH,
            IsSelected = unit.IsSelected
        });
    }

    /// <summary> 添加 body MotionVisualItem（长条，宽度同 Dash） </summary>
    private void AddMotionBody(List<MotionVisualItem> items, MotionUnit unit,
        double laneWidth, double laneSpacing, int currentTime, double pps, double jlp)
    {
        var data = unit.Data;
        double headY = PositionCalculator.TimeToY(data.StartTime, currentTime, pps, jlp, _viewHeight);
        double endY  = PositionCalculator.TimeToY(data.StartTime + data.Duration, currentTime, pps, jlp, _viewHeight);
        double laneX = LaneToX(data.LaneIndex, laneWidth, laneSpacing, VisibleLanes);

        items.Add(new MotionVisualItem
        {
            SelectionTarget = unit,
            IsHead = false,
            X = laneX + (laneWidth - BodyWidth) / 2,
            Y = endY,
            Width = BodyWidth,
            Height = Math.Max(0, headY - endY),
            IsSelected = unit.IsSelected
        });
    }

    /// <summary> 返回显示顺序权重，越小越底层：身体高亮 0 → 身体 1 → 头高亮 2 → 头 3 </summary>
    private static int GetZOrder(MotionVisualItem item)
    {
        if (item.IsHead)
            return item.IsHighlight ? 2 : 3;
        return item.IsHighlight ? 0 : 1;
    }

    private void RefreshVisibleLanes()
    {
        if (_userSettings == null) return;
        var lanes = new List<int>();
        for (int i = -3; i <= 3; i++)
            if (_userSettings.IsLaneVisible(i))
                lanes.Add(i);
        VisibleLanes = lanes;
    }
}
