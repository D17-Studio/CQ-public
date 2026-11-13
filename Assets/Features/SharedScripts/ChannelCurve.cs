namespace CQMusicGame.Shared
{
    public enum ValueMode
    {
        Absolute,
        Relative
    }

    /// <summary> 单个通道的曲线数据 </summary>
    public struct ChannelCurve
    {
        public float From;
        public ValueMode FromMode;
        public float To;
        public ValueMode ToMode;   // Relative 时为相对起点的增量
        public EaseCurve Ease;
    }
}
