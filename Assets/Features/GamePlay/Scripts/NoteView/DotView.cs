using CQMusicGame.Shared;
using UnityEngine;

/// <summary>
/// Dot 视觉：单点下落，击中/丢失后归还
/// </summary>
public class DotView : SimpleNoteView
{
    private void Update()
    {
        CalculatePositions();
        UpdateVisual();
    }

    protected override void CalculatePositions()
    {
        _calculator.Calculate(_noteData, TrackTimeMs, PositionBuffer);
    }

    protected override void UpdateVisual()
    {
        if (PositionBuffer.Count > 0)
            _rectTransform.anchoredPosition = ScreenAdapter.ToScreenPos(PositionBuffer[0]);
    }

    protected override void PlayHitEffect(JudgeResult result)
    {
        switch (result)
        {
            case JudgeResult.Bad:
                _vFXManager.PlayBad(_rectTransform.anchoredPosition);
                break;
            default:
                _vFXManager.PlayHit(_rectTransform.anchoredPosition, result);
                break;
        }
    }

    protected override void PlayMissEffect(JudgeResult result)
    {
        // TODO
    }
}
