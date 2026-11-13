using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 难度按钮视图（挂每个难度按钮物体）：点击上抛；按 Presenter 下推的状态写定数数字，平滑收敛配色与缩放。
/// 选中放大可能压到邻座——被选中时提到同级最后（渲染在最上层）。
/// </summary>
public class DifficultyButtonView : MonoBehaviour, IPointerClickHandler
{
    // 背景图（选中 / 无谱换色）。
    [SerializeField] private Image _background;
    // 等级文本（EZ / ST / HD / EX，静态，只换色）。
    [SerializeField] private TMP_Text _levelText;
    // 数字文本（定数；无谱留空）。
    [SerializeField] private TMP_Text _numberText;

    [Header("普通态（有谱未选中）")]
    [SerializeField] private Color _normalBackground = new(0f, 0f, 0f, 1f);
    [SerializeField] private Color _normalFont = Color.white;

    [Header("选中态")]
    [SerializeField] private Color _selectedBackground = Color.white;
    [SerializeField] private Color _selectedFont = new(0.1f, 0.1f, 0.12f, 1f);
    [SerializeField] private float _selectedScale = 1.1f;

    [Header("无谱态")]
    [SerializeField] private Color _missingBackground = new(0f, 0f, 0f, 0.45f);
    [SerializeField] private Color _missingFont = new(0.5f, 0.5f, 0.5f, 1f);

    [Header("过渡收敛速度")]
    [SerializeField] private float _smoothSpeed = 12f;

    // 运行时目标态：是否有谱面 / 是否选中。
    private bool _hasChart;
    private bool _selected;

    /// <summary>本按钮被点击时触发。</summary>
    public event Action Clicked;

    /// <summary>下推本按钮状态：数字文本即时写；配色与缩放每帧向目标态平滑收敛。</summary>
    public void SetState(bool hasChart, int constant, bool selected)
    {
        _hasChart = hasChart;
        _selected = selected;

        if (_numberText != null)
            _numberText.text = hasChart ? constant.ToString() : string.Empty;

        // 选中放大压住邻座：提到同级最后 = 渲染在最上层
        if (hasChart && selected && transform.GetSiblingIndex() != transform.parent.childCount - 1)
            transform.SetAsLastSibling();
    }

    private void Update()
    {
        // 目标：无谱 → 灰；有谱 → 选中 / 普通
        Color background = _hasChart
            ? (_selected ? _selectedBackground : _normalBackground)
            : _missingBackground;
        Color font = _hasChart
            ? (_selected ? _selectedFont : _normalFont)
            : _missingFont;
        float scale = _hasChart && _selected ? _selectedScale : 1f;

        float t = 1f - Mathf.Exp(-_smoothSpeed * Time.deltaTime);

        transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * scale, t);
        if (_background != null) _background.color = Color.Lerp(_background.color, background, t);
        if (_levelText != null) _levelText.color = Color.Lerp(_levelText.color, font, t);
        if (_numberText != null) _numberText.color = Color.Lerp(_numberText.color, font, t);
    }

    /// <summary>点击上抛。</summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        Clicked?.Invoke();
    }
}
