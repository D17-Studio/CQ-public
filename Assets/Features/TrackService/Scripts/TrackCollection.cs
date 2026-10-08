using System.Collections.Generic;

/// <summary>
/// 曲目集合，加载全部官谱和自制谱信息，提供统一查询入口
/// </summary>
public class TrackCollection
{
    private readonly List<TrackInfo> _tracks = new();

    public TrackCollection(List<OfficialTrackAsset> officialTracks)
    {
        foreach (var asset in officialTracks)
        {
            LoadChartContent(asset.TrackInfo);
            _tracks.Add(asset.TrackInfo);
        }
    

        // TODO: 扫描自制谱目录，解析文本文件，创建 TrackInfo 并加入 _tracks
    }

    /// <summary>
    /// 获取全部曲目列表
    /// </summary>
    public IReadOnlyList<TrackInfo> GetTracks() => _tracks;

    private void LoadChartContent(TrackInfo trackInfo)
    {
        foreach (var chartInfo in trackInfo.ChartInfos)
        {
            if (chartInfo.ChartText != null)
            {
                chartInfo.ChartContent = chartInfo.ChartText.text;
            }
        }
    }
}
