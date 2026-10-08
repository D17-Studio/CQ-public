using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CQ_ChartMaker.Models;
using CQMusicGame.Shared;

namespace CQ_ChartMaker.ViewModels;

public partial class InspectorViewModel : ObservableObject
{
    // 注意：下面的属性名与枚举名同名（NoteType / NoteSpeedMode）。属性类型改可空后，
    // C# 的"同名类型优先"消歧规则失效，写成 NoteType.Dot 会被解析成属性，所以这里必须用全名

    public static List<NoteType> NoteTypeOptions { get; } = new()
        { CQMusicGame.Shared.NoteType.Dot, CQMusicGame.Shared.NoteType.Dash, CQMusicGame.Shared.NoteType.Mute };
    public static List<TuneDirection> DirectionOptions { get; } = new() { TuneDirection.Left, TuneDirection.Right };
    public static List<NoteSpeedMode> SpeedModeOptions { get; } = new()
    {
        CQMusicGame.Shared.NoteSpeedMode.Default,
        CQMusicGame.Shared.NoteSpeedMode.Multiplier,
        CQMusicGame.Shared.NoteSpeedMode.Fixed
    };

    /// <summary> 属性即时刷新时触发（每次键入都刷新画布，但不记录撤销） </summary>
    public System.Action? OnPropertyEdited;

    /// <summary> 失焦或切选中时提交，传入待记录的编辑命令 </summary>
    public System.Action<IUndoableCommand>? OnCommitEdit;

    /// <summary> 暂存的待提交命令列表，同一次编辑会话持续复用（每次输入重新生成整批） </summary>
    private List<IUndoableCommand> _pendingCommands = new();

