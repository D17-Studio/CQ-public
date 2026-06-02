using CQMusicGame.Shared;
using UnityEngine;

/// <summary>
/// Tune 视觉：附着在 Dash 弧段上的方向箭头，击中/丢失后归还
/// </summary>
public class TuneView : SimpleNoteView
{
    private TuneData _tuneData;
    private NoteData _parentDash;

    /// <summary>
    /// Tune 专用初始化，需要额外传入 TuneData 和父 Dash 的 NoteData
    /// </summary>
    public void Initialize(TuneData tuneData, NoteData parentDash, NotePositionCalculator calculator, TrackController trackController, VFXManager vfxManager)
    {
        _tuneData = tuneData;
        _parentDash = parentDash;
        Initialize(parentDash, calculator, trackController, vfxManager);
    }

    private void Update()
    {
        CalculatePositions();
        UpdateVisual();
    }

    protected override void CalculatePositions()
    {
        _calculator.Calculate(_tuneData, _parentDash, TrackTimeMs, PositionBuffer);
    }

    protected override void UpdateVisual()
    {
        if (PositionBuffer.Count > 0)
            _rectTransform.anchoredPosition = ScreenAdapter.ToScreenPos(PositionBuffer[0]);
    }

    protected override void PlayHitEffect(JudgeResult result)
    {
        _vFXManager.PlayHit(_rectTransform.anchoredPosition, result);
    }

    protected override void PlayMissEffect(JudgeResult result)
    {
        // TODO
    }
}
