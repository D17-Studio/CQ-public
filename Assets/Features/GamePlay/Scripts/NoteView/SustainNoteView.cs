using CQMusicGame.Shared;

/// <summary>
/// 长按 Note 视觉（Dash / Mute），击中后保持显示直到长按结束或丢失
/// </summary>
public abstract class SustainNoteView : NoteView
{
    private bool _isHit;
    private bool _isMissed;

    /// <summary>头部是否已被击中</summary>
    protected bool IsHit => _isHit;

    /// <summary>是否已丢失</summary>
    protected bool IsMissed => _isMissed;

    void Update()
    {
        // 击中后长按时间结束 → 归还
        if (TrackTimeMs > _noteData.TargetTime + _noteData.SustainDuration)
        {
            OnSustainEnd();
            ReturnToPool();
            return;
        }

        CalculatePositions();
        UpdateVisual();
    }

    /// <summary>击中：标记状态，不归还（等待长按结束）</summary>
    public override void OnHit(JudgeResult result)
    {
        _isHit = true;
        PlayHitEffect(result);
    }

    /// <summary>丢失：标记状态，立即归还</summary>
    public override void OnMiss(JudgeResult result)
    {
        _isMissed = true;
        PlayMissEffect(result);
    }

    /// <summary>长按正常结束时的钩子</summary>
    protected virtual void OnSustainEnd() { }
}
