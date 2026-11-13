using System;
using System.Collections.Generic;
using System.ComponentModel;
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

    // 项目级共享音频（由 MainWindowViewModel 持有并释放，本类只负责时间轴联动）
    private readonly AudioModel _audio;

    private readonly Stopwatch _stopwatch = new();
    private long _lastElapsedMs;

    // 打击音效游标：已处理到的歌曲时间（毫秒）。
    // 只有播放中时间向前推进时才触发 (游标, 当前时间] 内的 note，保证每个 note 只响一次
    private int _hitSoundCursorMs;

    /// <summary> 处理后音频不足 1ms（曲目信息页显示浅红提示，音频已被替换为 1ms 空白） </summary>
    [ObservableProperty]
    private bool _isAudioShorterThanOneMs;

    /// <summary> 处理后音频总时长（毫秒），无音频时为 0；曲目信息页用于预览片段范围校验 </summary>
    [ObservableProperty]
    private int _audioDurationMs;

    //ChartSheetModel
    private readonly ChartSheetModel _chartSheet;

    /// <summary> 当前项目的轨道集合（会话内存在性 + 显隐） </summary>
    public LaneCatalogModel LaneCatalog { get; } = new();

    // 用户设置引用
    public UserSettingsModel UserSettingsModel { get; }

    /// <summary>
    /// 构造函数，传入谱面文本、用户设置和项目级共享音频
    /// </summary>
    public ChartViewModel(string chartText, UserSettingsModel userSettingsModel, AudioModel audio)
    {
        UserSettingsModel = userSettingsModel;
        _timeline = new TimelineModel();

        // 共享音频已加载时（切换谱面场景），时间轴上限直接跟随处理后的音频总时长；
        // 打开项目时音频尚未加载，由随后的 LoadBgm 设置
        if (audio.HasBgm)
        {
            _timeline.MaxTime = audio.TotalDurationMs;
            IsAudioShorterThanOneMs = audio.IsAudioShorterThanOneMs;
            AudioDurationMs = audio.TotalDurationMs;
        }

        _chartSheet = new ChartSheetModel(chartText);

        // 轨道集合 = 谱面数据实际使用的轨道 ∪ 默认轨道 0~3。
        // 空轨道不写入谱面，关闭项目后自然消失；0~3 永远保留
        LaneCatalog.Ensure(_chartSheet.ChartSheet.GetLaneIndexList());
        LaneCatalog.Ensure(LaneCatalogModel.DefaultLanes);
        LaneCatalog.VisibilityChanged += FilterSelectionByLaneVisibility;

        // UserSettings 必须在 ChartSheet 之前赋值：ChartSheet 赋值会立即重建计算器，需要读到已保存的流速
        ViewVM = new ViewViewModel
        {
            Timeline = _timeline,
            UserSettings = userSettingsModel,
            ChartSheet = _chartSheet,
            LaneCatalog = LaneCatalog
        };
        EditorVM = new EditorViewModel(_chartSheet, _timeline, userSettingsModel, LaneCatalog);
        // 轨道增删会改变预览要绘制的轨道列表；暂停时没有帧驱动，需要主动标脏
        LaneCatalog.Changed += ViewVM.NotifyFrameDirty;
        // 点击选中：无 Ctrl = 单选替换，Ctrl = 加选/减选
        EditorVM.OnSelectUnit = OnClickSelect;
        EditorVM.BeatsVM.OnSelectUnit = OnClickSelect;
        EditorVM.EventsVm.OnSelectUnit = OnClickSelect;
        // 框选结果：由各子 VM 判定出框内 unit 后整批交给这里（append = Ctrl 追加模式）
        EditorVM.NotesVM.OnSelectMany = (units, append) => SelectMany(units, append);
        EditorVM.BeatsVM.OnSelectMany = (units, append) => SelectMany(units, append);
        EditorVM.EventsVm.OnSelectMany = (units, append) => SelectMany(units, append);
        // 双击轨道空白：收集该轨道上的同类 unit
        EditorVM.NotesVM.OnSelectLane = SelectNotesOnLane;
        EditorVM.EventsVm.OnSelectLane = SelectEventsOnLane;
        // Beats 只有一条轨道：双击空白直接全选 BPM
        EditorVM.BeatsVM.OnSelectAll = SelectAllBpms;
        EditorVM.NotesVM.OnPlaceNote = AddNote;
        EditorVM.NotesVM.OnPlaceTune = AddTune;
        // 快捷删除：三个子视图共用同一入口，按 unit 类型分派
        EditorVM.NotesVM.OnDeleteUnitRequested = DeleteUnitAt;
        EditorVM.EventsVm.OnDeleteUnitRequested = DeleteUnitAt;
        EditorVM.BeatsVM.OnDeleteUnitRequested = DeleteUnitAt;
        EditorVM.NotesVM.OnCommitGhostPlacement = CommitNoteGhostPlacement;
        EditorVM.EventsVm.OnCommitGhostPlacement = CommitEventGhostPlacement;
        EditorVM.BeatsVM.OnCommitGhostPlacement = CommitBpmGhostPlacement;
        // 拖拽开始：由 View 上报被抓住的基准项
        EditorVM.NotesVM.OnBeginDrag = BeginNoteDrag;
        EditorVM.EventsVm.OnBeginDrag = BeginEventDrag;
        EditorVM.BeatsVM.OnBeginDrag = BeginBpmDrag;
        EditorVM.BeatsVM.OnPlaceBpm = AddBpm;
        EditorVM.EventsVm.OnPlaceEvent = AddEvent;

        // 三个子视图的悬浮跳转按钮统一走 Inspector 同款跳转（暂停 + clamp）
        EditorVM.NotesVM.OnJumpToTime = JumpToTime;
        EditorVM.EventsVm.OnJumpToTime = JumpToTime;
        EditorVM.BeatsVM.OnJumpToTime = JumpToTime;
        InspectorVM = new InspectorViewModel { LaneCatalog = LaneCatalog };
        // 曲线初值采样：按当前 struct 列表现场构建 LaneState（ChartSheet.LaneState 是解析快照，编辑后已过期）。
        // 忽略父轨合成，取轨道本地值——新创建的曲线存的就是本地值。
        InspectorVM.OnSampleLaneState = (laneIndex, time) =>
        {
            LaneState laneState = _chartSheet.CreateLaneState(includeParents: false);
            LaneFrame frame = laneState.GetFrame(laneIndex, time);
            return new LaneStateSample(
                frame.Position,
                frame.Rotation,
                laneState.HeadOpacity(laneIndex, time),
                laneState.LineOpacity(laneIndex, time),
                laneState.LabelOpacity(laneIndex, time),
                laneState.NoteOpacity(laneIndex, time));
        };
        InspectorVM.OnPropertyEdited = () =>
        {
            EditorVM.NotesVM.RefreshNotesPublic();
            // 先同步回 struct 列表，再刷新底栏/预览，保证换轨后的“有数据”标记与 LaneState 都是新数据
            _chartSheet.SyncNotesToData();
            _chartSheet.SyncBpmsToData();
            _chartSheet.SyncEventsToData();
            EditorVM.RefreshLaneBar();
            EditorVM.BeatsVM.RefreshPublic();
            ViewVM.RebuildLaneState();
            EditorVM.EventsVm.RefreshPublic();
            ViewVM.NotifyFrameDirty();

            // 编辑后若选中的 event 已不在 Events 当前界面内（如 Inspector 改了轨道），
            // 清空选中，避免继续操作不可见内容
            if (SelectionOutOfEventsView())
                ClearSelection();
        };
        InspectorVM.OnCommitEdit = cmd => Record(cmd);
        InspectorVM.OnRelocateRequested = BeginRelocate;
        InspectorVM.OnJumpRequested = JumpToTime;
        InspectorVM.OnSelectTuneRequested = SelectSingle;
        InspectorVM.OnAddTuneRequested = AddTuneFromInspector;
        InspectorVM.OnDeleteTuneRequested = DeleteTuneFromInspector;
        InspectorVM.OnSelectNoteRequested = SelectSingle;
        EditorVM.NotesVM.OnCommitRelocate = CommitRelocate;
        EditorVM.EventsVm.OnCommitRelocate = CommitRelocate;
        EditorVM.BeatsVM.OnCommitRelocate = CommitRelocate;

        // 切换 Notes/Events/Beats 子视图时：三个面板各有独立撤销池，无需清空；
        // 这里只取消各视图的放置模式，并清空选择和依赖旧选择的临时状态
        EditorVM.OnSubViewSwitched = () =>
        {
            ClearSelection(); // 切换子视图清空选择（多选也一样）
            CancelGhosts(); // 幽灵快照基于切换前的选择，一并取消
            CancelRelocations();
            EditorVM.NotesVM.PlacingMode = NotePlacingMode.None;
            EditorVM.BeatsVM.IsPlacing = false;
            EditorVM.EventsVm.IsPlacing = false;
        };

        // Events 页事件类型栏按钮：统一走 ChartVM 入口，切换前取消幽灵/重定位（选中保留）
        EditorVM.OnSwitchLaneEventKind = SwitchLaneEventKind;

        // 显示模式切换：更新 Inspector 轨道重定位按钮，并清空选中
        EditorVM.EventsVm.OnDisplayModeSwitched = OnLaneDisplayModeSwitched;
        // 平铺模式类型显隐：隐藏类型时移除该类型的选中项
        EditorVM.EventsVm.OnKindVisibilityChanged = OnLaneKindVisibilityChanged;

        // 恢复上次选择的 Events 显示模式（轨道/类型/平铺）。
        // 必须放在回调接线之后：切换会触发列重建和 Inspector 重定位按钮状态刷新
        EditorVM.EventsVm.DisplayMode = UserSettingsModel.EventsDisplayMode;

        _stopwatch.Start();

        // 音频由 MainWindowViewModel 以项目为单位创建并初始化；本 VM 只负责接线。
        // 切换谱面重建 VM 时复用同一份音频管线：不重建 WASAPI 设备、不重新解码 BGM。
        _audio = audio;

        // 时间轴状态变化 → 同步音频：暂停时静音、恢复时重定位播放、倍速变化时即时改速率
        _timeline.PropertyChanged += OnTimelinePropertyChanged;

        // 音频自然播完 → 时间轴对齐到末尾并暂停（严格进入“播到头”状态）
        _audio.BgmEnded += OnBgmEnded;

        UserSettingsModel.PropertyChanged += OnUserSettingsPropertyChanged;
    }

    /// <summary> 时间轴状态变化 → 同步音频：暂停时静音、恢复时播放、倍速即时生效 </summary>
    private void OnTimelinePropertyChanged(object? sender, PropertyChangedEventArgs e)
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
    }

    /// <summary> BGM 自然播完 → 时间轴对齐到末尾并暂停 </summary>
    private void OnBgmEnded()
    {
        _timeline.SetTime(_timeline.MaxTime);
        _timeline.Pause();
    }

    /// <summary> 用户设置变化：轨道显隐影响选中；音频相关设置即时应用到共享音频 </summary>
    private void OnUserSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            // 打击音效路径变化 → 重载缓存（设置窗口打开时时间轴已自动暂停，无并发问题）
            case nameof(UserSettingsModel.HitSoundPath):
                _audio.LoadHitSound(UserSettingsModel.HitSoundPath);
                break;
            // 音量设置变化 → 即时应用
            case nameof(UserSettingsModel.MasterVolume):
                _audio.SetMasterVolume(UserSettingsModel.MasterVolume);
                break;
            case nameof(UserSettingsModel.MusicVolume):
                _audio.SetMusicVolume(UserSettingsModel.MusicVolume);
                break;
            case nameof(UserSettingsModel.HitSoundVolume):
                _audio.SetHitSoundVolume(UserSettingsModel.HitSoundVolume);
                break;
            // 静音打击音效：只改音效开关，BGM 不受影响
            case nameof(UserSettingsModel.IsHitSoundMuted):
                _audio.SetHitSoundMuted(UserSettingsModel.IsHitSoundMuted);
                break;
        }
    }

    // === 撤销/重做池：Note/Tune、BPM、LaneEvent 各一个，互不影响 ===
    private readonly UndoManagerModel _notesUndo = new();
    private readonly UndoManagerModel _eventsUndo = new();
    private readonly UndoManagerModel _beatsUndo = new();

    /// <summary> 当前子视图对应的撤销池（Ctrl+Z / Ctrl+Shift+Z 只操作活动面板的池） </summary>
    private UndoManagerModel ActiveUndo => EditorVM.CurrentSubView switch
    {
        EventsViewModel => _eventsUndo,
        BeatsViewModel => _beatsUndo,
        _ => _notesUndo,
    };

    /// <summary>
    /// 命令入池的唯一入口：按命令类型归入对应池。
    /// 不按当前面板分流——切换面板的瞬间可能提交旧面板的 Inspector 暂存编辑，按类型才能落回正确的池
    /// </summary>
    private void Record(IUndoableCommand cmd) => PoolOf(cmd).Record(cmd);

    /// <summary> 命令类型 → 撤销池；复合命令看首条子命令（现有复合命令都由单面板操作打包） </summary>
    private UndoManagerModel PoolOf(IUndoableCommand cmd) => cmd switch
    {
        AddNoteCommand or RemoveNoteCommand or EditNoteCommand or
        AddTuneCommand or RemoveTuneCommand or EditTuneCommand => _notesUndo,
        AddBpmCommand or RemoveBpmCommand or EditBpmCommand => _beatsUndo,
        AddLaneEventCommand or RemoveLaneEventCommand or EditLaneEventCommand => _eventsUndo,
        CompositeCommand composite when composite.Commands.Count > 0 => PoolOf(composite.Commands[0]),
        _ => ActiveUndo, // 未识别的新命令类型兜底进当前面板池
    };

    /// <summary> 撤销活动面板最近一次操作（只操作该面板自己的撤销池） </summary>
    public void Undo()
    {
        CancelGhosts(); // 先退出幽灵，避免其快照基于将被撤销的数据
        CancelRelocations(); // 重定位的旧值快照同样可能过期
        var command = ActiveUndo.Undo();
        if (command == null) return; // 活动面板没有可撤销操作：不跨池取，也跳过整轮刷新
        _chartSheet.SyncNotesToData(); // 命令可能改动了 Note/Tune 单位列表，同步回 struct 列表
        _chartSheet.SyncBpmsToData(); // 命令可能改动了 BPM 单位列表，先同步回 struct 列表再刷新
        _chartSheet.SyncEventsToData(); // 命令可能改动了事件单位列表
        LaneCatalog.Ensure(_chartSheet.GetDataLanes()); // 撤销可能恢复已删除轨道的 note，自动补轨
        EditorVM.RefreshLaneBar();
        ViewVM.RebuildLaneState();
        EditorVM.NotesVM.RefreshNotesPublic();
        EditorVM.BeatsVM.RefreshPublic();
        EditorVM.EventsVm.RefreshPublic();
        InspectorVM.RefreshDisplay();
        ViewVM.NotifyFrameDirty(); // 命令也可能改动 Note，覆盖预览重绘
        AutoSelectOnUndo(command);
    }

    /// <summary> 重做活动面板最近一次撤销（只操作该面板自己的撤销池） </summary>
    public void Redo()
    {
        CancelGhosts(); // 同 Undo：先退出幽灵
        CancelRelocations();
        var command = ActiveUndo.Redo();
        if (command == null) return; // 活动面板没有可重做操作：不跨池取，也跳过整轮刷新
        _chartSheet.SyncNotesToData(); // 同上：命令可能改动 Note/Tune 单位列表
        _chartSheet.SyncBpmsToData(); // 同上：命令可能改动 BPM 单位列表，同步回 struct 列表
        _chartSheet.SyncEventsToData(); // 同上：命令可能改动事件单位列表
        LaneCatalog.Ensure(_chartSheet.GetDataLanes()); // 重做可能恢复已删除轨道的 note，自动补轨
        EditorVM.RefreshLaneBar();
        ViewVM.RebuildLaneState();
        EditorVM.NotesVM.RefreshNotesPublic();
        EditorVM.BeatsVM.RefreshPublic();
        EditorVM.EventsVm.RefreshPublic();
        InspectorVM.RefreshDisplay();
        ViewVM.NotifyFrameDirty(); // 命令也可能改动 Note，覆盖预览重绘
        AutoSelectOnRedo(command);
    }

    /// <summary> 添加 NoteUnit 并记录撤销命令，自动选中 </summary>
    public void AddNote(NoteUnit note)
    {
        _chartSheet.NoteUnitList.Add(note);
        LaneCatalog.Add(note.Data.LaneIndex); // 数据落点若不存在则自动补轨
        Record(new AddNoteCommand(_chartSheet.NoteUnitList, note));
        _chartSheet.SyncNotesToData(); // 同步回 struct 列表，View 预览立即能看到新 Note
        ViewVM.NotifyFrameDirty();
        EditorVM.RefreshLaneBar(); // 底栏“有数据”标记更新
        SelectSingle(note);
    }

    /// <summary> 添加 TuneUnit 到其父 NoteUnit.TuneList 并记录撤销命令，自动选中 </summary>
    public void AddTune(TuneUnit tune)
    {
        tune.Parent!.TuneList ??= [];
        tune.Parent.TuneList.Add(tune);
        LaneCatalog.Add(tune.Data.TargetLane); // Tune 的目标轨若不存在则自动补轨
        Record(new AddTuneCommand(tune.Parent, tune));
        _chartSheet.SyncNotesToData(); // 同步回 struct 列表，View 预览立即能看到新 Tune
        ViewVM.NotifyFrameDirty();
        EditorVM.RefreshLaneBar();
        SelectSingle(tune);
    }

    /// <summary>
    /// Inspector Tune 表格的 + 按钮：为指定 Note 创建一个零长度 Tune 并走正常添加流程。
    /// 按当前约定允许挂到任意 Note 类型；非 Dash 的 Tune 不会被 Shared 序列化，保存时会丢失
    /// </summary>
    private void AddTuneFromInspector(NoteUnit parent)
    {
        AddTune(new TuneUnit
        {
            Parent = parent,
            Data = ChartDefaults.CreateTune(parent.Data.TargetTime, parent.Data.LaneIndex)
        });
    }

    /// <summary>
    /// Inspector Tune 行内 × 按钮：从父 Note 的 TuneList 移除该 Tune 并记入撤销池。
    /// 与 DeleteSelected 不同：保持当前 Note 选中，删除后 TuneList 表格继续显示
    /// </summary>
    private void DeleteTuneFromInspector(TuneUnit tune)
    {
        if (tune.Parent?.TuneList is not { } tuneList) return;
        if (!tuneList.Remove(tune)) return;

        Record(new RemoveTuneCommand(tune.Parent, tune));
        _chartSheet.SyncNotesToData(); // 同步回 struct 列表，View 预览立即更新
        EditorVM.NotesVM.RefreshNotesPublic();
        ViewVM.NotifyFrameDirty();
        EditorVM.RefreshLaneBar(); // Tune 删除后目标轨可能变为空轨
        InspectorVM.RefreshDisplay(); // 重建 TuneList 表格行
    }

    /// <summary> 添加 BpmUnit 并记录撤销命令，自动选中 </summary>
    public void AddBpm(BpmUnit bpm)
    {
        _chartSheet.BpmUnitList.Add(bpm);
        Record(new AddBpmCommand(_chartSheet.BpmUnitList, bpm));
        _chartSheet.SyncBpmsToData(); // 同步回 struct 列表，节拍线和吸附网格立即生效
        SelectSingle(bpm);
    }

    /// <summary> 添加事件 Unit 并记录撤销命令，自动选中 </summary>
    public void AddEvent(ILaneEventUnit unit)
    {
        _chartSheet.LaneEventUnitList.Add(unit);
        LaneCatalog.Add(unit.LaneIndex); // 数据落点若不存在则自动补轨
        Record(new AddLaneEventCommand(_chartSheet.LaneEventUnitList, unit));
        _chartSheet.SyncEventsToData();
        ViewVM.RebuildLaneState();
        EditorVM.RefreshLaneBar();
        SelectSingle(unit);
    }

    /// <summary> 切换 Events 页显示的事件类型：清理幽灵/重定位后设置类型，选中保留 </summary>
    public void SwitchLaneEventKind(LaneEventKind kind)
    {
        if (!EditorVM.IsEventsSelected) return;
        if (EditorVM.EventsVm.CurrentEventKind == kind) return;

        CancelGhosts();
        CancelRelocations();
        EditorVM.EventsVm.CurrentEventKind = kind;
    }

    ///// <summary> 切换 Events 页的事件类型（←→ 临时按键与后续页签 UI 共用） </summary>
    //public void SwitchLaneEventPage(int delta)
    //{
    //    if (!EditorVM.IsEventsSelected) return;
    //
    //    int next = Math.Clamp(
    //        (int)EditorVM.EventsVm.CurrentEventKind + delta,
    //        (int)LaneEventKind.Transform,
    //        (int)LaneEventKind.Parent);
    //    if (next == (int)EditorVM.EventsVm.CurrentEventKind) return;
    //
    //    CancelGhosts();
    //    CancelRelocations();
    //    ClearSelection();
    //    EditorVM.EventsVm.CurrentEventKind = (LaneEventKind)next;
    //}

    /// <summary> 撤销后根据命令类型自动选中：撤销"添加"清选中（Tune 回到父 Note），撤销"删除"恢复选中 </summary>
    private void AutoSelectOnUndo(IUndoableCommand? command)
    {
        switch (command)
        {
            case AddNoteCommand:                 ClearSelection(); break;
            case RemoveNoteCommand removeNote:   SelectSingle(removeNote.Note); break;
            case AddTuneCommand addTune:
                // 【简化版选中处理】撤销生成 Tune：若消失的是当前选中的 Tune，回到父 Note。
                // 注意：这是临时补丁。后续若系统性地优化选中逻辑（统一的“选中项消失”处理），
                // 需要评估此分支是否与新逻辑冲突，大概率需要删除。
                if (SelectedUnit == addTune.Tune) SelectAfterTuneVanished(addTune.Tune);
                else ClearSelection();
                break;
            case RemoveTuneCommand removeTune:   SelectSingle(removeTune.Tune); break;
            case AddBpmCommand:                  ClearSelection(); break;
            case RemoveBpmCommand removeBpm:     SelectSingle(removeBpm.Bpm); break;
            case AddLaneEventCommand:            ClearSelection(); break;
            case RemoveLaneEventCommand removeEvent:
                // 撤销可能落在其他页面：只在事件属于当前界面时恢复选中，避免选中不可见事件
                if (EditorVM.EventsVm.IsUnitInView(removeEvent.Unit))
                    SelectSingle(removeEvent.Unit);
                else
                    ClearSelection();
                break;
            // 复合命令目前只有批量删除：撤销后把整批恢复的 unit 重新选中。
            // 事件若不属于当前 Events 页类型，跳过选中，避免选中不可见项
            case CompositeCommand composite when IsBatchRemove(composite):
            {
                var removed = RemovedUnitsOf(composite);
                removed.RemoveAll(u => u is ILaneEventUnit e && !EditorVM.EventsVm.IsUnitInView(e));
                SelectMany(removed);
                break;
            }
        }
    }

    /// <summary> 判断复合命令是否为一组"删除"操作（批量删除） </summary>
    private static bool IsBatchRemove(CompositeCommand composite)
    {
        foreach (var cmd in composite.Commands)
        {
            if (cmd is not (RemoveNoteCommand or RemoveTuneCommand or RemoveBpmCommand or RemoveLaneEventCommand))
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
                case RemoveLaneEventCommand c: units.Add(c.Unit); break;
            }
        }
        return units;
    }

    /// <summary> 重做后根据命令类型自动选中：重做"添加"选中，重做"删除"清选中（Tune 回到父 Note） </summary>
    private void AutoSelectOnRedo(IUndoableCommand? command)
    {
        switch (command)
        {
            case AddNoteCommand addNote:         SelectSingle(addNote.Note); break;
            case RemoveNoteCommand:              ClearSelection(); break;
            case AddTuneCommand addTune:         SelectSingle(addTune.Tune); break;
            case RemoveTuneCommand removeTune:
                // 【简化版选中处理】重做删除 Tune：若消失的是当前选中的 Tune，回到父 Note。
                // 注意：这是临时补丁。后续若系统性地优化选中逻辑（统一的“选中项消失”处理），
                // 需要评估此分支是否与新逻辑冲突，大概率需要删除。
                if (SelectedUnit == removeTune.Tune) SelectAfterTuneVanished(removeTune.Tune);
                else ClearSelection();
                break;
            case AddBpmCommand addBpm:           SelectSingle(addBpm.Bpm); break;
            case RemoveBpmCommand:               ClearSelection(); break;
            case AddLaneEventCommand addEvent:
                if (EditorVM.EventsVm.IsUnitInView(addEvent.Unit))
                    SelectSingle(addEvent.Unit);
                else
                    ClearSelection();
                break;
            case RemoveLaneEventCommand:         ClearSelection(); break;
            // 重做批量删除 → 被删的 unit 再次消失，清空选中
            case CompositeCommand composite when IsBatchRemove(composite):
                ClearSelection();
                break;
        }
    }

    /// <summary>
    /// 当前选中的 Tune 消失后的选中规则：父 Note 仍在 → 选中父 Note（Dash 的 TuneList 表格随之出现）；
    /// 父 Note 也一起消失 → 清空选中。
    /// 【简化版选中处理】这是临时补丁：后续若系统性地优化选中逻辑（统一的“选中项消失”处理），
    /// 需要评估所有调用点是否与新逻辑冲突，本方法大概率需要删除。
    /// </summary>
    private void SelectAfterTuneVanished(TuneUnit tune)
    {
        if (tune.Parent != null && _chartSheet.NoteUnitList.Contains(tune.Parent))
            SelectSingle(tune.Parent);
        else
            ClearSelection();
    }

    /// <summary>
    /// 删除所有选中的 Unit 并记录撤销命令：单项记一条，多项打包成一条复合命令（撤销一次全恢复）。
    /// 循环内只做纯数据操作，不触发任何刷新；清空选中时统一刷新一轮
    /// </summary>
    public void DeleteSelected()
    {
        CancelGhosts(); // 删除先退出幽灵（快照源即将被删除）
        if (_selectedUnits.Count == 0) return;

        // 【简化版选中处理】记下当前选中的 Tune，删除完成后回到它的父 Note。
        // 注意：这是临时补丁。后续若系统性地优化选中逻辑（统一的“选中项消失”处理），
        // 需要评估此处是否与新逻辑冲突，大概率需要删除。
        TuneUnit? selectedTune = _selectedUnits.Count == 1 ? _selectedUnits[0] as TuneUnit : null;

        var commands = new List<IUndoableCommand>();
        bool bpmRemoved = false;
        bool noteRemoved = false;
        bool eventRemoved = false;

        foreach (var unit in _selectedUnits)
        {
            switch (unit)
            {
                case NoteUnit noteUnit:
                    _chartSheet.NoteUnitList.Remove(noteUnit);
                    commands.Add(new RemoveNoteCommand(_chartSheet.NoteUnitList, noteUnit));
                    noteRemoved = true;
                    break;
                case TuneUnit tuneUnit: // 多选不含 Tune，这里是单击选中 Tune 的单删路径
                    tuneUnit.Parent!.TuneList.Remove(tuneUnit);
                    commands.Add(new RemoveTuneCommand(tuneUnit.Parent, tuneUnit));
                    noteRemoved = true;
                    break;
                case BpmUnit bpmUnit:
                    _chartSheet.BpmUnitList.Remove(bpmUnit);
                    commands.Add(new RemoveBpmCommand(_chartSheet.BpmUnitList, bpmUnit));
                    bpmRemoved = true;
                    break;
                case ILaneEventUnit eventUnit:
                    _chartSheet.LaneEventUnitList.Remove(eventUnit);
                    commands.Add(new RemoveLaneEventCommand(_chartSheet.LaneEventUnitList, eventUnit));
                    eventRemoved = true;
                    break;
            }
        }

        if (noteRemoved)
            _chartSheet.SyncNotesToData(); // 同步回 struct 列表，View 预览立即更新

        if (bpmRemoved)
            _chartSheet.SyncBpmsToData(); // 同步回 struct 列表，节拍线立即更新

        if (eventRemoved)
        {
            _chartSheet.SyncEventsToData();
            ViewVM.RebuildLaneState();
        }

        if (noteRemoved)
            ViewVM.NotifyFrameDirty(); // Note/Tune 删除后预览重绘

        Record(commands.Count == 1 ? commands[0] : new CompositeCommand(commands));
        EditorVM.RefreshLaneBar(); // 删除后底栏“有数据”标记更新（空轨道保留）
        // 【简化版选中处理】删除的是当前选中的 Tune → 回到父 Note；其余情况维持清空选中。
        // 后续若优化选中逻辑，此分支大概率需要删除。
        if (selectedTune != null) SelectAfterTuneVanished(selectedTune);
        else ClearSelection(); // 内部统一刷新三视图（一轮）
    }

    /// <summary>
    /// 快捷删除：删除鼠标命中的 unit（与当前选中无关，其余选中项保持不变）。
    /// 删除记入撤销池；被删项若在选中集合中，只把它自己（以及随 Note 一起消失的 Tune）移出
    /// </summary>
    private void DeleteUnitAt(object unit)
    {
        CancelGhosts(); // 幽灵快照可能引用被删项

        bool noteRemoved = false;
        bool bpmRemoved = false;
        bool eventRemoved = false;
        switch (unit)
        {
            case NoteUnit note:
                noteRemoved = _chartSheet.NoteUnitList.Remove(note);
                if (noteRemoved)
                    Record(new RemoveNoteCommand(_chartSheet.NoteUnitList, note));
                break;
            case TuneUnit tune when tune.Parent?.TuneList is { } tuneList:
                noteRemoved = tuneList.Remove(tune);
                if (noteRemoved)
                    Record(new RemoveTuneCommand(tune.Parent, tune));
                break;
            case BpmUnit bpm:
                bpmRemoved = _chartSheet.BpmUnitList.Remove(bpm);
                if (bpmRemoved)
                    Record(new RemoveBpmCommand(_chartSheet.BpmUnitList, bpm));
                break;
            case ILaneEventUnit eventUnit:
                eventRemoved = _chartSheet.LaneEventUnitList.Remove(eventUnit);
                if (eventRemoved)
                    Record(new RemoveLaneEventCommand(_chartSheet.LaneEventUnitList, eventUnit));
                break;
            default:
                return;
        }
        if (!noteRemoved && !bpmRemoved && !eventRemoved) return;

        if (noteRemoved)
        {
            _chartSheet.SyncNotesToData();
            ViewVM.NotifyFrameDirty();
        }
        if (bpmRemoved)
            _chartSheet.SyncBpmsToData(); // 同步回 struct 列表，节拍线与吸附网格立即更新
        if (eventRemoved)
        {
            _chartSheet.SyncEventsToData();
            ViewVM.RebuildLaneState(); // 内部会触发预览重绘
        }
        EditorVM.RefreshLaneBar(); // 底栏“有数据”标记与空轨状态更新

        // 从选中集合中剔除被删项（删除 Note 时附属 Tune 一并剔除），其余选中保持不变
        var remaining = new List<object>(_selectedUnits.Count);
        foreach (var selected in _selectedUnits)
        {
            if (ReferenceEquals(selected, unit)) continue;
            if (unit is NoteUnit parentNote && selected is TuneUnit childTune &&
                ReferenceEquals(childTune.Parent, parentNote))
                continue;
            remaining.Add(selected);
        }

        if (remaining.Count != _selectedUnits.Count)
        {
            ApplySelection(remaining); // 内部统一刷新三视图、Inspector 与游戏预览选中提示
            return;
        }

        // 被删项不在选中集合：只刷新对应视图
        if (noteRemoved) EditorVM.NotesVM.RefreshNotesPublic();
        if (bpmRemoved) EditorVM.BeatsVM.RefreshPublic();
        if (eventRemoved) EditorVM.EventsVm.RefreshPublic();
    }

    // === 批量剪贴/复制（幽灵模式） ===

    /// <summary>
    /// Ctrl+X/Ctrl+C 快捷键入口：按当前子视图进入批量剪贴/复制幽灵（Notes / Lanes / Beats 三套同构）。
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
            case EventsViewModel eventsVm:
            {
                if (eventsVm.IsGhostActive && eventsVm.GhostIsCopy == isCopy) { eventsVm.CancelGhost(); return; }
                // 选中可跨显示模式保留；复制/剪切只对当前界面内的选中子集生效
                var units = new List<ILaneEventUnit>();
                foreach (var unit in _selectedUnits)
                    if (unit is ILaneEventUnit e && eventsVm.IsUnitInView(e))
                        units.Add(e);
                if (units.Count == 0) return;
                eventsVm.BeginGhost(units, isCopy);
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

    /// <summary> F 键：切换当前幽灵的左右镜像（以基准为对称轴）。Beats 视图没有轨道概念，F 无效 </summary>
    public void ToggleGhostFlip()
    {
        switch (EditorVM.CurrentSubView)
        {
            case NotesViewModel notesVm: notesVm.ToggleGhostFlip(); break;
            case EventsViewModel eventsVm:
                // 类型模式禁止横向移动，F 镜像无效
                if (eventsVm.DisplayMode == LaneDisplayMode.ByLane) return;
                eventsVm.ToggleGhostFlip();
                break;
        }
    }

    /// <summary>
    /// 幽灵放置提交（Note）：剪贴 → 每条一个 EditNoteCommand（Tune 只平移时间）；
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
            // 剪贴：改属性。Tune 只跟随时间平移，其余属性（含 TargetLane）不动
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
        _chartSheet.SyncNotesToData(); // 批量剪贴/复制后同步回 struct 列表，View 预览立即更新
        LaneCatalog.Ensure(_chartSheet.GetDataLanes()); // 防御性补轨：目标轨一定可见，但保证数据一致
        ViewVM.NotifyFrameDirty();
        Record(commands.Count == 1 ? commands[0] : new CompositeCommand(commands));
        EditorVM.RefreshLaneBar();
        ApplySelection(new List<object>(_selectedUnits)); // 统一刷新一轮（含 Inspector 数值更新）
    }

    /// <summary>
    /// 幽灵放置提交（事件）：剪贴 → EditLaneEventCommand；复制 → 值拷贝 + AddLaneEventCommand。
    /// 复制必须保留 Layer、曲线、Targets 等全部字段，不能走工厂。
    /// </summary>
    public void CommitEventGhostPlacement(List<LaneEventGhostPlacement> placements, bool isCopy)
    {
        var commands = new List<IUndoableCommand>();

        foreach (var p in placements)
        {
            if (isCopy)
            {
                var copy = p.Unit.CloneAt(p.NewLane, p.NewTime);
                _chartSheet.LaneEventUnitList.Add(copy);
                commands.Add(new AddLaneEventCommand(_chartSheet.LaneEventUnitList, copy));
            }
            else
            {
                var unit = p.Unit;
                if (unit.StartTime == p.NewTime && unit.LaneIndex == p.NewLane) continue;
                object oldData = unit.CaptureData();
                unit.StartTime = p.NewTime;
                unit.LaneIndex = p.NewLane;
                object newData = unit.CaptureData();
                commands.Add(new EditLaneEventCommand(unit, oldData, newData));
            }
        }

        if (commands.Count == 0) return;
        _chartSheet.SyncEventsToData();
        LaneCatalog.Ensure(_chartSheet.GetDataLanes());
        ViewVM.RebuildLaneState();
        Record(commands.Count == 1 ? commands[0] : new CompositeCommand(commands));
        EditorVM.RefreshLaneBar();
        ApplySelection(new List<object>(_selectedUnits));
    }

    /// <summary>
    /// 幽灵放置提交（BPM）：剪贴 → 每条一个 EditBpmCommand；复制 → 值拷贝 + AddBpmCommand。
    /// 打包一条复合命令入撤销池，同步回 struct 列表让节拍线/吸附立即跟上
    /// </summary>
    public void CommitBpmGhostPlacement(List<BpmGhostPlacement> placements, bool isCopy)
    {
        var commands = new List<IUndoableCommand>();

        foreach (var p in placements)
        {
            if (isCopy)
            {
                var data = p.Unit.Data;
                data.StartTime = p.NewTime;
                var copy = new BpmUnit { Data = data };
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
        Record(commands.Count == 1 ? commands[0] : new CompositeCommand(commands));
        ApplySelection(new List<object>(_selectedUnits)); // 统一刷新一轮
    }

    // === 拖拽开始：从选中集合收集同类型 unit，校验通过后进入拖拽幽灵 ===

    /// <summary> Notes 拖拽开始：基准必须是选中集合中的 NoteUnit，否则放弃 </summary>
    private void BeginNoteDrag(NoteUnit anchor, double pressX, double pressY)
    {
        if (UserSettingsModel.IsDragDisabled) return;

        var units = new List<NoteUnit>();
        foreach (var unit in _selectedUnits)
        {
            if (unit is NoteUnit note) units.Add(note);
            else return; // 选中集合混入非 Note（如单选 Tune）：本次拖拽无效
        }
        if (units.Count == 0 || !units.Contains(anchor)) return;

        EditorVM.NotesVM.BeginDragGhost(units, anchor, pressX, pressY);
    }

    /// <summary> Events 拖拽开始：基准必须是选中集合中的当前类型事件，否则放弃 </summary>
    private void BeginEventDrag(ILaneEventUnit anchor, double pressX, double pressY)
    {
        if (UserSettingsModel.IsDragDisabled) return;

        // 只拖动当前界面内的选中项；界面外选中保留不动
        var units = new List<ILaneEventUnit>();
        foreach (var unit in _selectedUnits)
            if (unit is ILaneEventUnit e && EditorVM.EventsVm.IsUnitInView(e))
                units.Add(e);
        if (units.Count == 0 || !units.Contains(anchor)) return;

        EditorVM.EventsVm.BeginDragGhost(units, anchor, pressX, pressY);
    }

    /// <summary> Beats 拖拽开始：基准必须是选中集合中的 BpmUnit，否则放弃 </summary>
    private void BeginBpmDrag(BpmUnit anchor, double pressX, double pressY)
    {
        if (UserSettingsModel.IsDragDisabled) return;

        var units = new List<BpmUnit>();
        foreach (var unit in _selectedUnits)
        {
            if (unit is BpmUnit bpm) units.Add(bpm);
            else return;
        }
        if (units.Count == 0 || !units.Contains(anchor)) return;

        EditorVM.BeatsVM.BeginDragGhost(units, anchor, pressX, pressY);
    }

    /// <summary> 显示模式切换：更新轨道重定位按钮可见性，并清空选中（切换类型/轨道本身不清） </summary>
    private void OnLaneDisplayModeSwitched()
    {
        // 类型模式没有轨道重定位（轨道/平铺模式允许横向拖动）
        InspectorVM.CanRelocateLane = EditorVM.EventsVm.DisplayMode != LaneDisplayMode.ByLane;
        ClearSelection();
    }

    /// <summary> 平铺模式隐藏某类型：移除该类型的选中项（其他选中保留） </summary>
    private void OnLaneKindVisibilityChanged(LaneEventKind kind, bool visible)
    {
        if (visible) return;

        var kept = new List<object>();
        bool removed = false;
        foreach (var unit in _selectedUnits)
        {
            if (unit is ILaneEventUnit e && e.Kind == kind) removed = true;
            else kept.Add(unit);
        }
        if (removed)
            ApplySelection(kept);
    }

    /// <summary> 选中集合中是否存在已不在 Events 当前界面内的 LaneEvent </summary>
    private bool SelectionOutOfEventsView()
    {
        foreach (var unit in _selectedUnits)
            if (unit is ILaneEventUnit e && !EditorVM.EventsVm.IsUnitInView(e))
                return true;
        return false;
    }

    /// <summary> 取消三个子视图的幽灵（未激活的为无操作） </summary>
    private void CancelGhosts()
    {
        EditorVM.NotesVM.CancelGhost();
        EditorVM.EventsVm.CancelGhost();
        EditorVM.BeatsVM.CancelGhost();
    }

    /// <summary> 进入重定位：先提交输入框暂存，再按单位类型分发给对应编辑器 </summary>
    private void BeginRelocate(RelocateField field)
    {
        if (SelectedUnit is not { } unit) return;
        InspectorVM.CommitPendingEdit();
        CancelGhosts();
        switch (unit)
        {
            case NoteUnit or TuneUnit:
                EditorVM.NotesVM.BeginRelocate(unit, field);
                break;
            case ILaneEventUnit:
                EditorVM.EventsVm.BeginRelocate(unit, field);
                break;
            case BpmUnit:
                EditorVM.BeatsVM.BeginRelocate(unit, field);
                break;
        }
    }

    /// <summary> 重定位提交：命令已在编辑器 VM 中执行，这里入撤销池并统一刷新一轮 </summary>
    private void CommitRelocate(IUndoableCommand command)
    {
        Record(command);
        _chartSheet.SyncNotesToData();
        _chartSheet.SyncBpmsToData();
        _chartSheet.SyncEventsToData();
        EditorVM.NotesVM.RefreshNotesPublic();
        EditorVM.BeatsVM.RefreshPublic();
        EditorVM.EventsVm.RefreshPublic();
        EditorVM.RefreshLaneBar(); // 换轨后底栏“有数据”标记更新
        InspectorVM.RefreshDisplay();
        ViewVM.RebuildLaneState();
        ViewVM.NotifyFrameDirty();
    }

    /// <summary> 取消三个子视图的重定位（未激活的为无操作） </summary>
    private void CancelRelocations()
    {
        EditorVM.NotesVM.CancelRelocate();
        EditorVM.EventsVm.CancelRelocate();
        EditorVM.BeatsVM.CancelRelocate();
    }

    /// <summary>
    /// 深拷贝一个 NoteUnit（Data 值拷贝 + TuneList 全部新建），并按放置结果改写时间/轨道；
    /// 复制出的 Tune 时间跟随同一条 note 的位移，其余属性原样
    /// </summary>
    private static NoteUnit CloneNote(NoteUnit src, int newTime, int newLane)
    {
        var oldData = src.Data;
        // 新模型只有单一视觉轨；Fake 原样拷贝，判定区 JudgeZone 由 ChartSheet 重新派生
        var newData = oldData;
        newData.LaneIndex = newLane;
        newData.TargetTime = newTime;
        newData.LinkedTunes = null; // Tune 由 TuneList 单独重建
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

    /// <summary> 隐藏轨道时把选中集合里落在不可见轨道上的 note/event 移出选中（影响后续 Ctrl+X/Ctrl+C；不影响已生成的幽灵） </summary>
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
        NoteUnit note => LaneCatalog.IsVisible(note.Data.LaneIndex),
        TuneUnit tune => tune.Parent != null && LaneCatalog.IsVisible(tune.Parent.Data.LaneIndex),
        ILaneEventUnit e => LaneCatalog.IsVisible(e.LaneIndex),
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

    /// <summary> Ctrl+A：全选当前子视图可见轨道上的所有 unit（Tune 不参与；Beats 无轨道概念，全选 BPM） </summary>
    public void SelectAllVisible()
    {
        switch (EditorVM.CurrentSubView)
        {
            case NotesViewModel:
            {
                // 只取可见轨道上的 NoteUnit；Tune 挂在 NoteUnit.TuneList 中，天然不参与
                var units = new List<object>();
                foreach (var note in _chartSheet.NoteUnitList)
                    if (LaneCatalog.IsVisible(note.Data.LaneIndex))
                        units.Add(note);
                SelectMany(units);
                break;
            }
            case EventsViewModel eventsVm:
            {
                var units = new List<object>();
                foreach (var e in _chartSheet.LaneEventUnitList)
                    if (eventsVm.IsUnitInView(e))
                        units.Add(e);
                SelectMany(units);
                break;
            }
            case BeatsViewModel:
                // BPM 没有轨道概念：直接全选
                SelectAllBpms(append: false);
                break;
        }
    }

    /// <summary> 双击 Notes 轨道空白：选中该轨道上的全部 Note（append = Ctrl 追加） </summary>
    private void SelectNotesOnLane(int lane, bool append)
    {
        var units = new List<object>();
        foreach (var note in _chartSheet.NoteUnitList)
            if (note.Data.LaneIndex == lane)
                units.Add(note);
        SelectMany(units, append);
    }

    /// <summary> 双击 Events 列空白：选中该列当前界面内的全部事件（轨道=轨道列；类型=类型列） </summary>
    private void SelectEventsOnLane(int column, bool append)
    {
        var eventsVm = EditorVM.EventsVm;
        var units = new List<object>();
        foreach (var e in _chartSheet.LaneEventUnitList)
            if (eventsVm.GetColumnOfUnit(e) == column)
                units.Add(e);
        SelectMany(units, append);
    }

    /// <summary> 全选 BPM（Ctrl+A / 双击 Beats 轨道空白，append = Ctrl 追加） </summary>
    private void SelectAllBpms(bool append)
    {
        var units = new List<object>();
        foreach (var bpm in _chartSheet.BpmUnitList)
            units.Add(bpm);
        SelectMany(units, append);
    }

    /// <summary> 清空选中 </summary>
    public void ClearSelection() => ApplySelection(new List<object>());

    /// <summary>
    /// 应用选中集合的唯一入口：提交 Inspector 暂存编辑 → 同步各 unit 的 IsSelected 镜像标记 →
    /// 分发子 ViewModel → 三个视图各刷新一轮（选中多少项都只刷新这一轮）
    /// </summary>
    private void ApplySelection(List<object> units)
    {
        CancelRelocations(); // 选择变化时退出重定位（目标已不是原单位）
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
        EditorVM.EventsVm.RefreshPublic();

        // View 预览同步选中提示光效（方法内部会触发重绘）
        ViewVM.SetSelection(_selectedUnits);
    }

    /// <summary> 设置 Unit 的 IsSelected 状态 </summary>
    private static void SetSelection(object? unit, bool selected)
    {
        switch (unit)
        {
            case NoteUnit nu: nu.IsSelected = selected; break;
            case TuneUnit tu: tu.IsSelected = selected; break;
            case BpmUnit bu: bu.IsSelected = selected; break;
            case ILaneEventUnit eu: eu.IsSelected = selected; break;
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

    /// <summary> Inspector 跳转：播放中先暂停，再把时间轴定位到 unit 时间点（仅导航，不进撤销池） </summary>
    private void JumpToTime(int timeMs)
    {
        if (!_timeline.IsPaused)
            _timeline.Pause();
        _timeline.SetTime(Math.Clamp(timeMs, 0, _timeline.MaxTime));
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

        // 先记录本帧开始前是否在播放：Update 可能让时间轴播到末尾并自动暂停，
        // 若在更新后再判断会漏掉最后一小段内的 note
        bool wasPlaying = !_timeline.IsPaused;
        if (deltaMs > 0)
        {
            _timeline.Update(deltaMs);
        }
        UpdateHitSounds(wasPlaying, deltaMs);
    }

    /// <summary>
    /// 打击音效触发：仅播放中且时间正常向前推进时，遍历当前 Note 列表，
    /// 对 (游标, 当前时间] 内的每个 note 触发一次音效。
    /// 暂停/滚轮拖动/跳转期间只同步游标，不发声（避免快进时补放一大堆音效）。
    /// Note 列表只含 Dot / Dash / Mute，Tune 不是 Note 不参与；Fake note 不参与判定，也不发打击音效
    /// </summary>
    private void UpdateHitSounds(bool wasPlaying, int deltaMs)
    {
        int current = _timeline.TimeMs;

        // 正常播放一帧最多推进 deltaMs × 倍速（+2 容差吸收取整误差）；
        // 明显超出说明发生了跳转（如进度条点击后立即恢复播放），整段跳过不补响
        double maxAdvance = deltaMs * _timeline.Speed + 2;
        bool isSeek = current - _hitSoundCursorMs > maxAdvance;

        if (wasPlaying && current > _hitSoundCursorMs && !isSeek)
        {
            int from = _hitSoundCursorMs;
            foreach (var note in _chartSheet.NoteUnitList)
            {
                // Fake note 不参与判定，也没有打击音效
                if (note.Data.Fake.HasValue) continue;

                int target = note.Data.TargetTime;
                if (target > from && target <= current)
                    _audio.PlayHitSound(); // 每个 note 独立一路混音输入，多押时自然叠加
            }
        }

        // 不论是否触发，游标都同步到当前时间：暂停期间的 seek 不会在恢复时补响
        _hitSoundCursorMs = current;
    }
    
    /// <summary>
    /// 是否为创建时的默认谱面：没有任何 Note，且只有一个时间 0、BPM 200 的默认点。
    /// 任何改动（包括修改默认 BPM）都会返回 false，用于删除前的警告判断
    /// </summary>
    public bool IsDefaultChart()
    {
        if (_chartSheet.NoteUnitList.Count > 0)
            return false;
        if (_chartSheet.LaneEventUnitList.Count > 0)
            return false;
        if (_chartSheet.BpmUnitList.Count != 1)
            return false;

        var bpm = _chartSheet.BpmUnitList[0].Data;
        return bpm.StartTime == 0 && Math.Abs(bpm.Bpm - 200f) < 0.001f;
    }

    /// <summary>
    /// 序列化当前谱面数据为谱面文件文本。
    /// ChartSheetModel.Serialize 会先把 Note/BPM/LaneEvent 编辑列表同步回 struct 列表，
    /// 再排序并调用 Shared 的 ChartSerializer。
    /// </summary>
    public string SerializeChart() => _chartSheet.Serialize();

    /// <summary>
    /// 应用起始偏移：AudioModel 会按新偏移重新解析音频（补静音/裁剪），
    /// 这里同步时间轴上限并恢复原来的播放位置
    /// </summary>
    public void SetMusicStartOffset(int offsetMs)
    {
        if (_audio.MusicStartOffsetMs == offsetMs) return;

        int time = _timeline.TimeMs;
        bool wasPlaying = !_timeline.IsPaused;
        _timeline.Pause(); // 重新解析前先停音频

        _audio.SetMusicStartOffset(offsetMs);

        if (_audio.HasBgm)
        {
            _timeline.MaxTime = _audio.TotalDurationMs;
            IsAudioShorterThanOneMs = _audio.IsAudioShorterThanOneMs;
            AudioDurationMs = _audio.TotalDurationMs;
        }
        else
        {
            IsAudioShorterThanOneMs = false;
            AudioDurationMs = 0;
        }

        _timeline.SetTime(Math.Clamp(time, 0, _timeline.MaxTime));
        if (wasPlaying) _timeline.Resume();
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
            IsAudioShorterThanOneMs = false;
            AudioDurationMs = 0;
            return false;
        }

        _timeline.MaxTime = _audio.TotalDurationMs; // 时间轴范围跟随处理后的音频时长
        _timeline.SetTime(0);                       // 导入新音频后位置归零
        IsAudioShorterThanOneMs = _audio.IsAudioShorterThanOneMs;
        AudioDurationMs = _audio.TotalDurationMs;
        return true;
    }

    /// <summary>
    /// 清空 BGM 并恢复无音频模式：时间轴回到默认长度，播放位置归零
    /// </summary>
    public void ClearBgm()
    {
        _timeline.Pause();
        _audio.LoadBgm(string.Empty); // 空路径会让 AudioModel 清空已缓存的 BGM
        _timeline.MaxTime = 180000;    // TimelineModel 的无音频默认上限
        _timeline.SetTime(0);
        IsAudioShorterThanOneMs = false;
        AudioDurationMs = 0;
    }

    /// <summary>
    /// 释放资源：停止时间轴并解除事件订阅。
    /// 音频是项目级共享对象，由 MainWindowViewModel 统一释放，这里不能 Dispose
    /// </summary>
    public void Dispose()
    {
        _timeline.Pause(); // 先停播放（挂接会同步停止共享音频输出）
        _timeline.PropertyChanged -= OnTimelinePropertyChanged;
        _audio.BgmEnded -= OnBgmEnded;
        UserSettingsModel.PropertyChanged -= OnUserSettingsPropertyChanged;
    }
}