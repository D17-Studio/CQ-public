using CQMusicGame.Shared;

namespace CQ_ChartMaker.Models;

/// <summary>
/// 制谱器新建谱面数据时的统一初值入口。
/// 所有新建 Note / Tune / BPM / LaneEvent 必须经过这里的工厂方法。
/// 读档、编辑、重定位、撤销重做、预览临时对象等场景不经过这里，保持结构体零值或旧值。
/// </summary>
public static class ChartDefaults
{
    /// <summary> 新建 LaneEvent 的 Layer（制谱器暂不使用） </summary>
    private const int LaneEventLayer = 0;

    // ===== 工厂方法：所有新建谱面数据都必须走这里 =====

    /// <summary>
    /// 新建 Note（Dot / Dash / Mute）。
    /// 类型、轨道、正解时间、时长由创建场景决定；其余字段使用统一初值。
    /// </summary>
    public static NoteData CreateNote(NoteType type, int laneIndex, int targetTime, int sustainDuration = 0)
        => new()
        {
            Type = type,
            LaneIndex = laneIndex,
            TargetTime = targetTime,
            SustainDuration = sustainDuration,
            AppearOffset = -3000,   // 相对正解时间提前 3 秒出现（单位毫秒）
            Fake = null,            // null = 普通 Note
            LinkedTunes = null!,    // null = 无；Tune 由 TuneList 单独管理
        };

    /// <summary>
    /// 新建 Tune。
    /// 正解时间、父 Dash 轨道和时长由创建场景决定；其余字段使用统一初值。
    /// </summary>
    public static TuneData CreateTune(int targetTime, int parentLane, int duration = 0)
        => new()
        {
            TargetTime = targetTime,
            UseRelative = true,
            TargetLane = parentLane,
            PosValue = Vec2.Zero,
            Duration = duration,
        };

    /// <summary>
    /// 新建手动放置的 BPM 节点。
    /// 开始时间由创建场景决定；BPM 值使用统一初值。
    /// </summary>
    public static BpmData CreateBpm(int startTime)
        => new() { StartTime = startTime, Bpm = 0f };

    /// <summary>
    /// 新建曲线（ChannelCurve）：绝对值平坦曲线（From = To = 当前值），线性。
    /// 当前值由调用方从 LaneState 采样传入，保证启用/新建曲线时轨道状态不跳变。
    /// </summary>
    public static ChannelCurve CreateCurve(float value) => new()
    {
        From = value,
        FromMode = ValueMode.Absolute,
        To = value,
        ToMode = ValueMode.Absolute,
        Ease = EaseCurve.Linear,
    };

    /// <summary>
    /// 新建轨道变换事件。
    /// 轨道、开始时间、时长由创建场景决定；三条曲线初值为 null（该轴无动画）。
    /// </summary>
    public static LaneEvent.Transform CreateLaneTransform(int laneIndex, int startTime, int duration)
        => new()
        {
            LaneIndex = laneIndex,
            StartTime = startTime,
            Duration = duration,
            Anchor = Vec2.Zero,
            XCurve = null,          // 三条曲线 null = 该轴无动画
            YCurve = null,
            RotationCurve = null,
            Layer = LaneEventLayer,
        };

    /// <summary>
    /// 新建不透明度事件。
    /// 轨道、开始时间、时长由创建场景决定；目标全选，曲线取第一个目标部位的当前不透明度。
    /// </summary>
    public static LaneEvent.Opacity CreateLaneOpacity(int laneIndex, int startTime, int duration, float opacity)
        => new()
        {
            LaneIndex = laneIndex,
            StartTime = startTime,
            Duration = duration,
            Targets = OpacityTarget.Head | OpacityTarget.Line | OpacityTarget.Label | OpacityTarget.Note,  // 全选
            ValueCurve = CreateCurve(opacity),
            Layer = LaneEventLayer,
        };

    /// <summary>
    /// 新建轨道线长度事件。
    /// 轨道、开始时间、时长由创建场景决定；曲线取当前长度。
    /// </summary>
    public static LaneEvent.Length CreateLaneLength(int laneIndex, int startTime, int duration, float length)
        => new()
        {
            LaneIndex = laneIndex,
            StartTime = startTime,
            Duration = duration,
            ValueCurve = CreateCurve(length),
            Layer = LaneEventLayer,
        };

    /// <summary>
    /// 新建轨道流速事件。
    /// 轨道、开始时间、时长由创建场景决定；模式与数值取当前流速采样。
    /// </summary>
    public static LaneEvent.Speed CreateLaneSpeed(int laneIndex, int startTime, int duration,
        LaneSpeedMode mode, float speed)
        => new()
        {
            LaneIndex = laneIndex,
            StartTime = startTime,
            Duration = duration,
            SpeedMode = mode,
            ValueCurve = CreateCurve(speed),
            Layer = LaneEventLayer,
        };

    /// <summary>
    /// 新建判定区事件（瞬时，无时长）。
    /// 轨道、开始时间由创建场景决定；判定区初值为 All。
    /// </summary>
    public static LaneEvent.Zone CreateLaneZone(int laneIndex, int startTime)
        => new()
        {
            LaneIndex = laneIndex,
            StartTime = startTime,
            TargetZone = JudgeZone.All,
            Layer = LaneEventLayer,
        };

    /// <summary>
    /// 新建父轨事件（瞬时，无时长）。
    /// 轨道、开始时间由创建场景决定；ParentLane 初值为 null（无父）。
    /// </summary>
    public static LaneEvent.Parent CreateLaneParent(int laneIndex, int startTime)
        => new()
        {
            LaneIndex = laneIndex,
            StartTime = startTime,
            ParentLane = null,
            Layer = LaneEventLayer,
        };
}
