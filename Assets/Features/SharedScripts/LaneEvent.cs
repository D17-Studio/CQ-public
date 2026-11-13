using System;

namespace CQMusicGame.Shared
{
    [Flags]
    public enum OpacityTarget
    {
        None = 0,
        Head = 1,
        Line = 2,
        Label = 4,
        Note = 8
    }

    public enum JudgeZone
    {
        All,
        Left,
        Right,
        L1,
        L2,
        R1,
        R2
    }

    public enum LaneSpeedMode
    {
        Multiplier,   // 玩家流速 × 曲线值
        Fixed         // 忽略玩家流速
    }

    /// <summary>
    /// 轨道事件容器；LaneIndex 为视觉轨道，约定从 0 开始的非负整数（不强制校验）
    /// </summary>
    public static class LaneEvent
    {
        /// <summary> 轨道变换事件（X/Y/旋转） </summary>
        public struct Transform
        {
            public int LaneIndex;
            public int StartTime;
            public int Duration;
            public Vec2 Anchor;                // 旋转锚点
            public ChannelCurve? XCurve;       // 三条曲线可各自为空
            public ChannelCurve? YCurve;
            public ChannelCurve? RotationCurve;
            public int Layer;                  // 仅供制谱器
        }

        /// <summary> 不透明度事件 </summary>
        public struct Opacity
        {
            public int LaneIndex;
            public int StartTime;
            public int Duration;
            public OpacityTarget Targets;      // 可组合
            public ChannelCurve ValueCurve;
            public int Layer;                  // 仅供制谱器
        }

        /// <summary> 轨道线长度事件 </summary>
        public struct Length
        {
            public int LaneIndex;
            public int StartTime;
            public int Duration;
            public ChannelCurve ValueCurve;
            public int Layer;                  // 仅供制谱器
        }

        /// <summary> 轨道流速事件 </summary>
        public struct Speed
        {
            public int LaneIndex;
            public int StartTime;
            public int Duration;
            public LaneSpeedMode SpeedMode;
            public ChannelCurve ValueCurve;
            public int Layer;                  // 仅供制谱器
        }

        /// <summary> 判定区事件（瞬时） </summary>
        public struct Zone
        {
            public int LaneIndex;
            public int StartTime;
            public JudgeZone TargetZone;
            public int Layer;                  // 仅供制谱器
        }

        /// <summary> 父轨事件（瞬时） </summary>
        public struct Parent
        {
            public int LaneIndex;
            public int StartTime;
            public int? ParentLane;            // 约定从 0 开始的非负整数（不强制校验）；null = 无父
            public int Layer;                  // 仅供制谱器
        }
    }
}
