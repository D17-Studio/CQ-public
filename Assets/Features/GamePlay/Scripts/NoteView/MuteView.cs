using System.Collections.Generic;
using CQMusicGame.Shared;
using UnityEngine;

/// <summary>
/// Mute 视觉：黑色矩形 + 白色描边（无头），长按自动击中
/// </summary>
public class MuteView : SustainNoteView
{
    [SerializeField] private float _miterLimit = 3f;

    private readonly List<Vector3> _localPoints = new();

    protected override void OnSpawn()
    {
        base.OnSpawn();
        MeshRenderer.sortingOrder = _noteData.TargetTime;
    }

    protected override void CalculatePositions()
    {
        _calculator.Calculate(_noteData, TrackTimeMs, PositionBuffer);
    }

    protected override void BuildMesh()
    {
        CalculatePositions();
        RegenerateMesh();
    }

    protected override void UpdateVisual()
    {
        RegenerateMesh();
    }

    private void RegenerateMesh()
    {
        if (PositionBuffer.Count < 2) return;

        Vector2 wHead = _screenAdapter.ToWorldPos(PositionBuffer[0]);
        _localPoints.Clear();
        for (int i = 0; i < PositionBuffer.Count; i++)
        {
            Vector2 w = _screenAdapter.ToWorldPos(PositionBuffer[i]);
            _localPoints.Add(new Vector3(w.x - wHead.x, w.y - wHead.y, 0));
        }

        var mesh = MeshFilter.mesh;
        mesh.Clear();
        NoteMeshGenerator.BuildPolyline(mesh, _localPoints, _bodyWidth, _strokeWidth, _miterLimit);

        transform.position = wHead;
    }

    protected override void PlayHitEffect(JudgeResult result)
    {
        _vFXManager.PlayHit(_screenAdapter.ToScreenPos(PositionBuffer[0]), result);
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
