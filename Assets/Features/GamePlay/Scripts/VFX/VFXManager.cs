using System;
using CQMusicGame.Shared;
using UnityEngine;

/// <summary>
/// 特效管理器，管理 HitEffect 等特效对象池
/// </summary>
public class VFXManager : MonoBehaviour
{
    [SerializeField] private HitEffect _hitEffectPrefab;
    [SerializeField] private BadEffect _badEffectPrefab;
    [SerializeField] private RectTransform _parent;

    private ObjectPool<HitEffect> _hitPool;
    private ObjectPool<BadEffect> _badPool;

    void Awake()
    {
        _hitPool = new ObjectPool<HitEffect>(_hitEffectPrefab);
        _badPool = new ObjectPool<BadEffect>(_badEffectPrefab);
    }

    /// <summary>播放击中特效</summary>
    public void PlayHit(Vector2 screenPos, JudgeResult result)
    {
        var effect = _hitPool.Get();
        effect.transform.SetParent(_parent);
        effect.Play(screenPos, result);
    }

    /// <summary>播放 Bad 特效</summary>
    public void PlayBad(Vector2 screenPos)
    {
        var effect = _badPool.Get();
        effect.transform.SetParent(_parent);
        effect.Play(screenPos);
    }

    [ContextMenu("Test HitEffect at Center")]
    private void TestHitEffect()
    {
        PlayHit(Vector2.zero, JudgeResult.Perfect);
    }

    [ContextMenu("Test BadEffect at Center")]
    private void TestBadEffect()
    {
        PlayBad(Vector2.zero);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.H))
        {
            TestHitEffect();
        }
    }
}
