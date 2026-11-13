using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CQ_ChartMaker.Common;
using CQ_ChartMaker.Models;
using CQMusicGame.Shared;

namespace CQ_ChartMaker.ViewModels;

public partial class InspectorViewModel : ObservableObject
{
    // 注意：属性名与枚举名同名（NoteType）。属性类型改可空后，
    // C# 的"同名类型优先"消歧规则失效，写成 NoteType.Dot 会被解析成属性，所以这里必须用全名

    public static List<NoteType> NoteTypeOptions { get; } = new()
        { CQMusicGame.Shared.NoteType.Dot, CQMusicGame.Shared.NoteType.Dash, CQMusicGame.Shared.NoteType.Mute };

    /// <summary> 属性即时刷新时触发（每次键入都刷新画布，但不记录撤销） </summary>
    public System.Action? OnPropertyEdited;

    /// <summary> 失焦或切选中时提交，传入待记录的编辑命令 </summary>
    public System.Action<IUndoableCommand>? OnCommitEdit;

    /// <summary> 请求进入重定位模式（时间/轨道/持续时间），由 ChartVM 按选中类型分发 </summary>
    public System.Action<RelocateField>? OnRelocateRequested;

    /// <summary> 请求把时间轴跳转到指定时间（ms），由 ChartVM 执行定位 </summary>
    public System.Action<int>? OnJumpRequested;

    /// <summary> 请求选中指定 Tune（Tune 表格行内按钮），由 ChartVM 执行 </summary>
    public System.Action<TuneUnit>? OnSelectTuneRequested;

    /// <summary> 请求为指定 Note 添加 Tune（Tune 表格 AddTune 按钮），由 ChartVM 执行 </summary>
    public System.Action<NoteUnit>? OnAddTuneRequested;

    /// <summary> 请求删除指定 Tune（Tune 行内 × 按钮），由 ChartVM 执行 </summary>
    public System.Action<TuneUnit>? OnDeleteTuneRequested;

    /// <summary> 请求选中指定 Note（Tune 面板 Back 按钮），由 ChartVM 执行 </summary>
    public System.Action<NoteUnit>? OnSelectNoteRequested;

    /// <summary> 请求采样指定轨道在指定时刻的状态（启用动画初始值时取默认值），由 ChartVM 提供 </summary>
    public System.Func<int, int, LaneStateSample>? OnSampleLaneState;

    /// <summary> 暂存的待提交命令列表，同一次编辑会话持续复用（每次输入重新生成整批） </summary>
    private List<IUndoableCommand> _pendingCommands = new();

    /// <summary>
    /// 重建选中界面期间为 true。曲线行会被同一 DataTemplate 复用视觉树并只换 DataContext，
    /// 行内控件换绑时可能把上一个 unit 的值回写到新选中项上；这些回写全部发生在
    /// 同步重建过程中，统一丢弃，避免曲线数据串到别的 unit。
    /// </summary>
    private bool _suppressEdits;

    /// <summary> 本次编辑会话中各 unit 的旧值快照：首次编辑时锁定、提交后清空。
    /// 多选时每个 unit 各锁一份，撤销时连同别的属性一起整份还原，各自的差异天然保留 </summary>
    private readonly Dictionary<NoteUnit, NoteData> _noteEditOldData = new();
    private readonly Dictionary<BpmUnit, BpmData> _bpmEditOldData = new();
    private readonly Dictionary<ILaneEventUnit, object> _eventEditOldData = new();
    private TuneData? _tuneEditOldData; // Tune 不参与多选，保持单值快照

    // === 选中集合（由 ChartVM 下发） ===

    private IReadOnlyList<object> _selectedUnits = new List<object>();

    /// <summary>
    /// 当前选中集合：单选/多选/空选都由它承载。
    /// 多选时集合内必为同一类型（切换子视图会清空选择、Tune 不参与多选），
    /// 所以面板类型按"集合内全为某类型"判定，各属性按"全体相同才显示值"取值
    /// </summary>
    public IReadOnlyList<object> SelectedUnits
    {
        get => _selectedUnits;
        set
        {
            _selectedUnits = value;
            OnPropertyChanged(nameof(SelectedUnits)); // 通知视图选中集合已变化
            OnSelectedUnitChanged();
        }
    }

    /// <summary> 恰好选中 1 项时返回该项（Tune 属性固定走单选路径），多选/空选返回 null </summary>
    private object? SelectedUnit => _selectedUnits.Count == 1 ? _selectedUnits[0] : null;

    public bool IsNoteSelected   => AllSelectedAre<NoteUnit>();
    public bool IsTuneSelected   => AllSelectedAre<TuneUnit>();
    public bool IsBpmSelected    => AllSelectedAre<BpmUnit>();

    /// <summary> 选中集合全部是 LaneEvent 且具体类型一致；混合类型时整块事件面板隐藏 </summary>
    public bool IsEventSelected =>
        AllSelectedAre<ILaneEventUnit>() && AllSelectedSameEventKind();

    /// <summary> 选中集合的事件类型是否一致（单选恒为 true） </summary>
    private bool AllSelectedSameEventKind()
    {
        LaneEventKind? kind = null;
        foreach (var unit in _selectedUnits)
        {
            if (unit is not ILaneEventUnit e) return false;
            if (kind == null) kind = e.Kind;
            else if (kind != e.Kind) return false;
        }
        return kind != null;
    }
    public bool IsTransformEventSelected => AllSelectedAre<TransformEventUnit>();
    public bool IsOpacityEventSelected   => AllSelectedAre<OpacityEventUnit>();
    public bool IsLengthEventSelected    => AllSelectedAre<LengthEventUnit>();
    public bool IsSpeedEventSelected     => AllSelectedAre<SpeedEventUnit>();
    public bool IsZoneEventSelected      => AllSelectedAre<ZoneEventUnit>();
    public bool IsParentEventSelected    => AllSelectedAre<ParentEventUnit>();

    /// <summary> 重定位按钮仅单选时可用 </summary>
    public bool CanRelocate => _selectedUnits.Count == 1;

    /// <summary> 轨道重定位按钮是否显示：轨道模式显示，类型模式隐藏 </summary>
    [ObservableProperty]
    private bool _canRelocateLane = true;

    /// <summary> 轨道重定位按钮最终可见性：类型模式隐藏；多选时所有重定位按钮统一隐藏 </summary>
    public bool CanShowRelocateLane => CanRelocate && CanRelocateLane;

    partial void OnCanRelocateLaneChanged(bool value)
        => OnPropertyChanged(nameof(CanShowRelocateLane));

    /// <summary> 跳转按钮仅单选时可用（多选无法确定跳转目标） </summary>
    public bool CanJump => _selectedUnits.Count == 1;

    /// <summary> TuneList 是否显示：仅单选 Dash 时显示（非 Dash 的 Tune 数据仍保留，只是不展示） </summary>
    public bool IsTuneSectionVisible =>
        SelectedUnit is NoteUnit note && note.Data.Type == CQMusicGame.Shared.NoteType.Dash;

    private List<TuneRowItem> _tuneRows = new();

    /// <summary> 是否存在 Tune 行（空列表时隐藏 ItemsControl，避免多出一段行间距） </summary>
    public bool HasTuneRows => _tuneRows.Count > 0;

