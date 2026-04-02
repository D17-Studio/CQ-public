using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public class ObjectPool : MonoBehaviour
{
    private readonly List<GameObject> _objectPool = new List<GameObject>();
    
    [SerializeField] private readonly GameObject _objectPrefab;
    [SerializeField] private readonly int _warmCount;
    [SerializeField] private readonly int _poolSize;
    [SerializeField] private readonly bool _isDontDestroyOnLoad;
    
    [SerializeField] private bool onWarmUp;

    public ObjectPool(GameObject objectPrefab, int warmCount = 0, int poolSize = 10000 , bool isDontDestroyOnLoad = false )
    {
        var obj = new GameObject(objectPrefab.name + " Pool");
        obj.AddComponent<ObjectPool>();
        
        _objectPrefab = objectPrefab;
        _warmCount = warmCount;
        _poolSize = poolSize;
        _isDontDestroyOnLoad = isDontDestroyOnLoad;

        if (_isDontDestroyOnLoad)
        {
            DontDestroyOnLoad(obj);
        }
        
        WarmUpObjectPool(_warmCount);
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
    private void ReturnObject(GameObject obj)
    {
        if (_objectPool.Count < _poolSize)
        {
            obj.SetActive(false);
            _objectPool.Add(obj);
        }
        else
        {
            Object.Destroy(obj);
        }
    }

    /// <summary>
    /// 暖池
    /// </summary>
    /// <param name="count">暖池数量</param>
    private void WarmUpObjectPool(int count)
    {
        onWarmUp = true;
        for (int i = 0; i < count; i++)
        {
            if (_objectPool.Count < _poolSize)
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
            Object.Destroy(obj);
        }
        _objectPool.Clear();
    }
    
    /// <summary>
    /// 创建对象
    /// </summary>
    /// <returns></returns>
    private GameObject CreateObject()
    {
        var obj = Instantiate(_objectPrefab);
        obj.SetActive(false);
        
        if (onWarmUp)
        {
            _objectPool.Add(obj);
        }

        if (_isDontDestroyOnLoad)
        {
            DontDestroyOnLoad(obj);
        }
        
        var poolable = obj.AddComponent<Poolable>();
        poolable.Initialize(() => ReturnObject(obj));
        
        return obj;
    }

    private void OnDestroy()
    {
        ClearObjectPool();
    }
}
