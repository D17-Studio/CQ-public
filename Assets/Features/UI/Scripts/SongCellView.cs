using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 歌曲格视图（挂 SongCell.prefab）：Bind 显示曲目 / SetLayout 摆位 / SetSelected 选中表现 / 点击上抛。
/// </summary>
public class SongCellView : MonoBehaviour, IPointerClickHandler
{
    // 根上的 CanvasGroup：出场淡出（SetLayout 的 alpha）写在它上面。
    [SerializeField] private CanvasGroup _canvasGroup;
    // 底条图：选中时颜色 / 不透明度随之变化。
    [SerializeField] private Image _backgroundImage;
    // 曲名 / 曲师（没有曲师物件可留空）。
    [SerializeField] private TMP_Text _titleText;
    [SerializeField] private TMP_Text _artistText;

    // 选中时的文字颜色 / 底条颜色（idle 颜色取 prefab 现值）。
    [SerializeField] private Color _titleColorSelected = Color.white;
    [SerializeField] private Color _backgroundColorSelected = new(1f, 1f, 1f, 0.6f);

    private Color _titleColorIdle;
    private Color _backgroundColorIdle;

    /// <summary>本格被点击时触发。</summary>
    public event Action Clicked;

    private void Awake()
    {
        _titleColorIdle = _titleText.color;
        _backgroundColorIdle = _backgroundImage.color;
    }

    /// <summary>把一条曲目画进本格（曲名 / 曲师）。</summary>
    public void Bind(TrackData track)
    {
        _titleText.text = track.MusicTitle;
        if (_artistText != null) _artistText.text = track.MusicArtist;
    }

    /// <summary>写入本帧的轮盘采样：位置 / 缩放 / 出场透明度。</summary>
    public void SetLayout(float posX, float posY, float scale, float alpha)
    {
        var rect = (RectTransform)transform;
        rect.anchoredPosition = new Vector2(posX, posY);
        rect.localScale = new Vector3(scale, scale, 1f);

        _canvasGroup.alpha = alpha;
        _canvasGroup.blocksRaycasts = alpha > 0.01f;   // 淡出中的格子不再吃点击
    }

    /// <summary>选中表现：strength 0 = 未选中、1 = 完全选中，插值文字与底条颜色。</summary>
    public void SetSelected(float strength)
    {
        _titleText.color = Color.Lerp(_titleColorIdle, _titleColorSelected, strength);
        _backgroundImage.color = Color.Lerp(_backgroundColorIdle, _backgroundColorSelected, strength);
    }

    /// <summary>点击上抛；格内承接点击的 Image 需保持 Raycast Target。</summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        Clicked?.Invoke();
    }
}
