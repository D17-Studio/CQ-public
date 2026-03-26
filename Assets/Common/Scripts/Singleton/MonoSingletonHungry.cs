using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MonoSingletonHungry<T> : MonoBehaviour,IInitialize where T : MonoSingletonHungry<T>
{
    private static T _instance;
    
    public static T Instance => _instance;

    public int InitializePriority { get; }
    
    
    public void Initialize()
    {
        GameObject obj = new GameObject(typeof(T).Name);
        DontDestroyOnLoad(obj);
        _instance = obj.AddComponent<T>();
    }
}
