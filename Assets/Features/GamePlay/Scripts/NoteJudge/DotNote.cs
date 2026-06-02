using CQMusicGame.Shared;

/// <summary>
/// Claude: Dot音符类，常规点击判定，完成后直接结算
/// </summary>
public class DotNote : Note
{
    public DotNote(NoteData noteData, NoteJudgeConfig config , ScoringSystem scoring) : base(noteData, config , scoring)
    {
    }

    /// <summary>
    /// Dot判定窗口：[-BadOffset, +GoodOffset]
    /// </summary>
    public override bool CanBeHit(LaneInput input, int time)
    {
        if (input.LaneIndex != _noteData.LaneIndex)
            return false;
        if (input.InputType != LaneInputType.Press)
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