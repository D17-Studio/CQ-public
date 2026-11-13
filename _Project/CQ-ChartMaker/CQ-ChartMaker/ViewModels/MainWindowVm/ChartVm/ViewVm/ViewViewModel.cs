using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
// using CQ_ChartMaker.Models.Timeline; // 原命名空间
using CQ_ChartMaker.Common;
using CQ_ChartMaker.Models;
using CQMusicGame.Shared;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace CQ_ChartMaker.ViewModels;

/// <summary>
/// 视图面板的 ViewModel，负责显示时间轴信息并提供播放控制
/// </summary>
public partial class ViewViewModel : ObservableObject
{
    [ObservableProperty]
    private TimelineModel? _timeline;

    /// <summary> 谱面数据模型：预览渲染读取同步后的 struct 数据 </summary>
    [ObservableProperty]
    private ChartSheetModel? _chartSheet;

    private LaneCatalogModel? _laneCatalog;

    /// <summary> 当前项目的轨道集合：预览按其中全部存在的轨道绘制 </summary>
    public LaneCatalogModel? LaneCatalog
    {
        get => _laneCatalog;
        set
        {
            if (_laneCatalog == value) return;
            _laneCatalog = value;
            NotifyFrameDirty();
        }
    }

    // 换谱面时从六类 LaneEvent 重建 LaneState
    partial void OnChartSheetChanged(ChartSheetModel? value) => RebuildLaneState();

    /// <summary>
    /// 当前时间（毫秒），可读可写，用于 Slider 双向绑定
    /// </summary>
    public int CurrentTimeMs
    {
        get => Timeline?.TimeMs ?? 0;
        set
        {
            if (Timeline != null && value != Timeline.TimeMs)
                Timeline.SetTime(value);
        }
    }

    /// <summary>
    /// 最大时间（毫秒），用于 Slider 的 Maximum
    /// </summary>
    public int MaxTime => Timeline?.MaxTime ?? 10000;

    /// <summary>
    /// 格式化的当前时间（mm:ss:fff）
    /// </summary>
    public string FormattedTime
    {
        get
        {
            int ms = CurrentTimeMs;
            int minutes = ms / 60000;
            int seconds = (ms / 1000) % 60;
            int millis = ms % 1000;
            return $"{minutes:D2}:{seconds:D2}:{millis:D3}";
        }
    }

    /// <summary>
    /// 格式化的最大时间（mm:ss:fff）
    /// </summary>
    public string MaxTimeFormatted
    {
        get
        {
            int ms = MaxTime;
            int minutes = ms / 60000;
            int seconds = (ms / 1000) % 60;
            int millis = ms % 1000;
            return $"{minutes:D2}:{seconds:D2}:{millis:D3}";
        }
    }

    /// <summary>
    /// 当前倍速的显示文字（倍速按钮内容）
    /// </summary>
    public string SpeedText => Timeline?.Speed switch
    {
        0.25 => "0.25x",
        0.5 => "0.5x",
        _ => "1.0x"
    };

    /// <summary>
    /// 当前是否暂停（用于按钮图标）
    /// </summary>
    public bool IsPaused => Timeline?.IsPaused ?? true;

