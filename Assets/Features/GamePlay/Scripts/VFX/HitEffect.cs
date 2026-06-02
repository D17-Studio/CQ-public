using System;
using CQMusicGame.Shared;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
public class HitEffect : MonoBehaviour, IPoolable
{
    [SerializeField] private float _duration = 0.2f;
    [SerializeField] private float _baseScale = 1f;
    [SerializeField] private float _growth = 3f;
    [SerializeField] private float _startOpacity = 0.5f;

    private Action _onReturn;
    private float _timer;
    private Image _image;
    private RectTransform _rectTransform;

    void IPoolable.SetReturnCallback(Action onReturn)
    {
        _onReturn = onReturn;
    }

    private void SetAlpha(float a)
    {
        var c = _image.color;
        c.a = a;
        _image.color = c;
    }

    void Awake()
    {
        _image = GetComponent<Image>();
        _rectTransform = GetComponent<RectTransform>();
    }

    public void Play(Vector2 screenPos, JudgeResult result)
    {
        _rectTransform.anchoredPosition = screenPos;
        _timer = _duration;
        transform.localScale = Vector3.one * _baseScale;

        Color c = result switch
        {
            JudgeResult.Perfect => Color.yellow,
            JudgeResult.Early or JudgeResult.Late => Color.cyan,
            _ => Color.white
        };
        c.a = _startOpacity;
        _image.color = c;

        gameObject.SetActive(true);
    }

    void Update()
    {
        _timer -= Time.deltaTime;
        if (_timer <= 0f)
        {
            transform.localScale = Vector3.one;
            gameObject.SetActive(false);
            _onReturn?.Invoke();
            return;
        }

        float t = 1f - _timer / _duration;
        float eased = Easing.OutQuad(t);

        _rectTransform.localScale = Vector3.one * Mathf.Lerp(_baseScale, _baseScale * _growth, eased);
        SetAlpha(Mathf.Lerp(_startOpacity, 0f, eased));
    }
}
