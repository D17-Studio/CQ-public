using CQMusicGame.Shared;
using UnityEngine;

/// <summary>
/// 长按 Note 视觉（Dash / Mute），击中后保持显示直到长按结束或丢失
/// </summary>
public abstract class SustainNoteView : NoteView
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private MaterialPropertyBlock _tintBlock;

    private bool _isHit;
    private bool _isMissed;

    /// <summary>头部是否已被击中</summary>
    protected bool IsHit => _isHit;

    /// <summary>是否已丢失</summary>
    protected bool IsMissed => _isMissed;

    void Update()
    {
        // 判定已产生且长按时间结束 → 归还（避免回池后迟到的判定回调落到复用对象上）
        bool hasResult = _isHit || _isMissed;
        if (hasResult && TrackTimeMs > _noteData.TargetTime + _noteData.SustainDuration)
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

    /// <summary>丢失：标记状态，子类覆写做变灰</summary>
    public override void OnMiss(JudgeResult result)
    {
        _isMissed = true;
    }

    /// <summary>覆盖指定 submesh 的颜色</summary>
    protected void SetMissTint(MeshRenderer renderer, int materialIndex)
    {
        _tintBlock ??= new MaterialPropertyBlock();
        _tintBlock.SetColor(BaseColorId, VisualConfig.Feedback.MissColor);
        renderer.SetPropertyBlock(_tintBlock, materialIndex);
    }

    /// <summary>清除 tint，供对象池复用恢复</summary>
    protected static void ClearTint(MeshRenderer renderer, int materialIndex)
    {
        renderer.SetPropertyBlock(null, materialIndex);
    }

    /// <summary>复位判定状态，供对象池复用</summary>
    protected void ResetState()
    {
        _isHit = false;
        _isMissed = false;
    }

    /// <summary>长按正常结束时的钩子</summary>
    protected virtual void OnSustainEnd() { }
}
