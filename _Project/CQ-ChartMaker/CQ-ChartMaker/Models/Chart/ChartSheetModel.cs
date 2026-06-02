using CQMusicGame.Shared;

namespace CQ_ChartMaker.Models.Chart;

public class ChartSheetModel
{
    public ChartSheet ChartSheet { get; }

    public ChartSheetModel(string chartText)
    {
        ChartSheet = new ChartSheet(chartText);
    }
}
