using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Poolable : MonoBehaviour
{
    private Action _onDisable;

    public void Initialize(Action onDisable)
    {
        _onDisable = onDisable;
    }

    private void OnDisable()
    {
        _onDisable?.Invoke();
    }
}
