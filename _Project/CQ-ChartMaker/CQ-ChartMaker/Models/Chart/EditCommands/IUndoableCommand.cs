namespace CQ_ChartMaker.Models;

/// <summary>
/// 可撤销命令接口，封装一次操作的 Execute / Undo
/// </summary>
public interface IUndoableCommand
{
    /// <summary> 执行操作 </summary>
    void Execute();

    /// <summary> 撤销操作，回到 Execute 之前的状态 </summary>
    void Undo();

    /// <summary> 操作描述，用于 UI 历史面板显示 </summary>
    string Description { get; }
}
