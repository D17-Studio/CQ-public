using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 打歌 HUD 视图（挂 GameHUD 根）：分数 / Combo / 进度条；数据由 Presenter 每帧下推。
/// 显示规则仅"Combo 低于阈值时隐藏文本"。
/// </summary>
public class GameHudView : MonoBehaviour
{
    // 分数文本。
    [SerializeField] private TMP_Text _scoreText;
    // Combo 文本（低于阈值时隐藏）。
    [SerializeField] private TMP_Text _comboText;
    // Combo 显示阈值。
    [SerializeField] private int _comboVisibleThreshold = 2;
    // 进度条填充（Image Type = Filled / Horizontal / Left；只写 fillAmount）。
    [SerializeField] private Image _progressFill;
    // AutoPlay 标识（显隐由 Presenter 下推）。
    [SerializeField] private GameObject _autoPlayMark;

    /// <summary>分数下推。</summary>
    public void SetScore(int score)
    {
        _scoreText.text = score.ToString("D7");
    }

    /// <summary>连击下推（低于阈值隐藏）。</summary>
    public void SetCombo(int combo)
    {
        bool visible = combo >= _comboVisibleThreshold;
        if (_comboText.gameObject.activeSelf != visible)
            _comboText.gameObject.SetActive(visible);

        if (visible)
            _comboText.text = combo.ToString();
    }

    /// <summary>进度下推（0~1）。</summary>
    public void SetProgress(float progress)
    {
        _progressFill.fillAmount = progress;
    }

    /// <summary>AutoPlay 标识显隐。</summary>
    public void SetAutoPlayVisible(bool visible)
    {
        if (_autoPlayMark != null) _autoPlayMark.SetActive(visible);
    }
}
