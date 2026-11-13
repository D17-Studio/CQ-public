using System;

namespace CQMusicGame.Shared
{
    public enum NoteType
    {
        Dot,
        Dash,
        Mute
    }

    public enum FakeMode
    {
        Default   // 到正解时间瞬间消失，不参与判定与计分
    }

    /// <summary> 附属于 Dash，描述 Dash 的移动 </summary>
    public struct TuneData
    {
        public int TargetTime;
        public bool UseRelative;      // true: PosValue 为偏移；false: 世界坐标
        public int TargetLane;        // 视觉轨道，约定从 0 开始的非负整数（不强制校验）
        public Vec2 PosValue;
        public int Duration;          // 到达目标点后的过渡时长
    }

    /// <summary> 单个 Note 的谱面数据 </summary>
    public struct NoteData
    {
        public NoteType Type;
        public int LaneIndex;          // 视觉轨道，约定从 0 开始的非负整数（不强制校验）
        public int TargetTime;
        public int SustainDuration;
        public int AppearOffset;       // 相对正解时间的出现偏移，默认 -3000
        public FakeMode? Fake;         // null = 普通Note
        public TuneData[] LinkedTunes; // 仅 Dash 有效
    }
}
