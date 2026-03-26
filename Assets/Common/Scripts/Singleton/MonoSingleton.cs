using UnityEngine;

/// <summary>
/// 懒汉mono单例模板
/// </summary>
/// <typeparam name="T"></typeparam>
public class MonoSingleton<T> : MonoBehaviour where T : MonoSingleton<T>
{

    private static T _instance;
    private static readonly object _lock = new object();

    public static T Instance
    {
        get
        {
            if (_instance  == null)
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        GameObject obj =   new GameObject(typeof(T).Name);
                        _instance = obj.AddComponent<T>();
                    }
                }
            }
            
            return _instance;
        }
    }



    protected virtual void Awake()
    {
        DontDestroyOnLoad(gameObject);  //跨场景保留
        Init();
    }

    /// <summary>
    /// 初始化函、子类覆写
    /// </summary>
    protected virtual void Init()
    {
        
    }
}
