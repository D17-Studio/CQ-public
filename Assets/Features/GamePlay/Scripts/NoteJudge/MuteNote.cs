using CQMusicGame.Shared;

/// <summary>
/// Mute音符类，长按类型，不需要点击动作，按住自动判定
/// </summary>
public class MuteNote : Note
{
    public MuteNote(NoteData noteData, NoteJudgeConfig config, ScoringSystem scoring, JudgeZoneCatalog zoneCatalog, LaneState laneState)
        : base(noteData, config, scoring, zoneCatalog, laneState)
    {
    }

    /// <summary>
    /// Mute判定窗口：[0, +GoodOffset]
    /// </summary>
    public override bool CanBeHit(ZoneInput input, int time)
    {
        if (!_zoneCatalog.Matches(_zone, input.Zone))
            return false;
        if (input.Type != ZoneInputType.Press)
            return false;

        int offset = time - _noteData.TargetTime;
        return offset >= 0 && offset <= _config.GoodOffset;
    }

    /// <summary>
    /// Mute头部判定固定为Perfect（不提交），进入长按状态
    /// </summary>
    public override void OnHit(int time)
    {
        SetResult(JudgeResult.Perfect);
        NotifyHit();
    }
}