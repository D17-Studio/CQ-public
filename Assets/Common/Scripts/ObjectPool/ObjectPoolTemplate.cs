using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using Object = UnityEngine.Object;

public abstract class ObjectPoolTemplate : MonoBehaviour //<T> :MonoSingletonHungry<T> where T : ObjectPoolTemplate<T>
{
    private List<GameObject> _objectPool = new List<GameObject>();

    [SerializeField] protected int warmCount;
    [SerializeField]protected int poolSize;
    [SerializeField]protected GameObject objectPrefab;
    [SerializeField] private bool onWarmUp;
    
    // protected override void OnAwake()
    // {
    //     InitializeObjectPool();
    //     OnAAwake();
    // }
    //
    // protected virtual void OnAAwake()
    // {
    //     
    // }

    public virtual void Awake()
    {
        Debug.Log("ObjectPoolTemplate Awake");
        InitializeObjectPool();

        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
        
    }
    
    //场景加载中转函数
    public void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _objectPool =  new List<GameObject>();
        InitializeObjectPool();
    }
    
    //场景卸载中专函数
    public void OnSceneUnloaded(Scene scene)
    {
        ClearObjectPool();
    }


    /// <summary>
    /// 初始化池
    /// </summary>
    public void InitializeObjectPool()
    {
        WarmUpObjectPool(warmCount);
    }

    /// <summary>
    /// 创建对象
    /// </summary>
    /// <returns></returns>
    private GameObject CreateObject()
    {
        GameObject obj = null;
        
        if (_objectPool.Count < poolSize)
        {
            obj = Object.Instantiate(objectPrefab);
            obj.SetActive(false);
        }

        if (onWarmUp)
        {
            _objectPool.Add(obj);
        }

        return obj;
    }
    
    /// <summary>
    /// 获取对象
    /// </summary>
    /// <returns></returns>
    public GameObject GetObject()
    {
        GameObject obj;

        if (_objectPool.Count > 0)
        {
            obj = _objectPool[0];
            _objectPool.RemoveAt(0);
        }
        else
        {
            obj = CreateObject();
        }
        
        obj.SetActive(true);
        return obj;
    }

    /// <summary>
    /// 归还对象
    /// </summary>
    /// <param name="obj">归还对象</param>
    public void ReturnObject(GameObject obj)
    {
        if (_objectPool.Count < poolSize)
        {
            obj.SetActive(false);
            _objectPool.Add(obj);
        }
        else
        {
            Destroy(obj);
        }
    }

    /// <summary>
    /// 暖池
    /// </summary>
    /// <param name="count">暖池数量</param>
    public void WarmUpObjectPool(int count)
    {
        onWarmUp = true;
        for (int i = 0; i < count; i++)
        {
            if (_objectPool.Count < poolSize)
            {
                CreateObject();
            }
            else
            {
                return;
            }
            
        }
        onWarmUp = false;
    }

    /// <summary>
    /// 清空池
    /// </summary>
    public void ClearObjectPool()
    {
        foreach (var obj in _objectPool)
        {
            Destroy(obj);
        }
        _objectPool.Clear();
    }
}
