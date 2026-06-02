using CQMusicGame.Shared;
using UnityEngine;

/// <summary>
/// Dash 视觉：头部 + 多段折线 + 尾部，附有 Tune 位移点
/// </summary>
public class DashView : SustainNoteView
{
    protected override void CalculatePositions()
    {
        _calculator.Calculate(_noteData, TrackTimeMs, PositionBuffer);
    }

    protected override void UpdateVisual()
    {
        if (PositionBuffer.Count > 0)
            _rectTransform.anchoredPosition = ScreenAdapter.ToScreenPos(PositionBuffer[0]);
        // TODO：遍历 PositionBuffer，逐段摆放头部→Tune弧段→尾部
    }

    protected override void PlayHitEffect(JudgeResult result)
    {
        _vFXManager.PlayHit(_rectTransform.anchoredPosition, result);
    }

    protected override void PlayMissEffect(JudgeResult result)
    {
        // TODO
    }

    protected override void OnSustainEnd()
    {
        // TODO
    }
}
