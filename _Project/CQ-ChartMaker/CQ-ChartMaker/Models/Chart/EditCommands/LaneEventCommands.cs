using System.Collections.Generic;

namespace CQ_ChartMaker.Models;

/// <summary> 添加事件 Unit 到混合列表 </summary>
public class AddLaneEventCommand : IUndoableCommand
{
    private readonly List<ILaneEventUnit> _list;
    private readonly ILaneEventUnit _unit;

    public AddLaneEventCommand(List<ILaneEventUnit> list, ILaneEventUnit unit)
    {
        _list = list;
        _unit = unit;
    }

    /// <summary> 此命令操作的事件 Unit </summary>
    public ILaneEventUnit Unit => _unit;

    public void Execute() => _list.Add(_unit);
    public void Undo()    => _list.Remove(_unit);
    public string Description => $"添加 {_unit.Kind} ({_unit.StartTime}ms)";
}

/// <summary> 从混合列表移除事件 Unit </summary>
public class RemoveLaneEventCommand : IUndoableCommand
{
    private readonly List<ILaneEventUnit> _list;
    private readonly ILaneEventUnit _unit;

    public RemoveLaneEventCommand(List<ILaneEventUnit> list, ILaneEventUnit unit)
    {
        _list = list;
        _unit = unit;
    }

    /// <summary> 此命令操作的事件 Unit </summary>
    public ILaneEventUnit Unit => _unit;

    public void Execute() => _list.Remove(_unit);
    public void Undo()    => _list.Add(_unit);
    public string Description => $"删除 {_unit.Kind} ({_unit.StartTime}ms)";
}

/// <summary>
/// 编辑事件 Unit 的 Data。撤销/重做只还原同一 Unit 的结构体快照，
/// 不重新创建 Unit，也不经过 ChartDefaults 工厂。
/// </summary>
public class EditLaneEventCommand : IUndoableCommand
{
    private readonly ILaneEventUnit _target;
    private readonly object _oldData;
    private readonly object _newData;

    public EditLaneEventCommand(ILaneEventUnit target, object oldData, object newData)
    {
        _target = target;
        _oldData = oldData;
        _newData = newData;
    }

    /// <summary> 此命令操作的事件 Unit </summary>
    public ILaneEventUnit Target => _target;

    public void Execute() => _target.RestoreData(_newData);
    public void Undo()    => _target.RestoreData(_oldData);
    public string Description => $"编辑 {_target.Kind} 属性";
}
