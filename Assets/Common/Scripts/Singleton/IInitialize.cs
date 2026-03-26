using System;

/// <summary>
/// 需要自动初始化的接口
/// </summary>
public interface IInitialize
{
    /// <summary>
    /// 初始化优先级，数字越小越先执行
    /// </summary>
    int InitializePriority { get; }
    
    /// <summary>
    /// 初始化方法
    /// </summary>
    void Initialize();
}