    /// <summary> 当前选中 Note 的 Tune 表格行（按 TargetTime 升序，编号从 1 开始） </summary>
    public List<TuneRowItem> TuneRows
    {
        get => _tuneRows;
        private set
        {
            _tuneRows = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasTuneRows));
        }
    }

    /// <summary> 选中集合非空且全部为 T 类型 </summary>
    private bool AllSelectedAre<T>() where T : class
    {
        if (_selectedUnits.Count == 0) return false;
        foreach (var unit in _selectedUnits)
            if (unit is not T) return false;
        return true;
    }

    /// <summary> 取选中集合里所有 T 类型的 unit </summary>
    private List<T> SelectedUnitsOf<T>() where T : class
    {
        var list = new List<T>();
        foreach (var unit in _selectedUnits)
            if (unit is T t) list.Add(t);
        return list;
    }

    /// <summary> 全体一致取值：全部相同返回该值，否则 null（界面显示为空白）。空集合也返回 null </summary>
    private static TV? CommonValue<TU, TV>(List<TU> units, System.Func<TU, TV> get) where TV : struct
    {
        if (units.Count == 0) return null;
        var first = get(units[0]);
        for (int i = 1; i < units.Count; i++)
            if (!EqualityComparer<TV>.Default.Equals(first, get(units[i]))) return null;
        return first;
    }

    /// <summary> 引用类型的全体一致取值（曲线种类下拉项用；CurveKindItem 是 record，按值比较） </summary>
    private static TV? CommonRefValue<TU, TV>(List<TU> units, System.Func<TU, TV?> get) where TV : class
    {
        if (units.Count == 0) return null;
        var first = get(units[0]);
        for (int i = 1; i < units.Count; i++)
            if (!Equals(first, get(units[i]))) return null;
        return first;
    }

    /// <summary> 强制刷新所有绑定属性（撤销/重做后数据回退时调用） </summary>
    public void RefreshDisplay() => OnSelectedUnitChanged();

    /// <summary> 语言开关变化后刷新代码拼接文案与下拉选项（DynamicResource 文案自动更新） </summary>
    public void RefreshLocalization()
    {
        _speedModeOptions = BuildSpeedModeOptions();
        OnPropertyChanged(nameof(SpeedModeOptions));
        OnPropertyChanged(nameof(SpeedMode));
        OnPropertyChanged(nameof(EventTypeLabel));
        RebuildTuneRows();

        TransformXCurve?.RefreshLocalization();
        TransformYCurve?.RefreshLocalization();
        TransformRotationCurve?.RefreshLocalization();
        OpacityCurve?.RefreshLocalization();
        LengthCurve?.RefreshLocalization();
        SpeedCurve?.RefreshLocalization();
    }

    /// <summary> 提交暂存的编辑命令到撤销栈（若有），并清空暂存 </summary>
    public void CommitPendingEdit()
    {
        if (_pendingCommands.Count == 0) return;
        // 多条命令合并成一条复合命令（批量编辑在撤销池里只占一条）；
        // 传副本是因为复合命令会持有该列表，而下面还要清空暂存
        OnCommitEdit?.Invoke(_pendingCommands.Count == 1
            ? _pendingCommands[0]
            : new CompositeCommand(new List<IUndoableCommand>(_pendingCommands)));
        _pendingCommands.Clear();
        _noteEditOldData.Clear();
        _bpmEditOldData.Clear();
        _eventEditOldData.Clear();
        _tuneEditOldData = null;
    }

    // === 重定位命令：只发出请求，由 ChartVM 按当前选中类型分发到对应编辑器 ===

    [RelayCommand]
    private void RelocateTime() => OnRelocateRequested?.Invoke(RelocateField.Time);

    [RelayCommand]
    private void RelocateLane() => OnRelocateRequested?.Invoke(RelocateField.Lane);

    [RelayCommand]
    private void RelocateDuration() => OnRelocateRequested?.Invoke(RelocateField.Duration);

    /// <summary> 跳转到当前选中 unit 的时间点（Note/Tune 取 TargetTime，BPM/Motion 取 StartTime） </summary>
    [RelayCommand]
    private void JumpToTime()
    {
        int? time = SelectedUnit switch
        {
            NoteUnit note     => note.Data.TargetTime,
            TuneUnit tune     => tune.Data.TargetTime,
            BpmUnit bpm       => bpm.Data.StartTime,
            ILaneEventUnit e  => e.StartTime,
            _                 => null
        };
        if (time is { } t) OnJumpRequested?.Invoke(t);
    }

    /// <summary> Tune 表格行按钮：选中该 Tune（只改变选择，不移动时间轴） </summary>
    [RelayCommand]
    private void SelectTune(TuneUnit? tune)
    {
        if (tune == null) return;
        OnSelectTuneRequested?.Invoke(tune);
    }

    /// <summary> Tune 表格 AddTune 按钮：为当前选中 Note 添加一个 Tune，由 ChartVM 创建并自动选中 </summary>
    [RelayCommand]
    private void AddTune()
    {
        if (SelectedUnit is NoteUnit note) OnAddTuneRequested?.Invoke(note);
    }

    /// <summary> Tune 行内 × 按钮：删除该 Tune，由 ChartVM 记录撤销并刷新 </summary>
    [RelayCommand]
    private void DeleteTune(TuneUnit? tune)
    {
        if (tune == null) return;
        OnDeleteTuneRequested?.Invoke(tune);
    }

    /// <summary> Tune 面板 Back 按钮：回到该 Tune 的父 Note（只切换选中，不移动时间轴） </summary>
    [RelayCommand]
    private void BackToParentNote()
    {
        if (SelectedUnit is TuneUnit { Parent: { } parent })
            OnSelectNoteRequested?.Invoke(parent);
    }

    /// <summary>
    /// 重建 Tune 表格行：按 TargetTime 升序排列（不改动 TuneList 本身，保证显示与保存后的文件顺序一致）。
    /// LINQ OrderBy 是稳定排序，同时间的 Tune 保持原有相对顺序
    /// </summary>
    private void RebuildTuneRows()
    {
        if (SelectedUnit is not NoteUnit { TuneList: { } tuneList })
        {
            TuneRows = new List<TuneRowItem>();
            return;
        }

        TuneRows = tuneList
            .OrderBy(t => t.Data.TargetTime)
            .Select((t, index) => new TuneRowItem
            {
                Tune = t,
                Number = index + 1,
                TimeMs = t.Data.TargetTime
            })
            .ToList();
    }

    private void OnSelectedUnitChanged()
    {
        // 重建期间屏蔽曲线行/勾选框的写回：复用视觉树换 DataContext 时，
        // ComboBox 等控件可能把上一个 unit 的选中项写到新 row 上（详见 _suppressEdits）
        bool previousSuppress = _suppressEdits;
        _suppressEdits = true;
        try
        {
            OnPropertyChanged(nameof(IsNoteSelected));
            OnPropertyChanged(nameof(IsTuneSelected));
            OnPropertyChanged(nameof(IsBpmSelected));
            OnPropertyChanged(nameof(IsEventSelected));
            OnPropertyChanged(nameof(IsTransformEventSelected));
            OnPropertyChanged(nameof(IsOpacityEventSelected));
            OnPropertyChanged(nameof(IsLengthEventSelected));
            OnPropertyChanged(nameof(IsSpeedEventSelected));
            OnPropertyChanged(nameof(IsZoneEventSelected));
            OnPropertyChanged(nameof(IsParentEventSelected));
            OnPropertyChanged(nameof(CanRelocate));
            OnPropertyChanged(nameof(CanShowRelocateLane));
            OnPropertyChanged(nameof(CanJump));
            OnPropertyChanged(nameof(IsTuneSectionVisible));
            RebuildTuneRows();
            OnPropertyChanged(nameof(NoteType));
            OnPropertyChanged(nameof(NoteLaneIndex));
            OnPropertyChanged(nameof(NoteTargetTime));
            OnPropertyChanged(nameof(NoteSustainDuration));
            OnPropertyChanged(nameof(NoteAppearOffset));
            OnPropertyChanged(nameof(NoteFake));
            OnPropertyChanged(nameof(TuneTargetTime));
            OnPropertyChanged(nameof(TuneUseRelative));
            OnPropertyChanged(nameof(TuneTargetLane));
            OnPropertyChanged(nameof(TunePosValueX));
            OnPropertyChanged(nameof(TunePosValueY));
            OnPropertyChanged(nameof(TuneDuration));
            OnPropertyChanged(nameof(BpmStartTime));
            OnPropertyChanged(nameof(BpmValue));

            NotifyEventProperties();
            RebuildEventCurveRows();
        }
        finally
        {
            _suppressEdits = previousSuppress;
        }
    }

    /// <summary> 重通知全部 LaneEvent 属性（撤销/重做、选中切换、多选取值变化后调用） </summary>
    private void NotifyEventProperties()
    {
        OnPropertyChanged(nameof(EventTypeLabel));
        OnPropertyChanged(nameof(EventHasDuration));
        OnPropertyChanged(nameof(EventLaneIndex));
        OnPropertyChanged(nameof(EventStartTime));
        OnPropertyChanged(nameof(EventDuration));
        OnPropertyChanged(nameof(TransformAnchorX));
        OnPropertyChanged(nameof(TransformAnchorY));
        OnPropertyChanged(nameof(TransformXEnabled));
        OnPropertyChanged(nameof(TransformYEnabled));
        OnPropertyChanged(nameof(TransformRotationEnabled));
        OnPropertyChanged(nameof(TransformXCurveVisible));
        OnPropertyChanged(nameof(TransformYCurveVisible));
        OnPropertyChanged(nameof(TransformRotationCurveVisible));
        OnPropertyChanged(nameof(OpacityTargetHead));
        OnPropertyChanged(nameof(OpacityTargetLine));
        OnPropertyChanged(nameof(OpacityTargetLabel));
        OnPropertyChanged(nameof(OpacityTargetNote));
        OnPropertyChanged(nameof(SpeedMode));
        OnPropertyChanged(nameof(ZoneTarget));
        OnPropertyChanged(nameof(ParentLaneValue));
    }

    // /// <summary> 重通知全部 Motion 动画属性（撤销/重做、勾选或曲线变化后调用） </summary>
    // private void NotifyMotionProperties() { ... }

    #region Note 属性

    // 多选取值规则：全体选中项该属性相同才显示该值（否则 getter 返回 null → 界面留空）；
    // 输入后把该值应用到全部选中项。setter 收到 null 直接忽略（清空输入框不回写）

    /// <summary>
    /// 对全部选中的 NoteUnit 应用一次编辑：首次编辑为每个 unit 锁定旧值快照（撤销回到编辑会话开始前），
    /// 每次输入以当前数据为基准重算新数据，并重建整批待提交命令
    /// </summary>
    private void EditNotes(System.Func<NoteData, NoteData> mutate)
    {
        var commands = new List<IUndoableCommand>();
        foreach (var noteUnit in SelectedUnitsOf<NoteUnit>())
        {
            if (!_noteEditOldData.TryGetValue(noteUnit, out var oldData))
            {
                oldData = noteUnit.Data;
                _noteEditOldData[noteUnit] = oldData;
            }
            var command = new EditNoteCommand(noteUnit, oldData, mutate(noteUnit.Data));
            command.Execute();
            commands.Add(command);
        }
        _pendingCommands = commands;
    }

    public NoteType? NoteType
    {
        get => CommonValue(SelectedUnitsOf<NoteUnit>(), u => u.Data.Type);
        set
        {
            if (value is not { } v) return;
            EditNotes(d => { d.Type = v; return d; });
            OnPropertyChanged();
            OnPropertyEdited?.Invoke();
        }
    }

    /// <summary> 当前项目的轨道集合：Inspector 只允许输入已存在的轨道编号 </summary>
    public LaneCatalogModel? LaneCatalog { get; set; }

    public int? NoteLaneIndex
    {
        get => CommonValue(SelectedUnitsOf<NoteUnit>(), u => u.Data.LaneIndex);
        set
        {
            if (value is not { } v) return;
            // 只允许已存在的轨道；非法输入不应用，TextBox 回退到当前值
            if (LaneCatalog == null || !LaneCatalog.Contains(v))
            {
                OnPropertyChanged(nameof(NoteLaneIndex));
                return;
            }
            EditNotes(d => { d.LaneIndex = v; return d; });
            OnPropertyChanged();
            OnPropertyEdited?.Invoke();
        }
    }

    // 判定轨相关属性（NoteJudgeIndependent / NoteJudgeLaneIndex）随 Shared 的 JudgeLaneIndex 删除。
    // 新模型只有单一 LaneIndex，判定区 JudgeZone 由 ChartSheet 在运行时派生，制谱器不读写。

    public int? NoteTargetTime
    {
        get => CommonValue(SelectedUnitsOf<NoteUnit>(), u => u.Data.TargetTime);
        set
        {
            if (value is not { } v) return;
            EditNotes(d => { d.TargetTime = v; return d; });
            OnPropertyChanged();
            OnPropertyEdited?.Invoke();
        }
    }

    public int? NoteSustainDuration
    {
        get => CommonValue(SelectedUnitsOf<NoteUnit>(), u => u.Data.SustainDuration);
        set
        {
            if (value is not { } v) return;
            EditNotes(d => { d.SustainDuration = v; return d; });
            OnPropertyChanged();
            OnPropertyEdited?.Invoke();
        }
    }

    public int? NoteAppearOffset
    {
        get => CommonValue(SelectedUnitsOf<NoteUnit>(), u => u.Data.AppearOffset);
        set
        {
            if (value is not { } v) return;
            EditNotes(d => { d.AppearOffset = v; return d; });
            OnPropertyChanged();
            OnPropertyEdited?.Invoke();
        }
    }

    /// <summary>
    /// Fake 开关：false/null = 普通 Note，true = FakeMode.Default。
    /// 多选时全体一致才显示勾选（三态）；setter 收到 null（不确定态）直接忽略
    /// </summary>
    public bool? NoteFake
    {
        get
        {
            var units = SelectedUnitsOf<NoteUnit>();
            if (units.Count == 0) return null;

            bool first = units[0].Data.Fake.HasValue;
            for (int i = 1; i < units.Count; i++)
                if (units[i].Data.Fake.HasValue != first) return null;
            return first;
        }
        set
        {
            if (value is not { } v) return;
            EditNotes(d =>
            {
                d.Fake = v ? FakeMode.Default : null;
                return d;
            });
            OnPropertyChanged();
            OnPropertyEdited?.Invoke();
        }
    }

    #endregion

    #region Tune 属性

    /// <summary> 对当前选中的 TuneUnit 应用一次编辑（Tune 不参与多选，固定单选路径） </summary>
    private void EditTune(System.Func<TuneData, TuneData> mutate)
    {
        if (SelectedUnit is not TuneUnit tuneUnit) return;
        _tuneEditOldData ??= tuneUnit.Data; // 首次编辑锁定旧值，会话内复用
        var command = new EditTuneCommand(tuneUnit, _tuneEditOldData.Value, mutate(tuneUnit.Data));
        command.Execute();
        _pendingCommands = new List<IUndoableCommand> { command };
    }

    public int TuneTargetTime
    {
        get => (SelectedUnit as TuneUnit)?.Data.TargetTime ?? 0;
        set
        {
            EditTune(d => { d.TargetTime = value; return d; });
            OnPropertyChanged();
            OnPropertyEdited?.Invoke();
        }
    }

    public bool TuneUseRelative
    {
        get => (SelectedUnit as TuneUnit)?.Data.UseRelative ?? true;
        set
        {
            EditTune(d => { d.UseRelative = value; return d; });
            OnPropertyChanged();
            OnPropertyEdited?.Invoke();
        }
    }

    public int TuneTargetLane
    {
        get => (SelectedUnit as TuneUnit)?.Data.TargetLane ?? 0;
        set
        {
            // 只允许已存在的轨道；非法输入不应用，TextBox 回退到当前值
            if (LaneCatalog == null || !LaneCatalog.Contains(value))
            {
                OnPropertyChanged(nameof(TuneTargetLane));
                return;
            }
            EditTune(d => { d.TargetLane = value; return d; });
            OnPropertyChanged();
            OnPropertyEdited?.Invoke();
        }
    }

    public float TunePosValueX
    {
        get => (SelectedUnit as TuneUnit)?.Data.PosValue.x ?? 0;
        set
        {
            EditTune(d => { d.PosValue.x = value; return d; });
            OnPropertyChanged();
            OnPropertyEdited?.Invoke();
        }
    }

    public float TunePosValueY
    {
        get => (SelectedUnit as TuneUnit)?.Data.PosValue.y ?? 0;
        set
        {
            EditTune(d => { d.PosValue.y = value; return d; });
            OnPropertyChanged();
            OnPropertyEdited?.Invoke();
        }
    }

    public int TuneDuration
    {
        get => (SelectedUnit as TuneUnit)?.Data.Duration ?? 0;
        set
        {
            EditTune(d => { d.Duration = value; return d; });
            OnPropertyChanged();
            OnPropertyEdited?.Invoke();
        }
    }

    #endregion

    #region Bpm 属性

    /// <summary> 对全部选中的 BpmUnit 应用一次编辑（快照规则同 Note） </summary>
    private void EditBpms(System.Func<BpmData, BpmData> mutate)
    {
        var commands = new List<IUndoableCommand>();
        foreach (var bpmUnit in SelectedUnitsOf<BpmUnit>())
        {
            if (!_bpmEditOldData.TryGetValue(bpmUnit, out var oldData))
            {
                oldData = bpmUnit.Data;
                _bpmEditOldData[bpmUnit] = oldData;
            }
            var command = new EditBpmCommand(bpmUnit, oldData, mutate(bpmUnit.Data));
            command.Execute();
            commands.Add(command);
        }
        _pendingCommands = commands;
    }

    public int? BpmStartTime
    {
        get => CommonValue(SelectedUnitsOf<BpmUnit>(), u => u.Data.StartTime);
        set
        {
            if (value is not { } v) return;
            EditBpms(d => { d.StartTime = v; return d; });
            OnPropertyChanged();
            OnPropertyEdited?.Invoke();
        }
    }

    public float? BpmValue
    {
        get => CommonValue(SelectedUnitsOf<BpmUnit>(), u => u.Data.Bpm);
        set
        {
            if (value is not { } v) return;

            // 负值钳到 0：负 BPM 会让节拍间隔变负，BeatGridModel 的 while 循环死锁
            if (v < 0) v = 0;

            EditBpms(d => { d.Bpm = v; return d; });
            OnPropertyChanged();
            OnPropertyEdited?.Invoke();
        }
    }

    #endregion

    #region LaneEvent 属性

    /// <summary> 事件面板顶部提示：汉化开显示中文简称，否则显示 LaneEvent.Transform 原样 </summary>
    public string? EventTypeLabel
    {
        get
        {
            var units = SelectedUnitsOf<ILaneEventUnit>();
            if (units.Count == 0) return null;
            LaneEventKind kind = units[0].Kind;
            return InspectorLocalization.IsInspectorLocalized
                ? LaneEventKindInfo.GetLabel(kind)
                : $"LaneEvent.{kind}";
        }
    }

    /// <summary> 选中集合是否全部是事件且都带持续时间 </summary>
    public bool EventHasDuration
    {
        get
        {
            var units = SelectedUnitsOf<ILaneEventUnit>();
            if (units.Count == 0) return false;
            foreach (var unit in units)
                if (!unit.HasDuration) return false;
            return true;
        }
    }

    /// <summary>
    /// 对全部选中的 T 类型事件应用一次编辑。
    /// 首次编辑为每个 unit 锁定旧值快照（撤销回到编辑会话开始前）。
    /// </summary>
    private void EditEvents<T>(System.Action<T> mutate) where T : class, ILaneEventUnit
    {
        var commands = new List<IUndoableCommand>();
        foreach (var unit in SelectedUnitsOf<T>())
        {
            if (!_eventEditOldData.TryGetValue(unit, out var oldData))
            {
                oldData = unit.CaptureData();
                _eventEditOldData[unit] = oldData;
            }

            mutate(unit);
            var newData = unit.CaptureData();
            commands.Add(new EditLaneEventCommand(unit, oldData, newData));
        }
        _pendingCommands = commands;
    }

    public int? EventLaneIndex
    {
        get => CommonValue(SelectedUnitsOf<ILaneEventUnit>(), u => u.LaneIndex);
        set
        {
            if (value is not { } v) return;
            if (LaneCatalog == null || !LaneCatalog.Contains(v))
            {
                OnPropertyChanged(nameof(EventLaneIndex));
                return;
            }
            EditEvents<ILaneEventUnit>(u => u.LaneIndex = v);
            OnPropertyChanged();
            OnPropertyEdited?.Invoke();
        }
    }

    public int? EventStartTime
    {
        get => CommonValue(SelectedUnitsOf<ILaneEventUnit>(), u => u.StartTime);
        set
        {
            if (value is not { } v) return;
            EditEvents<ILaneEventUnit>(u => u.StartTime = v);
            OnPropertyChanged();
            OnPropertyEdited?.Invoke();
        }
    }

    public int? EventDuration
    {
        get => EventHasDuration
            ? CommonValue(SelectedUnitsOf<ILaneEventUnit>(), u => u.Duration)
            : null;
        set
        {
            if (value is not { } v || !EventHasDuration) return;
            EditEvents<ILaneEventUnit>(u => u.Duration = v);
            OnPropertyChanged();
            OnPropertyEdited?.Invoke();
        }
    }

    // ---- Transform ----

    public float? TransformAnchorX
    {
        get => CommonValue(SelectedUnitsOf<TransformEventUnit>(), u => u.Data.Anchor.x);
        set
        {
            if (value is not { } v) return;
            EditEvents<TransformEventUnit>(u =>
            {
                var d = u.Data;
                d.Anchor.x = v;
                u.Data = d;
            });
            OnPropertyChanged();
            OnPropertyEdited?.Invoke();
        }
    }

    public float? TransformAnchorY
    {
        get => CommonValue(SelectedUnitsOf<TransformEventUnit>(), u => u.Data.Anchor.y);
        set
        {
            if (value is not { } v) return;
            EditEvents<TransformEventUnit>(u =>
            {
                var d = u.Data;
                d.Anchor.y = v;
                u.Data = d;
            });
            OnPropertyChanged();
            OnPropertyEdited?.Invoke();
        }
    }

    /// <summary> 全体选中项都具备该曲线才返回 true；多选混合或空集合返回 false（界面显示为不勾选） </summary>
    private bool CurveEnabledForAll<T>(System.Func<T, bool> has) where T : class, ILaneEventUnit
    {
        var units = SelectedUnitsOf<T>();
        if (units.Count == 0) return false;
        foreach (var unit in units)
            if (!has(unit)) return false;
        return true;
    }

    // 勾选语义：已有该曲线的 unit 数据保持原样，只给缺少曲线的 unit 创建默认曲线。
    // 多选混合（有的有、有的没有）显示为不勾选，此时点击一次即全部勾选

    public bool TransformXEnabled
    {
        get => CurveEnabledForAll<TransformEventUnit>(u => u.Data.XCurve.HasValue);
        set
        {
            if (_suppressEdits) return; // 重建期间的回写不是用户操作，丢弃
            EditEvents<TransformEventUnit>(u =>
            {
                var d = u.Data;
                if (value)
                {
                    if (!d.XCurve.HasValue)
                        d.XCurve = ChartDefaults.CreateCurve(
                            OnSampleLaneState?.Invoke(d.LaneIndex, d.StartTime) is { } s ? s.Position.x : 0f);
                }
                else d.XCurve = null;
                u.Data = d;
            });
            OnPropertyChanged();
            OnPropertyChanged(nameof(TransformXCurveVisible));
            TransformXCurve?.Refresh();
            OnPropertyEdited?.Invoke();
        }
    }

    public bool TransformYEnabled
    {
        get => CurveEnabledForAll<TransformEventUnit>(u => u.Data.YCurve.HasValue);
        set
        {
            if (_suppressEdits) return; // 重建期间的回写不是用户操作，丢弃
            EditEvents<TransformEventUnit>(u =>
            {
                var d = u.Data;
                if (value)
                {
                    if (!d.YCurve.HasValue)
                        d.YCurve = ChartDefaults.CreateCurve(
                            OnSampleLaneState?.Invoke(d.LaneIndex, d.StartTime) is { } s ? s.Position.y : 0f);
                }
                else d.YCurve = null;
                u.Data = d;
            });
            OnPropertyChanged();
            OnPropertyChanged(nameof(TransformYCurveVisible));
            TransformYCurve?.Refresh();
            OnPropertyEdited?.Invoke();
        }
    }

    public bool TransformRotationEnabled
    {
        get => CurveEnabledForAll<TransformEventUnit>(u => u.Data.RotationCurve.HasValue);
        set
        {
            if (_suppressEdits) return; // 重建期间的回写不是用户操作，丢弃
            EditEvents<TransformEventUnit>(u =>
            {
                var d = u.Data;
                if (value)
                {
                    if (!d.RotationCurve.HasValue)
                        d.RotationCurve = ChartDefaults.CreateCurve(
                            OnSampleLaneState?.Invoke(d.LaneIndex, d.StartTime) is { } s ? s.Rotation : 0f);
                }
                else d.RotationCurve = null;
                u.Data = d;
            });
            OnPropertyChanged();
            OnPropertyChanged(nameof(TransformRotationCurveVisible));
            TransformRotationCurve?.Refresh();
            OnPropertyEdited?.Invoke();
        }
    }

    // ---- 曲线行 ----

    private ChannelCurveRow? _transformXCurve;
    private ChannelCurveRow? _transformYCurve;
    private ChannelCurveRow? _transformRotationCurve;
    private ChannelCurveRow? _opacityCurve;
    private ChannelCurveRow? _lengthCurve;
    private ChannelCurveRow? _speedCurve;

    public ChannelCurveRow? TransformXCurve
    {
        get => _transformXCurve;
        private set { _transformXCurve = value; OnPropertyChanged(); }
    }

    public ChannelCurveRow? TransformYCurve
    {
        get => _transformYCurve;
        private set { _transformYCurve = value; OnPropertyChanged(); }
    }

    public ChannelCurveRow? TransformRotationCurve
    {
        get => _transformRotationCurve;
        private set { _transformRotationCurve = value; OnPropertyChanged(); }
    }

    public ChannelCurveRow? OpacityCurve
    {
        get => _opacityCurve;
        private set { _opacityCurve = value; OnPropertyChanged(); }
    }

    public ChannelCurveRow? LengthCurve
    {
        get => _lengthCurve;
        private set { _lengthCurve = value; OnPropertyChanged(); }
    }

    public ChannelCurveRow? SpeedCurve
    {
        get => _speedCurve;
        private set { _speedCurve = value; OnPropertyChanged(); }
    }

    // 只在全体选中项都有该曲线时展开曲线编辑行
    public bool TransformXCurveVisible => TransformXEnabled;
    public bool TransformYCurveVisible => TransformYEnabled;
    public bool TransformRotationCurveVisible => TransformRotationEnabled;

    private void RebuildEventCurveRows()
    {
        // 先全部置空再赋新行：ContentPresenter 对同类型 Content 会复用 DataTemplate
        // 视觉树（DataTemplate 实现了 IRecyclingDataTemplate，只换 DataContext），
        // 导致行内 ComboBox 把上一个 unit 的选中项带到新 unit 上并回写成曲线数据。
        // 置 null 会让 ContentPresenter 移除旧 child，下一行赋值时重新构建视觉树
        TransformXCurve = null;
        TransformYCurve = null;
        TransformRotationCurve = null;
        OpacityCurve = null;
        LengthCurve = null;
        SpeedCurve = null;

        TransformXCurve = CreateCurveRow(
            SelectedUnitsOf<TransformEventUnit>(),
            u => u.Data.XCurve,
            (u, c) => { var d = u.Data; d.XCurve = c; u.Data = d; });

        TransformYCurve = CreateCurveRow(
            SelectedUnitsOf<TransformEventUnit>(),
            u => u.Data.YCurve,
            (u, c) => { var d = u.Data; d.YCurve = c; u.Data = d; });

        TransformRotationCurve = CreateCurveRow(
            SelectedUnitsOf<TransformEventUnit>(),
            u => u.Data.RotationCurve,
            (u, c) => { var d = u.Data; d.RotationCurve = c; u.Data = d; });

        // Opacity 数值范围 0-1：拖拽步长 0.001（鼠标位移 ÷1000），并钳制在 0-1 内
        OpacityCurve = CreateCurveRow(
            SelectedUnitsOf<OpacityEventUnit>(),
            u => u.Data.ValueCurve,
            (u, c) => { var d = u.Data; d.ValueCurve = c; u.Data = d; },
            dragStep: 0.001f, dragMin: 0f, dragMax: 1f);

        LengthCurve = CreateCurveRow(
            SelectedUnitsOf<LengthEventUnit>(),
            u => u.Data.ValueCurve,
            (u, c) => { var d = u.Data; d.ValueCurve = c; u.Data = d; });

        // Speed：Fixed 按常值拖拽（同 anchor）；Multiplier 是倍率，步长缩到 0.001（位移 ÷1000），不钳制
        SpeedCurve = CreateCurveRow(
            SelectedUnitsOf<SpeedEventUnit>(),
            u => u.Data.ValueCurve,
            (u, c) => { var d = u.Data; d.ValueCurve = c; u.Data = d; },
            dragStepProvider: () => SpeedMode?.Mode == LaneSpeedMode.Multiplier ? 0.001f : 0.1f);
    }

    private ChannelCurveRow? CreateCurveRow<T>(
        List<T> units,
        System.Func<T, ChannelCurve?> get,
        System.Action<T, ChannelCurve> set,
        float dragStep = 0.1f,
        float? dragMin = null,
        float? dragMax = null,
        System.Func<float>? dragStepProvider = null) where T : class, ILaneEventUnit
    {
        if (units.Count == 0) return null;

        return new ChannelCurveRow(
            () => CommonCurve(units, get),
            curve =>
            {
                if (_suppressEdits) return; // 重建期间的回写不是用户操作，丢弃
                EditEvents<T>(u => set(u, curve));
                OnPropertyEdited?.Invoke();
            },
            dragStep, dragMin, dragMax, dragStepProvider);
    }

    private static ChannelCurve? CommonCurve<T>(
        List<T> units, System.Func<T, ChannelCurve?> get) where T : class, ILaneEventUnit
    {
        if (units.Count == 0) return null;

        var first = get(units[0]);
        for (int i = 1; i < units.Count; i++)
            if (!System.Nullable.Equals(first, get(units[i]))) return null;
        return first;
    }

    // ---- Opacity ----

    private bool? OpacityTargetEnabled(OpacityTarget target)
    {
        var units = SelectedUnitsOf<OpacityEventUnit>();
        if (units.Count == 0) return null;

        bool first = (units[0].Data.Targets & target) != 0;
        for (int i = 1; i < units.Count; i++)
            if (((units[i].Data.Targets & target) != 0) != first) return null;
        return first;
    }

    private void SetOpacityTarget(OpacityTarget target, bool value)
    {
        EditEvents<OpacityEventUnit>(u =>
        {
            var d = u.Data;
            if (value) d.Targets |= target;
            else d.Targets &= ~target;
            u.Data = d;
        });
        OnPropertyEdited?.Invoke();
    }

    public bool? OpacityTargetHead
    {
        get => OpacityTargetEnabled(OpacityTarget.Head);
        set
        {
            if (value is not { } v) return;
            SetOpacityTarget(OpacityTarget.Head, v);
            OnPropertyChanged();
        }
    }

    public bool? OpacityTargetLine
    {
        get => OpacityTargetEnabled(OpacityTarget.Line);
        set
        {
            if (value is not { } v) return;
            SetOpacityTarget(OpacityTarget.Line, v);
            OnPropertyChanged();
        }
    }

    public bool? OpacityTargetLabel
    {
        get => OpacityTargetEnabled(OpacityTarget.Label);
        set
        {
            if (value is not { } v) return;
            SetOpacityTarget(OpacityTarget.Label, v);
            OnPropertyChanged();
        }
    }

    public bool? OpacityTargetNote
    {
        get => OpacityTargetEnabled(OpacityTarget.Note);
        set
        {
            if (value is not { } v) return;
            SetOpacityTarget(OpacityTarget.Note, v);
            OnPropertyChanged();
        }
    }

    // ---- Speed ----

    private List<SpeedModeItem> _speedModeOptions = BuildSpeedModeOptions();

    /// <summary> 流速模式下拉项（受 Inspector 界面汉化控制） </summary>
    public IReadOnlyList<SpeedModeItem> SpeedModeOptions => _speedModeOptions;

    private static List<SpeedModeItem> BuildSpeedModeOptions() => new()
    {
        new(LaneSpeedMode.Multiplier, InspectorLocalization.Text("倍率", "Multiplier")),
        new(LaneSpeedMode.Fixed, InspectorLocalization.Text("固定", "Fixed")),
    };

    public SpeedModeItem? SpeedMode
    {
        get => CommonValue(SelectedUnitsOf<SpeedEventUnit>(), u => u.Data.SpeedMode) is { } mode
            ? _speedModeOptions.FirstOrDefault(item => item.Mode == mode)
            : null;
        set
        {
            if (value is not { } v) return;
            EditEvents<SpeedEventUnit>(u =>
            {
                var d = u.Data;
                d.SpeedMode = v.Mode;
                u.Data = d;
            });
            // Fixed/Multiplier 切换会改变曲线拖拽步长，刷新 Speed 曲线行的按钮绑定
            SpeedCurve?.Refresh();
            OnPropertyChanged();
            OnPropertyEdited?.Invoke();
        }
    }

    // ---- Zone ----

    public static List<JudgeZone> JudgeZoneOptions { get; } = new()
    {
        JudgeZone.All, JudgeZone.Left, JudgeZone.Right,
        JudgeZone.L1, JudgeZone.L2, JudgeZone.R1, JudgeZone.R2
    };

    public JudgeZone? ZoneTarget
    {
        get => CommonValue(SelectedUnitsOf<ZoneEventUnit>(), u => u.Data.TargetZone);
        set
        {
            if (value is not { } v) return;
            EditEvents<ZoneEventUnit>(u =>
            {
                var d = u.Data;
                d.TargetZone = v;
                u.Data = d;
            });
            OnPropertyChanged();
            OnPropertyEdited?.Invoke();
        }
    }

    // ---- Parent ----

    public int? ParentLaneValue
    {
        get
        {
            var units = SelectedUnitsOf<ParentEventUnit>();
            if (units.Count == 0) return null;

            var first = units[0].Data.ParentLane;
            for (int i = 1; i < units.Count; i++)
                if (units[i].Data.ParentLane != first) return null;
            return first;
        }
        set
        {
            if (value is { } v && (LaneCatalog == null || !LaneCatalog.Contains(v)))
            {
                OnPropertyChanged();
                return;
            }

            EditEvents<ParentEventUnit>(u =>
            {
                var d = u.Data;
                d.ParentLane = value;
                u.Data = d;
            });
            OnPropertyChanged();
            OnPropertyEdited?.Invoke();
        }
    }

    #endregion

    // Motion 属性编辑本轮停用（LaneEvent 编辑下一波接入）。
    // 以下整段代码用 #if false 保留，作为后续改造参考。