    partial void OnTimelineChanged(TimelineModel? value)
    {
        if (value != null)
        {
            value.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(TimelineModel.TimeMs))
                {
                    OnPropertyChanged(nameof(CurrentTimeMs));
                    OnPropertyChanged(nameof(FormattedTime));
                }
                else if (e.PropertyName == nameof(TimelineModel.IsPaused))
                {
                    OnPropertyChanged(nameof(IsPaused));
                }
                else if (e.PropertyName == nameof(TimelineModel.MaxTime))
                {
                    OnPropertyChanged(nameof(MaxTime));
                    OnPropertyChanged(nameof(MaxTimeFormatted));
                }
                else if (e.PropertyName == nameof(TimelineModel.Speed))
                {
                    OnPropertyChanged(nameof(SpeedText));
                }
            };
        }
    }

    // === 游戏画面预览（ViewView） ===

    private const int GlobalSpeedMin = 100;
    private const int GlobalSpeedMax = 2000;
    private const int GlobalSpeedStep = 100;

    /// <summary> 用户设置：预览流速保存在这里并自动持久化 </summary>
    [ObservableProperty]
    private UserSettingsModel? _userSettings;

    partial void OnUserSettingsChanged(UserSettingsModel? oldValue, UserSettingsModel? newValue)
    {
        if (oldValue != null)
            oldValue.PropertyChanged -= OnUserSettingsPropertyChanged;
        if (newValue != null)
            newValue.PropertyChanged += OnUserSettingsPropertyChanged;
    }

    /// <summary> 预览全局流速（世界单位/秒），读取时夹到 100~2000 防止设置文件被手改越界 </summary>
    public int GlobalSpeed => Math.Clamp(UserSettings?.GlobalSpeed ?? 1000, GlobalSpeedMin, GlobalSpeedMax);

    /// <summary> 预览画面是否隐藏轨道标号：读取/写回用户设置（ToggleButton 双向绑定） </summary>
    public bool IsLaneLabelHidden
    {
        get => UserSettings?.IsLaneLabelHidden ?? false;
        set
        {
            if (UserSettings == null || UserSettings.IsLaneLabelHidden == value) return;
            UserSettings.IsLaneLabelHidden = value; // 写入设置模型后由模型自动持久化
        }
    }

    /// <summary> 设置项变化：流速变化时刷新界面数值并重建 Note 位置计算器；标号显隐变化时触发重绘 </summary>
    private void OnUserSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UserSettingsModel.IsLaneLabelHidden))
        {
            OnPropertyChanged(nameof(IsLaneLabelHidden));
            NotifyFrameDirty();
            return;
        }

        if (e.PropertyName != nameof(UserSettingsModel.GlobalSpeed)) return;

        OnPropertyChanged(nameof(GlobalSpeed));
        if (_laneState == null) return;

        // 流速变化只影响 Note 位置计算，LaneState（LaneEvent 构建）无需重建
        _calculator = new NotePositionCalculator(GlobalSpeed, _laneState);
        NotifyFrameDirty();
    }

    /// <summary> 轨道状态查询：Motion 数据变化时整体重建 </summary>
    private LaneState? _laneState;

    /// <summary> Note 坐标计算器（构造时持有 LaneState 引用，必须随其一起重建） </summary>
    private NotePositionCalculator? _calculator;

    /// <summary>
    /// 从当前谱面六类事件重建 LaneState，并重建 NotePositionCalculator。
    /// ChartSheet.LaneState 是解析时的一次性快照，事件编辑后必须调用本方法。
    /// </summary>
    public void RebuildLaneState()
    {
        if (ChartSheet == null)
        {
            _laneState = null;
            _calculator = null;
            NotifyFrameDirty();
            return;
        }

        var sheet = ChartSheet.ChartSheet;
        _laneState = new LaneState(
            sheet.GetTransformEvents(),
            sheet.GetOpacityEvents(),
            sheet.GetLengthEvents(),
            sheet.GetSpeedEvents(),
            sheet.GetZoneEvents(),
            sheet.GetParentEvents());
        _calculator = new NotePositionCalculator(GlobalSpeed, _laneState);
        NotifyFrameDirty();
    }

    /// <summary> 当前帧存在轨道的显示状态（顺序与 LaneCatalog 一致） </summary>
    private readonly List<LaneDrawInfo> _laneInfos = new();

    /// <summary> 当前帧需要绘制的 Note 信息（顶点切片 + 类型 + 不透明度） </summary>
    private readonly List<NoteDrawInfo> _noteInfos = new();

    /// <summary> 当前帧 Tune 标记位置（每个 Tune 的移动起点/终点各一个） </summary>
    private readonly List<TuneMarkerInfo> _tuneMarkers = new();

    /// <summary> 当前帧的打击特效（判定时刻的轨道头位置 + 距判定时刻的毫秒数） </summary>
    private readonly List<HitEffectInfo> _hitEffects = new();

    /// <summary> 世界坐标顶点总缓冲（所有 Note 共用，由 NoteDrawInfo 的切片引用） </summary>
    private readonly List<Vec2> _points = new();

    /// <summary> Note 临时点缓冲（计算器会先清空传入列表，不能直接传总缓冲） </summary>
    private readonly List<Vec2> _noteScratch = new();

    /// <summary>
    /// 选中 Note 的匹配键。预览渲染读取的是 struct 数据拷贝，无法按引用匹配，
    /// 因此用「目标时间 + 视觉轨 + 类型」定位；完全重叠的重复 Note 会一起高亮，视觉上无差别。
    /// </summary>
    private readonly HashSet<(int TargetTime, int LaneIndex, NoteType Type)> _selectedNoteKeys = new();

    /// <summary> 选中轨道下标：选中该轨道任意 LaneEvent 时整条轨道发光 </summary>
    private readonly HashSet<int> _selectedLaneIndices = new();

    /// <summary> 当前帧选中轨道的世界坐标锚点（按当前时间与轨道状态每帧计算） </summary>
    private readonly List<Vec2> _selectedAnchors = new();

    /// <summary>
    /// 选中的 Transform 事件：锚点会被 Inspector 实时编辑，每帧重新读取其坐标。
    /// 旧 MotionUnit 的锚点显示由 LaneEvent 的 Transform 事件继承。
    /// </summary>
    private readonly List<TransformEventUnit> _selectedTransformUnits = new();

    // 补缝圆已改为复用 Dash 的计算结果，此缓冲不再需要
    // private readonly List<Vec2> _tuneScratch = new();

    /// <summary> 当前帧轨道显示状态（只读视图，供自绘控件使用） </summary>
    public IReadOnlyList<LaneDrawInfo> LaneInfos => _laneInfos;

    /// <summary> 当前帧 Note 绘制信息（只读视图） </summary>
    public IReadOnlyList<NoteDrawInfo> NoteInfos => _noteInfos;

    /// <summary> 当前帧 Tune 标记（只读视图） </summary>
    public IReadOnlyList<TuneMarkerInfo> TuneMarkers => _tuneMarkers;

    /// <summary> 当前帧打击特效（只读视图） </summary>
    public IReadOnlyList<HitEffectInfo> HitEffects => _hitEffects;

    /// <summary> 当前帧世界坐标顶点总缓冲（只读视图） </summary>
    public IReadOnlyList<Vec2> Points => _points;

    /// <summary> 选中轨道的锚点位置（世界坐标，只读视图） </summary>
    public IReadOnlyList<Vec2> SelectedAnchors => _selectedAnchors;

    /// <summary> 画面失效通知：谱面数据编辑后触发，让自绘控件重绘（播放中由时间变化驱动） </summary>
    public event Action? FrameDirty;

    /// <summary> 触发画面重绘 </summary>
    public void NotifyFrameDirty() => FrameDirty?.Invoke();

    // LaneState 现在由 ChartSheet 构造时从六类 LaneEvent 生成，只读、无需重建。
    // 旧的 Motion 重建入口已删除（下一波 LaneEvent 编辑时如有需要再恢复）。

    /// <summary>
    /// 同步 ChartViewModel 的选中集合，用于 View 预览的选中提示光效。
    /// Note 记录匹配键；Tune 选中时高亮其所属的父 Dash Note；LaneEvent 记录轨道下标（整条轨道发光），
    /// 其中 Transform 事件额外记录引用，用于每帧绘制其旋转锚点。
    /// 其它类型（BPM 等）预览不显示，忽略。
    /// </summary>
    public void SetSelection(IReadOnlyList<object> units)
    {
        _selectedNoteKeys.Clear();
        _selectedLaneIndices.Clear();
        _selectedAnchors.Clear();
        _selectedTransformUnits.Clear();

        foreach (var unit in units)
        {
            switch (unit)
            {
                case NoteUnit note:
                    AddSelectedNote(note.Data);
                    break;
                case TuneUnit tune when tune.Parent != null:
                    AddSelectedNote(tune.Parent.Data);
                    break;
                case TransformEventUnit transform:
                    _selectedLaneIndices.Add(transform.LaneIndex);
                    _selectedTransformUnits.Add(transform);
                    break;
                case ILaneEventUnit e:
                    _selectedLaneIndices.Add(e.LaneIndex);
                    break;
            }
        }

        NotifyFrameDirty(); // 选中变化立即反映到预览画面
    }

    /// <summary> 记录一条 Note 的高亮匹配键 </summary>
    private void AddSelectedNote(NoteData note)
        => _selectedNoteKeys.Add((note.TargetTime, note.LaneIndex, note.Type));

    /// <summary>
    /// 收集当前帧的渲染数据（世界坐标，不做屏幕映射）：
    /// 7 条轨道状态 + 可见 Note 的顶点切片 + Tune 标记。
    /// 全部写入复用缓冲，帧间不产生分配。
    /// </summary>
    public void CollectFrame()
    {
        _selectedAnchors.Clear();

        _noteInfos.Clear();
        _tuneMarkers.Clear();
        _hitEffects.Clear();
        _points.Clear();

        if (ChartSheet == null || _laneState == null || _calculator == null) return;

        int time = CurrentTimeMs;

        // 选中 Transform 事件的锚点：Anchor 是轨道局部坐标下相对轨道头的偏移（旋转中心），
        // 世界坐标 = 轨道头世界坐标 + 旋转后的锚点偏移。
        // 每帧按当前时间计算，轨道移动/旋转以及 Inspector 编辑锚点都会实时反映到标记上。
        for (int i = 0; i < _selectedTransformUnits.Count; i++)
        {
            TransformEventUnit transform = _selectedTransformUnits[i];
            LaneFrame frame = _laneState.GetFrame(transform.Data.LaneIndex, time);
            _selectedAnchors.Add(frame.Position + transform.Data.Anchor.Rotate(frame.Rotation));
        }

        // 轨道：按当前项目的存在轨道集合逐条取帧。
        // GetFrame 会合成 Parent 事件后的世界坐标与旋转；Length 为轨道线长度（世界单位）。
        _laneInfos.Clear();
        if (LaneCatalog != null)
        {
            foreach (int laneIndex in LaneCatalog.Lanes)
            {
                LaneFrame frame = _laneState.GetFrame(laneIndex, time);
                _laneInfos.Add(new LaneDrawInfo
                {
                    LaneIndex = laneIndex,
                    Position = frame.Position,
                    Rotation = frame.Rotation,
                    Length = _laneState.Length(laneIndex, time),
                    HeadOpacity = _laneState.HeadOpacity(laneIndex, time),
                    LineOpacity = _laneState.LineOpacity(laneIndex, time),
                    LabelOpacity = _laneState.LabelOpacity(laneIndex, time),
                    NoteOpacity = _laneState.NoteOpacity(laneIndex, time),
                    IsSelected = _selectedLaneIndices.Contains(laneIndex),
                });
            }
        }

        var notes = ChartSheet.ChartSheet.GetNoteList();
        foreach (var note in notes)
        {
            // 打击特效：判定后窗口时长内，在判定时刻的轨道头位置生成特效。
            // 位置查询用固定的 note.TargetTime，与当前时间无关，所以轨道之后移走不会带着特效跑；
            // Fake note 不参与判定，不产生特效。必须放在下面的出现/结束时间过滤之前，
            // 否则 Dot 判定后会被立即剔除而看不到特效。
            if (!note.Fake.HasValue && note.TargetTime <= time &&
                time - note.TargetTime < HitEffectPainter.WindowMs)
            {
                _hitEffects.Add(new HitEffectInfo
                {
                    Position = _laneState.GetFrame(note.LaneIndex, note.TargetTime).Position,
                    AgeMs = time - note.TargetTime,
                });
            }

            // 未到出现时间（AppearOffset 为负，target + offset 之前不画）
            if (time < note.TargetTime + note.AppearOffset) continue;
            // 已过结束时间（非法负时长不做特殊处理，按方案原样剔除）
            if (time > note.TargetTime + note.SustainDuration) continue;

            float opacity = _laneState.NoteOpacity(note.LaneIndex, time);

            // 计算本帧顶点：Dot 1 点 / Mute 2 点 / Dash 2+2×Tune 点
            int start = _points.Count;
            _calculator.Calculate(note, time, _noteScratch);
            _points.AddRange(_noteScratch);

            // Dash 尾端若正好落在某个 Tune 的终点，且该 Tune 的标记圆正在显示，
            // 尾部封口光带需要从白圆外缘开始，避免盖住白圆
            bool tailHasMarker = false;
            if (note.Type == NoteType.Dash && note.LinkedTunes is { Length: > 0 })
            {
                var lastTune = note.LinkedTunes[^1];
                tailHasMarker =
                    note.TargetTime + note.SustainDuration <= lastTune.TargetTime + lastTune.Duration &&
                    time <= lastTune.TargetTime;
            }

            _noteInfos.Add(new NoteDrawInfo
            {
                PointStart = start,
                PointCount = _noteScratch.Count,
                Type = note.Type,
                Opacity = opacity,
                IsSelected = _selectedNoteKeys.Contains((note.TargetTime, note.LaneIndex, note.Type)),
                TailHasMarker = tailHasMarker,
            });

            // Dash 转折补缝圆：折线转弯处用平头线段拼接，外侧会露出缺口，用与身体同宽的白圆补齐。
            // 顶点直接复用上面 Calculate 对整条 Dash 的输出，顺序为 [Dash头, Tune0起点, Tune0终点, Tune1起点, ...]，
            // 所以第 i 个 Tune 对应的两个转折点下标是 1+2i 和 2+2i。
            if (note.Type == NoteType.Dash && note.LinkedTunes != null)
            {
                for (int i = 0; i < note.LinkedTunes.Length; i++)
                {
                    if (time > note.LinkedTunes[i].TargetTime) continue; // 已过判定时间，不再绘制
                    _tuneMarkers.Add(new TuneMarkerInfo
                    {
                        Position = _noteScratch[1 + 2 * i],
                        Opacity = opacity,
                    });
                    _tuneMarkers.Add(new TuneMarkerInfo
                    {
                        Position = _noteScratch[2 + 2 * i],
                        Opacity = opacity,
                    });
                }
            }
        }
    }

    [RelayCommand]
    private void Pause() => Timeline?.Pause();

    [RelayCommand]
    private void Resume() => Timeline?.Resume();

    [RelayCommand]
    private void SetTime(int timeMs) => Timeline?.SetTime(timeMs);

    [RelayCommand]
    private void TogglePlayPause() => Timeline?.TogglePlayPause();

    /// <summary> 切换播放倍速（参数为菜单项文字：1.0x / 0.5x / 0.25x） </summary>
    [RelayCommand]
    private void SetSpeed(string speedText)
    {
        if (Timeline == null) return;
        Timeline.Speed = speedText switch
        {
            "0.5x" => 0.5,
            "0.25x" => 0.25,
            _ => 1.0
        };
    }

    /// <summary> 流速 +100（上限 2000） </summary>
    [RelayCommand]
    private void IncreaseGlobalSpeed() => SetGlobalSpeed(GlobalSpeed + GlobalSpeedStep);

    /// <summary> 流速 -100（下限 100） </summary>
    [RelayCommand]
    private void DecreaseGlobalSpeed() => SetGlobalSpeed(GlobalSpeed - GlobalSpeedStep);

    /// <summary> 写入流速设置（UserSettingsModel 自动持久化），并夹在 100~2000 </summary>
    private void SetGlobalSpeed(int value)
    {
        if (UserSettings == null) return;
        UserSettings.GlobalSpeed = Math.Clamp(value, GlobalSpeedMin, GlobalSpeedMax);
    }
    
    // 拖拽进度条前的播放状态：松手时依据它决定是否恢复播放
    private bool _resumeAfterDrag;

    /// <summary>
    /// 进度条按下（拖拽把手或点击轨道）时暂停播放：避免拖拽改位置与播放推进互相打架
    /// 同时记住按下前的播放状态，供松手时决定是否恢复
    /// </summary>
    public void PauseOnDrag()
    {
        _resumeAfterDrag = !(Timeline?.IsPaused ?? true);
        Timeline?.Pause();
    }

    /// <summary>
    /// 进度条松开时调用：若按下前处于播放中，则从拖拽后的位置继续播放；原本暂停则保持暂停
    /// </summary>
    public void ResumeAfterDrag()
    {
        if (!_resumeAfterDrag) return;
        _resumeAfterDrag = false;
        Timeline?.Resume();
    }
}

