using System;
using System.Collections.Generic;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
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

    /// <summary> 放置 Note 回调，由 ChartVM 注入 </summary>
    public System.Action<NoteUnit>? OnPlaceNote;

    /// <summary> 放置 Tune 回调，由 ChartVM 注入 </summary>
    public System.Action<TuneUnit>? OnPlaceTune;

    /// <summary> 幽灵放置提交回调（存活 note 的位置表, 是否复制），由 ChartVM 注入 </summary>
    public System.Action<List<NoteGhostPlacement>, bool>? OnCommitGhostPlacement;

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
                RefreshVisibleLanes();
                RefreshNotes();
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
        set { _viewHeight = value; RefreshBeatLines(); RefreshNotes(); }
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

    /// <summary> 第一次点击的 X 坐标（Tune 方向判断用） </summary>
    private double _pendingX;

    /// <summary> 放置模式（Dash/Mute/Tune） </summary>
    private NotePlacingMode _pendingMode;

    /// <summary> 切换放置模式时清除待定状态，Tune 不可用时阻止进入 </summary>
    partial void OnPlacingModeChanged(NotePlacingMode value)
    {
        _pending = false;
        if (value == NotePlacingMode.Tune && !CanPlaceTune)
            PlacingMode = NotePlacingMode.None;
        if (value != NotePlacingMode.None)
            CancelGhost(); // 放置与幽灵互斥：进入任何放置模式都退出幽灵
        UpdatePreview();
    }

    /// <summary>
    /// 在鼠标位置尝试放置 note。Dot 直接创建，Dash/Mute 第一次定点第二次定长。
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

        switch (PlacingMode)
        {
            case NotePlacingMode.Dot:
            {
                if (lane == -4) return;
                var dot = new NoteUnit
                {
                    Data = new NoteData
                    {
                        Type = NoteType.Dot,
                        TargetTime = snapTime,
                        LaneIndex = lane
                    }
                };
                OnPlaceNote?.Invoke(dot);
                RefreshNotes();
                break;
            }
            case NotePlacingMode.Dash:
            case NotePlacingMode.Mute:
            {
                if (!_pending)
                {
                    // 第一次点击：记录头位置
                    if (lane == -4) return;
                    _pending     = true;
                    _pendingTime = snapTime;
                    _pendingLane = lane;
                    _pendingMode = PlacingMode;
                }
                else
                {
                    // 第二次点击：头始终是第一次点的位置，SustainDuration = 二减一（可负）
                    var note = new NoteUnit
                    {
                        Data = new NoteData
                        {
                            Type = _pendingMode == NotePlacingMode.Dash ? NoteType.Dash : NoteType.Mute,
                            TargetTime      = _pendingTime,
                            LaneIndex       = _pendingLane,
                            SustainDuration = snapTime - _pendingTime
                        }
                    };
                    OnPlaceNote?.Invoke(note);
                    RefreshNotes();
                    _pending = false;
                }
                break;
            }
            case NotePlacingMode.Tune:
            {
                // 找到父 Dash：选中 Dash → 直接用；选中 Tune → 取其 Parent
                NoteUnit? parent = _selectedUnit switch
                {
                    NoteUnit { Data.Type: NoteType.Dash } dash => dash,
                    TuneUnit tu => tu.Parent,
                    _ => null
                };
                if (parent == null)
                {
                    CancelPlacement();
                    return;
                }

                if (!_pending)
                {
                    // 第一次点击：记录时间
                    _pending     = true;
                    _pendingTime = snapTime;
                    _pendingX    = canvasX;
                    _pendingMode = NotePlacingMode.Tune;
                }
                else
                {
                    // 第二次点击：根据 X 位置判断左右方向
                    var direction = canvasX > _pendingX ? TuneDirection.Right : TuneDirection.Left;
                    var tune = new TuneUnit
                    {
                        Parent = parent,
                        Data = new TuneData
                        {
                            TargetTime = _pendingTime,
                            Direction = direction
                        }
                    };
                    OnPlaceTune?.Invoke(tune);
                    RefreshNotes();
                    _pending = false;
                }
                break;
            }
        }

        // 点击后放置状态有变（进入待定 / 放置完成回到跟随鼠标），重算预览
        UpdatePreview();
    }

    /// <summary> 右键取消放置 </summary>
    public void CancelPlacement()
    {
        _pending = false;
        PlacingMode = NotePlacingMode.None;
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
    /// 不在放置模式 → 无预览；
    /// 未点击第一次 → 预览头跟随鼠标吸附位置；
    /// 已点击第一次 → 头固定在第一次点击处，鼠标位置再追加 body（Dash/Mute）或方向三角（Tune）。
    /// 头/body 直接复用 Note 渲染用的 AddHead/AddBody，保证预览与真实 Note 几何完全一致；
    /// 这两个方法只用 SelectionTarget 做点击选中，预览传 null。
    /// </summary>
    private List<NoteVisualItem> BuildPreviewItems()
    {
        var items = new List<NoteVisualItem>();

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
                AddGhostNote(items, item.Unit.Data, item.Time, laneX,
                    ghostLaneWidth, ghostCurrentTime, ghostPps, ghostJlp);
            }
            return items;
        }

        if (PlacingMode == NotePlacingMode.None || _chartSheet == null ||
            _userSettings == null || _viewHeight <= 0)
            return items;

        int currentTime = _timeline?.TimeMs ?? 0;
        double pps = _userSettings.PixelPerSecond;
        double jlp = _userSettings.JudgmentLinePosition;
        double laneWidth = LaneWidth;
        double laneSpacing = LaneSpacing;

        // 预览头形状：Mute 用 Mute 头尺寸，Dot/Dash/Tune 都是常规头尺寸
        var headType = PlacingMode == NotePlacingMode.Mute ? NoteType.Mute
                     : PlacingMode == NotePlacingMode.Tune ? NoteType.Tune
                     : NoteType.Dot;

        // Tune 的头始终定位在父 Dash 轨道上（即最终渲染所在轨道），-4 表示没有父 Dash
        int tuneLane = PlacingMode == NotePlacingMode.Tune
            ? GetTuneParent()?.Data.LaneIndex ?? -4
            : 0;

        if (_pending)
        {
            // 头固定在第一次点击的位置（Tune 用父 Dash 轨道 + 第一次点击的时间）
            int headLane = PlacingMode == NotePlacingMode.Tune ? tuneLane : _pendingLane;
            if (headLane == -4) return items;
            AddHead(items, null!, _pendingTime, headLane, headType, false,
                laneWidth, laneSpacing, currentTime, pps, jlp);

            // 鼠标不在画布上：只保留固定头
            if (!_previewMouseOnCanvas) return items;

            if (PlacingMode == NotePlacingMode.Tune)
            {
                // 方向三角：与已放置 Tune 同形状同尺寸，方向判断与 TryPlace 相同（鼠标 X 对比第一次点击 X）
                var tri = MakeTuneTri(items[^1]);
                tri.Direction = _previewCanvasX > _pendingX ? TuneDirection.Right : TuneDirection.Left;
                items.Add(tri);
            }
            else
            {
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
            }
            return items;
        }

        // 未点击第一次：头跟随鼠标（Tune 只看时间，鼠标 X 不参与，所以不要求落在轨道区内）
        if (!_previewMouseOnCanvas) return items;
        int lane = PlacingMode == NotePlacingMode.Tune
            ? tuneLane
            : PositionCalculator.XSnapToLane(_previewCanvasX, laneWidth, laneSpacing, VisibleLanes);
        if (lane == -4) return items;

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

    // === 批量移动/复制幽灵 ===

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

    /// <summary> 当前幽灵是否为复制模式（false = 移动） </summary>
    private bool _ghostIsCopy;

    /// <summary> 幽灵模式是否激活（View 据此改变点击行为；ChartVM 据此做同键取消/异键切换） </summary>
    public bool IsGhostActive => _ghostEntries != null;

    /// <summary> 当前幽灵是否为复制模式 </summary>
    public bool GhostIsCopy => _ghostIsCopy;

    /// <summary>
    /// 启动批量移动/复制幽灵。传入的 note 必须都有可见轨道（#7 约定：隐藏轨道会先丢选中）。
    /// 当场冻结每个 note 相对基准的时间偏移与可见轨道下标偏移——此后幽灵是"刚性形状"，
    /// 鼠标怎么动、轨道怎么显隐都不重排，保证预览稳定、不发生漂移
    /// </summary>
    public void BeginGhost(List<NoteUnit> units, bool isCopy)
    {
        if (_chartSheet == null || _userSettings == null || _viewHeight <= 0 || units.Count == 0) return;

        // 只收有可见轨道下标的 note（正常不会发生，防御性过滤）
        var visible = new List<NoteUnit>();
        foreach (var unit in units)
            if (VisibleLanes.Contains(unit.Data.LaneIndex)) visible.Add(unit);
        if (visible.Count == 0) return;

        // 基准 = 最左下角：时间最低优先，并列取轨道编号最小
        var anchor = visible[0];
        foreach (var unit in visible)
        {
            var d = unit.Data;
            var a = anchor.Data;
            if (d.TargetTime < a.TargetTime ||
                (d.TargetTime == a.TargetTime && d.LaneIndex < a.LaneIndex))
                anchor = unit;
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
        PlacingMode = NotePlacingMode.None; // 与放置模式互斥（会自动重算预览）
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
    /// 左键提交幽灵放置：越界的 note 直接丢弃，其余打包位置表交给 ChartVM 应用。
    /// 移动模式放置一次即退出；复制模式保持激活，可连续放置（图章）
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

        if (!_ghostIsCopy) CancelGhost(); // 移动：放一次退出
    }

    /// <summary>
    /// 幽灵布局判定（渲染与提交共用的唯一来源，保证所见即所得）：
    /// 基准 = 鼠标所在轨道格 + 吸附网格时间；其余 note = 基准 + 冻结偏移；
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
                Type = data.Type,
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
            Type = data.Type,
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

    private void OnTimeChanged() { RefreshBeatLines(); RefreshNotes(); UpdatePreview(); }

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
        RefreshNotes();
    }

    /// <summary> 遍历 NoteUnitList 生成 NoteVisualItems，过滤不可见 note 并按 z-order 排列 </summary>
    private void RefreshNotes()
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

        var allItems = new List<NoteVisualItem>();

        // 第一遍遍历：收集 Dash 可见性（Tune 判断需要）
        var dashVisible = new HashSet<NoteUnit>();
        foreach (var unit in _chartSheet.NoteUnitList)
        {
            if (unit.Data.Type != NoteType.Dash) continue;
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
            var data = unit.Data;
            if (!visibleLanes.Contains(data.LaneIndex)) continue;

            switch (data.Type)
            {
                case NoteType.Dot:
                    if (data.TargetTime < visibleStartMs || data.TargetTime > visibleEndMs) continue;
                    AddHead(allItems, unit, data.TargetTime, data.LaneIndex, data.Type, unit.IsSelected, laneWidth, laneSpacing, currentTime, pps, jlp);
                    break;

                case NoteType.Dash:
                    if (!dashVisible.Contains(unit)) continue;
                    if (data.SustainDuration > 0)
                        AddBody(allItems, unit, data, currentTime, pps, jlp, data.Type, unit.IsSelected, laneWidth, laneSpacing);
                    AddHead(allItems, unit, data.TargetTime, data.LaneIndex, data.Type, unit.IsSelected, laneWidth, laneSpacing, currentTime, pps, jlp);
                    if (unit.TuneList != null)
                    {
                        foreach (var tu in unit.TuneList)
                        {
                            if (tu.Data.TargetTime < visibleStartMs || tu.Data.TargetTime > visibleEndMs) continue;
                            AddHead(allItems, tu, tu.Data.TargetTime, data.LaneIndex, NoteType.Tune, tu.IsSelected, laneWidth, laneSpacing, currentTime, pps, jlp, tu.Data.Direction);
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
                            AddBody(allItems, unit, data, currentTime, pps, jlp, data.Type, unit.IsSelected, laneWidth, laneSpacing);
                        AddHead(allItems, unit, data.TargetTime, data.LaneIndex, data.Type, unit.IsSelected, laneWidth, laneSpacing, currentTime, pps, jlp);
                    }
                    break;
            }
        }

        // 展开：每个原始 item 生成高亮副本 + 三角（Tune），穿插排列
        var expanded = new List<NoteVisualItem>();
        foreach (var src in allItems)
        {
            if (src.Type == NoteType.Tune && src.IsHead)
            {
                // Tune 头：头高亮 → 三角高亮 → 头本体 → 三角本体
                expanded.Add(MakeHighlight(src));
                expanded.Add(MakeTuneTriHighlight(src));
                expanded.Add(src);
                expanded.Add(MakeTuneTri(src));
            }
            else
            {
                // 其他：高亮 → 本体
                expanded.Add(MakeHighlight(src));
                expanded.Add(src);
            }
        }

        expanded.Sort((a, b) => GetZOrder(a).CompareTo(GetZOrder(b)));

        _noteVisualItems.Clear();
        _noteVisualItems.AddRange(expanded);
        OnPropertyChanged(nameof(NoteVisualItems));
    }

    /// <summary> 生成高亮副本：比本体大 2px，偏移 -2,-2 </summary>
    private static NoteVisualItem MakeHighlight(NoteVisualItem src)
    {
        return new NoteVisualItem
        {
            Type = src.Type,
            IsHead = src.IsHead,
            IsHighlight = true,
            X = src.X - 2,
            Y = src.Y - 2,
            Width = src.Width + 4,
            Height = src.Height + 4,
            IsSelected = src.IsSelected,
            Direction = src.Direction
        };
    }

    /// <summary> 生成 Tune 三角本体（20x20，居中于 head） </summary>
    private static NoteVisualItem MakeTuneTri(NoteVisualItem head)
    {
        const double size = 20;
        return new NoteVisualItem
        {
            Type = NoteType.Tune,
            IsTriangle = true,
            X = head.X + (head.Width - size) / 2,
            Y = head.Y + (head.Height - size) / 2,
            Width = size,
            Height = size,
            IsSelected = head.IsSelected,
            Direction = head.Direction
        };
    }

    /// <summary> 生成 Tune 三角高亮（24x24，比三角大 2px） </summary>
    private static NoteVisualItem MakeTuneTriHighlight(NoteVisualItem head)
    {
        const double size = 20;
        return new NoteVisualItem
        {
            Type = NoteType.Tune,
            IsTriangle = true,
            IsHighlight = true,
            X = head.X + (head.Width - size) / 2 - 2,
            Y = head.Y + (head.Height - size) / 2 - 2,
            Width = size + 4,
            Height = size + 4,
            IsSelected = head.IsSelected,
            Direction = head.Direction
        };
    }

    /// <summary> 添加 head NoteVisualItem（Dot/Dash/Mute/Tune 通用） </summary>
    private void AddHead(List<NoteVisualItem> items, object target, int timeMs, int laneIndex,
        NoteType type, bool isSelected, double laneWidth, double laneSpacing,
        int currentTime, double pps, double jlp, TuneDirection direction = TuneDirection.Left)
    {
        double y = PositionCalculator.TimeToY(timeMs, currentTime, pps, jlp, _viewHeight);
        double laneX = LaneToX(laneIndex, laneWidth, laneSpacing, VisibleLanes);

        bool isMute = type == NoteType.Mute;
        double headW = isMute ? MuteHeadWidth : Math.Max(MinHeadOuterWidth, laneWidth - 6);
        double headH = isMute ? MuteHeadHeight : RegularHeadHeight;

        items.Add(new NoteVisualItem
        {
            SelectionTarget = target,
            Type = type,
            IsHead = true,
            X = laneX + (laneWidth - headW) / 2,
            Y = y - headH / 2,
            Width = headW,
            Height = headH,
            IsSelected = isSelected,
            Direction = direction
        });
    }

    /// <summary> 添加 body NoteVisualItem（Dash/Mute） </summary>
    private void AddBody(List<NoteVisualItem> items, NoteUnit unit, NoteData data,
        int currentTime, double pps, double jlp, NoteType type, bool isSelected,
        double laneWidth, double laneSpacing)
    {
        double headY = PositionCalculator.TimeToY(data.TargetTime, currentTime, pps, jlp, _viewHeight);
        double endY  = PositionCalculator.TimeToY(data.TargetTime + data.SustainDuration, currentTime, pps, jlp, _viewHeight);
        double laneX = LaneToX(data.LaneIndex, laneWidth, laneSpacing, VisibleLanes);

        items.Add(new NoteVisualItem
        {
            SelectionTarget = unit,
            Type = type,
            IsHead = false,
            X = laneX + (laneWidth - BodyWidth) / 2,
            Y = endY,
            Width = BodyWidth,
            Height = Math.Max(0, headY - endY),
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
    /// 顺序：Dash身体高亮→Dash身体→Mute身体高亮→Mute身体→
    ///       Dash头高亮→Dash头→Dot头高亮→Dot头→
    ///       Tune头高亮→Tune三角高亮→Tune头→Tune三角→
    ///       Mute头高亮→Mute头
    /// </summary>
    private static int GetZOrder(NoteVisualItem item)
    {
        if (item.IsTriangle && item.IsHighlight) return 9;
        if (item.IsTriangle && !item.IsHighlight) return 11;

        if (!item.IsHead) // body
        {
            if (item.Type == NoteType.Mute)
                return item.IsHighlight ? 2 : 3;
            return item.IsHighlight ? 0 : 1; // Dash body
        }

        // head
        return item.Type switch
        {
            NoteType.Mute => item.IsHighlight ? 12 : 13,
            NoteType.Tune => item.IsHighlight ? 8 : 10,
            NoteType.Dot  => item.IsHighlight ? 6 : 7,
            NoteType.Dash => item.IsHighlight ? 4 : 5,
            _ => 0
        };
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
