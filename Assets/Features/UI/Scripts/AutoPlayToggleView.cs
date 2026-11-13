using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// AutoPlay 勾选框视图：点击上抛；按 Presenter 下推显示 / 隐藏勾选标记。
/// </summary>
public class AutoPlayToggleView : MonoBehaviour, IPointerClickHandler
{
    // 勾选标记（开时显示，关时隐藏）。
    [SerializeField] private GameObject _checkmark;

    /// <summary>本勾选框被点击时触发。</summary>
    public event Action Clicked;

    /// <summary>下推勾选态：控制勾选标记的显隐。</summary>
    public void SetOn(bool isOn)
    {
        if (_checkmark != null) _checkmark.SetActive(isOn);
    }

    /// <summary>点击上抛。</summary>
    public void OnPointerClick(PointerEventData eventData) => Clicked?.Invoke();
}
