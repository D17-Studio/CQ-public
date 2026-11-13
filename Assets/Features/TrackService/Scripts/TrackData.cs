using UnityEngine;

/// <summary>
/// 运行时曲目标准模型：官谱与自制谱转换后的唯一消费类型。
/// </summary>
public class TrackData
{
    public string MusicTitle;
    public string MusicArtist;
    public string MusicAlbum;
    public string MusicCoverArtist;
    public Sprite MusicCover;
    public int BPM;

    /// <summary>存档签名：拼进分数存档键，可为空。</summary>
    public string ArchiveSignature;

    /// <summary>预览区间（谱面时间轴，毫秒）；0/0 由播放侧使用默认长度。</summary>
    public int PreviewStartMs;
    public int PreviewEndMs;

    public TrackAudio Audio;
    public ChartData[] Charts;
}

/// <summary>
/// 运行时单难度谱面标准模型。
/// </summary>
public class ChartData
{
    public ChartDif Difficulty;
    public string ChartArtist;
    public int ChartConstant;
    public string ChartContent;
}
