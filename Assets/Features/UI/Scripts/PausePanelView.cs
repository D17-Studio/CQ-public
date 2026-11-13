using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 暂停面板视图（挂 PausePanel 根）：面板显隐、按钮组 / 倒数块切换与倒数文本均由 Presenter 下推；
/// 三个按钮上抛点击。
/// </summary>
public class PausePanelView : MonoBehaviour
{
    // 继续按钮。
    [SerializeField] private Button _resumeButton;
    // 重试按钮。
    [SerializeField] private Button _retryButton;
    // 返回选歌按钮。
    [SerializeField] private Button _returnButton;

    // 按钮组容器（Paused 阶段显示）。
    [SerializeField] private GameObject _buttonsBlock;
    // 倒数块容器（Resuming 阶段显示）。
    [SerializeField] private GameObject _countdownBlock;
    // 倒数文本（3 / 2 / 1）。
    [SerializeField] private TMP_Text _countdownText;

    /// <summary>点了"继续"。</summary>
    public event Action ResumeClicked;

    /// <summary>点了"重试"。</summary>
    public event Action RetryClicked;

    /// <summary>点了"返回选歌"。</summary>
    public event Action ReturnClicked;

    private void Awake()
    {
        if (_resumeButton != null)
            _resumeButton.onClick.AddListener(() => ResumeClicked?.Invoke());

        if (_retryButton != null)
            _retryButton.onClick.AddListener(() => RetryClicked?.Invoke());

        if (_returnButton != null)
            _returnButton.onClick.AddListener(() => ReturnClicked?.Invoke());
    }

    /// <summary>面板整体显隐。</summary>
    public void SetVisible(bool visible)
    {
        gameObject.SetActive(visible);
    }

    /// <summary>按钮组显隐。</summary>
    public void SetButtonsVisible(bool visible)
    {
        _buttonsBlock.SetActive(visible);
    }

    /// <summary>倒数块显隐。</summary>
    public void SetCountdownVisible(bool visible)
    {
        _countdownBlock.SetActive(visible);
    }

    /// <summary>倒数文本下推。</summary>
    public void SetCountdownText(string content)
    {
        _countdownText.text = content;
    }
}
