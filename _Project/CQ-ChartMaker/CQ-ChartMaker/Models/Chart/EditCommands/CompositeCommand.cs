using System.Collections.Generic;

namespace CQ_ChartMaker.Models;

/// <summary>
/// 复合命令：把多条命令打包成一次撤销单位（批量操作在撤销池中只占一条）
/// </summary>
public class CompositeCommand : IUndoableCommand
{
    private readonly List<IUndoableCommand> _commands;

    public CompositeCommand(List<IUndoableCommand> commands)
    {
        _commands = commands;
    }

    /// <summary> 子命令列表（撤销/重做后恢复选中时，需要从中提取涉及的 unit） </summary>
    public IReadOnlyList<IUndoableCommand> Commands => _commands;

    public void Execute()
    {
        foreach (var cmd in _commands)
            cmd.Execute();
    }

    public void Undo()
    {
        // 逆序撤销：后执行的先还原，是复合命令的通用安全约定
        for (int i = _commands.Count - 1; i >= 0; i--)
            _commands[i].Undo();
    }

    public string Description => $"批量操作（{_commands.Count} 项）";
}