    /// <summary> 本次编辑会话中各 unit 的旧值快照：首次编辑时锁定、提交后清空。
    /// 多选时每个 unit 各锁一份，撤销时连同别的属性一起整份还原，各自的差异天然保留 </summary>
    private readonly Dictionary<NoteUnit, NoteData> _noteEditOldData = new();
    private readonly Dictionary<BpmUnit, BpmData> _bpmEditOldData = new();
    private readonly Dictionary<MotionUnit, LaneMotionData> _motionEditOldData = new();
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
            OnSelectedUnitChanged();
        }
    }

    /// <summary> 恰好选中 1 项时返回该项（Tune 属性固定走单选路径），多选/空选返回 null </summary>
    private object? SelectedUnit => _selectedUnits.Count == 1 ? _selectedUnits[0] : null;

    public bool IsNoteSelected   => AllSelectedAre<NoteUnit>();
    public bool IsTuneSelected   => AllSelectedAre<TuneUnit>();
    public bool IsBpmSelected    => AllSelectedAre<BpmUnit>();
    public bool IsMotionSelected => AllSelectedAre<MotionUnit>();

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
        _motionEditOldData.Clear();
        _tuneEditOldData = null;
    }

    private void OnSelectedUnitChanged()
    {
        OnPropertyChanged(nameof(IsNoteSelected));
        OnPropertyChanged(nameof(IsTuneSelected));
        OnPropertyChanged(nameof(IsBpmSelected));
        OnPropertyChanged(nameof(IsMotionSelected));
        OnPropertyChanged(nameof(NoteType));
        OnPropertyChanged(nameof(NoteLaneIndex));
        OnPropertyChanged(nameof(NoteTargetTime));
        OnPropertyChanged(nameof(NoteSustainDuration));
        OnPropertyChanged(nameof(NoteAppearOffset));
        OnPropertyChanged(nameof(NoteSpeedMode));
        OnPropertyChanged(nameof(NoteSpeedValue));
        OnPropertyChanged(nameof(TuneTargetTime));
        OnPropertyChanged(nameof(TuneDirection));
        OnPropertyChanged(nameof(TuneUseRelative));
        OnPropertyChanged(nameof(TuneTargetLane));
        OnPropertyChanged(nameof(TunePosValueX));
        OnPropertyChanged(nameof(TunePosValueY));
        OnPropertyChanged(nameof(TuneDuration));
        OnPropertyChanged(nameof(BpmStartTime));
        OnPropertyChanged(nameof(BpmValue));
        OnPropertyChanged(nameof(MotionStartTime));
        OnPropertyChanged(nameof(MotionLaneIndex));
        OnPropertyChanged(nameof(MotionDuration));
        OnPropertyChanged(nameof(MotionAnchorX));
        OnPropertyChanged(nameof(MotionAnchorY));
        NotifyMotionProperties();
    }

    /// <summary> 重通知全部 Motion 动画属性（撤销/重做、勾选或曲线变化后调用） </summary>
    private void NotifyMotionProperties()
    {
        OnPropertyChanged(nameof(MotionPositionEnabled));
        OnPropertyChanged(nameof(MotionPositionX));
        OnPropertyChanged(nameof(MotionPositionY));
        OnPropertyChanged(nameof(MotionPositionRelative));
        OnPropertyChanged(nameof(MotionPositionCurveKind));
        OnPropertyChanged(nameof(MotionPositionCurveDirection));
        OnPropertyChanged(nameof(MotionPositionDirectionEnabled));
        OnPropertyChanged(nameof(MotionRotationEnabled));
        OnPropertyChanged(nameof(MotionRotationAngle));
        OnPropertyChanged(nameof(MotionRotationRelative));
        OnPropertyChanged(nameof(MotionRotationCurveKind));
        OnPropertyChanged(nameof(MotionRotationCurveDirection));
        OnPropertyChanged(nameof(MotionRotationDirectionEnabled));
        OnPropertyChanged(nameof(MotionOpacityEnabled));
        OnPropertyChanged(nameof(MotionOpacityValue));
        OnPropertyChanged(nameof(MotionOpacityAffectHead));
        OnPropertyChanged(nameof(MotionOpacityAffectLine));
        OnPropertyChanged(nameof(MotionOpacityAffectKey));
        OnPropertyChanged(nameof(MotionOpacityAffectNote));
        OnPropertyChanged(nameof(MotionOpacityCurveKind));
        OnPropertyChanged(nameof(MotionOpacityCurveDirection));
        OnPropertyChanged(nameof(MotionOpacityDirectionEnabled));
        OnPropertyChanged(nameof(CurveDirectionVisible));
    }

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

    public int? NoteLaneIndex
    {
        get => CommonValue(SelectedUnitsOf<NoteUnit>(), u => u.Data.LaneIndex);
        set
        {
            if (value is not { } v) return;
            EditNotes(d => { d.LaneIndex = v; return d; });
            OnPropertyChanged();
            OnPropertyEdited?.Invoke();
        }
    }

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

    public NoteSpeedMode? NoteSpeedMode
    {
        get => CommonValue(SelectedUnitsOf<NoteUnit>(), u => u.Data.SpeedMode);
        set
        {
            if (value is not { } v) return;
            EditNotes(d => { d.SpeedMode = v; return d; });
            OnPropertyChanged();
            OnPropertyChanged(nameof(NoteSpeedValue)); // 流速值框的可用性随模式变化
            OnPropertyEdited?.Invoke();
        }
    }

    public float? NoteSpeedValue
    {
        get => CommonValue(SelectedUnitsOf<NoteUnit>(), u => u.Data.SpeedValue);
        set
        {
            if (value is not { } v) return;
            EditNotes(d => { d.SpeedValue = v; return d; });
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

    public TuneDirection TuneDirection
    {
        get => (SelectedUnit as TuneUnit)?.Data.Direction ?? TuneDirection.Left;
        set
        {
            EditTune(d => { d.Direction = value; return d; });
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

    // ---- 方向选择框可用性：全体选中项都是 Linear 才置灰，有任一非 Linear 即可编辑 ----

    public bool MotionPositionDirectionEnabled =>
        HasNonLinear(MotionUnitsHaving(u => u.Data.Position.HasValue), u => u.Data.Position?.CurveMode);
    public bool MotionRotationDirectionEnabled =>
        HasNonLinear(MotionUnitsHaving(u => u.Data.Rotation.HasValue), u => u.Data.Rotation?.CurveMode);
    public bool MotionOpacityDirectionEnabled =>
        HasNonLinear(MotionUnitsHaving(u => u.Data.Opacity.HasValue), u => u.Data.Opacity?.CurveMode);

    #endregion
}
