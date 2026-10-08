using System;
using System.Collections.Generic;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CQ_ChartMaker.Models;
using CQMusicGame.Shared;

namespace CQ_ChartMaker.ViewModels;

public partial class ChartViewModel : ObservableObject, IDisposable
{
    //子viewmodel
    public ViewViewModel ViewVM { get; }
    public EditorViewModel EditorVM { get; }
    public InspectorViewModel InspectorVM { get; }
    
    //TimelineModel
    private readonly TimelineModel _timeline;

    //AudioModel
    private readonly AudioModel _audio;

    private readonly Stopwatch _stopwatch = new();
    private long _lastElapsedMs;

    //ChartSheetModel
    private readonly ChartSheetModel _chartSheet;

    // 用户设置引用
    public UserSettingsModel UserSettingsModel { get; }

    /// <summary>
    /// 构造函数，传入谱面文本和用户设置
    /// </summary>
    public ChartViewModel(string chartText, UserSettingsModel userSettingsModel)
    {
        UserSettingsModel = userSettingsModel;
        _timeline = new TimelineModel();
        _chartSheet = new ChartSheetModel(chartText);

        ViewVM = new ViewViewModel
        {
            Timeline = _timeline
        };
        EditorVM = new EditorViewModel(_chartSheet, _timeline, userSettingsModel);
        // 点击选中：无 Ctrl = 单选替换，Ctrl = 加选/减选（EditorVM.OnSelectUnit 转发给 NotesVM）
        EditorVM.OnSelectUnit = OnClickSelect;
        EditorVM.BeatsVM.OnSelectUnit = OnClickSelect;
        EditorVM.LanesVM.OnSelectUnit = OnClickSelect;
        // 框选结果：由各子 VM 判定出框内 unit 后整批交给这里（append = Ctrl 追加模式）
        EditorVM.NotesVM.OnSelectMany = (units, append) => SelectMany(units, append);
        EditorVM.BeatsVM.OnSelectMany = (units, append) => SelectMany(units, append);
        EditorVM.LanesVM.OnSelectMany = (units, append) => SelectMany(units, append);
        EditorVM.NotesVM.OnPlaceNote = AddNote;
        EditorVM.NotesVM.OnPlaceTune = AddTune;
        EditorVM.NotesVM.OnCommitGhostPlacement = CommitNoteGhostPlacement;
        EditorVM.LanesVM.OnCommitGhostPlacement = CommitMotionGhostPlacement;
        EditorVM.BeatsVM.OnCommitGhostPlacement = CommitBpmGhostPlacement;
        EditorVM.BeatsVM.OnPlaceBpm = AddBpm;
        EditorVM.LanesVM.OnPlaceMotion = AddMotion;
        InspectorVM = new InspectorViewModel();
        InspectorVM.OnPropertyEdited = () =>
        {
            EditorVM.NotesVM.RefreshNotesPublic();
            // BPM 编辑后同步回 struct 列表，让节拍线和吸附网格实时跟着变
            _chartSheet.SyncBpmsToData();
            EditorVM.BeatsVM.RefreshPublic();
            // Motion 编辑后同步回 struct 列表，并刷新 Lanes 画布
            _chartSheet.SyncMotionsToData();
            EditorVM.LanesVM.RefreshPublic();
        };
        InspectorVM.OnCommitEdit = cmd => _undoManager.Record(cmd);

        _undoManager = new UndoManagerModel();

        // 切换 Notes/Lanes/Beats 子视图时：清空撤销/重做池（避免跨视图撤销上一步编辑），并取消各视图的放置模式
        EditorVM.OnSubViewSwitched = () =>
        {
            _undoManager.Clear();
            ClearSelection(); // 切换子视图清空选择（多选也一样）
            CancelGhosts(); // 幽灵快照基于切换前的选择，一并取消
            EditorVM.NotesVM.PlacingMode = NotePlacingMode.None;
            EditorVM.BeatsVM.IsPlacing = false;
            EditorVM.LanesVM.IsPlacing = false;
        };

        _stopwatch.Start();

        // 创建音频模块，并挂接它与时间轴的双向同步
        // 两个 Model 互不认识，所有接线集中在这里（与 EditorVM 的回调挂接风格一致）
        _audio = new AudioModel();

        // 时间轴状态变化 → 同步音频：暂停时静音、恢复时重定位播放、倍速变化时即时改速率
        _timeline.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(TimelineModel.IsPaused):
                    if (_timeline.IsPaused)
                        _audio.Pause();
                    else
                        _audio.PlayFrom(_timeline.TimeMs);
                    break;
                case nameof(TimelineModel.Speed):
                    _audio.SetSpeed(_timeline.Speed);
                    break;
            }
        };

        // 音频自然播完 → 时间轴对齐到末尾并暂停（严格进入“播到头”状态）
        _audio.BgmEnded += () =>
        {
            _timeline.SetTime(_timeline.MaxTime);
            _timeline.Pause();
        };

        // 隐藏轨道 → 选中集合里落在该轨道上的 note 丢选中（#7 约定：防止移动/复制
        // 用到没有可见轨道下标的 note）；已生成的幽灵不受影响（幽灵是自包含快照）
        UserSettingsModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UserSettingsModel.LaneVisibilityMask))
                FilterSelectionByLaneVisibility();
        };
    }

    private readonly UndoManagerModel _undoManager;

    /// <summary> 撤销最近一次操作 </summary>
    public void Undo()
    {
        CancelGhosts(); // 先退出幽灵，避免其快照基于将被撤销的数据
        var command = _undoManager.Undo();
        _chartSheet.SyncBpmsToData(); // 命令可能改动了 BPM 单位列表，先同步回 struct 列表再刷新
        _chartSheet.SyncMotionsToData(); // 命令可能改动了 Motion 单位列表，同步回 struct 列表
        EditorVM.NotesVM.RefreshNotesPublic();
        EditorVM.BeatsVM.RefreshPublic();
        EditorVM.LanesVM.RefreshPublic();
        InspectorVM.RefreshDisplay();
        AutoSelectOnUndo(command);
    }

    /// <summary> 重做最近一次撤销 </summary>
    public void Redo()
    {
        CancelGhosts(); // 同 Undo：先退出幽灵
        var command = _undoManager.Redo();
        _chartSheet.SyncBpmsToData(); // 同上：命令可能改动 BPM 单位列表，同步回 struct 列表
        _chartSheet.SyncMotionsToData(); // 同上：命令可能改动 Motion 单位列表
        EditorVM.NotesVM.RefreshNotesPublic();
        EditorVM.BeatsVM.RefreshPublic();
        EditorVM.LanesVM.RefreshPublic();
        InspectorVM.RefreshDisplay();
        AutoSelectOnRedo(command);
    }

    /// <summary> 添加 NoteUnit 并记录撤销命令，自动选中 </summary>
    public void AddNote(NoteUnit note)
    {
        _chartSheet.NoteUnitList.Add(note);
        _undoManager.Record(new AddNoteCommand(_chartSheet.NoteUnitList, note));
        SelectSingle(note);
    }

    /// <summary> 添加 TuneUnit 到其父 NoteUnit.TuneList 并记录撤销命令，自动选中 </summary>
    public void AddTune(TuneUnit tune)
    {
        tune.Parent!.TuneList ??= [];
        tune.Parent.TuneList.Add(tune);
        _undoManager.Record(new AddTuneCommand(tune.Parent, tune));
        SelectSingle(tune);
    }

    /// <summary> 添加 BpmUnit 并记录撤销命令，自动选中 </summary>
    public void AddBpm(BpmUnit bpm)
    {
        _chartSheet.BpmUnitList.Add(bpm);
        _undoManager.Record(new AddBpmCommand(_chartSheet.BpmUnitList, bpm));
        _chartSheet.SyncBpmsToData(); // 同步回 struct 列表，节拍线和吸附网格立即生效
        SelectSingle(bpm);
    }

    /// <summary> 添加 MotionUnit 并记录撤销命令，自动选中 </summary>
    public void AddMotion(MotionUnit motion)
    {
        _chartSheet.MotionUnitList.Add(motion);
        _undoManager.Record(new AddMotionCommand(_chartSheet.MotionUnitList, motion));
        _chartSheet.SyncMotionsToData(); // 同步回 struct 列表
        SelectSingle(motion);
    }

    /// <summary> 撤销后根据命令类型自动选中：撤销"添加"清选中，撤销"删除"恢复选中 </summary>
    private void AutoSelectOnUndo(IUndoableCommand? command)
    {
        switch (command)
        {
            case AddNoteCommand:                 ClearSelection(); break;
            case RemoveNoteCommand removeNote:   SelectSingle(removeNote.Note); break;
            case AddTuneCommand:                 ClearSelection(); break;
            case RemoveTuneCommand removeTune:   SelectSingle(removeTune.Tune); break;
            case AddBpmCommand:                  ClearSelection(); break;
            case RemoveBpmCommand removeBpm:     SelectSingle(removeBpm.Bpm); break;
            case AddMotionCommand:               ClearSelection(); break;
            case RemoveMotionCommand removeMotion: SelectSingle(removeMotion.Motion); break;
            // 复合命令目前只有批量删除：撤销后把整批恢复的 unit 重新选中
            case CompositeCommand composite when IsBatchRemove(composite):
                SelectMany(RemovedUnitsOf(composite));
                break;
        }
    }

    /// <summary> 判断复合命令是否为一组"删除"操作（批量删除） </summary>
    private static bool IsBatchRemove(CompositeCommand composite)
    {
        foreach (var cmd in composite.Commands)
        {
            if (cmd is not (RemoveNoteCommand or RemoveTuneCommand or RemoveBpmCommand or RemoveMotionCommand))
                return false;
        }
        return true;
    }

    /// <summary> 提取批量删除命令涉及的 unit（撤销后恢复选中用） </summary>
    private static List<object> RemovedUnitsOf(CompositeCommand composite)
    {
        var units = new List<object>();
        foreach (var cmd in composite.Commands)
        {
            switch (cmd)
            {
                case RemoveNoteCommand c:   units.Add(c.Note); break;
                case RemoveTuneCommand c:   units.Add(c.Tune); break;
                case RemoveBpmCommand c:    units.Add(c.Bpm); break;
                case RemoveMotionCommand c: units.Add(c.Motion); break;
            }
        }
        return units;
    }

    /// <summary> 重做后根据命令类型自动选中：重做"添加"选中，重做"删除"清选中 </summary>
    private void AutoSelectOnRedo(IUndoableCommand? command)
    {
        switch (command)
        {
            case AddNoteCommand addNote:         SelectSingle(addNote.Note); break;
            case RemoveNoteCommand:              ClearSelection(); break;
            case AddTuneCommand addTune:         SelectSingle(addTune.Tune); break;
            case RemoveTuneCommand:              ClearSelection(); break;
            case AddBpmCommand addBpm:           SelectSingle(addBpm.Bpm); break;
            case RemoveBpmCommand:               ClearSelection(); break;
            case AddMotionCommand addMotion:     SelectSingle(addMotion.Motion); break;
            case RemoveMotionCommand:            ClearSelection(); break;
            // 重做批量删除 → 被删的 unit 再次消失，清空选中
            case CompositeCommand composite when IsBatchRemove(composite):
                ClearSelection();
                break;
        }
    }

    /// <summary>
    /// 删除所有选中的 Unit 并记录撤销命令：单项记一条，多项打包成一条复合命令（撤销一次全恢复）。
    /// 循环内只做纯数据操作，不触发任何刷新；清空选中时统一刷新一轮
    /// </summary>
    public void DeleteSelected()
    {
        CancelGhosts(); // 删除先退出幽灵（快照源即将被删除）
        if (_selectedUnits.Count == 0) return;

        var commands = new List<IUndoableCommand>();
        bool bpmRemoved = false;

        foreach (var unit in _selectedUnits)
        {
            switch (unit)
            {
                case NoteUnit noteUnit:
                    _chartSheet.NoteUnitList.Remove(noteUnit);
                    commands.Add(new RemoveNoteCommand(_chartSheet.NoteUnitList, noteUnit));
                    break;
                case TuneUnit tuneUnit: // 多选不含 Tune，这里是单击选中 Tune 的单删路径
                    tuneUnit.Parent!.TuneList.Remove(tuneUnit);
                    commands.Add(new RemoveTuneCommand(tuneUnit.Parent, tuneUnit));
                    break;
                case BpmUnit bpmUnit:
                    _chartSheet.BpmUnitList.Remove(bpmUnit);
                    commands.Add(new RemoveBpmCommand(_chartSheet.BpmUnitList, bpmUnit));
                    bpmRemoved = true;
                    break;
                case MotionUnit motionUnit:
                    _chartSheet.MotionUnitList.Remove(motionUnit);
                    commands.Add(new RemoveMotionCommand(_chartSheet.MotionUnitList, motionUnit));
                    break;
            }
        }

        if (bpmRemoved)
            _chartSheet.SyncBpmsToData(); // 同步回 struct 列表，节拍线立即更新

        _undoManager.Record(commands.Count == 1 ? commands[0] : new CompositeCommand(commands));
        ClearSelection(); // 内部统一刷新三视图（一轮）
    }

    // === 批量移动/复制（幽灵模式） ===

    /// <summary>
    /// M/C 快捷键入口：按当前子视图进入批量移动/复制幽灵（Notes / Lanes / Beats 三套同构）。
    /// 同键再按 = 取消，异键 = 切换模式；选中必须非空且全为对应类型
    /// </summary>
    public void BeginMoveOrCopy(bool isCopy)
    {
        switch (EditorVM.CurrentSubView)
        {
            case NotesViewModel notesVm:
            {
                if (notesVm.IsGhostActive && notesVm.GhostIsCopy == isCopy) { notesVm.CancelGhost(); return; }
                // 收集选中（遇到非 NoteUnit——如单选 Tune——则本次操作无效）
                var units = new List<NoteUnit>();
                foreach (var unit in _selectedUnits)
                {
                    if (unit is NoteUnit note) units.Add(note);
                    else return;
                }
                if (units.Count == 0) return;
                notesVm.BeginGhost(units, isCopy);
                break;
            }
            case LanesViewModel lanesVm:
            {
                if (lanesVm.IsGhostActive && lanesVm.GhostIsCopy == isCopy) { lanesVm.CancelGhost(); return; }
                var units = new List<MotionUnit>();
                foreach (var unit in _selectedUnits)
                {
                    if (unit is MotionUnit motion) units.Add(motion);
                    else return;
                }
                if (units.Count == 0) return;
                lanesVm.BeginGhost(units, isCopy);
                break;
            }
            case BeatsViewModel beatsVm:
            {
                if (beatsVm.IsGhostActive && beatsVm.GhostIsCopy == isCopy) { beatsVm.CancelGhost(); return; }
                var units = new List<BpmUnit>();
                foreach (var unit in _selectedUnits)
                {
                    if (unit is BpmUnit bpm) units.Add(bpm);
                    else return;
                }
                if (units.Count == 0) return;
                beatsVm.BeginGhost(units, isCopy);
                break;
            }
        }
    }

    /// <summary>
    /// 幽灵放置提交（Note）：移动 → 每条一个 EditNoteCommand（Tune 只平移时间）；
    /// 复制 → 深拷贝（含 TuneList）+ AddNoteCommand。打包成一条复合命令记入撤销池
    /// （一次操作一条撤销），最后走 ApplySelection 统一刷新一轮；选中保持不变
    /// </summary>
    public void CommitNoteGhostPlacement(List<NoteGhostPlacement> placements, bool isCopy)
    {
        var commands = new List<IUndoableCommand>();

        if (isCopy)
        {
            // 复制：新对象直接入列表，撤销 = 移除整棵（含 TuneList）
            foreach (var p in placements)
            {
                var copy = CloneNote(p.Unit, p.NewTime, p.NewLane);
                _chartSheet.NoteUnitList.Add(copy);
                commands.Add(new AddNoteCommand(_chartSheet.NoteUnitList, copy));
            }
        }
        else
        {
            // 移动：改属性。Tune 只跟随时间平移，其余属性（含 TargetLane）不动
            foreach (var p in placements)
            {
                var unit = p.Unit;
                var oldData = unit.Data;
                int deltaTime = p.NewTime - oldData.TargetTime;
                if (deltaTime == 0 && p.NewLane == oldData.LaneIndex) continue; // 原地不动：不产生命令

                var newData = oldData;
                newData.TargetTime = p.NewTime;
                newData.LaneIndex = p.NewLane;
                unit.Data = newData;
                commands.Add(new EditNoteCommand(unit, oldData, newData));

                if (deltaTime != 0 && unit.TuneList != null)
                {
                    foreach (var tune in unit.TuneList)
                    {
                        var oldTune = tune.Data;
                        var newTune = oldTune;
                        newTune.TargetTime = oldTune.TargetTime + deltaTime;
                        tune.Data = newTune;
                        commands.Add(new EditTuneCommand(tune, oldTune, newTune));
                    }
                }
            }
        }

        if (commands.Count == 0) return;
        _undoManager.Record(commands.Count == 1 ? commands[0] : new CompositeCommand(commands));
        ApplySelection(new List<object>(_selectedUnits)); // 统一刷新一轮（含 Inspector 数值更新）
    }

    /// <summary>
    /// 幽灵放置提交（Motion）：移动 → 每条一个 EditMotionCommand；复制 → 值拷贝 + AddMotionCommand。
    /// 打包一条复合命令入撤销池，同步回 struct 列表后统一刷新一轮
    /// </summary>
    public void CommitMotionGhostPlacement(List<MotionGhostPlacement> placements, bool isCopy)
    {
        var commands = new List<IUndoableCommand>();

        foreach (var p in placements)
        {
            if (isCopy)
            {
                // LaneMotionData 全部是值类型，直接拷贝即可
                var newData = p.Unit.Data;
                newData.StartTime = p.NewTime;
                newData.LaneIndex = p.NewLane;
                var copy = new MotionUnit { Data = newData };
                _chartSheet.MotionUnitList.Add(copy);
                commands.Add(new AddMotionCommand(_chartSheet.MotionUnitList, copy));
            }
            else
            {
                var oldData = p.Unit.Data;
                if (oldData.StartTime == p.NewTime && oldData.LaneIndex == p.NewLane) continue; // 原地不动：不产生命令
                var newData = oldData;
                newData.StartTime = p.NewTime;
                newData.LaneIndex = p.NewLane;
                p.Unit.Data = newData;
                commands.Add(new EditMotionCommand(p.Unit, oldData, newData));
            }
        }

        if (commands.Count == 0) return;
        _chartSheet.SyncMotionsToData(); // 同步回 struct 列表（与现有增删路径一致）
        _undoManager.Record(commands.Count == 1 ? commands[0] : new CompositeCommand(commands));
        ApplySelection(new List<object>(_selectedUnits)); // 统一刷新一轮
    }

    /// <summary>
    /// 幽灵放置提交（BPM）：移动 → 每条一个 EditBpmCommand；复制 → 值拷贝 + AddBpmCommand。
    /// 打包一条复合命令入撤销池，同步回 struct 列表让节拍线/吸附立即跟上
    /// </summary>
    public void CommitBpmGhostPlacement(List<BpmGhostPlacement> placements, bool isCopy)
    {
        var commands = new List<IUndoableCommand>();

        foreach (var p in placements)
        {
            if (isCopy)
            {
                var copy = new BpmUnit { Data = new BpmData(p.NewTime, p.Unit.Data.Bpm) };
                _chartSheet.BpmUnitList.Add(copy);
                commands.Add(new AddBpmCommand(_chartSheet.BpmUnitList, copy));
            }
            else
            {
                var oldData = p.Unit.Data;
                if (oldData.StartTime == p.NewTime) continue; // 原地不动：不产生命令
                var newData = oldData;
                newData.StartTime = p.NewTime;
                p.Unit.Data = newData;
                commands.Add(new EditBpmCommand(p.Unit, oldData, newData));
            }
        }

        if (commands.Count == 0) return;
        _chartSheet.SyncBpmsToData(); // 节拍线与吸附网格立即跟上（与 AddBpm 路径一致）
        _undoManager.Record(commands.Count == 1 ? commands[0] : new CompositeCommand(commands));
        ApplySelection(new List<object>(_selectedUnits)); // 统一刷新一轮
    }

    /// <summary> 取消三个子视图的幽灵（未激活的为无操作） </summary>
    private void CancelGhosts()
    {
        EditorVM.NotesVM.CancelGhost();
        EditorVM.LanesVM.CancelGhost();
        EditorVM.BeatsVM.CancelGhost();
    }

    /// <summary>
    /// 深拷贝一个 NoteUnit（Data 值拷贝 + TuneList 全部新建），并按放置结果改写时间/轨道；
    /// 复制出的 Tune 时间跟随同一条 note 的位移，其余属性原样
    /// </summary>
    private static NoteUnit CloneNote(NoteUnit src, int newTime, int newLane)
    {
        var oldData = src.Data;
        var newData = new NoteData(
            oldData.Type, newLane, newTime, oldData.SustainDuration,
            oldData.SpeedMode, oldData.SpeedValue, null, oldData.AppearOffset);
        var newUnit = new NoteUnit { Data = newData };

        if (src.TuneList is { Count: > 0 })
        {
            int deltaTime = newTime - oldData.TargetTime;
            newUnit.TuneList = new List<TuneUnit>(src.TuneList.Count);
            foreach (var tune in src.TuneList)
            {
                var oldTune = tune.Data;
                var newTune = oldTune;
                newTune.TargetTime = oldTune.TargetTime + deltaTime;
                newUnit.TuneList.Add(new TuneUnit { Parent = newUnit, Data = newTune });
            }
        }
        return newUnit;
    }

    /// <summary> 隐藏轨道时把选中集合里落在不可见轨道上的 note/motion 移出选中（影响后续 M/C；不影响已生成的幽灵） </summary>
    private void FilterSelectionByLaneVisibility()
    {
        if (_selectedUnits.Count == 0) return;

        var kept = new List<object>();
        bool removed = false;
        foreach (var unit in _selectedUnits)
        {
            if (IsUnitLaneVisible(unit)) kept.Add(unit);
            else removed = true;
        }
        if (removed)
            ApplySelection(kept); // 走统一入口：被移出项的 IsSelected 镜像会被一并清掉
    }

    /// <summary> 判断选中项所属轨道是否仍可见（NoteUnit 看自身；TuneUnit 看父 Dash；其余类型恒可见） </summary>
    private bool IsUnitLaneVisible(object unit) => unit switch
    {
        NoteUnit note => UserSettingsModel.IsLaneVisible(note.Data.LaneIndex),
        TuneUnit tune => tune.Parent != null && UserSettingsModel.IsLaneVisible(tune.Parent.Data.LaneIndex),
        MotionUnit motion => UserSettingsModel.IsLaneVisible(motion.Data.LaneIndex),
        _ => true
    };

    // PPS 调节参数（倒数基准：默认 1/200=0.005，±0.0003 步进，17步下界/13步上界）
    private const double PpsInverseSensitivity = 0.0003;
    private const double PpsInverseLower = 0.0011;  // 对应 PPS ≈ 909
    private const double PpsInverseUpper = 0.0101;  // 对应 PPS ≈ 99

    // === 选择状态 ===

    /// <summary> 选中集合：唯一数据源，删除/撤销恢复/框选等所有选中判断都读它；
    /// 各 Unit 上的 IsSelected 只是渲染层用的镜像标记，仅在 ApplySelection 里同步 </summary>
    private readonly List<object> _selectedUnits = new();

    /// <summary> 当前选中的 Unit：仅恰好选中 1 项时有值，多选或空选均为 null </summary>
    public object? SelectedUnit => _selectedUnits.Count == 1 ? _selectedUnits[0] : null;

    /// <summary> View 点击选中的统一入口：Ctrl 加选/减选，否则单选替换（unit 为 null 即点空白） </summary>
    private void OnClickSelect(object? unit, bool isCtrl)
    {
        if (isCtrl) ToggleSelection(unit);
        else SelectSingle(unit);
    }

    /// <summary> 单选：替换当前选中（传 null 即清空） </summary>
    public void SelectSingle(object? unit)
    {
        ApplySelection(unit == null ? new List<object>() : new List<object> { unit });
    }

    /// <summary> Ctrl+点击：已选中则移出，未选中则加入 </summary>
    public void ToggleSelection(object? unit)
    {
        // Tune 不参与多选（单击选中 Tune 走 SelectSingle，不受影响）
        if (unit is null or TuneUnit) return;

        var list = new List<object>(_selectedUnits);
        list.RemoveAll(x => x is TuneUnit); // 当前单选着 Tune 时，转入多选要把它排除
        if (!list.Remove(unit)) list.Add(unit);
        ApplySelection(list);
    }

    /// <summary> 整批设置选中（框选、撤销恢复用）；append=true 时在现有选中基础上追加 </summary>
    public void SelectMany(IEnumerable<object> units, bool append = false)
    {
        var list = append ? new List<object>(_selectedUnits) : new List<object>();
        foreach (var unit in units)
            if (!list.Contains(unit)) list.Add(unit);
        ApplySelection(list);
    }

    /// <summary> 清空选中 </summary>
    public void ClearSelection() => ApplySelection(new List<object>());

    /// <summary>
    /// 应用选中集合的唯一入口：提交 Inspector 暂存编辑 → 同步各 unit 的 IsSelected 镜像标记 →
    /// 分发子 ViewModel → 三个视图各刷新一轮（选中多少项都只刷新这一轮）
    /// </summary>
    private void ApplySelection(List<object> units)
    {
        InspectorVM.CommitPendingEdit();

        foreach (var unit in _selectedUnits)
            SetSelection(unit, false);
        _selectedUnits.Clear();
        _selectedUnits.AddRange(units);
        foreach (var unit in _selectedUnits)
            SetSelection(unit, true);

        // 多选时 SelectedUnit 为 null：Tune 放置自动失效；Inspector 按整个集合判定面板与属性值
        var single = SelectedUnit;
        InspectorVM.SelectedUnits = new List<object>(_selectedUnits); // 传副本，避免 Inspector 侧改动内部集合
        EditorVM.NotesVM.SelectedUnit = single;

        EditorVM.NotesVM.RefreshNotesPublic();
        EditorVM.BeatsVM.RefreshPublic();
        EditorVM.LanesVM.RefreshPublic();
    }

    /// <summary> 设置 Unit 的 IsSelected 状态 </summary>
    private static void SetSelection(object? unit, bool selected)
    {
        switch (unit)
        {
            case NoteUnit nu: nu.IsSelected = selected; break;
            case TuneUnit tu: tu.IsSelected = selected; break;
            case BpmUnit bu: bu.IsSelected = selected; break;
            case MotionUnit mu: mu.IsSelected = selected; break;
        }
    }

    public void TogglePlayPause() => _timeline.TogglePlayPause();
    public void Pause() => _timeline.Pause();

    public void AdjustPps(int sign)
    {
        double inverse = 1.0 / UserSettingsModel.PixelPerSecond;
        inverse -= sign * PpsInverseSensitivity;
        inverse = Math.Clamp(inverse, PpsInverseLower, PpsInverseUpper);
        UserSettingsModel.PixelPerSecond = 1.0 / inverse;
    }

    public void ScrollTime(int deltaMs)
    {
        if (!_timeline.IsPaused)
            _timeline.Pause();
        int newTime = _timeline.TimeMs + deltaMs;
        newTime = Math.Clamp(newTime, 0, _timeline.MaxTime);
        _timeline.SetTime(newTime);
    }

    /// <summary>
    /// 由 View 层 RequestAnimationFrame 每帧调用
    /// </summary>
    public void OnFrameUpdate()
    {
        long elapsedMs = _stopwatch.ElapsedMilliseconds;
        int deltaMs = (int)(elapsedMs - _lastElapsedMs);
        _lastElapsedMs = elapsedMs;

        deltaMs = Math.Min(deltaMs, 100); // 限制最大步长
        if (deltaMs > 0)
        {
            _timeline.Update(deltaMs);
        }
    }
    
    /// <summary>
    /// 序列化当前谱面数据为 chart.txt 文本。
    /// 落盘前必须把三个 Unit 列表同步回 ChartSheet 的 struct 列表，否则 Serialize() 读到的
    /// 是加载时的旧数据（此前 Note 的同步完全漏了，导致 note 改动保存不进去）
    /// </summary>
    public string SerializeChart()
    {
        _chartSheet.SyncNotesToData();
        _chartSheet.SyncBpmsToData();
        _chartSheet.SyncMotionsToData();
        return _chartSheet.ChartSheet.Serialize();
    }

    /// <summary>
    /// 导入音频：加载文件后把总时长投影到时间轴，并将播放位置归零
    /// </summary>
    /// <returns>是否加载成功（失败时保持无音频模式，时间轴维持原状）</returns>
    public bool LoadBgm(string path)
    {
        _timeline.Pause(); // 若正在播放先停止（挂接会同时停止当前音频）

        if (!_audio.LoadBgm(path))
        {
            // 文件不存在、非 WAV 等情况：退回无音频模式
            // 目前仅 Debug 提示，后续可改为 UI 弹窗提示“音频路径不可用”
            System.Diagnostics.Debug.WriteLine($"[LoadBgm] 音频加载失败: {path}");
            return false;
        }

        _timeline.MaxTime = _audio.TotalDurationMs; // 时间轴范围跟随音频总时长
        _timeline.SetTime(0);                       // 导入新音频后位置归零
        return true;
    }

    /// <summary> 释放资源：停止播放并关闭音频设备 </summary>
    public void Dispose()
    {
        _timeline.Pause(); // 先停播放（挂接会同步停止音频输出）
        _audio.Dispose();  // 再释放音频设备
    }
}