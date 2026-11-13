using CQMusicGame.Shared;

/// <summary>
/// Dash音符类，长按类型，头部判定后进入长按池
/// </summary>
public class DashNote : Note
{
    public DashNote(NoteData noteData, NoteJudgeConfig config, ScoringSystem scoring, JudgeZoneCatalog zoneCatalog, LaneState laneState)
        : base(noteData, config, scoring, zoneCatalog, laneState)
    {
    }

    /// <summary>
    /// Dash判定窗口：[-GoodOffset, +GoodOffset]
    /// </summary>
    public override bool CanBeHit(ZoneInput input, int time)
    {
        if (!_zoneCatalog.Matches(_zone, input.Zone))
            return false;
        if (input.Type != ZoneInputType.Press)
            return false;

        int offset = time - _noteData.TargetTime;
        return offset >= -_config.GoodOffset && offset <= _config.GoodOffset;
    }

    /// <summary>
    /// Dash头部击中后保存结果（不提交），进入长按状态
    /// </summary>
    public override void OnHit(int time)
    {
        SetResult(CalcJudgeResult(time));
        NotifyHit();
    }
}