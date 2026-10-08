using System.Collections.Generic;
using CQMusicGame.Shared;
using UnityEngine;

/// <summary>
/// Dash 视觉：头圆（Note 层）+ 身子折线（DashBody 层），附有 Tune 转折点
/// </summary>
public class DashView : SustainNoteView
{
    [Header("Head")]
    [SerializeField] private MeshFilter _headFilter;
    [SerializeField] private MeshRenderer _headRenderer;
    [SerializeField] private Material _headFillMaterial;
    [SerializeField] private Material _headStrokeMaterial;

    [Header("Body")]
    [SerializeField] private MeshFilter _bodyFilter;
    [SerializeField] private MeshRenderer _bodyRenderer;
    [SerializeField] private Material _bodyFillMaterial;
    [SerializeField] private Material _bodyStrokeMaterial;
    [SerializeField] private float _miterLimit = 3f;

    private readonly List<Vector3> _localPoints = new();

    protected override void OnSpawn()
    {
        SetupHeadRenderer();
        SetupBodyRenderer();
        BuildMesh();
    }

    private void SetupHeadRenderer()
    {
        _headRenderer.sharedMaterials = new[] { _headStrokeMaterial, _headFillMaterial };
        _headRenderer.sortingOrder = -_noteData.TargetTime;
    }

    private void SetupBodyRenderer()
    {
        _bodyRenderer.sharedMaterials = new[] { _bodyStrokeMaterial, _bodyFillMaterial };
        _bodyRenderer.sortingOrder = _noteData.TargetTime;
    }

    protected override void CalculatePositions()
    {
        _calculator.Calculate(_noteData, TrackTimeMs, PositionBuffer);
    }

    protected override void BuildMesh()
    {
        var headMesh = new Mesh();
        NoteMeshGenerator.BuildCircle(headMesh, _circleRadius, _circleSegments, _strokeWidth);
        _headFilter.sharedMesh = headMesh;

        CalculatePositions();
        RegenerateBodyMesh();
    }

    protected override void UpdateVisual()
    {
        if (PositionBuffer.Count < 2) return;

        RegenerateBodyMesh();
        transform.position = _screenAdapter.ToWorldPos(PositionBuffer[0]);
    }

    private void RegenerateBodyMesh()
    {
        Vector2 wHead = _screenAdapter.ToWorldPos(PositionBuffer[0]);
        _localPoints.Clear();
        for (int i = 0; i < PositionBuffer.Count; i++)
        {
            Vector2 w = _screenAdapter.ToWorldPos(PositionBuffer[i]);
            _localPoints.Add(new Vector3(w.x - wHead.x, w.y - wHead.y, 0));
        }

        var mesh = _bodyFilter.mesh;
        mesh.Clear();
        NoteMeshGenerator.BuildPolyline(mesh, _localPoints, _bodyWidth, _strokeWidth, _miterLimit);
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
