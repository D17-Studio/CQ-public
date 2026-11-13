using System.Collections.Generic;
using CQMusicGame.Shared;

namespace CQ_ChartMaker.Models;

/// <summary>
/// ChartSheetModel 中 NoteData 的可编辑包装，持有引用以便选中和修改
/// </summary>
public class NoteUnit
{
    public NoteData Data;
    public bool IsSelected;
    public List<TuneUnit>? TuneList;
}

// 判定轨相关扩展随 Shared 的 JudgeLaneIndex 字段一起删除：
// 新模型只有单一 LaneIndex，判定区 JudgeZone 由 ChartSheet 在运行时派生，制谱器不读写。
