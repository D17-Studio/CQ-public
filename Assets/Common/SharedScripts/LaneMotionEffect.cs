namespace CQMusicGame.Shared
{
    /// <summary>
    /// 轨道动画效果
    /// </summary>
    public struct LaneMotionEffect
    {
        /// <summary>
        /// 开始时间（毫秒）
        /// </summary>
        public int StartTime;  
        
        /// <summary>
        /// 轨道序号
        /// </summary>
        public int LaneIndex;
        
        /// <summary>
        /// 持续时间（毫秒）
        /// </summary>
        public int Duration;
        
        /// <summary>
        /// 锚点坐标
        /// </summary>
        public Vec2 Anchor; 
        
        /// <summary>
        /// 位置动画（null 表示不执行该动画）
        /// </summary>
        public PositionMotion? Position;
        
        /// <summary>
        /// 旋转动画（null 表示不执行该动画）
        /// </summary>
        public RotationMotion? Rotation;
        
        /// <summary>
        /// 不透明度动画（null 表示不执行该动画）
        /// </summary>
        public OpacityMotion? Opacity;
    }
    
    /// <summary>
    /// 位置运动
    /// </summary>
    public struct PositionMotion
    {
        /// <summary>
        /// 是否使用相对位置
        /// </summary>
        public bool UseRelative;
        
        /// <summary>
        /// 变化后位置
        /// </summary>
        public Vec2 Pos;
        
        /// <summary>
        /// 动画曲线
        /// </summary>
        public EaseCurve CurveMode;
    }
    
    /// <summary>
    /// 旋转变化
    /// </summary>
    public struct RotationMotion
    {
        /// <summary>
        /// 是否使用相对旋转量
        /// </summary>
        public bool UseRelative;
        
        /// <summary>
        /// 角度（度）
        /// </summary>
        public float Angle;
        
        /// <summary>
        /// 动画曲线
        /// </summary>
        public EaseCurve CurveMode;
    }
    
    /// <summary>
    /// 不透明度变化
    /// </summary>
    public struct OpacityMotion
    {
        /// <summary>
        /// 是否影响轨道头
        /// </summary>
        public bool AffectHead;
        
        /// <summary>
        /// 是否影响轨道线
        /// </summary>
        public bool AffectLine;
        
        /// <summary>
        /// 是否影响轨道按键
        /// </summary>
        public bool AffectKey;

        /// <summary>
        /// 是否影响轨道上的 Note
        /// </summary>
        public bool AffectNote;

        /// <summary>
        /// 不透明度（0~1）
        /// </summary>
        public float Opacity;
        
        /// <summary>
        /// 动画曲线
        /// </summary>
        public EaseCurve CurveMode;
    }
}
