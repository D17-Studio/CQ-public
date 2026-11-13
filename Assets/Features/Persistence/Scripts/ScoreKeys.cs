/// <summary>
/// 谱面索引拼接：乐曲名_难度代号_存档签名。签名为空时保留末尾下划线（不省略）。
/// 格式发布后视同稳定（改 = 旧档失联）。
/// </summary>
public static class ScoreKeys
{
    /// <summary>由曲目 + 谱面拼出存档索引。</summary>
    public static string Of(TrackData track, ChartData chart)
        => track.MusicTitle.Trim() + "_" + DifficultyCode(chart.Difficulty) + "_" + track.ArchiveSignature;

    // 难度存档代号：与枚举名 / 顺序解耦（枚举可改名重排）；发布后不可改，新增难度在此补一条。
    private static string DifficultyCode(ChartDif difficulty) => difficulty switch
    {
        ChartDif.EZ => "Dif0",
        ChartDif.ST => "Dif1",
        ChartDif.HD => "Dif2",
        ChartDif.EX => "Dif3",
        _ => ((int)difficulty).ToString(),
    };
}
