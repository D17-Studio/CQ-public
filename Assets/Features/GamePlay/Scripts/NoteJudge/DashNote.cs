using System.Collections.Generic;
using CQMusicGame.Shared;

/// <summary>
/// Dash音符类，长按类型，头部判定后进入长按池
/// </summary>
public class DashNote : Note
{
    private readonly List<TuneNote> _linkedTunes = new();

    public DashNote(NoteData noteData, NoteJudgeConfig config, ScoringSystem scoring) : base(noteData, config,  scoring)
    {
    }

    /// <summary>
    /// 添加关联的Tune
    /// </summary>
    public void AddLinkedTune(TuneNote tune)
    {
        _linkedTunes.Add(tune);
    }

    /// <summary>
    /// Dash判定窗口：[-GoodOffset, +GoodOffset]
    /// </summary>
    public override bool CanBeHit(LaneInput input, int time)
    {
        if (input.LaneIndex != _noteData.LaneIndex)
            return false;
        if (input.InputType != LaneInputType.Press)
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

    /// <summary>
    /// 获取关联的Tune列表（供NoteManager处理连锁）
    /// </summary>
    public IReadOnlyList<TuneNote> GetLinkedTunes() => _linkedTunes;
}