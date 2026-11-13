/// <summary>成绩查询结果（只读快照）。Exists = 是否打过（区分"没打过"与"打了 0 分"）。</summary>
public readonly struct ScoreView
{
    public bool Exists { get; }
    public int BestScore { get; }
    public bool IsFullCombo { get; }

    public ScoreView(bool exists, int bestScore, bool isFullCombo)
    {
        Exists = exists;
        BestScore = bestScore;
        IsFullCombo = isFullCombo;
    }
}
