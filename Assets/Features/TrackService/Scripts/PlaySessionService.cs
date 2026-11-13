/// <summary>
/// 跨场景本局会话服务（选曲 + 打歌模式），挂 ProjectContext。菜单设，局内读
/// </summary>
public class PlaySessionService
{
    public TrackData SelectedTrack { get; set; }
    public ChartData SelectedChart { get; set; }

    public bool AutoPlay { get; set; }
}
