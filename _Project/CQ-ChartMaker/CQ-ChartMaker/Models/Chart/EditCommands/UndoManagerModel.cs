using System.Collections.Generic;
using System.Linq;

namespace CQ_ChartMaker.Models;

/// <summary>
/// 撤销/重做管理器，维护两个栈（撤销栈和重做栈）
/// </summary>
public class UndoManagerModel
{
    /// <summary> 最大撤销步数，超出后丢弃最旧的命令 </summary>
    private const int MaxSteps = 40;

    private readonly Stack<IUndoableCommand> _undoStack = new();
    private readonly Stack<IUndoableCommand> _redoStack = new();
    

    /// <summary> 记录一个已执行的操作，不调用 Execute，直接入栈 </summary>
    public void Record(IUndoableCommand cmd)
    {
        _undoStack.Push(cmd);
        TrimExcess();
        _redoStack.Clear();
    }

    /// <summary> 撤销最近一次操作，返回执行的命令（供调用方判断类型做选中逻辑） </summary>
    public IUndoableCommand? Undo()
    {
        if (_undoStack.TryPop(out var cmd))
        {
            cmd.Undo();
            _redoStack.Push(cmd);
            return cmd;
        }
        return null;
    }

    /// <summary> 重做最近一次撤销，返回执行的命令 </summary>
    public IUndoableCommand? Redo()
    {
        if (_redoStack.TryPop(out var cmd))
        {
            cmd.Execute();
            _undoStack.Push(cmd);
            return cmd;
        }
        return null;
    }

    /// <summary> 清空撤销/重做栈 </summary>
    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }

    /// <summary> 当撤销栈超出 MaxSteps 时，丢弃最旧的命令（栈底），保留最新的 MaxSteps 条 </summary>
    private void TrimExcess()
    {
        if (_undoStack.Count <= MaxSteps) return;

        // Stack.ToArray() 返回顺序为栈顶→栈底，保留前 MaxSteps 个（最新的）
        var kept = _undoStack.ToArray().Take(MaxSteps).Reverse();
        _undoStack.Clear();
        foreach (var cmd in kept)
            _undoStack.Push(cmd);
    }

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
}
