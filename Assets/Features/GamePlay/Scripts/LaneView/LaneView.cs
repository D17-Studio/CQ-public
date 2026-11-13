using CQMusicGame.Shared;
using TMPro;
using UnityEngine;

/// <summary>
/// 单条轨道的视觉组件，管理轨道头、轨道线和按键的显示与动画
/// </summary>
public class LaneView : MonoBehaviour
{
    [SerializeField] private SpriteRenderer _headRenderer;
    [SerializeField] private SpriteRenderer _lineRenderer;
    [SerializeField] private TMP_Text _keyTMPText;

    private ScreenAdapter _screenAdapter;
    private LaneState _laneState;
    private int _laneIndex;

    private float _originHeadAlpha;
    private float _originLineAlpha;
    private float _originKeyAlpha;

    void Awake()
    {
        _originHeadAlpha = _headRenderer.color.a;
        _originLineAlpha = _lineRenderer.color.a;
        _originKeyAlpha = _keyTMPText.color.a;
    }

    public void Initialize(int laneIndex, string keyText, LaneState laneTransform, ScreenAdapter screenAdapter)
    {
        _laneIndex = laneIndex;
        _keyTMPText.text = keyText;
        _laneState = laneTransform;
        _screenAdapter = screenAdapter;
    }

    public void UpdateVisual(int trackTime)
    {
        LaneFrame frame = _laneState.GetFrame(_laneIndex, trackTime);

        transform.position = _screenAdapter.ToWorldPos(frame.Position);
        transform.localEulerAngles = new Vector3(0, 0, frame.Rotation);

        _keyTMPText.transform.localEulerAngles = new Vector3(0, 0, -frame.Rotation);

        SetHeadAlpha(_laneState.HeadOpacity(_laneIndex, trackTime));
        SetLineAlpha(_laneState.LineOpacity(_laneIndex, trackTime));
        SetKeyAlpha(_laneState.LabelOpacity(_laneIndex, trackTime));
    }

    private void SetHeadAlpha(float alpha)
    {
        var c = _headRenderer.color;
        c.a = alpha * _originHeadAlpha;
        _headRenderer.color = c;
    }

    private void SetLineAlpha(float alpha)
    {
        var c = _lineRenderer.color;
        c.a = alpha * _originLineAlpha;
        _lineRenderer.color = c;
    }

    private void SetKeyAlpha(float alpha)
    {
        var c = _keyTMPText.color;
        c.a = alpha * _originKeyAlpha;
        _keyTMPText.color = c;
    }
}