/// <summary> 单条轨道在一帧中的显示状态（世界坐标 + 长度 + 四种不透明度） </summary>
public struct LaneDrawInfo
{
    /// <summary> 轨道编号（绘制标号与选中高亮用） </summary>
    public int LaneIndex;

    public Vec2 Position;
    public float Rotation;

    /// <summary> 轨道线长度（世界单位，来自 LaneState.Length 采样） </summary>
    public float Length;

    public float HeadOpacity;
    public float LineOpacity;
    public float LabelOpacity;
    public float NoteOpacity;

    /// <summary> 该轨道是否有选中的 MotionUnit（整条轨道发光提示） </summary>
    public bool IsSelected;
}

/// <summary> 单个 Note 在一帧中的顶点切片与绘制信息 </summary>
public struct NoteDrawInfo
{
    /// <summary> 顶点在 ViewViewModel.Points 中的起始下标 </summary>
    public int PointStart;

    /// <summary> 顶点数量 </summary>
    public int PointCount;

    public NoteType Type;

    /// <summary> 所在轨道的 Note 不透明度 </summary>
    public float Opacity;

    /// <summary> 该 Note 是否被选中（选中提示光效） </summary>
    public bool IsSelected;

    /// <summary> Dash 尾部顶点上是否有正在显示的 Tune 白圆（尾端封口光带需避开它） </summary>
    public bool TailHasMarker;
}

/// <summary> 单个 Tune 标记（移动起点/终点，世界坐标） </summary>
public struct TuneMarkerInfo
{
    public Vec2 Position;
    public float Opacity;
}