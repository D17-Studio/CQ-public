using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CQ_ChartMaker.Models;
using CQ_ChartMaker.Models.Chart;
using CQMusicGame.Shared;

namespace CQ_ChartMaker.ViewModels;

public partial class BeatsViewModel : ObservableObject
{
    public ChartSheetModel? ChartSheet { get; set; }
    public TimelineModel? Timeline { get; set; }
    public UserSettingsModel? UserSettings { get; set; }

    // === 测试功能：BPM输入框 ===
    [ObservableProperty]
    private string _bpmText = string.Empty;

    /// <summary>
    /// [测试] 切换到 Beats 标签页时调用，加载 BPM 0 的已有值
    /// </summary>
    public void LoadInitialBpm()
    {
        if (ChartSheet == null) return;

        var points = ChartSheet.ChartSheet.GetBpmPoints();
        for (int i = 0; i < points.Count; i++)
        {
            if (points[i].StartTime == 0)
            {
                BpmText = points[i].Bpm.ToString();
                return;
            }
        }
        BpmText = string.Empty;
    }

    /// <summary>
    /// [测试] 应用BPM值
    /// </summary>
    [RelayCommand]
    private void ApplyBpm()
    {
        if (ChartSheet == null) return;
        if (!float.TryParse(BpmText, out float bpmValue) || bpmValue <= 0) return;

        var points = ChartSheet.ChartSheet.GetBpmPoints();
        for (int i = 0; i < points.Count; i++)
        {
            if (points[i].StartTime == 0)
            {
                points[i] = new BpmPoint(0, bpmValue);
                return;
            }
        }

        points.Add(new BpmPoint(0, bpmValue));
    }
}
