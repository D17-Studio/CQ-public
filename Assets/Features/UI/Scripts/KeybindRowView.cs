using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 键位区单行视图（行根挂载，一行一个）：整行上抛点击；键名显示由 Presenter 下推。
/// </summary>
public class KeybindRowView : MonoBehaviour
{
    // 整行按钮（行根自己身上的 Unity Button）。
    [SerializeField] private Button _button;
    // 键名文本（当前绑定按键显示）。
    [SerializeField] private TMP_Text _keyText;

    /// <summary>点了这一行（请求重绑）。</summary>
    public event Action Clicked;

    private void Awake()
    {
        if (_button != null)
        {
            _button.onClick.AddListener(() => Clicked?.Invoke());
        }
    }

    /// <summary>键名显示。</summary>
    public void SetKeyName(string keyName)
    {
        if (_keyText != null) _keyText.text = keyName;
    }
}
