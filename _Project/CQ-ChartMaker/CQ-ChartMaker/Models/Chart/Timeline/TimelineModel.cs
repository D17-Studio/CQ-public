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

    // 时间轴最大时间（毫秒）：无音频时用默认值，导入音频后由 ChartViewModel 设为音频总时长
    // 用 ObservableProperty 以便变化时通知 UI（Slider 范围、时间文字随之刷新）
    [ObservableProperty]
    private int _maxTime = 180000;

    /// <summary> 播放倍速（1.0 / 0.5 / 0.25）：影响时间推进速度，音频侧由 ChartViewModel 同步 </summary>
    [ObservableProperty]
    private double _speed = 1.0;

    /// <summary> TimeMs 变化时触发（每帧变化时通知订阅者刷新） </summary>
    public event Action? TimeChanged;

    partial void OnTimeMsChanged(int value)
    {
        TimeChanged?.Invoke();
    }

    // 高精度时间累加器：倍速下 deltaMs × Speed 常带小数，若每帧取整后再累加会持续丢精度
    // （例如 0.5× 按 60fps 播放，一分钟可累积数百毫秒误差）
    private double _timeAccumulatorMs;

    /// <summary>
    /// 更新时间（通常由外部定时器每帧调用）
    /// </summary>
    /// <param name="deltaTimeMs">自上次更新以来经过的毫秒数</param>
    public void Update(int deltaTimeMs)
    {
        if (!IsPaused && deltaTimeMs > 0)
        {
            // 按倍速推进：先用 double 累加精确值，再取整输出给 TimeMs
            _timeAccumulatorMs += deltaTimeMs * Speed;
            TimeMs = (int)_timeAccumulatorMs;
            if (TimeMs >= MaxTime)
            {
                TimeMs = MaxTime;
                _timeAccumulatorMs = MaxTime;
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
        // 已播到头时不启动：否则会 seek 到音频末尾立即结束，表现为按了没反应
        if (TimeMs >= MaxTime) return;
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
        _timeAccumulatorMs = targetTimeMs; // 同步高精度累加器，避免下一帧按旧值把位置拉回去
        TimeMs = targetTimeMs;
    }
}