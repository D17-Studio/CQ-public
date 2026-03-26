using System;
using System.Threading;
using UnityEngine;


/// <summary>
/// 懒汉无mono单例模板
/// </summary>
/// <typeparam name="T"></typeparam>
    public class Singleton<T> where T :Singleton<T>,new()
    {
        private static readonly Lazy<T> _instance = new Lazy<T>(() =>
        {
            //创建实例时调用初始化函数
            var instance = new T();
            instance.Init();
            return new T();
        },LazyThreadSafetyMode.ExecutionAndPublication);
    
        //全局访问点
        public static T Instance  => _instance.Value;

        /// <summary>
        /// 初始化函数、子类覆写
        /// </summary>
        protected virtual void Init()
        {
            
        }
    }


