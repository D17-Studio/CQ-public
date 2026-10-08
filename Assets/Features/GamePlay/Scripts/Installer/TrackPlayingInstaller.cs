using CQMusicGame.Shared;
using UnityEngine;
using Zenject;

public class TrackPlayingInstaller : MonoInstaller
{
    [Inject] private TrackSelectionService _trackSelectionService;
    [Inject] private NoteJudgeConfig _noteConfig;
    

    [SerializeField] private TrackController _trackController;
    [SerializeField] private VFXManager _vfxManager;
    [SerializeField] private NoteViewSpawner _noteViewSpawner;

    public override void InstallBindings()
    {
        //绑定谱面信息
        var chart = new ChartSheet(_trackSelectionService.SelectedChart.ChartContent);
        //var chart = new ChartSheet(TestChart.Get());//测试语句，须删除
        Container.BindInstance(chart).AsSingle();
        
        //绑定屏幕位置转换工具类
        Container.BindInstance(new ScreenAdapter()).AsSingle();

        //绑定计分系统
        var scoring = new ScoringSystem(chart);
        Container.BindInstance(scoring).AsSingle();

        //绑定轨道状态类
        var laneTransform = new LaneState(chart.GetLaneMotionList());
        Container.BindInstance(laneTransform).AsSingle();

        //绑定Note判定类
        var noteManager = new NoteJudge(_noteConfig, scoring, chart.GetNoteList());
        Container.BindInstance(noteManager).AsSingle();

        //绑定Note位置计算工具类
        var calculator = new NotePositionCalculator(1000, laneTransform);
        Container.BindInstance(calculator).AsSingle();

        //绑定乐曲管理类
        Container.BindInstance(_trackController).AsSingle();
        
        //绑定特效管理类
        Container.BindInstance(_vfxManager).AsSingle();
        
        //绑定noteView生成类
        Container.BindInstance(_noteViewSpawner).AsSingle();
    }
}