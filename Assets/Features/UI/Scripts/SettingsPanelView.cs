using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 设置弹层视图（挂 SettingsPanel 根）：持子控件引用并上抛点击；显隐与数值显示由 Presenter 下推。
/// </summary>
public class SettingsPanelView : MonoBehaviour
{
    // 关闭按钮
    [SerializeField] private Button _closeButton;

    // 流速行：数值文本 + 减 / 加按钮。
    [SerializeField] private TMP_Text _speedValue;
    [SerializeField] private Button _speedMinusButton;
    [SerializeField] private Button _speedPlusButton;

    // 偏移行：同上。
    [SerializeField] private TMP_Text _offsetValue;
    [SerializeField] private Button _offsetMinusButton;
    [SerializeField] private Button _offsetPlusButton;

    // 键位区：7 行（数组序 = 轨道 -3 → +3），每行一个 KeybindRowView。
    [SerializeField] private KeybindRowView[] _keybindRows;
    // 重绑"请按键"提示弹层（默认隐藏，捕获中显示）。
    [SerializeField] private GameObject _capturePrompt;
    // 恢复默认键位按钮。
    [SerializeField] private Button _resetButton;

    /// <summary>点了关闭按钮。</summary>
    public event Action CloseClicked;

    /// <summary>请求流速步进：+1 = 加一档、-1 = 减一档。</summary>
    public event Action<int> SpeedStepRequested;

    /// <summary>请求偏移步进：+1 = 加一档、-1 = 减一档。</summary>
    public event Action<int> OffsetStepRequested;

    /// <summary>点了某条键位行（rowIndex 序 = 轨道 -3 → +3）。</summary>
    public event Action<int> KeybindRowClicked;

    /// <summary>点了恢复默认键位按钮。</summary>
    public event Action ResetClicked;

    private void Awake()
    {
        if (_closeButton != null)
        {
            _closeButton.onClick.AddListener(() => CloseClicked?.Invoke());
        }

        if (_speedMinusButton != null)
        {
            _speedMinusButton.onClick.AddListener(() => SpeedStepRequested?.Invoke(-1));
        }

        if (_speedPlusButton != null)
        {
            _speedPlusButton.onClick.AddListener(() => SpeedStepRequested?.Invoke(1));
        }

        if (_offsetMinusButton != null)
        {
            _offsetMinusButton.onClick.AddListener(() => OffsetStepRequested?.Invoke(-1));
        }

        if (_offsetPlusButton != null)
        {
            _offsetPlusButton.onClick.AddListener(() => OffsetStepRequested?.Invoke(1));
        }

        if (_keybindRows != null)
        {
            for (int i = 0; i < _keybindRows.Length; i++)
            {
                var row = _keybindRows[i];
                if (row == null) continue;

                int rowIndex = i;   // 闭包捕获副本
                row.Clicked += () => KeybindRowClicked?.Invoke(rowIndex);
            }
        }

        if (_resetButton != null)
        {
            _resetButton.onClick.AddListener(() => ResetClicked?.Invoke());
        }
    }

    /// <summary>显示面板。</summary>
    public void Show() => gameObject.SetActive(true);

    /// <summary>隐藏面板。</summary>
    public void Hide() => gameObject.SetActive(false);

    /// <summary>流速数值显示。</summary>
    public void SetSpeed(int speed)
    {
        if (_speedValue != null) _speedValue.text = speed.ToString();
    }

    /// <summary>偏移数值显示（带符号）。</summary>
    public void SetOffset(int offsetMs)
    {
        if (_offsetValue != null) _offsetValue.text = offsetMs > 0 ? $"+{offsetMs}" : offsetMs.ToString();
    }

    /// <summary>键位区某行的键名显示。</summary>
    public void SetKeyName(int rowIndex, string keyName)
    {
        if (_keybindRows == null || _keybindRows[rowIndex] == null) return;
        _keybindRows[rowIndex].SetKeyName(keyName);
    }

    /// <summary>重绑捕获提示弹层开关。</summary>
    public void SetPromptActive(bool active)
    {
        if (_capturePrompt != null) _capturePrompt.SetActive(active);
    }
}
