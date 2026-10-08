using CQMusicGame.Shared;
using UnityEngine;

/// <summary>
/// Dot 视觉：白色圆形 + 黑色描边，单点下落，击中/丢失后归还
/// </summary>
public class DotView : SimpleNoteView
{
    protected override void OnSpawn()
    {
        base.OnSpawn();
        MeshRenderer.sortingOrder = -_noteData.TargetTime;
    }

    private void Update()
    {
        CalculatePositions();
        UpdateVisual();
    }

    protected override void CalculatePositions()
    {
        _calculator.Calculate(_noteData, TrackTimeMs, PositionBuffer);
    }

    protected override void BuildMesh()
    {
        var mesh = new Mesh();
        NoteMeshGenerator.BuildCircle(mesh, _circleRadius, _circleSegments, _strokeWidth);
        MeshFilter.sharedMesh = mesh;
    }

    protected override void UpdateVisual()
    {
        if (PositionBuffer.Count > 0)
            transform.position = _screenAdapter.ToWorldPos(PositionBuffer[0]);
    }

    protected override void PlayHitEffect(JudgeResult result)
    {
        switch (result)
        {
            case JudgeResult.Bad:
                _vFXManager.PlayBad(_screenAdapter.ToScreenPos(PositionBuffer[0]));
                break;
            default:
                _vFXManager.PlayHit(_screenAdapter.ToScreenPos(PositionBuffer[0]), result);
                break;
        }
    }

    protected override void PlayMissEffect(JudgeResult result)
    {
        // TODO
    }
}
