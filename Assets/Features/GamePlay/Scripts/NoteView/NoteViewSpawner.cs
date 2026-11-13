using System.Collections.Generic;
using CQMusicGame.Shared;
using UnityEngine;
using VFXTest.Vfx;
using Zenject;

/// <summary>
/// NoteView 生成器，管理三个对象池，按 SpawnTime 在正确时机生成视觉对象
/// </summary>
public class NoteViewSpawner : MonoBehaviour
{
    [SerializeField] private DotView _dotPrefab;
    [SerializeField] private DashView _dashPrefab;
    [SerializeField] private MuteView _mutePrefab;

    [Inject] private TrackController _trackController;
    [Inject] private NoteJudge _noteJudge;
    [Inject] private NotePositionCalculator _calculator;
    [Inject] private ScreenAdapter _screenAdapter;
    [Inject] private NoteVisualConfig _visualConfig;
    [Inject] private VfxService _vfxService;

    private ObjectPool<DotView> _dotPool;
    private ObjectPool<DashView> _dashPool;
    private ObjectPool<MuteView> _mutePool;

    // 尚未生成的 Note，按 SpawnTime 升序；Dequeue 后释放对 Note 的引用
    private Queue<NoteSpawnEntry> _spawnQueue;

    void Awake()
    {
        _dotPool = new ObjectPool<DotView>(_dotPrefab);
        _dashPool = new ObjectPool<DashView>(_dashPrefab);
        _mutePool = new ObjectPool<MuteView>(_mutePrefab);

        var entries = _noteJudge.TakeSpawnEntries();
        entries.Sort((a, b) => a.SpawnTime.CompareTo(b.SpawnTime));
        _spawnQueue = new Queue<NoteSpawnEntry>(entries);
    }

    void Update()
    {
        int time = _trackController.TrackTimeMs;

        while (_spawnQueue.Count > 0 && time >= _spawnQueue.Peek().SpawnTime)
        {
            Spawn(_spawnQueue.Dequeue());
        }
    }

    /// <summary>根据类型分发到对应池</summary>
    private void Spawn(NoteSpawnEntry entry)
    {
        switch (entry.Data.Type)
        {
            case NoteType.Dot:
                SpawnView(_dotPool.Get(), entry);
                break;
            case NoteType.Dash:
                SpawnView(_dashPool.Get(), entry);
                break;
            case NoteType.Mute:
                SpawnView(_mutePool.Get(), entry);
                break;
        }
    }

    private void SpawnView(NoteView view, NoteSpawnEntry entry)
    {
        view.Initialize(entry.Data, _calculator, _trackController, _screenAdapter, _visualConfig, _vfxService);
        entry.Note.SetOnHit(view.OnHit);
        entry.Note.SetOnMiss(view.OnMiss);
        view.gameObject.SetActive(true);
    }
}
