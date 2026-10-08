using System.Collections.Generic;
using CQMusicGame.Shared;

namespace CQ_ChartMaker.Models;

/// <summary> 添加 MotionUnit 到列表 </summary>
public class AddMotionCommand : IUndoableCommand
{
    private readonly List<MotionUnit> _list;
    private readonly MotionUnit _motion;

    public AddMotionCommand(List<MotionUnit> list, MotionUnit motion)
    {
        _list = list;
        _motion = motion;
    }

    /// <summary> 此命令操作的 MotionUnit </summary>
    public MotionUnit Motion => _motion;

    public void Execute() => _list.Add(_motion);
    public void Undo()    => _list.Remove(_motion);
    public string Description => $"添加 LaneMotion ({_motion.Data.StartTime}ms)";
}

/// <summary> 从列表移除 MotionUnit </summary>
public class RemoveMotionCommand : IUndoableCommand
{
    private readonly List<MotionUnit> _list;
    private readonly MotionUnit _motion;

    public RemoveMotionCommand(List<MotionUnit> list, MotionUnit motion)
    {
        _list = list;
        _motion = motion;
    }

    /// <summary> 此命令操作的 MotionUnit </summary>
    public MotionUnit Motion => _motion;

    public void Execute() => _list.Remove(_motion);
    public void Undo()    => _list.Add(_motion);
    public string Description => $"删除 LaneMotion ({_motion.Data.StartTime}ms)";
}

/// <summary> 编辑 MotionUnit.Data 的任意属性，快照完整 Data 结构体 </summary>
public class EditMotionCommand : IUndoableCommand
{
    private readonly MotionUnit _target;
    private readonly LaneMotionData _oldData;
    private readonly LaneMotionData _newData;

    public EditMotionCommand(MotionUnit target, LaneMotionData oldData, LaneMotionData newData)
    {
        _target = target;
        _oldData = oldData;
        _newData = newData;
    }

    public void Execute() => _target.Data = _newData;
    public void Undo()    => _target.Data = _oldData;
    public string Description => "编辑 LaneMotion 属性";
}
