using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
// using CQ_ChartMaker.Models.Timeline; // 原命名空间
using CQ_ChartMaker.Models;
using System;

namespace CQ_ChartMaker.ViewModels;

/// <summary>
/// 视图面板的 ViewModel，负责显示时间轴信息并提供播放控制
/// </summary>
public partial class ViewViewModel : ObservableObject
{
    [ObservableProperty]
    private TimelineModel? _timeline;

    /// <summary>
    /// 当前时间（毫秒），可读可写，用于 Slider 双向绑定
    /// </summary>
    public int CurrentTimeMs
    {
        get => Timeline?.TimeMs ?? 0;
        set
        {
            if (Timeline != null && value != Timeline.TimeMs)
                Timeline.SetTime(value);
        }
    }

    /// <summary>
    /// 最大时间（毫秒），用于 Slider 的 Maximum
    /// </summary>
    public int MaxTime => Timeline?.MaxTime ?? 10000;

    /// <summary>
    /// 格式化的当前时间（mm:ss:fff）
    /// </summary>
    public string FormattedTime
    {
        get
        {
            int ms = CurrentTimeMs;
            int minutes = ms / 60000;
            int seconds = (ms / 1000) % 60;
            int millis = ms % 1000;
            return $"{minutes:D2}:{seconds:D2}:{millis:D3}";
        }
    }

    /// <summary>
    /// 格式化的最大时间（mm:ss:fff）
    /// </summary>
    public string MaxTimeFormatted
    {
        get
        {
            int ms = MaxTime;
            int minutes = ms / 60000;
            int seconds = (ms / 1000) % 60;
            int millis = ms % 1000;
            return $"{minutes:D2}:{seconds:D2}:{millis:D3}";
        }
    }

    /// <summary>
    /// 当前是否暂停（用于按钮图标）
    /// </summary>
    public bool IsPaused => Timeline?.IsPaused ?? true;

    partial void OnTimelineChanged(TimelineModel? value)
    {
        if (value != null)
        {
            value.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(TimelineModel.TimeMs))
                {
                    OnPropertyChanged(nameof(CurrentTimeMs));
                    OnPropertyChanged(nameof(FormattedTime));
                }
                else if (e.PropertyName == nameof(TimelineModel.IsPaused))
                {
                    OnPropertyChanged(nameof(IsPaused));
                }
                else if (e.PropertyName == nameof(TimelineModel.MaxTime))
                {
                    OnPropertyChanged(nameof(MaxTime));
                    OnPropertyChanged(nameof(MaxTimeFormatted));
                }
            };
        }
    }

    [RelayCommand]
    private void Pause() => Timeline?.Pause();

    [RelayCommand]
    private void Resume() => Timeline?.Resume();

    [RelayCommand]
    private void SetTime(int timeMs) => Timeline?.SetTime(timeMs);

    [RelayCommand]
    private void TogglePlayPause() => Timeline?.TogglePlayPause();
    
    public void PauseOnDrag()
    {
        Timeline?.Pause();
    }
}