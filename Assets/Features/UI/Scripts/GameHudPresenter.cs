using UnityEngine;
using Zenject;

/// <summary>
/// 打歌 HUD 大脑（纯 C#，由 TrackPlayingInstaller 装配；ITickable 每帧推数据）。
/// 读 ScoringSystem 与 TrackController：分数 / 连击只在值变化时下推；进度只前进不后退；AutoPlay 标识随本局模式显隐。
/// </summary>
public class GameHudPresenter : ITickable
{
    private readonly ScoringSystem _scoring;
    private readonly TrackController _trackController;
    private readonly GameHudView _view;

    // 缓存上次下推值（-1 = 尚未推过），避免每帧刷 TMP。
    private int _lastScore = -1;
    private int _lastCombo = -1;
    private float _lastProgress = float.NegativeInfinity;
    private bool? _lastAutoPlay;

    public GameHudPresenter(ScoringSystem scoring, TrackController trackController, GameHudView view)
    {
        _scoring = scoring;
        _trackController = trackController;
        _view = view;
    }

    public void Tick()
    {
        bool isAutoPlay = _trackController.IsAutoPlay;
        if (isAutoPlay != _lastAutoPlay)
        {
            _lastAutoPlay = isAutoPlay;
            _view.SetAutoPlayVisible(isAutoPlay);
        }

        int score = _scoring.Score;
        if (score != _lastScore)
        {
            _lastScore = score;
            _view.SetScore(score);
        }

        int combo = _scoring.ComboCount;
        if (combo != _lastCombo)
        {
            _lastCombo = combo;
            _view.SetCombo(combo);
        }

        // 进度：未开始时（time ≤ 0）不更新；曲终后时间源冻结，值不再变化
        int time = _trackController.TrackTimeMs;
        if (time <= 0) return;

        int duration = _trackController.TrackDurationMs;
        float progress = duration > 0 ? Mathf.Clamp01((float)time / duration) : 0f;
        if (progress > _lastProgress)
        {
            _lastProgress = progress;
            _view.SetProgress(progress);
        }
    }
}
