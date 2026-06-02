using CQMusicGame.Shared;
using UnityEngine;

/// <summary>
/// Mute 视觉：头部 + 长条身，无 Tune 位移
/// </summary>
public class MuteView : SustainNoteView
{
    protected override void CalculatePositions()
    {
        _calculator.Calculate(_noteData, TrackTimeMs, PositionBuffer);
    }

    protected override void UpdateVisual()
    {
        if (PositionBuffer.Count > 0)
            _rectTransform.anchoredPosition = ScreenAdapter.ToScreenPos(PositionBuffer[0]);
        // TODO：根据 PositionBuffer[0]（头）和 PositionBuffer[1]（尾）摆放
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
