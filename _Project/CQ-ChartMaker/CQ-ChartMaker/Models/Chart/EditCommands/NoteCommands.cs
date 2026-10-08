using System.Collections.Generic;
using CQMusicGame.Shared;

namespace CQ_ChartMaker.Models;

/// <summary> 添加 NoteUnit 到列表 </summary>
public class AddNoteCommand : IUndoableCommand
{
    private readonly List<NoteUnit> _list;
    private readonly NoteUnit _note;

    public AddNoteCommand(List<NoteUnit> list, NoteUnit note)
    {
        _list = list;
        _note = note;
    }

    /// <summary> 此命令操作的 NoteUnit </summary>
    public NoteUnit Note => _note;

    public void Execute() => _list.Add(_note);
    public void Undo()    => _list.Remove(_note);
    public string Description => $"添加 Note ({_note.Data.TargetTime:F2}s)";
}

/// <summary> 从列表移除 NoteUnit </summary>
public class RemoveNoteCommand : IUndoableCommand
{
    private readonly List<NoteUnit> _list;
    private readonly NoteUnit _note;

    public RemoveNoteCommand(List<NoteUnit> list, NoteUnit note)
    {
        _list = list;
        _note = note;
    }

    /// <summary> 此命令操作的 NoteUnit </summary>
    public NoteUnit Note => _note;

    public void Execute() => _list.Remove(_note);
    public void Undo()    => _list.Add(_note);
    public string Description => $"删除 Note ({_note.Data.TargetTime:F2}s)";
}

/// <summary> 编辑 NoteUnit.Data 的任意属性，快照完整 Data 结构体 </summary>
public class EditNoteCommand : IUndoableCommand
{
    private readonly NoteUnit _target;
    private readonly NoteData _oldData;
    private readonly NoteData _newData;

    public EditNoteCommand(NoteUnit target, NoteData oldData, NoteData newData)
    {
        _target = target;
        _oldData = oldData;
        _newData = newData;
    }

    public void Execute() => _target.Data = _newData;
    public void Undo()    => _target.Data = _oldData;
    public string Description => "编辑 Note 属性";
}
