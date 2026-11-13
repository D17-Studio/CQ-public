using System.Collections.Generic;
using CQMusicGame.Shared;
using UnityEngine;
using VFXTest.Vfx;

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

    private readonly List<Vector3> _localPoints = new();

    protected override void OnSpawn()
    {
        ResetState();
        SetupHeadRenderer();
        SetupBodyRenderer();
        ClearTint(_headRenderer, 1);
        ClearTint(_bodyRenderer, 1);
        BuildMesh();
    }

    public override void OnMiss(JudgeResult result)
    {
        base.OnMiss(result);
        SetMissTint(_headRenderer, 1);
        SetMissTint(_bodyRenderer, 1);
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
        var shape = VisualConfig.Dash;
        var headMesh = new Mesh();
        NoteMeshGenerator.BuildCircle(headMesh, shape.CircleRadius, shape.CircleSegments, shape.StrokeWidth);
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

        var shape = VisualConfig.Dash;
        var mesh = _bodyFilter.mesh;
        mesh.Clear();
        NoteMeshGenerator.BuildPolyline(mesh, _localPoints, shape.BodyWidth, shape.StrokeWidth);
    }

    protected override void PlayHitEffect(JudgeResult result)
    {
        // 用判定这一帧的时间重算位置，避免比 Update 晚一帧导致火花偏一点
        CalculatePositions();
        if (PositionBuffer.Count == 0) return;

        Vector2 worldPos = _screenAdapter.ToWorldPos(PositionBuffer[0]);

        float intensity = result switch
        {
            JudgeResult.Perfect => 1.2f,
            JudgeResult.Early or JudgeResult.Late => 0.9f,
            JudgeResult.Bad => 0.6f,      // Dash 才可能走到
            _ => 0.8f,
        };

        Color tint = result switch
        {
            JudgeResult.Perfect => new Color(1f, 0.85f, 0.2f),                  // 黄
            JudgeResult.Early or JudgeResult.Late => new Color(0.35f, 0.7f, 1f), // 蓝
            _ => Color.white,
        };

        _vfxService.Play(new VfxRequest("fx_note_impact")
            .At(worldPos)
            .WithParam(VfxParamValue.MakeColor("color", tint)));
    }

    protected override void OnSustainEnd()
    {
        // TODO
    }
}
