namespace CQMusicGame.Shared
{
    /// <summary>
    /// 曲目元数据，制谱器导出曲目后由游戏端读取并加载
    /// </summary>
    public class TrackInfo
    {
        /// <summary> 曲目信息格式版本，由 TrackInfoSerializer 写入（当前为 1） </summary>
        public int? Version;

        /// <summary> 曲目名 </summary>
        public string MusicTitle;
        
        /// <summary> 曲绘文件路径 </summary>
        public string MusicCoverPath;

        /// <summary> 乐曲文件路径 </summary>
        public string MusicPath;

        /// <summary> 乐曲起始偏移（毫秒） </summary>
        public int MusicStartOffset;

        /// <summary> 乐曲预览片段起点（毫秒），供游戏端使用 </summary>
        public int PreviewStart;

        /// <summary> 乐曲预览片段终点（毫秒），供游戏端使用 </summary>
        public int PreviewEnd;

        /// <summary> 预览 BPM（仅作信息填写，不参与制谱逻辑） </summary>
        public float PreviewBpm;

        /// <summary> 曲师 </summary>
        public string MusicArtist;

        /// <summary> 专辑 </summary>
        public string MusicAlbum;

        /// <summary> 曲绘画师 </summary>
        public string MusicCoverArtist;

        /// <summary> 各难度谱面，难度由 ChartInfo.DifficultyId 标识，可只包含已制作的难度 </summary>
        public ChartInfo[] Charts;
    }

    /// <summary>
    /// 单个难度的谱面信息
    /// </summary>
    public class ChartInfo
    {
        /// <summary> 难度编号（暂用整数标识，后续可替换为 Easy/Hard 等命名） </summary>
        public int DifficultyId;

        /// <summary> 定数（音游中代表实际难度评级的数值） </summary>
        public int ChartConstant;

        /// <summary> 谱面文件路径 </summary>
        public string ChartPath;

        /// <summary> 谱师 </summary>
        public string ChartArtist;
    }
}