#if false
    #region Motion 属性

    /// <summary>
    /// 对全部选中的 MotionUnit 应用一次编辑：mutate 返回 null 表示该 unit 不适用本次编辑
    /// （如没有对应动画），跳过不记命令也不锁快照；首次编辑为每个 unit 锁定旧值快照
    /// </summary>
    private void EditMotions(System.Func<LaneMotionData, LaneMotionData?> mutate)
    {
        var commands = new List<IUndoableCommand>();
        foreach (var motionUnit in SelectedUnitsOf<MotionUnit>())
        {
            var newData = mutate(motionUnit.Data);
            if (newData is not { } nd) continue;

            if (!_motionEditOldData.TryGetValue(motionUnit, out var oldData))
            {
                oldData = motionUnit.Data;
                _motionEditOldData[motionUnit] = oldData;
            }
            var command = new EditMotionCommand(motionUnit, oldData, nd);
            command.Execute();
            commands.Add(command);
        }
        _pendingCommands = commands;
        // 动画属性之间存在连带显示关系（勾选后展开细节、曲线种类决定方向框是否可见），
        // 且无参 OnPropertyChanged() 只会通知调用处的名字，所以这里统一重通知全部动画属性
        OnSelectedUnitChanged();
        OnPropertyEdited?.Invoke();
    }

    public int? MotionStartTime
    {
        get => CommonValue(SelectedUnitsOf<MotionUnit>(), u => u.Data.StartTime);
        set
        {
            if (value is not { } v) return;
            EditMotions(d => { d.StartTime = v; return d; });
        }
    }

    public int? MotionLaneIndex
    {
        get => CommonValue(SelectedUnitsOf<MotionUnit>(), u => u.Data.LaneIndex);
        set
        {
            if (value is not { } v) return;
            EditMotions(d => { d.LaneIndex = v; return d; });
        }
    }

    public int? MotionDuration
    {
        get => CommonValue(SelectedUnitsOf<MotionUnit>(), u => u.Data.Duration);
        set
        {
            if (value is not { } v) return;
            EditMotions(d => { d.Duration = v; return d; });
        }
    }

    public float? MotionAnchorX
    {
        get => CommonValue(SelectedUnitsOf<MotionUnit>(), u => u.Data.Anchor.x);
        set
        {
            if (value is not { } v) return;
            EditMotions(d => { d.Anchor.x = v; return d; });
        }
    }

    public float? MotionAnchorY
    {
        get => CommonValue(SelectedUnitsOf<MotionUnit>(), u => u.Data.Anchor.y);
        set
        {
            if (value is not { } v) return;
            EditMotions(d => { d.Anchor.y = v; return d; });
        }
    }

    #endregion

    #region Motion 动画属性

    // 存储结构：Data.Position / Data.Rotation / Data.Opacity 为可空 struct，null 表示不执行该动画。
    // 勾选 = 创建带默认值的动画；取消勾选 = 整个置 null。
    // 曲线拆成两个选择框：种类（Linear/Quad/...）+ 方向（In/Out/InOut），组合还原为 EaseCurve。
    //
    // 多选规则（与其余属性不同）：勾选框与子面板"全体启用才勾选并展开"，勾选时给缺该动画的
    // unit 创建默认动画；子字段（位移/角度/透明度/曲线等）仍按"全体相同才显示值"取值；
    // 曲线方向框"全体都是 Linear 才置灰"，有任一非 Linear 即可编辑

    /// <summary> 曲线选择第二框（方向）是否可见：任一动画使用了非 Linear 曲线即显示 </summary>
    public bool CurveDirectionVisible =>
        MotionPositionCurveDirection >= 0 ||
        MotionRotationCurveDirection >= 0 ||
        MotionOpacityCurveDirection >= 0;

    // ---- 多选取值辅助 ----

    /// <summary> 全体选中项都具备该动画才返回 true（多选时决定勾选框与子面板是否展开） </summary>
    private bool EnabledForAll(System.Func<MotionUnit, bool> has)
    {
        var units = SelectedUnitsOf<MotionUnit>();
        if (units.Count == 0) return false;
        foreach (var unit in units)
            if (!has(unit)) return false;
        return true;
    }

    /// <summary> 选中集合里具备指定动画的 MotionUnit 列表（曲线取值与方向框可用性判定用） </summary>
    private List<MotionUnit> MotionUnitsHaving(System.Func<MotionUnit, bool> has)
    {
        var list = new List<MotionUnit>();
        foreach (var unit in SelectedUnitsOf<MotionUnit>())
            if (has(unit)) list.Add(unit);
        return list;
    }

    /// <summary> 是否存在使用非 Linear 曲线的选中项（全体 Linear 时方向框置灰不可交互） </summary>
    private static bool HasNonLinear(List<MotionUnit> units, System.Func<MotionUnit, EaseCurve?> curveOf)
    {
        foreach (var unit in units)
        {
            var curve = curveOf(unit);
            if (curve.HasValue && EaseCurveKindExtensions.DirectionOf(curve.Value) != null) return true;
        }
        return false;
    }

    // ---- Position ----

    public bool MotionPositionEnabled
    {
        // 全体选中项都有 Position 才显示勾选并展开，有一个没有就不勾选不展开
        get => EnabledForAll(u => u.Data.Position.HasValue);
        set
        {
            if (value == MotionPositionEnabled) return; // 已全体一致，无需改动
            EditMotions(d =>
            {
                if (value)
                {
                    if (d.Position.HasValue) return null; // 已有该动画：跳过（不重复创建）
                    // 勾选创建默认动画（相对模式 + 位移 0,0），曲线默认 InOutQuad
                    d.Position = new PositionMotion { UseRelative = true, CurveMode = EaseCurve.InOutQuad };
                    return d;
                }
                if (!d.Position.HasValue) return null;
                d.Position = null;
                return d;
            });
        }
    }

    public float? MotionPositionX
    {
        get => CommonValue(MotionUnitsHaving(u => u.Data.Position.HasValue), u => u.Data.Position!.Value.Pos.x);
        set
        {
            if (value is not { } v) return;
            EditMotions(d =>
            {
                if (d.Position is not { } p) return null; // 没有该动画：跳过
                if (p.Pos.x == v) return null;
                p.Pos.x = v;
                d.Position = p;
                return d;
            });
        }
    }

    public float? MotionPositionY
    {
        get => CommonValue(MotionUnitsHaving(u => u.Data.Position.HasValue), u => u.Data.Position!.Value.Pos.y);
        set
        {
            if (value is not { } v) return;
            EditMotions(d =>
            {
                if (d.Position is not { } p) return null;
                if (p.Pos.y == v) return null;
                p.Pos.y = v;
                d.Position = p;
                return d;
            });
        }
    }

    /// <summary>
    /// 是否使用显式起始位置。勾选时为每个选中项采样其起点处的轨道状态作为 StartPos，
    /// 保证勾选瞬间画面与“未勾选时从上一动画状态采样”的结果一致
    /// </summary>
    public bool? MotionPositionUseStart
    {
        get => CommonValue(MotionUnitsHaving(u => u.Data.Position.HasValue),
                           u => u.Data.Position!.Value.UseStartValue);
        set
        {
            if (value is not { } v) return;
            EditMotions(d =>
            {
                if (d.Position is not { } p) return null;
                if (p.UseStartValue == v) return null;
                p.UseStartValue = v;
                if (v) p.StartPos = SampleLaneState(d)?.Position ?? Vec2.Zero;
                d.Position = p;
                return d;
            });
        }
    }

    /// <summary> 起始位置 X（仅启用了初始值的选中项参与取值/编辑） </summary>
    public float? MotionPositionStartX
    {
        get => CommonValue(MotionUnitsHaving(u => u.Data.Position?.UseStartValue == true),
                           u => u.Data.Position!.Value.StartPos.x);
        set
        {
            if (value is not { } v) return;
            EditMotions(d =>
            {
                if (d.Position is not { } p || !p.UseStartValue) return null;
                if (p.StartPos.x == v) return null;
                p.StartPos.x = v;
                d.Position = p;
                return d;
            });
        }
    }

    /// <summary> 起始位置 Y（仅启用了初始值的选中项参与取值/编辑） </summary>
    public float? MotionPositionStartY
    {
        get => CommonValue(MotionUnitsHaving(u => u.Data.Position?.UseStartValue == true),
                           u => u.Data.Position!.Value.StartPos.y);
        set
        {
            if (value is not { } v) return;
            EditMotions(d =>
            {
                if (d.Position is not { } p || !p.UseStartValue) return null;
                if (p.StartPos.y == v) return null;
                p.StartPos.y = v;
                d.Position = p;
                return d;
            });
        }
    }

    public bool? MotionPositionRelative
    {
        get => CommonValue(MotionUnitsHaving(u => u.Data.Position.HasValue), u => u.Data.Position!.Value.UseRelative);
        set
        {
            if (value is not { } v) return;
            EditMotions(d =>
            {
                if (d.Position is not { } p) return null;
                if (p.UseRelative == v) return null;
                p.UseRelative = v;
                d.Position = p;
                return d;
            });
        }
    }

    public CurveKindItem? MotionPositionCurveKind
    {
        get => CommonRefValue(MotionUnitsHaving(u => u.Data.Position.HasValue),
                              u => ItemOfCurve(u.Data.Position?.CurveMode));
        set
        {
            if (value == null) return;
            var kind = value.Kind;
            EditMotions(d =>
            {
                if (d.Position is not { } p) return null;
                // 换种类保留原方向；原为 Linear（无方向）则默认取 In
                var newCurve = EaseCurveKindExtensions.ToEaseCurve(kind, DirectionOrDefault(p.CurveMode));
                if (p.CurveMode == newCurve) return null;
                p.CurveMode = newCurve;
                d.Position = p;
                return d;
            });
        }
    }

    public int MotionPositionCurveDirection
    {
        get => CommonValue(MotionUnitsHaving(u => u.Data.Position.HasValue),
                           u => EaseCurveDirectionExtensions.ToIndex(DirectionOf(u.Data.Position?.CurveMode))) ?? -1;

        set
        {
            if (value < 0) return;
            var direction = EaseCurveDirectionExtensions.FromIndex(value);
            EditMotions(d =>
            {
                if (d.Position is not { } p) return null;
                // 只换方向时保留原种类；Linear 无方向，ToEaseCurve 会原样返回 Linear
                var newCurve = EaseCurveKindExtensions.ToEaseCurve(EaseCurveKindExtensions.BaseOf(p.CurveMode), direction);
                if (p.CurveMode == newCurve) return null;
                p.CurveMode = newCurve;
                d.Position = p;
                return d;
            });
        }
    }

    // ---- Rotation ----

    public bool MotionRotationEnabled
    {
        // 全体选中项都有 Rotation 才显示勾选并展开
        get => EnabledForAll(u => u.Data.Rotation.HasValue);
        set
        {
            if (value == MotionRotationEnabled) return;
            EditMotions(d =>
            {
                if (value)
                {
                    if (d.Rotation.HasValue) return null;
                    d.Rotation = new RotationMotion { UseRelative = true, CurveMode = EaseCurve.InOutQuad };
                    return d;
                }
                if (!d.Rotation.HasValue) return null;
                d.Rotation = null;
                return d;
            });
        }
    }

    public float? MotionRotationAngle
    {
        get => CommonValue(MotionUnitsHaving(u => u.Data.Rotation.HasValue), u => u.Data.Rotation!.Value.Angle);
        set
        {
            if (value is not { } v) return;
            EditMotions(d =>
            {
                if (d.Rotation is not { } r) return null;
                if (r.Angle == v) return null;
                r.Angle = v;
                d.Rotation = r;
                return d;
            });
        }
    }

    /// <summary> 是否使用显式起始角度。勾选时采样 Motion 起点处的轨道旋转作为 StartAngle </summary>
    public bool? MotionRotationUseStart
    {
        get => CommonValue(MotionUnitsHaving(u => u.Data.Rotation.HasValue),
                           u => u.Data.Rotation!.Value.UseStartValue);
        set
        {
            if (value is not { } v) return;
            EditMotions(d =>
            {
                if (d.Rotation is not { } r) return null;
                if (r.UseStartValue == v) return null;
                r.UseStartValue = v;
                if (v) r.StartAngle = SampleLaneState(d)?.Rotation ?? 0f;
                d.Rotation = r;
                return d;
            });
        }
    }

    /// <summary> 起始角度（仅启用了初始值的选中项参与取值/编辑） </summary>
    public float? MotionRotationStartAngle
    {
        get => CommonValue(MotionUnitsHaving(u => u.Data.Rotation?.UseStartValue == true),
                           u => u.Data.Rotation!.Value.StartAngle);
        set
        {
            if (value is not { } v) return;
            EditMotions(d =>
            {
                if (d.Rotation is not { } r || !r.UseStartValue) return null;
                if (r.StartAngle == v) return null;
                r.StartAngle = v;
                d.Rotation = r;
                return d;
            });
        }
    }

    public bool? MotionRotationRelative
    {
        get => CommonValue(MotionUnitsHaving(u => u.Data.Rotation.HasValue), u => u.Data.Rotation!.Value.UseRelative);
        set
        {
            if (value is not { } v) return;
            EditMotions(d =>
            {
                if (d.Rotation is not { } r) return null;
                if (r.UseRelative == v) return null;
                r.UseRelative = v;
                d.Rotation = r;
                return d;
            });
        }
    }

    public CurveKindItem? MotionRotationCurveKind
    {
        get => CommonRefValue(MotionUnitsHaving(u => u.Data.Rotation.HasValue),
                              u => ItemOfCurve(u.Data.Rotation?.CurveMode));
        set
        {
            if (value == null) return;
            var kind = value.Kind;
            EditMotions(d =>
            {
                if (d.Rotation is not { } r) return null;
                var newCurve = EaseCurveKindExtensions.ToEaseCurve(kind, DirectionOrDefault(r.CurveMode));
                if (r.CurveMode == newCurve) return null;
                r.CurveMode = newCurve;
                d.Rotation = r;
                return d;
            });
        }
    }

    public int MotionRotationCurveDirection
    {
        get => CommonValue(MotionUnitsHaving(u => u.Data.Rotation.HasValue),
                           u => EaseCurveDirectionExtensions.ToIndex(DirectionOf(u.Data.Rotation?.CurveMode))) ?? -1;

        set
        {
            if (value < 0) return;
            var direction = EaseCurveDirectionExtensions.FromIndex(value);
            EditMotions(d =>
            {
                if (d.Rotation is not { } r) return null;
                var newCurve = EaseCurveKindExtensions.ToEaseCurve(EaseCurveKindExtensions.BaseOf(r.CurveMode), direction);
                if (r.CurveMode == newCurve) return null;
                r.CurveMode = newCurve;
                d.Rotation = r;
                return d;
            });
        }
    }

    // ---- Opacity ----

    public bool MotionOpacityEnabled
    {
        // 全体选中项都有 Opacity 才显示勾选并展开
        get => EnabledForAll(u => u.Data.Opacity.HasValue);
        set
        {
            if (value == MotionOpacityEnabled) return;
            EditMotions(d =>
            {
                if (value)
                {
                    if (d.Opacity.HasValue) return null;
                    d.Opacity = new OpacityMotion { CurveMode = EaseCurve.InOutQuad };
                    return d;
                }
                if (!d.Opacity.HasValue) return null;
                d.Opacity = null;
                return d;
            });
        }
    }

    public float? MotionOpacityValue
    {
        get => CommonValue(MotionUnitsHaving(u => u.Data.Opacity.HasValue), u => u.Data.Opacity!.Value.Opacity);
        set
        {
            if (value is not { } v) return;
            EditMotions(d =>
            {
                if (d.Opacity is not { } o) return null;
                if (o.Opacity == v) return null;
                o.Opacity = v;
                d.Opacity = o;
                return d;
            });
        }
    }

    public bool? MotionOpacityAffectHead
    {
        get => CommonValue(MotionUnitsHaving(u => u.Data.Opacity.HasValue), u => u.Data.Opacity!.Value.AffectHead);
        set
        {
            if (value is not { } v) return;
            EditMotions(d =>
            {
                if (d.Opacity is not { } o) return null;
                if (o.AffectHead == v) return null;
                o.AffectHead = v;
                d.Opacity = o;
                return d;
            });
        }
    }

    public bool? MotionOpacityAffectLine
    {
        get => CommonValue(MotionUnitsHaving(u => u.Data.Opacity.HasValue), u => u.Data.Opacity!.Value.AffectLine);
        set
        {
            if (value is not { } v) return;
            EditMotions(d =>
            {
                if (d.Opacity is not { } o) return null;
                if (o.AffectLine == v) return null;
                o.AffectLine = v;
                d.Opacity = o;
                return d;
            });
        }
    }

    public bool? MotionOpacityAffectKey
    {
        get => CommonValue(MotionUnitsHaving(u => u.Data.Opacity.HasValue), u => u.Data.Opacity!.Value.AffectKey);
        set
        {
            if (value is not { } v) return;
            EditMotions(d =>
            {
                if (d.Opacity is not { } o) return null;
                if (o.AffectKey == v) return null;
                o.AffectKey = v;
                d.Opacity = o;
                return d;
            });
        }
    }

    public bool? MotionOpacityAffectNote
    {
        get => CommonValue(MotionUnitsHaving(u => u.Data.Opacity.HasValue), u => u.Data.Opacity!.Value.AffectNote);
        set
        {
            if (value is not { } v) return;
            EditMotions(d =>
            {
                if (d.Opacity is not { } o) return null;
                if (o.AffectNote == v) return null;
                o.AffectNote = v;
                d.Opacity = o;
                return d;
            });
        }
    }

    /// <summary>
    /// 是否使用显式起始不透明度。勾选时取第一个受影响部位的当前不透明度作为 StartOpacity
    /// （没有受影响部位时取 1）
    /// </summary>
    public bool? MotionOpacityUseStart
    {
        get => CommonValue(MotionUnitsHaving(u => u.Data.Opacity.HasValue),
                           u => u.Data.Opacity!.Value.UseStartValue);
        set
        {
            if (value is not { } v) return;
            EditMotions(d =>
            {
                if (d.Opacity is not { } o) return null;
                if (o.UseStartValue == v) return null;
                o.UseStartValue = v;
                if (v) o.StartOpacity = SampleLaneState(d) is { } s ? SampleOpacity(s, o) : 1f;
                d.Opacity = o;
                return d;
            });
        }
    }

    /// <summary> 起始不透明度（仅启用了初始值的选中项参与取值/编辑） </summary>
    public float? MotionOpacityStartValue
    {
        get => CommonValue(MotionUnitsHaving(u => u.Data.Opacity?.UseStartValue == true),
                           u => u.Data.Opacity!.Value.StartOpacity);
        set
        {
            if (value is not { } v) return;
            EditMotions(d =>
            {
                if (d.Opacity is not { } o || !o.UseStartValue) return null;
                if (o.StartOpacity == v) return null;
                o.StartOpacity = v;
                d.Opacity = o;
                return d;
            });
        }
    }

    public CurveKindItem? MotionOpacityCurveKind
    {
        get => CommonRefValue(MotionUnitsHaving(u => u.Data.Opacity.HasValue),
                              u => ItemOfCurve(u.Data.Opacity?.CurveMode));
        set
        {
            if (value == null) return;
            var kind = value.Kind;
            EditMotions(d =>
            {
                if (d.Opacity is not { } o) return null;
                var newCurve = EaseCurveKindExtensions.ToEaseCurve(kind, DirectionOrDefault(o.CurveMode));
                if (o.CurveMode == newCurve) return null;
                o.CurveMode = newCurve;
                d.Opacity = o;
                return d;
            });
        }
    }

    public int MotionOpacityCurveDirection
    {
        get => CommonValue(MotionUnitsHaving(u => u.Data.Opacity.HasValue),
                           u => EaseCurveDirectionExtensions.ToIndex(DirectionOf(u.Data.Opacity?.CurveMode))) ?? -1;

        set
        {
            if (value < 0) return;
            var direction = EaseCurveDirectionExtensions.FromIndex(value);
            EditMotions(d =>
            {
                if (d.Opacity is not { } o) return null;
                var newCurve = EaseCurveKindExtensions.ToEaseCurve(EaseCurveKindExtensions.BaseOf(o.CurveMode), direction);
                if (o.CurveMode == newCurve) return null;
                o.CurveMode = newCurve;
                d.Opacity = o;
                return d;
            });
        }
    }

    // ---- 动画属性共用实现 ----

    /// <summary> 曲线 → 下拉项，null（无该动画）返回 null </summary>
    private static CurveKindItem? ItemOfCurve(EaseCurve? curve)
        => curve.HasValue ? CurveKindItem.ItemOf(EaseCurveKindExtensions.BaseOf(curve.Value)) : null;

    /// <summary> 曲线 → 方向，Linear/null 返回 null（表示无方向） </summary>
    private static EaseCurveDirection? DirectionOf(EaseCurve? curve)
        => curve.HasValue ? EaseCurveKindExtensions.DirectionOf(curve.Value) : null;

    /// <summary> 换种类时使用的方向：保留原方向，原为 Linear（无方向）则默认 In </summary>
    private static EaseCurveDirection? DirectionOrDefault(EaseCurve curve)
        => EaseCurveKindExtensions.DirectionOf(curve) ?? EaseCurveDirection.In;

    // ---- 初始值（UseStartValue）辅助 ----

    /// <summary> 采样指定 Motion 起点处的轨道状态；未接入采样回调时返回 null </summary>
    private LaneStateSample? SampleLaneState(LaneMotionData motion)
        => OnSampleLaneState?.Invoke(motion.LaneIndex, motion.StartTime);

    /// <summary> 取第一个受影响部位的当前不透明度作为初始值；没有受影响部位时取 1 </summary>
    private static float SampleOpacity(LaneStateSample sample, OpacityMotion motion)
    {
        if (motion.AffectHead) return sample.HeadOpacity;
        if (motion.AffectLine) return sample.LineOpacity;
        if (motion.AffectKey) return sample.KeyOpacity;
        if (motion.AffectNote) return sample.NoteOpacity;
        return 1f;
    }

    /// <summary> StartPos 输入行是否显示（全体启用初始值时为 true） </summary>
    public bool MotionPositionStartFieldsEnabled => MotionPositionUseStart == true;

    /// <summary> StartAngle 输入行是否显示 </summary>
    public bool MotionRotationStartFieldsEnabled => MotionRotationUseStart == true;

    /// <summary> StartValue 输入行是否显示 </summary>
    public bool MotionOpacityStartFieldsEnabled => MotionOpacityUseStart == true;

    // ---- 方向选择框可用性：全体选中项都是 Linear 才置灰，有任一非 Linear 即可编辑 ----

    public bool MotionPositionDirectionEnabled =>
        HasNonLinear(MotionUnitsHaving(u => u.Data.Position.HasValue), u => u.Data.Position?.CurveMode);
    public bool MotionRotationDirectionEnabled =>
        HasNonLinear(MotionUnitsHaving(u => u.Data.Rotation.HasValue), u => u.Data.Rotation?.CurveMode);
    public bool MotionOpacityDirectionEnabled =>
        HasNonLinear(MotionUnitsHaving(u => u.Data.Opacity.HasValue), u => u.Data.Opacity?.CurveMode);

    #endregion
#endif
}

/// <summary> 流速模式下拉项：实际枚举值 + 显示名 </summary>
public sealed record SpeedModeItem(LaneSpeedMode Mode, string DisplayName);
