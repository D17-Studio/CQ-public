using System.Collections.Generic;
using CQMusicGame.Shared;
using UnityEngine;
using VFXTest.Vfx;

/// <summary>
/// Mute 视觉：黑色矩形 + 白色描边（无头），长按自动击中
/// </summary>
public class MuteView : SustainNoteView
{
    private readonly List<Vector3> _localPoints = new();

    protected override void OnSpawn()
    {
        base.OnSpawn();
        ResetState();
        ClearTint(MeshRenderer, 0);
        MeshRenderer.sortingOrder = _noteData.TargetTime;
    }

    public override void OnMiss(JudgeResult result)
    {
        base.OnMiss(result);
        SetMissTint(MeshRenderer, 0);
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

        var shape = VisualConfig.Mute;
        var mesh = MeshFilter.mesh;
        mesh.Clear();
        NoteMeshGenerator.BuildPolyline(mesh, _localPoints, shape.BodyWidth, shape.StrokeWidth);

        transform.position = wHead;
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
