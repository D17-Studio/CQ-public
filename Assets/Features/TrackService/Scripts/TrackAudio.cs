/// <summary>
/// 运行时音频来源基类：官谱走 Wwise 事件，自制谱走外部文件。
/// </summary>
public abstract class TrackAudio
{
}

/// <summary>
/// 官谱音频：由 Wwise 事件播放。
/// </summary>
public sealed class WwiseTrackAudio : TrackAudio
{
    public WwiseSongPlayConfig Config;
}

/// <summary>
/// 自制谱音频：由 Wwise External Source 播放本地文件。
/// </summary>
public sealed class ExternalTrackAudio : TrackAudio
{
    /// <summary>音频文件绝对路径。</summary>
    public string FilePath;

    /// <summary>时间轴起始偏移（毫秒，正 = 头部补静音，负 = 裁头）。</summary>
    public int StartOffsetMs;

    /// <summary>音频文件原始时长（毫秒，不含偏移）。</summary>
    public int DurationMs;
}
