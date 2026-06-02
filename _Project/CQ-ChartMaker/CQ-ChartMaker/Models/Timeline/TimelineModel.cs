using System;
using CommunityToolkit.Mvvm.ComponentModel;

// namespace CQ_ChartMaker.Models.Timeline; // 原命名空间
namespace CQ_ChartMaker.Models;

public partial class TimelineModel : ObservableObject
{
    [ObservableProperty]
    private int _timeMs;          // 当前时间（毫秒）

    [ObservableProperty]
    private bool _isPaused = true;       // 是否暂停

    public int MaxTime { get; set; } = 100000;

    /// <summary> TimeMs 变化时触发（每帧变化时通知订阅者刷新） </summary>
    public event Action? TimeChanged;

    partial void OnTimeMsChanged(int value)
    {
        TimeChanged?.Invoke();
    }

    /// <summary>
    /// 更新时间（通常由外部定时器每帧调用）
    /// </summary>
    /// <param name="deltaTimeMs">自上次更新以来经过的毫秒数</param>
    public void Update(int deltaTimeMs)
    {
        if (!IsPaused && deltaTimeMs > 0)
        {
            TimeMs += deltaTimeMs;
            if (TimeMs >= MaxTime)
            {
                TimeMs = MaxTime;
                Pause();
            }
        }
    }

    public void Pause()
    {
        IsPaused = true;
    }

    public void Resume()
    {
        IsPaused = false;
    }

    public void TogglePlayPause()
    {
        if (IsPaused)
            Resume();
        else
            Pause();
    }

    public void SetTime(int targetTimeMs)
    {
        if (targetTimeMs < 0) return;
        TimeMs = targetTimeMs;
    }
}