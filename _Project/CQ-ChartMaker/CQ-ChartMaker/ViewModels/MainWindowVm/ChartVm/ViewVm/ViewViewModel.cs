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
    /// 当前倍速的显示文字（倍速按钮内容）
    /// </summary>
    public string SpeedText => Timeline?.Speed switch
    {
        0.25 => "0.25x",
        0.5 => "0.5x",
        _ => "1.0x"
    };

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
                else if (e.PropertyName == nameof(TimelineModel.Speed))
                {
                    OnPropertyChanged(nameof(SpeedText));
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

    /// <summary> 切换播放倍速（参数为菜单项文字：1.0x / 0.5x / 0.25x） </summary>
    [RelayCommand]
    private void SetSpeed(string speedText)
    {
        if (Timeline == null) return;
        Timeline.Speed = speedText switch
        {
            "0.5x" => 0.5,
            "0.25x" => 0.25,
            _ => 1.0
        };
    }
    
    // 拖拽进度条前的播放状态：松手时依据它决定是否恢复播放
    private bool _resumeAfterDrag;

    /// <summary>
    /// 进度条按下（拖拽把手或点击轨道）时暂停播放：避免拖拽改位置与播放推进互相打架
    /// 同时记住按下前的播放状态，供松手时决定是否恢复
    /// </summary>
    public void PauseOnDrag()
    {
        _resumeAfterDrag = !(Timeline?.IsPaused ?? true);
        Timeline?.Pause();
    }

    /// <summary>
    /// 进度条松开时调用：若按下前处于播放中，则从拖拽后的位置继续播放；原本暂停则保持暂停
    /// </summary>
    public void ResumeAfterDrag()
    {
        if (!_resumeAfterDrag) return;
        _resumeAfterDrag = false;
        Timeline?.Resume();
    }
}