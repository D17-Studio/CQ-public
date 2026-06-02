using CQMusicGame.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 单条轨道的视觉组件，管理轨道头、轨道线和按键的显示与动画
/// </summary>
public class LaneView : MonoBehaviour
{
    [SerializeField] private Image _headImage;
    [SerializeField] private Image _lineImage;
    [SerializeField] private TMP_Text _keyTMPText;

    private LaneState _laneTransform;
    private RectTransform _rectTransform;
    private int _laneIndex;

    private float _originHeadAlpha;   
    private float _originLineAlpha;
    private float _originKeyAlpha;

    void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
        _originHeadAlpha = _headImage.color.a;
        _originLineAlpha = _lineImage.color.a;
        _originKeyAlpha = _keyTMPText.color.a;
    }

    public void Initialize(int laneIndex , string keyText , LaneState laneTransform)
    {
        _laneIndex = laneIndex;
        _keyTMPText.text = keyText;
        _laneTransform = laneTransform;
    }

    public void UpdateVisual(int tackTime)
    {
        _rectTransform.anchoredPosition = ScreenAdapter.ToScreenPos(_laneTransform.Position(_laneIndex, tackTime));
        _rectTransform.localEulerAngles = new Vector3(0,0,_laneTransform.Rotation(_laneIndex, tackTime));
        
        _keyTMPText.transform.localEulerAngles = new Vector3(0,0,-_laneTransform.Rotation(_laneIndex, tackTime));
        
        SetHeadAlpha(_laneTransform.HeadOpacity(_laneIndex, tackTime));
        SetLineAlpha(_laneTransform.LineOpacity(_laneIndex, tackTime));
        SetKeyAlpha(_laneTransform.KeyOpacity(_laneIndex, tackTime));
    }

    private void SetHeadAlpha(float alpha)
    {
        var c = _headImage.color;
        c.a = alpha*_originHeadAlpha;
        _headImage.color = c;
    }

    private void SetLineAlpha(float alpha)
    {
        var c = _lineImage.color;
        c.a = alpha*_originLineAlpha;
        _lineImage.color = c;
    }
    
    private void SetKeyAlpha(float alpha)
    {
        var c = _keyTMPText.color;
        c.a = alpha*_originKeyAlpha;
        _keyTMPText.color = c;
    }
}
