using System.Collections.Generic;
using CQMusicGame.Shared;
using UnityEngine;
using Zenject;

/// <summary>
/// NoteView 生成器，管理四个对象池，按 AppearOffset 在正确时机生成视觉对象
/// </summary>
public class NoteViewSpawner : MonoBehaviour
{
    [SerializeField] private DotView _dotPrefab;
    [SerializeField] private DashView _dashPrefab;
    [SerializeField] private MuteView _mutePrefab;
    [SerializeField] private TuneView _tunePrefab;
    [SerializeField] private RectTransform _parent;

    [Inject] private ChartSheet _chart;
    [Inject] private TrackController _trackController;
    [Inject] private NoteJudge _noteJudge;
    [Inject] private NotePositionCalculator _calculator;
    [Inject] private VFXManager _vfxManager;

    private ObjectPool<DotView> _dotPool;
    private ObjectPool<DashView> _dashPool;
    private ObjectPool<MuteView> _mutePool;
    private ObjectPool<TuneView> _tunePool;

    // 按实际出现时间（TargetTime + AppearOffset）排序的 Note 列表
    private List<NoteData> _sortedNotes;
    private int _spawnIndex;

    void Awake()
    {
        _dotPool = new ObjectPool<DotView>(_dotPrefab);
        _dashPool = new ObjectPool<DashView>(_dashPrefab);
        _mutePool = new ObjectPool<MuteView>(_mutePrefab);
        _tunePool = new ObjectPool<TuneView>(_tunePrefab);

        _sortedNotes = new List<NoteData>(_chart.GetNotes());
        _sortedNotes.Sort((a, b) => (a.TargetTime + a.AppearOffset).CompareTo(b.TargetTime + b.AppearOffset));
    }

    void Update()
    {
        int time = _trackController.TrackTimeMs;

        while (_spawnIndex < _sortedNotes.Count)
        {
            var data = _sortedNotes[_spawnIndex];
            int spawnTime = data.TargetTime + data.AppearOffset;

            if (time < spawnTime)
                break;

            Spawn(data);
            _spawnIndex++;
        }
    }

    /// <summary>根据类型分发到对应池，Dash 同时生成关联 Tune</summary>
    private void Spawn(NoteData data)
    {
        switch (data.Type)
        {
            case NoteType.Dot:
                SpawnDot(data);
                break;
            case NoteType.Dash:
                SpawnDash(data);
                break;
            case NoteType.Mute:
                SpawnMute(data);
                break;
        }
    }

    private void SpawnDot(NoteData data)
    {
        var view = _dotPool.Get();
        view.Initialize(data, _calculator, _trackController, _vfxManager);
        BindCallbacks(view, data);
        view.transform.SetParent(_parent);
        view.gameObject.SetActive(true);
    }

    private void SpawnDash(NoteData data)
    {
        var view = _dashPool.Get();
        view.Initialize(data, _calculator, _trackController, _vfxManager);
        BindCallbacks(view, data);
        view.transform.SetParent(_parent);
        view.gameObject.SetActive(true);

        // Dash 的 Tune 视觉同时生成
        if (data.LinkedTunes != null)
        {
            foreach (var tuneData in data.LinkedTunes)
                SpawnTune(tuneData, data);
        }
    }

    private void SpawnMute(NoteData data)
    {
        var view = _mutePool.Get();
        view.Initialize(data, _calculator, _trackController, _vfxManager);
        BindCallbacks(view, data);
        view.transform.SetParent(_parent);
        view.gameObject.SetActive(true);
    }

    /// <summary>生成 Tune 视觉，通过 NoteJudge 查询对应 Tune 并绑定回调</summary>
    private void SpawnTune(TuneData tuneData, NoteData parentDash)
    {
        var view = _tunePool.Get();
        view.Initialize(tuneData, parentDash, _calculator, _trackController, _vfxManager);
        view.transform.SetParent(_parent);

        var tune = _noteJudge.GetTune(parentDash, tuneData);
        if (tune != null)
        {
            tune.SetOnHit(view.OnHit);
            tune.SetOnMiss(view.OnMiss);
        }

        view.gameObject.SetActive(true);
    }

    /// <summary>将 Note 的 OnHit / OnMiss 回调绑定到 NoteView</summary>
    private void BindCallbacks(NoteView view, NoteData data)
    {
        var note = _noteJudge.GetNote(data);
        if (note == null) return;

        note.SetOnHit(view.OnHit);
        note.SetOnMiss(view.OnMiss);
    }
}
