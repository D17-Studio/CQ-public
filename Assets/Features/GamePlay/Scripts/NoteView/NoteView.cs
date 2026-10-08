using System;
using System.Collections.Generic;
using CQMusicGame.Shared;
using UnityEngine;

/// <summary>
/// Note 视觉基类，管理 MeshRenderer、对象池归还、判定回调
/// </summary>
public abstract class NoteView : MonoBehaviour, IPoolable
{
    [Header("Mesh Rendering")]
    [SerializeField] protected Material _fillMaterial;
    [SerializeField] protected Material _strokeMaterial;
    [SerializeField] protected float _circleRadius = 20f;
    [SerializeField] protected int _circleSegments = 32;
    [SerializeField] protected float _bodyWidth = 20f;
    [SerializeField] protected float _strokeWidth = 3f;

    protected NoteData _noteData;
    protected NotePositionCalculator _calculator;
    protected VFXManager _vFXManager;
    protected ScreenAdapter _screenAdapter;

    protected MeshFilter MeshFilter;
    protected MeshRenderer MeshRenderer;

    protected readonly List<Vec2> PositionBuffer = new();

    private TrackController _trackController;
    private Action _onReturn;

    void Awake()
    {
        transform.position = new Vector3(-10000, -10000, 0);
    }

    void IPoolable.SetReturnCallback(Action onReturn)
    {
        _onReturn = onReturn;
    }

    /// <summary>
    /// Spawner 调用，注入依赖并触发 OnSpawn 钩子
    /// </summary>
    public void Initialize(NoteData data, NotePositionCalculator calculator, TrackController trackController, VFXManager vfxManager, ScreenAdapter screenAdapter)
    {
        _noteData = data;
        _calculator = calculator;
        _trackController = trackController;
        _vFXManager = vfxManager;
        _screenAdapter = screenAdapter;
        OnSpawn();
    }

    /// <summary>当前乐曲时间（ms）</summary>
    protected int TrackTimeMs => _trackController.TrackTimeMs;

    /// <summary>从池取出后调用，子类覆写做额外初始化（mesh 渲染器 + mesh 构建）</summary>
    protected virtual void OnSpawn()
    {
        SetupMeshRenderer();
        BuildMesh();
    }

    /// <summary>计算位置点列表，填入 PositionBuffer</summary>
    protected abstract void CalculatePositions();

    /// <summary>根据 PositionBuffer 更新 Transform 和 Mesh</summary>
    protected abstract void UpdateVisual();

    /// <summary>构建 Mesh，子类实现具体形状</summary>
    protected abstract void BuildMesh();

    /// <summary>播放击中特效，默认空实现</summary>
    protected virtual void PlayHitEffect(JudgeResult result) { }

    /// <summary>播放丢失特效，默认空实现</summary>
    protected virtual void PlayMissEffect(JudgeResult result) { }

    /// <summary>Note 判定回调（击中）</summary>
    public abstract void OnHit(JudgeResult result);

    /// <summary>Note 判定回调（丢失）</summary>
    public abstract void OnMiss(JudgeResult result);

    /// <summary>归还到对象池</summary>
    protected void ReturnToPool()
    {
        transform.position = new Vector3(-10000, -10000, 0);
        _onReturn?.Invoke();
    }

    /// <summary>确保 MeshFilter 和 MeshRenderer 存在，设置 sharedMaterials</summary>
    protected void SetupMeshRenderer()
    {
        MeshFilter = GetComponent<MeshFilter>();
        if (MeshFilter == null) MeshFilter = gameObject.AddComponent<MeshFilter>();

        MeshRenderer = GetComponent<MeshRenderer>();
        if (MeshRenderer == null) MeshRenderer = gameObject.AddComponent<MeshRenderer>();

        MeshRenderer.sharedMaterials = new[] { _strokeMaterial, _fillMaterial };
    }
}
