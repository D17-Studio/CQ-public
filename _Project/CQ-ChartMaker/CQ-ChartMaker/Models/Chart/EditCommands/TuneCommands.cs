using System.Collections.Generic;
using CQMusicGame.Shared;

namespace CQ_ChartMaker.Models;

/// <summary> 向父 NoteUnit.TuneList 添加 TuneUnit </summary>
public class AddTuneCommand : IUndoableCommand
{
    private readonly NoteUnit _parent;
    private readonly TuneUnit _tune;

    public AddTuneCommand(NoteUnit parent, TuneUnit tune)
    {
        _parent = parent;
        _tune = tune;
    }

    /// <summary> 此命令操作的 TuneUnit </summary>
    public TuneUnit Tune => _tune;

    public void Execute()
    {
        _parent.TuneList ??= [];
        _parent.TuneList.Add(_tune);
        _tune.Parent = _parent;
    }

    public void Undo() => _parent.TuneList.Remove(_tune);
    public string Description => "添加 Tune";
}

/// <summary> 从父 NoteUnit.TuneList 移除 TuneUnit </summary>
public class RemoveTuneCommand : IUndoableCommand
{
    private readonly NoteUnit _parent;
    private readonly TuneUnit _tune;

    public RemoveTuneCommand(NoteUnit parent, TuneUnit tune)
    {
        _parent = parent;
        _tune = tune;
    }

    /// <summary> 此命令操作的 TuneUnit </summary>
    public TuneUnit Tune => _tune;

    public void Execute() => _parent.TuneList.Remove(_tune);
    public void Undo()
    {
        _parent.TuneList.Add(_tune);
        _tune.Parent = _parent;
    }
    public string Description => "删除 Tune";
}

/// <summary> 编辑 TuneUnit.Data 的任意属性，快照完整 Data 结构体 </summary>
public class EditTuneCommand : IUndoableCommand
{
    private readonly TuneUnit _target;
    private readonly TuneData _oldData;
    private readonly TuneData _newData;

    public EditTuneCommand(TuneUnit target, TuneData oldData, TuneData newData)
    {
        _target = target;
        _oldData = oldData;
        _newData = newData;
    }

    public void Execute() => _target.Data = _newData;
    public void Undo()    => _target.Data = _oldData;
    public string Description => "编辑 Tune 属性";
}
