using System;
using UnityEngine.SceneManagement;
using Zenject;

/// <summary>
/// 菜单导航：场景跳转 + 设置弹层开合 + ESC 统一分发（有弹层先关弹层）。
/// 弹层预实例化：常驻场景、默认隐藏；SettingsPresenter 在 Initialize 时注册。
/// </summary>
public class MenuNavigator : ITickable
{
    // 打歌场景名
    private const string TrackPlayingSceneName = "TrackPlayingScene";

    private readonly InputService _inputService;

    // 设置弹层（SettingsPresenter 在 Initialize 注册进来）
    private SettingsPresenter _settings;

    public MenuNavigator(InputService inputService)
    {
        _inputService = inputService;
    }

    /// <summary>弹层关闭后触发（ESC 与关闭按钮同一出口）。</summary>
    public event Action SettingsClosed;

    /// <summary>是否有弹层打开。</summary>
    public bool IsPopupOpen => _settings != null && _settings.IsOpen;

    /// <summary>注册设置弹层。</summary>
    public void AttachSettings(SettingsPresenter settings)
    {
        _settings = settings;
    }

    /// <summary>载入打歌场景。</summary>
    public void EnterGame()
    {
        SceneManager.LoadScene(TrackPlayingSceneName);
    }

    /// <summary>打开设置弹层；已打开时忽略。</summary>
    public void OpenSettings()
    {
        if (IsPopupOpen) return;
        _settings.Open();
    }

    /// <summary>关闭最上层弹层；无弹层打开时忽略。</summary>
    public void CloseTop()
    {
        if (!IsPopupOpen) return;
        _settings.Close();
        SettingsClosed?.Invoke();
    }

    /// <summary>ESC 分发：有弹层先关弹层。</summary>
    public void Tick()
    {
        if (!_inputService.BackDown()) return;
        CloseTop();
    }
}
