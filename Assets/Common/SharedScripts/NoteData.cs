namespace CQMusicGame.Shared
{
    /// <summary>
    /// Note类型枚举
    /// </summary>
    public enum NoteType
    {
        Dot,
        Dash,
        Mute,
        Tune
    }

    /// <summary>
    /// Claude: Tune的方向枚举
    /// </summary>
    public enum TuneDirection
    {
        Left,
        Right
    }
    
    /// <summary>
    /// Note流速模式
    /// </summary>
    public enum NoteSpeedMode
    {
        Default,   // 使用玩家全局流速
        Multiplier,// 玩家流速 × 倍率
        Fixed      // 固定流速（无视玩家设置）
    }

    /// <summary>
    /// Claude: Tune音符数据，附属于Dash，描述Dash的移动
    /// </summary>
    public struct TuneData
    {
        /// <summary>
        /// 正解时间（毫秒）
        /// </summary>
        public int TargetTime;

        /// <summary>
        /// 方向（左/右），决定玩家需要按哪侧按键
        /// </summary>
        public TuneDirection Direction;

        /// <summary>
        /// 是否使用相对位置
        /// </summary>
        public bool UseRelative;

        /// <summary>
        /// 目标轨道序号（相对位置时使用，-3 ~ 3）
        /// </summary>
        public int TargetLane;

        /// <summary>
        /// 偏移量（相对位置时）/ 世界坐标（绝对位置时）
        /// </summary>
        public Vec2 PosValue;

        /// <summary>
        /// 过渡持续时间（毫秒）
        /// </summary>
        public int Duration;

        public TuneData(int targetTime, TuneDirection direction, bool useRelative, int targetLane, Vec2 posValue, int duration = 0)
        {
            TargetTime = targetTime;
            Direction = direction;
            UseRelative = useRelative;
            TargetLane = targetLane;
            PosValue = posValue;
            Duration = duration;
        }
    }

    /// <summary>
    /// Note数据结构体
    /// </summary>
    public struct NoteData
    {
        /// <summary>
        /// Note类型
        /// </summary>
        public NoteType Type;
    
        /// <summary>
        /// Note判定轨道序号
        /// </summary>
        public int LaneIndex;
    
        /// <summary>
        /// Note正解时间
        /// </summary>
        public int TargetTime;
    
        /// <summary>
        /// Note长按时长
        /// </summary>
        public int SustainDuration;
        
        /// <summary>
        /// 流速模式
        /// </summary>
        public NoteSpeedMode SpeedMode;
        
        /// <summary>
        /// 流速值（倍率或固定值，仅在 SpeedMode 不是 Default 时有效）
        /// </summary>
        public float SpeedValue;

        /// <summary>
        /// 附属Tune列表（仅Type为Dash时有效）
        /// </summary>
        public TuneData[] LinkedTunes;

        /// <summary>
        /// 视觉出现偏移（ms），默认 -3000 即正解时间前3秒出现
        /// </summary>
        public int AppearOffset;

        /// <summary>
        /// Note结构体的构造函数
        /// </summary>
        public NoteData(NoteType type , int laneIndex , int targetTime, int sustainDuration = 0 , NoteSpeedMode speedMode =  NoteSpeedMode.Default , float speedValue = 1f, TuneData[] linkedTunes = null, int appearOffset = -3000)
        {
            Type = type;
            LaneIndex = laneIndex;
            TargetTime = targetTime;
            SustainDuration = sustainDuration;
            SpeedMode = speedMode;
            SpeedValue = speedValue;
            LinkedTunes = linkedTunes;
            AppearOffset = appearOffset;
        }
    }
}


