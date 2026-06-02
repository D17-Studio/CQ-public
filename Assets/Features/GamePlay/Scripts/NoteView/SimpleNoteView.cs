using CQMusicGame.Shared;

/// <summary>
/// 简单 Note 视觉（Dot / Tune），击中或丢失后立即归还池
/// </summary>
public abstract class SimpleNoteView : NoteView
{
    public override void OnHit(JudgeResult result)
    {
        PlayHitEffect(result);
        ReturnToPool();
    }

    public override void OnMiss(JudgeResult result)
    {
        PlayMissEffect(result);
        ReturnToPool();
    }
}
