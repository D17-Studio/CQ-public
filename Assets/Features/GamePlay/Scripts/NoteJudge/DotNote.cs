using CQMusicGame.Shared;

/// <summary>
/// Claude: Dot音符类，常规点击判定，完成后直接结算
/// </summary>
public class DotNote : Note
{
    public DotNote(NoteData noteData, NoteJudgeConfig config, ScoringSystem scoring, JudgeZoneCatalog zoneCatalog, LaneState laneState)
        : base(noteData, config, scoring, zoneCatalog, laneState)
    {
    }

    /// <summary>
    /// Dot判定窗口：[-BadOffset, +GoodOffset]
    /// </summary>
    public override bool CanBeHit(ZoneInput input, int time)
    {
        if (!_zoneCatalog.Matches(_zone, input.Zone))
            return false;
        if (input.Type != ZoneInputType.Press)
            return false;

        int offset = time - _noteData.TargetTime;
        return offset >= -_config.BadOffset && offset <= _config.GoodOffset;
    }

    /// <summary>
    /// Dot击中后直接计算并提交
    /// </summary>
    public override void OnHit(int time)
    {
        SetResult(CalcJudgeResult(time));
        SubmitResult();
        NotifyHit();
    }
}