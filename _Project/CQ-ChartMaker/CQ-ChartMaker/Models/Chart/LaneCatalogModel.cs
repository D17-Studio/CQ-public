using System;
using System.Collections.Generic;

namespace CQ_ChartMaker.Models;

/// <summary>
/// 编辑器当前存在的轨道编号集合（纯会话状态，不写入谱面）。
/// 读入谱面时由“数据实际使用的轨道 ∪ 默认轨道 0~3”初始化；
/// 编辑过程中可新增/删除，删除只允许空轨道。
/// 显隐状态也保存在这里，跟随项目会话，不持久化到用户设置。
/// </summary>
public class LaneCatalogModel
{
    /// <summary> 永久存在的默认轨道（即使没有任何数据） </summary>
    public static readonly IReadOnlyList<int> DefaultLanes = new[] { 0, 1, 2, 3 };

    private readonly List<int> _lanes = new();
    private List<int> _visibleLanes = new();
    private readonly HashSet<int> _hiddenLanes = new();

    /// <summary> 全部存在轨道（升序）。只读使用，不要在外部修改 </summary>
    public List<int> Lanes => _lanes;

    /// <summary> 可见轨道（升序）。只读使用，不要在外部修改 </summary>
    public List<int> VisibleLanes => _visibleLanes;

    /// <summary> 轨道新增/删除后触发 </summary>
    public event Action? Changed;

    /// <summary> 显隐状态变化后触发 </summary>
    public event Action? VisibilityChanged;

    /// <summary> 是否为永久默认轨道（0~3，不可删除） </summary>
    public static bool IsDefaultLane(int lane) => lane >= 0 && lane <= 3;

    /// <summary> 轨道是否存在 </summary>
    public bool Contains(int lane) => _lanes.BinarySearch(lane) >= 0;

    /// <summary> 轨道是否存在且未被隐藏 </summary>
    public bool IsVisible(int lane) => Contains(lane) && !_hiddenLanes.Contains(lane);

    /// <summary> 批量补齐轨道（已存在的忽略），用于初始化与撤销后恢复 </summary>
    public void Ensure(IEnumerable<int> lanes)
    {
        bool added = false;
        foreach (var lane in lanes)
            added |= Insert(lane);

        if (!added) return;
        RebuildVisible();
        Changed?.Invoke();
    }

    /// <summary> 添加单条轨道（已存在则忽略） </summary>
    public void Add(int lane)
    {
        if (!Insert(lane)) return;
        RebuildVisible();
        Changed?.Invoke();
    }

    /// <summary> 删除轨道；同时清理它的显隐状态 </summary>
    public bool Remove(int lane)
    {
        if (!_lanes.Remove(lane)) return false;

        _hiddenLanes.Remove(lane);
        RebuildVisible();
        Changed?.Invoke();
        return true;
    }

    /// <summary> 设置轨道显隐（轨道不存在时无效果） </summary>
    public void SetVisible(int lane, bool visible)
    {
        if (!Contains(lane)) return;

        bool changed = visible ? _hiddenLanes.Remove(lane) : _hiddenLanes.Add(lane);
        if (!changed) return;

        RebuildVisible();
        VisibilityChanged?.Invoke();
    }

    /// <summary> 升序插入；已存在返回 false </summary>
    private bool Insert(int lane)
    {
        int index = _lanes.BinarySearch(lane);
        if (index >= 0) return false;

        _lanes.Insert(~index, lane);
        return true;
    }

    private void RebuildVisible()
    {
        // 每次变化都换新 List 实例：ItemsControl 绑定的是 List 本身，
        // 原地 Clear/Add 不会触发 ItemsSource 重新绑定，轨道框和标号不会刷新
        var visible = new List<int>(_lanes.Count);
        foreach (var lane in _lanes)
            if (!_hiddenLanes.Contains(lane))
                visible.Add(lane);

        _visibleLanes = visible;
    }
}
