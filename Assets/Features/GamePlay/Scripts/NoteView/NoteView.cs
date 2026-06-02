using System;
using System.Collections.Generic;
using CQMusicGame.Shared;
using UnityEngine;

/// <summary>
/// Note 视觉基类，管理 RectTransform 位置、对象池归还、判定回调
/// </summary>
public abstract class NoteView : MonoBehaviour, IPoolable
{
    protected NoteData _noteData;
    protected NotePositionCalculator _calculator;
    protected RectTransform _rectTransform;
    protected VFXManager _vFXManager;
    
    protected readonly List<Vec2> PositionBuffer = new();

    private TrackController _trackController;
    private Action _onReturn;

    void IPoolable.SetReturnCallback(Action onReturn)
    {
        _onReturn = onReturn;
    }

    /// <summary>
    /// Spawner 调用，注入依赖并触发 OnSpawn 钩子
    /// </summary>
    public void Initialize(NoteData data, NotePositionCalculator calculator, TrackController trackController, VFXManager vfxManager)
    {
        _noteData = data;
        _calculator = calculator;
        _trackController = trackController;
        _vFXManager = vfxManager;
        _rectTransform = GetComponent<RectTransform>();
        OnSpawn();
    }

    /// <summary>当前乐曲时间（ms）</summary>
    protected int TrackTimeMs => _trackController.TrackTimeMs;

    /// <summary>从池取出后调用，子类可覆写做额外初始化</summary>
    protected virtual void OnSpawn() { }

    /// <summary>计算位置点列表，填入 PositionBuffer</summary>
    protected abstract void CalculatePositions();

    /// <summary>根据 PositionBuffer 更新子物体 Transform</summary>
    protected abstract void UpdateVisual();

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
        _rectTransform.anchoredPosition = new Vector2(-10000, -10000);
        _onReturn?.Invoke();
    }
}
