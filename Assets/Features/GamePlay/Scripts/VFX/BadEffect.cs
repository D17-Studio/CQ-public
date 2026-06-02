using System;
using UnityEngine;

public class BadEffect : MonoBehaviour, IPoolable
{
    private Action _onReturn;

    void IPoolable.SetReturnCallback(Action onReturn)
    {
        _onReturn = onReturn;
    }

    public void Play(Vector2 screenPos)
    {
        // TODO
    }

    void Update()
    {
        // TODO
    }
}
