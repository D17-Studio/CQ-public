using CQMusicGame.Shared;
using UnityEngine;
using VFXTest.Vfx;

/// <summary>
/// Dot 视觉：白色圆形 + 黑色描边，单点下落；Bad 时原地按 Bad 色渐隐，其余击中/丢失后归还
/// </summary>
public class DotView : SimpleNoteView
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private MaterialPropertyBlock _fadeBlock;
    private Color _strokeColor;
    private bool _isMissed;
    private int _missStartTime;
    private bool _isBad;
    private int _badStartTime;

    protected override void OnSpawn()
    {
        base.OnSpawn();
        MeshRenderer.sortingOrder = -_noteData.TargetTime;
        MeshRenderer.SetPropertyBlock(null, 0);
        MeshRenderer.SetPropertyBlock(null, 1);
        _strokeColor = _strokeMaterial.GetColor(BaseColorId);
        _isMissed = false;
        _isBad = false;
    }

    private void Update()
    {
        if (_isBad)
        {
            UpdateFade(_badStartTime, VisualConfig.Feedback.BadFadeMs, VisualConfig.Feedback.BadColor);
            return;
        }

        CalculatePositions();
        UpdateVisual();

        if (_isMissed)
            UpdateFade(_missStartTime, VisualConfig.Feedback.MissFadeMs, VisualConfig.Feedback.MissColor);
    }

    private void UpdateFade(int startTime, float durationMs, Color fillColor)
    {
        float progress = (TrackTimeMs - startTime) / durationMs;
        if (progress >= 1f)
        {
            ReturnToPool();
            return;
        }

        SetAlpha(1f - progress, fillColor);
    }

    public override void OnHit(JudgeResult result)
    {
        if (result == JudgeResult.Bad)
        {
            _isBad = true;
            _badStartTime = TrackTimeMs;
            SetAlpha(1f, VisualConfig.Feedback.BadColor);
            return;
        }

        PlayHitEffect(result);
        ReturnToPool();
    }

    public override void OnMiss(JudgeResult result)
    {
        _isMissed = true;
        _missStartTime = TrackTimeMs;
    }

    private void SetAlpha(float alpha, Color fillColor)
    {
        _fadeBlock ??= new MaterialPropertyBlock();

        var stroke = _strokeColor;
        stroke.a = alpha;
        _fadeBlock.SetColor(BaseColorId, stroke);
        MeshRenderer.SetPropertyBlock(_fadeBlock, 0);

        var fill = fillColor;
        fill.a = alpha;
        _fadeBlock.SetColor(BaseColorId, fill);
        MeshRenderer.SetPropertyBlock(_fadeBlock, 1);
    }

    protected override void CalculatePositions()
    {
        _calculator.Calculate(_noteData, TrackTimeMs, PositionBuffer);
    }

    protected override void BuildMesh()
    {
        var mesh = new Mesh();
        var shape = VisualConfig.Dot;
        NoteMeshGenerator.BuildCircle(mesh, shape.CircleRadius, shape.CircleSegments, shape.StrokeWidth);
        MeshFilter.sharedMesh = mesh;
    }

    protected override void UpdateVisual()
    {
        if (PositionBuffer.Count > 0)
            transform.position = _screenAdapter.ToWorldPos(PositionBuffer[0]);
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

    protected override void PlayMissEffect(JudgeResult result)
    {
        // TODO
    }
}
