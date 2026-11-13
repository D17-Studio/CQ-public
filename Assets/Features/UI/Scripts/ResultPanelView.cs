using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 结算面板视图（挂 ResultsPanel 根）：文本与显隐由 Presenter 下推；两个按钮上抛点击。
/// </summary>
public class ResultPanelView : MonoBehaviour
{
    // 结算文本（内容由 Presenter 拼装后下推）。
    [SerializeField] private TMP_Text _resultText;
    // 重试按钮。
    [SerializeField] private Button _retryButton;
    // 返回选歌按钮。
    [SerializeField] private Button _continueButton;

    /// <summary>点了"重试"。</summary>
    public event Action RetryClicked;

    /// <summary>点了"返回选歌"。</summary>
    public event Action ContinueClicked;

    private void Awake()
    {
        if (_retryButton != null)
            _retryButton.onClick.AddListener(() => RetryClicked?.Invoke());

        if (_continueButton != null)
            _continueButton.onClick.AddListener(() => ContinueClicked?.Invoke());
    }

    /// <summary>结算文本下推。</summary>
    public void SetResultText(string content)
    {
        _resultText.text = content;
    }

    /// <summary>面板显隐。</summary>
    public void SetVisible(bool visible)
    {
        gameObject.SetActive(visible);
    }
}
