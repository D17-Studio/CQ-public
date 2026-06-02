using CQMusicGame.Shared;

/// <summary>
/// Tune音符类，附属于Dash，单点判定，按键为Dash所在轨道的侧边轨道
/// </summary>
public class TuneNote : Note
{
    private DashNote _linkedDash;
    private TuneDirection _direction;

    public TuneNote(NoteData noteData, NoteJudgeConfig config, ScoringSystem scoring) : base(noteData, config,  scoring)
    {
    }

    /// <summary>
    /// 设置关联的Dash音符和方向
    /// </summary>
    public void SetLinkedDash(DashNote dash, TuneDirection direction)
    {
        _linkedDash = dash;
        _direction = direction;
    }

    /// <summary>
    /// Tune判定窗口：[-BadOffset, +GoodOffset]，需要按下侧边轨道的按键
    /// </summary>
    public override bool CanBeHit(LaneInput input, int time)
    {
        if (_linkedDash == null) return false;
        if (_judgeRecord.Result == JudgeResult.Miss) return false;
        if (input.InputType != LaneInputType.Press)
            return false;

        // 检查按键是否在正确的侧边
        int dashLane = _linkedDash.GetLaneIndex();
        int inputLane = input.LaneIndex;
        if (_direction == TuneDirection.Left)
        {
            if (inputLane >= dashLane) return false;  // 必须按左侧按键
        }
        else
        {
            if (inputLane <= dashLane) return false;  // 必须按右侧按键
        }

        int offset = time - _noteData.TargetTime;
        return offset >= -_config.BadOffset && offset <= _config.GoodOffset;
    }

    /// <summary>
    /// Tune击中后直接计算并提交
    /// </summary>
    public override void OnHit(int time)
    {
        SetResult(CalcJudgeResult(time));
        SubmitResult();
        NotifyHit();
    }

    /// <summary>
    /// 获取关联的Dash
    /// </summary>
    public DashNote GetLinkedDash() => _linkedDash;

    /// <summary>
    /// 获取方向
    /// </summary>
    public TuneDirection GetDirection() => _direction;
}