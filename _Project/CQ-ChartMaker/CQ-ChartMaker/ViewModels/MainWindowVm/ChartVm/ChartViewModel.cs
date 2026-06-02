using System;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CQ_ChartMaker.Models;
using CQ_ChartMaker.Models.Chart;

namespace CQ_ChartMaker.ViewModels;

public partial class ChartViewModel : ObservableObject
{
    //子viewmodel
    public ViewViewModel ViewVM { get; }
    public EditorViewModel EditorVM { get; }
    public InspectorViewModel InspectorVM { get; }
    
    //TimelineModel
    private readonly TimelineModel _timeline;

    private readonly Stopwatch _stopwatch = new();
    private long _lastElapsedMs;

    //ChartSheetModel
    private readonly ChartSheetModel _chartSheet;

    // 用户设置引用
    public UserSettingsModel UserSettingsModel { get; }

    /// <summary>
    /// 构造函数，传入谱面文本和用户设置
    /// </summary>
    public ChartViewModel(string chartText, UserSettingsModel userSettingsModel)
    {
        UserSettingsModel = userSettingsModel;
        _timeline = new TimelineModel();
        _chartSheet = new ChartSheetModel(chartText);

        ViewVM = new ViewViewModel
        {
            Timeline = _timeline
        };
        EditorVM = new EditorViewModel(_chartSheet, _timeline, userSettingsModel);
        InspectorVM = new InspectorViewModel();

        _stopwatch.Start();
    }

    // PPS 调节参数（倒数基准：默认 1/200=0.005，±0.0003 步进，17步下界/13步上界）
    private const double PpsInverseSensitivity = 0.0003;
    private const double PpsInverseLower = 0.0011;  // 对应 PPS ≈ 909
    private const double PpsInverseUpper = 0.0101;  // 对应 PPS ≈ 99

    public void TogglePlayPause() => _timeline.TogglePlayPause();
    public void Pause() => _timeline.Pause();

    public void AdjustPps(int sign)
    {
        double inverse = 1.0 / UserSettingsModel.PixelPerSecond;
        inverse -= sign * PpsInverseSensitivity;
        inverse = Math.Clamp(inverse, PpsInverseLower, PpsInverseUpper);
        UserSettingsModel.PixelPerSecond = 1.0 / inverse;
    }

    public void ScrollTime(int deltaMs)
    {
        if (!_timeline.IsPaused)
            _timeline.Pause();
        int newTime = _timeline.TimeMs + deltaMs;
        newTime = Math.Clamp(newTime, 0, _timeline.MaxTime);
        _timeline.SetTime(newTime);
    }

    /// <summary>
    /// 由 View 层 RequestAnimationFrame 每帧调用
    /// </summary>
    public void OnFrameUpdate()
    {
        long elapsedMs = _stopwatch.ElapsedMilliseconds;
        int deltaMs = (int)(elapsedMs - _lastElapsedMs);
        _lastElapsedMs = elapsedMs;

        deltaMs = Math.Min(deltaMs, 100); // 限制最大步长
        if (deltaMs > 0)
        {
            _timeline.Update(deltaMs);
        }
    }
    
    /// <summary>
    /// 序列化当前谱面数据为 chart.txt 文本
    /// </summary>
    public string SerializeChart() => _chartSheet.ChartSheet.Serialize();
}