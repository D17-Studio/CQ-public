using CQMusicGame.Shared;

namespace CQ_ChartMaker.Models;

/// <summary>
/// ChartSheetModel 中 TuneData 的可编辑包装，持有 Parent 引用以便双向索引
/// </summary>
public class TuneUnit
{
    public NoteUnit Parent;
    public TuneData Data;
    public bool IsSelected;
}
