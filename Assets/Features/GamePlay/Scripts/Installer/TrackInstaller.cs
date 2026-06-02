using CQMusicGame.Shared;
using UnityEngine;
using Zenject;

public class TrackInstaller : MonoInstaller
{
    [Inject] private NoteJudgeConfig _noteConfig;

    [SerializeField] private TrackController _trackController;
    [SerializeField] private VFXManager _vfxManager;
    [SerializeField] private NoteViewSpawner _noteViewSpawner;

    public override void InstallBindings()
    {
        var chart = new ChartSheet(TestChart.Get());
        Container.BindInstance(chart).AsSingle();

        var scoring = new ScoringSystem(chart);
        Container.BindInstance(scoring).AsSingle();

        var laneTransform = new LaneState(chart.GetLaneMotions());
        Container.BindInstance(laneTransform).AsSingle();

        var noteManager = new NoteJudge(_noteConfig, scoring, chart.GetNotes());
        Container.BindInstance(noteManager).AsSingle();

        var calculator = new NotePositionCalculator(1000, laneTransform);
        Container.BindInstance(calculator).AsSingle();

        Container.BindInstance(_trackController).AsSingle();
        Container.BindInstance(_vfxManager).AsSingle();
        Container.BindInstance(_noteViewSpawner).AsSingle();
    }
}