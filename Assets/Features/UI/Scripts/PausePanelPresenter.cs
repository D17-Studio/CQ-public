using UnityEngine;
using UnityEngine.SceneManagement;
using Zenject;

/// <summary>
/// 暂停面板大脑（纯 C#，由 TrackPlayingInstaller 装配；IInitializable 隐藏面板并接按钮，ITickable 读 Phase 下推）。
/// Paused → 按钮组；Resuming → 倒数数字；其余阶段隐藏。
/// 按钮：继续 = 发起恢复倒数、重试 = 重载打歌场景、返回 = 载 MenuScene。
/// </summary>
public class PausePanelPresenter : IInitializable, ITickable
{
    // 阶段 → UI 形态
    private enum UiMode
    {
        Hidden,     // Loading / Playing / Result
        Buttons,    // Paused
        Countdown,  // Resuming
    }

    // 场景名
    private const string TrackPlayingSceneName = "TrackPlayingScene";
    private const string MenuSceneName = "MenuScene";

    private readonly TrackController _trackController;
    private readonly PausePanelView _view;

    // 缓存上次下推值，变化才刷
    private UiMode _lastMode = UiMode.Hidden;
    private int _lastCountdown = -1;

    public PausePanelPresenter(TrackController trackController, PausePanelView view)
    {
        _trackController = trackController;
        _view = view;
    }

    /// <summary>初始化：隐藏面板、接按钮上抛。</summary>
    public void Initialize()
    {
        _view.SetVisible(false);
        _view.ResumeClicked += OnResumeClicked;
        _view.RetryClicked += OnRetryClicked;
        _view.ReturnClicked += OnReturnClicked;
    }

    public void Tick()
    {
        UiMode mode = _trackController.Phase switch
        {
            TrackPhase.Paused => UiMode.Buttons,
            TrackPhase.Resuming => UiMode.Countdown,
            _ => UiMode.Hidden,
        };

        if (mode != _lastMode)
        {
            _lastMode = mode;
            _lastCountdown = -1; // 强制重推倒数数字

            switch (mode)
            {
                case UiMode.Hidden:
                    _view.SetVisible(false);
                    break;
                case UiMode.Buttons:
                    _view.SetCountdownVisible(false);
                    _view.SetButtonsVisible(true);
                    _view.SetVisible(true);
                    break;
                case UiMode.Countdown:
                    _view.SetButtonsVisible(false);
                    _view.SetCountdownVisible(true);
                    _view.SetVisible(true);
                    break;
            }
        }

        if (mode == UiMode.Countdown)
        {
            int countdown = Mathf.CeilToInt(_trackController.ResumeCountdownRemaining);
            if (countdown != _lastCountdown)
            {
                _lastCountdown = countdown;
                _view.SetCountdownText(countdown.ToString());
            }
        }
    }

    /// <summary>继续：发起恢复倒数。</summary>
    private void OnResumeClicked()
    {
        _trackController.RequestResume();
    }

    /// <summary>重试：重载打歌场景。</summary>
    private void OnRetryClicked()
    {
        SceneManager.LoadScene(TrackPlayingSceneName);
    }

    /// <summary>返回选歌：载入 MenuScene。</summary>
    private void OnReturnClicked()
    {
        SceneManager.LoadScene(MenuSceneName);
    }
}
