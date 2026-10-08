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
