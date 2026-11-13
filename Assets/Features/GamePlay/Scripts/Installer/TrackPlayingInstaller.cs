using CQMusicGame.Shared;
using UnityEngine;
using Zenject;

public class TrackPlayingInstaller : MonoInstaller
{
    [Inject] private PlaySessionService _playSessionService;
    [Inject] private NoteJudgeConfig _noteConfig;
    [Inject] private PlayerConfigStore _playerConfigStore;
    [Inject] private JudgeZoneCatalog _zoneCatalog;
    

    [SerializeField] private TrackController _trackController;
    [SerializeField] private NoteViewSpawner _noteViewSpawner;
    [SerializeField] private ResultPanelView _resultPanelView;
    [SerializeField] private GameHudView _gameHudView;
    [SerializeField] private PausePanelView _pausePanelView;

    public override void InstallBindings()
    {
        // 选曲由菜单写入（PlaySessionService 不再预置默认值）；直接 Play 本场景会为空。
        if (_playSessionService.SelectedChart == null)
        {
            Debug.LogError("PlaySessionService 未写入选曲：请从 MenuScene 进入打歌场景（直接 Play 本场景不会加载谱面）。");
            return;
        }

        //绑定谱面信息
        ChartSheet chart;
        try
        {
            chart = ChartSheet.Parse(_playSessionService.SelectedChart.ChartContent, msg => Debug.LogWarning(msg));
        }
        catch (ChartException ex)
        {
            Debug.LogError($"谱面加载失败（{_playSessionService.SelectedTrack?.MusicTitle} / {_playSessionService.SelectedChart.Difficulty}）：{ex.Message}");
            return;
        }

        Container.BindInstance(chart).AsSingle();
        
        //绑定屏幕位置转换工具类
        Container.BindInstance(new ScreenAdapter()).AsSingle();

        //绑定计分系统
        var scoring = new ScoringSystem(chart);
        Container.BindInstance(scoring).AsSingle();

        //绑定轨道状态类（ChartSheet 构造时唯一生成）
        var laneState = chart.LaneState;
        Container.BindInstance(laneState).AsSingle();

        //绑定Note判定类
        var noteManager = new NoteJudge(_noteConfig, scoring, chart.GetNoteList(), _zoneCatalog, laneState);
        Container.BindInstance(noteManager).AsSingle();

        //绑定Note位置计算工具类
        var calculator = new NotePositionCalculator(_playerConfigStore.ScrollSpeed, laneState);
        Container.BindInstance(calculator).AsSingle();

        //绑定乐曲管理类
        Container.BindInstance(_trackController).AsSingle();
        
        
        //绑定noteView生成类
        Container.BindInstance(_noteViewSpawner).AsSingle();

        // 结算：曲终出面板（ResultPresenter 订阅 TrackController.TrackEnded；view 为场景手拖引用）
        Container.BindInterfacesAndSelfTo<ResultPresenter>()
                 .AsSingle()
                 .WithArguments(_resultPanelView);

        // 打歌 HUD：分数 / 连击 / 进度（GameHudPresenter 每帧读计分与播放进度下推）
        Container.BindInterfacesTo<GameHudPresenter>()
                 .AsSingle()
                 .WithArguments(_gameHudView);

        // 暂停面板：Phase → 面板显隐 / 倒数数字下推（PausePanelPresenter；view 为场景手拖引用）
        Container.BindInterfacesTo<PausePanelPresenter>()
                 .AsSingle()
                 .WithArguments(_pausePanelView);

        // 暂停输入路由：ESC（Playing → 暂停；Paused → 发起恢复倒数）
        Container.BindInterfacesTo<PauseInputRouter>().AsSingle();
    }
}