using CQMusicGame.Shared;
using UnityEngine;

/// <summary>
/// Tune 视觉：白色圆形 + 黑色描边（与 Dot 相同），附着在 Dash 弧段上，附带方向指示箭头
/// </summary>
public class TuneView : SimpleNoteView
{
    private TuneData _tuneData;
    private NoteData _parentDash;

    [Header("Direction Arrows")]
    [SerializeField] private float _arrowStartDistance = 25f;
    [SerializeField] private float _arrowInterval = 15f;
    [SerializeField] private float _arrowLength = 20f;
    [SerializeField] private float _arrowWidth = 10f;
    [SerializeField] private float _arrowSlant = 5f;

    [SerializeField] private MeshFilter[] _arrowFilters;
    [SerializeField] private MeshRenderer[] _arrowRenderers;

    /// <summary>
    /// Tune 专用初始化，需要额外传入 TuneData 和父 Dash 的 NoteData
    /// </summary>
    public void Initialize(TuneData tuneData, NoteData parentDash, NotePositionCalculator calculator, TrackController trackController, VFXManager vfxManager, ScreenAdapter screenAdapter)
    {
        _tuneData = tuneData;
        _parentDash = parentDash;
        Initialize(parentDash, calculator, trackController, vfxManager, screenAdapter);
    }

    protected override void OnSpawn()
    {
        base.OnSpawn();
        MeshRenderer.sortingLayerName = "Note";
        MeshRenderer.sortingOrder = -_noteData.TargetTime;
        SetupArrowRenderers();
        BuildArrowMeshes();
    }

    private void SetupArrowRenderers()
    {
        for (int i = 0; i < _arrowFilters.Length; i++)
        {
            if (_arrowRenderers[i] != null)
            {
                _arrowRenderers[i].sharedMaterials = new[] { _strokeMaterial, _fillMaterial };
                _arrowRenderers[i].sortingLayerName = "Note";
                _arrowRenderers[i].sortingOrder = -_noteData.TargetTime;
            }
            if (_arrowFilters[i] != null)
                _arrowFilters[i].sharedMesh = null;
        }
    }

    private void BuildArrowMeshes()
    {
        CalculatePositions();

        if (PositionBuffer.Count < 2)
            return;

        Vec2 pos0 = PositionBuffer[0];
        Vec2 pos1 = PositionBuffer[1];
        float dx = pos1.x - pos0.x;
        float dy = pos1.y - pos0.y;
        float designPathLength = Mathf.Sqrt(dx * dx + dy * dy);

        float requiredLength = _arrowStartDistance + _arrowInterval * 2 + _arrowLength;
        if (designPathLength == 0)
            return;

        var arrowMesh = new Mesh();
        NoteMeshGenerator.BuildArrow(arrowMesh, _arrowLength, _arrowWidth, _arrowSlant, _strokeWidth);

        for (int i = 0; i < _arrowFilters.Length; i++)
        {
            if (_arrowFilters[i] != null)
                _arrowFilters[i].sharedMesh = arrowMesh;
        }
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

    protected override void BuildMesh()
    {
        var mesh = new Mesh();
        NoteMeshGenerator.BuildCircle(mesh, _circleRadius, _circleSegments, _strokeWidth);
        MeshFilter.sharedMesh = mesh;
    }

    protected override void UpdateVisual()
    {
        if (PositionBuffer.Count < 2)
        {
            if (PositionBuffer.Count > 0)
                transform.position = _screenAdapter.ToWorldPos(PositionBuffer[0]);
            return;
        }

        Vector2 worldPos0 = _screenAdapter.ToWorldPos(PositionBuffer[0]);
        Vector2 worldPos1 = _screenAdapter.ToWorldPos(PositionBuffer[1]);

        transform.position = worldPos0;
        UpdateArrowTransforms(worldPos0, worldPos1);
    }

    private void UpdateArrowTransforms(Vector2 worldPos0, Vector2 worldPos1)
    {
        Vector2 worldDir = worldPos1 - worldPos0;
        float worldPathLength = worldDir.magnitude;
        if (worldPathLength < 0.001f)
            return;

        worldDir /= worldPathLength;

        Vec2 d0 = PositionBuffer[0];
        Vec2 d1 = PositionBuffer[1];
        float ddx = d1.x - d0.x;
        float ddy = d1.y - d0.y;
        float designPathLength = Mathf.Sqrt(ddx * ddx + ddy * ddy);
        if (designPathLength < 0.001f)
            return;

        float angle = Mathf.Atan2(worldDir.y, worldDir.x) * Mathf.Rad2Deg;
        Quaternion rotation = Quaternion.Euler(0f, 0f, angle);

        for (int i = 0; i < _arrowFilters.Length; i++)
        {
            if (_arrowFilters[i] == null) continue;

            float t = (_arrowStartDistance + _arrowInterval * i) / designPathLength;

            Transform tform = _arrowFilters[i].transform;
            tform.localPosition = new Vector3(
                worldDir.x * t * worldPathLength,
                worldDir.y * t * worldPathLength,
                0f
            );
            tform.localRotation = rotation;
        }
    }

    protected override void PlayHitEffect(JudgeResult result)
    {
        _vFXManager.PlayHit(_screenAdapter.ToScreenPos(PositionBuffer[0]), result);
    }

    protected override void PlayMissEffect(JudgeResult result)
    {
        // TODO
    }
}
