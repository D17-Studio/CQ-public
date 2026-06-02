using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

public class ObjectPool<T> where T : Component, IPoolable
{
    private readonly Stack<T> _pool = new();
    private readonly T _prefab;
    private readonly int _maxSize;

    public int Count => _pool.Count;
    public int MaxSize => _maxSize;

    public ObjectPool(T prefab, int maxSize = 500)
    {
        _prefab = prefab;
        _maxSize = maxSize;
    }

    public T Get()
    {
        T obj;
        if (_pool.Count > 0)
        {
            obj = _pool.Pop();
        }
        else
        {
            obj = Object.Instantiate(_prefab);
            obj.gameObject.SetActive(false);
        }

        obj.SetReturnCallback(() => Return(obj));
        return obj;
    }

    public void Return(T obj)
    {
        if (obj == null) return;

        if (_pool.Count < _maxSize)
        {
            obj.gameObject.SetActive(false);
            _pool.Push(obj);
        }
        else
        {
            Object.Destroy(obj.gameObject);
        }
    }

    public void WarmUp(int count)
    {
        int toCreate = Math.Min(count, _maxSize - _pool.Count);
        for (int i = 0; i < toCreate; i++)
        {
            var obj = Object.Instantiate(_prefab);
            obj.gameObject.SetActive(false);
            _pool.Push(obj);
        }
    }

    public void Clear()
    {
        while (_pool.Count > 0)
        {
            var obj = _pool.Pop();
            if (obj != null)
                Object.Destroy(obj.gameObject);
        }
    }
}
