using UnityEngine.SceneManagement;
using Zenject;

/// <summary>
/// 结算页大脑（纯 C#，由 TrackPlayingInstaller 装配）：订阅 TrackEnded，曲终按模式写入成绩（AutoPlay 跳过）并下推结算文本。
/// 重试 = 重载打歌场景、返回 = 载 MenuScene。
/// </summary>
public class ResultPresenter : IInitializable
{
    // 场景名
    private const string TrackPlayingSceneName = "TrackPlayingScene";
    private const string MenuSceneName = "MenuScene";

    private readonly TrackController _trackController;
    private readonly ScoringSystem _scoring;
    private readonly PlaySessionService _playSession;
    private readonly ScoreArchive _archive;
    private readonly ResultPanelView _view;

    public ResultPresenter(TrackController trackController,
                           ScoringSystem scoring,
                           PlaySessionService playSession,
                           ScoreArchive archive,
                           ResultPanelView view)
    {
        _trackController = trackController;
        _scoring = scoring;
        _playSession = playSession;
        _archive = archive;
        _view = view;
    }

    /// <summary>初始化：隐藏面板、接按钮上抛、订阅曲终。</summary>
    public void Initialize()
    {
        _view.SetVisible(false);
        _view.RetryClicked += OnRetryClicked;
        _view.ContinueClicked += OnContinueClicked;
        _trackController.TrackEnded += OnTrackEnded;
    }

    private void OnTrackEnded()
    {
        // AutoPlay 不计入存档：跳过最佳分 / 首次 FC 写入
        bool isNewBest = false;
        if (!_trackController.IsAutoPlay)
            isNewBest = RecordResult();

        _view.SetResultText(BuildResultText(isNewBest));
        _view.SetVisible(true);
    }

    /// <summary>写入本局成绩：无记录或更高分才 RecordScore，首次全连才 RecordFullCombo；返回是否刷新了最佳分。</summary>
    private bool RecordResult()
    {
        string key = ScoreKeys.Of(_playSession.SelectedTrack, _playSession.SelectedChart);
        var old = _archive.GetBest(key);
        int score = _scoring.Score;
        bool isNewBest = !old.Exists || score > old.BestScore;

        if (isNewBest)
            _archive.RecordScore(key, score);
        if (_scoring.IsFullCombo && !old.IsFullCombo)
            _archive.RecordFullCombo(key, true);

        return isNewBest;
    }

    /// <summary>重试：重载打歌场景。</summary>
    private void OnRetryClicked()
    {
        SceneManager.LoadScene(TrackPlayingSceneName);
    }

    /// <summary>返回选歌：载入 MenuScene。</summary>
    private void OnContinueClicked()
    {
        SceneManager.LoadScene(MenuSceneName);
    }

    /// <summary>结算文本拼装：AutoPlay 只显示曲目信息与 AUTO PLAY，正式成绩显示完整结算。</summary>
    private string BuildResultText(bool isNewBest)
    {
        var track = _playSession.SelectedTrack;
        var chart = _playSession.SelectedChart;

        string title = track != null ? track.MusicTitle : "—";
        string difficulty = chart != null ? $"{chart.Difficulty} {chart.ChartConstant}" : "—";

        if (_trackController.IsAutoPlay)
            return $"{title}  [{difficulty}]\nAUTO PLAY";

        return $"{title}  [{difficulty}]\n" +
               $"Score {_scoring.Score}\n" +
               (isNewBest ? "★ NEW BEST\n" : "") +
               $"Evaluate {_scoring.GetEvaluate()}\n" +
               $"MaxCombo {_scoring.MaxCombo}\n" +
               $"Perfect {_scoring.GetCount(JudgeResult.Perfect)}\n" +
               $"Early {_scoring.GetCount(JudgeResult.Early)}\n" +
               $"Late {_scoring.GetCount(JudgeResult.Late)}\n" +
               $"Bad {_scoring.GetCount(JudgeResult.Bad)}\n" +
               $"Miss {_scoring.GetCount(JudgeResult.Miss)}";
    }
}
