using System.Collections.Generic;
using CQMusicGame.Shared;

namespace CQ_ChartMaker.Models;

/// <summary> 添加 BpmUnit 到列表 </summary>
public class AddBpmCommand : IUndoableCommand
{
    private readonly List<BpmUnit> _list;
    private readonly BpmUnit _bpm;

    public AddBpmCommand(List<BpmUnit> list, BpmUnit bpm)
    {
        _list = list;
        _bpm = bpm;
    }

    /// <summary> 此命令操作的 BpmUnit </summary>
    public BpmUnit Bpm => _bpm;

    public void Execute() => _list.Add(_bpm);
    public void Undo()    => _list.Remove(_bpm);
    public string Description => $"添加 BPM ({_bpm.Data.StartTime}ms)";
}

/// <summary> 从列表移除 BpmUnit </summary>
public class RemoveBpmCommand : IUndoableCommand
{
    private readonly List<BpmUnit> _list;
    private readonly BpmUnit _bpm;

    public RemoveBpmCommand(List<BpmUnit> list, BpmUnit bpm)
    {
        _list = list;
        _bpm = bpm;
    }

    /// <summary> 此命令操作的 BpmUnit </summary>
    public BpmUnit Bpm => _bpm;

    public void Execute() => _list.Remove(_bpm);
    public void Undo()    => _list.Add(_bpm);
    public string Description => $"删除 BPM ({_bpm.Data.StartTime}ms)";
}

/// <summary> 编辑 BpmUnit.Data 的任意属性，快照完整 Data 结构体 </summary>
public class EditBpmCommand : IUndoableCommand
{
    private readonly BpmUnit _target;
    private readonly BpmData _oldData;
    private readonly BpmData _newData;

    public EditBpmCommand(BpmUnit target, BpmData oldData, BpmData newData)
    {
        _target = target;
        _oldData = oldData;
        _newData = newData;
    }

    public void Execute() => _target.Data = _newData;
    public void Undo()    => _target.Data = _oldData;
    public string Description => "编辑 BPM 属性";
}
