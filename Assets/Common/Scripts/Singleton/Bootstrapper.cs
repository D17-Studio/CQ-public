using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>
/// 自动注册和初始化所有实现了 IInitialize 接口的类
/// </summary>
public static class Bootstrapper
{
    private static bool _isInitialized = false;
    private static List<InitializerInfo> _initializers = new List<InitializerInfo>();

    /// <summary>
    /// 启动器状态
    /// </summary>
    public enum BootstrapperState
    {
        NotStarted,
        Collecting,
        Initializing,
        Completed,
        Failed
    }

    public static BootstrapperState State { get; private set; } = BootstrapperState.NotStarted;
    public static int TotalInitializers => _initializers.Count;
    public static int CompletedInitializers => _initializers.Count(i => i.IsCompleted);
    public static IReadOnlyList<InitializerInfo> Initializers => _initializers.AsReadOnly();

    /// <summary>
    /// 初始化信息包装类
    /// </summary>
    public class InitializerInfo
    {
        public Type Type { get; }
        public IInitialize Instance { get; }
        public int Priority { get; }
        public bool IsCompleted { get; private set; }
        public Exception Error { get; private set; }
        public float StartTime { get; private set; }
        public float EndTime { get; private set; }
        public float Duration => IsCompleted ? EndTime - StartTime : 0;

        public InitializerInfo(Type type, IInitialize instance, int priority)
        {
            Type = type;
            Instance = instance;
            Priority = priority;
        }

        public void MarkStarted()
        {
            StartTime = Time.realtimeSinceStartup;
        }

        public void MarkCompleted()
        {
            IsCompleted = true;
            EndTime = Time.realtimeSinceStartup;
        }

        public void MarkFailed(Exception error)
        {
            IsCompleted = true;
            EndTime = Time.realtimeSinceStartup;
            Error = error;
        }
    }

    /// <summary>
    /// 在场景加载前自动执行初始化
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void OnBeforeSceneLoad()
    {
        InitializeAll();
    }

    /// <summary>
    /// 手动触发初始化（如果自动初始化失败）
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void OnAfterSceneLoad()
    {
        // 如果 BeforeSceneLoad 没有执行，尝试再次初始化
        if (!_isInitialized)
        {
            Debug.LogWarning("Bootstrapper: Initialization not completed in BeforeSceneLoad, trying again...");
            InitializeAll();
        }
    }

    /// <summary>
    /// 收集所有需要初始化的类
    /// </summary>
    private static void CollectInitializers()
    {
        State = BootstrapperState.Collecting;
        _initializers.Clear();

        try
        {
            // 获取当前程序集中的所有类型
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            
            // 过滤掉系统程序集以减少搜索范围
            var relevantAssemblies = assemblies.Where(assembly =>
                !assembly.FullName.StartsWith("System") &&
                !assembly.FullName.StartsWith("Unity") &&
                !assembly.FullName.StartsWith("Microsoft") &&
                !assembly.FullName.StartsWith("mscorlib") &&
                !assembly.FullName.StartsWith("netstandard"));

            foreach (var assembly in relevantAssemblies)
            {
                try
                {
                    Type[] types;
                    try
                    {
                        types = assembly.GetTypes();
                    }
                    catch (ReflectionTypeLoadException e)
                    {
                        types = e.Types.Where(t => t != null).ToArray();
                        Debug.LogWarning($"Bootstrapper: Could not load some types from assembly {assembly.FullName}");
                    }

                    foreach (var type in types)
                    {
                        // 检查类型是否实现了 IInitialize 接口
                        if (typeof(IInitialize).IsAssignableFrom(type) && 
                            !type.IsAbstract && 
                            !type.IsInterface &&
                            !type.IsGenericTypeDefinition)
                        {
                            // 检查是否有合适的构造函数
                            ConstructorInfo constructor = type.GetConstructor(
                                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, 
                                null, Type.EmptyTypes, null);

                            if (constructor != null)
                            {
                                try
                                {
                                    // 创建实例
                                    IInitialize instance = (IInitialize)Activator.CreateInstance(type, true);
                                    int priority = instance.InitializePriority;
                                    
                                    _initializers.Add(new InitializerInfo(type, instance, priority));
                                }
                                catch (Exception ex)
                                {
                                    Debug.LogError($"Bootstrapper: Failed to create instance of {type.FullName}: {ex.Message}");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Bootstrapper: Could not process assembly {assembly.FullName}: {ex.Message}");
                }
            }

            // 按优先级排序
            _initializers = _initializers
                .OrderBy(i => i.Priority)
                .ThenBy(i => i.Type.FullName)
                .ToList();

            Debug.Log($"Bootstrapper: Found {_initializers.Count} initializers to execute.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Bootstrapper: Error collecting initializers: {ex.Message}");
            State = BootstrapperState.Failed;
        }
    }

    /// <summary>
    /// 执行所有初始化
    /// </summary>
    public static void InitializeAll()
    {
        if (_isInitialized)
        {
            Debug.LogWarning("Bootstrapper: Already initialized. Skipping.");
            return;
        }

        try
        {
            // 收集需要初始化的类
            CollectInitializers();

            if (_initializers.Count == 0)
            {
                Debug.Log("Bootstrapper: No initializers found.");
                _isInitialized = true;
                State = BootstrapperState.Completed;
                return;
            }

            State = BootstrapperState.Initializing;
            Debug.Log($"Bootstrapper: Starting initialization of {_initializers.Count} initializers...");

            // 执行初始化
            foreach (var initializer in _initializers)
            {
                try
                {
                    initializer.MarkStarted();
                    
                    initializer.Instance.Initialize();
                    initializer.MarkCompleted();
                    
                }
                catch (Exception ex)
                {
                    initializer.MarkFailed(ex);
                    
                    // 根据需求决定是否继续执行其他初始化
                    // 这里选择继续执行，但你可以根据需要修改
                }
            }

            _isInitialized = true;
            State = BootstrapperState.Completed;
            
            // 输出总结
            int successCount = _initializers.Count(i => i.IsCompleted && i.Error == null);
            int failCount = _initializers.Count(i => i.Error != null);
            
            Debug.Log($"Bootstrapper: Initialization completed. Success: {successCount}, Failed: {failCount}, Total: {_initializers.Count}");
        }
        catch (Exception ex)
        {
            State = BootstrapperState.Failed;
            Debug.LogError($"Bootstrapper: Critical error during initialization: {ex.Message}\n{ex.StackTrace}");
            throw;
        }
    }

    /// <summary>
    /// 重置启动器状态（主要用于测试）
    /// </summary>
    public static void Reset()
    {
        _isInitialized = false;
        _initializers.Clear();
        State = BootstrapperState.NotStarted;
    }

    /// <summary>
    /// 手动注册一个初始化器
    /// </summary>
    public static void RegisterManual(IInitialize instance)
    {
        if (_isInitialized)
        {
            Debug.LogWarning("Bootstrapper: Already initialized. Cannot register new initializer.");
            return;
        }

        var info = new InitializerInfo(instance.GetType(), instance, instance.InitializePriority);
        _initializers.Add(info);
        
        // 重新排序
        _initializers = _initializers
            .OrderBy(i => i.Priority)
            .ThenBy(i => i.Type.FullName)
            .ToList();
    }
}