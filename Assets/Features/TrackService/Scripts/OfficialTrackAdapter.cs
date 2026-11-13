using System.Collections.Generic;

/// <summary>
/// 官谱 SO 数据 → 运行时标准模型的转换器。
/// </summary>
public static class OfficialTrackAdapter
{
    /// <summary>把一条官谱 SO 信息转换为运行时 TrackData。</summary>
    public static TrackData Convert(OfficialTrackInfo source)
    {
        var charts = new List<ChartData>();
        if (source.ChartInfos != null)
        {
            foreach (var chart in source.ChartInfos)
            {
                if (chart == null) continue;

                charts.Add(new ChartData
                {
                    Difficulty = chart.ChartDifficulty,
                    ChartArtist = chart.ChartArtist,
                    ChartConstant = chart.ChartConstant,
                    ChartContent = chart.ChartText != null ? chart.ChartText.text : string.Empty,
                });
            }
        }

        return new TrackData
        {
            MusicTitle = source.MusicTitle,
            MusicArtist = source.MusicArtist,
            MusicAlbum = source.MusicAlbum,
            MusicCoverArtist = source.MusicCoverArtist,
            MusicCover = source.MusicCover,
            BPM = source.BPM,
            ArchiveSignature = source.ArchiveSignature,
            PreviewStartMs = source.PreviewStart,
            PreviewEndMs = source.PreviewEnd,
            Audio = new WwiseTrackAudio { Config = source.WwiseConfig },
            Charts = charts.ToArray(),
        };
    }
}
