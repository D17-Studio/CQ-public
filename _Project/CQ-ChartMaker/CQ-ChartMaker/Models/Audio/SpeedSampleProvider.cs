using System;
using NAudio.Wave;

namespace CQ_ChartMaker.Models;

/// <summary>
/// 磁带式变速率采样器：以倍速为步长在源帧间线性插值，音高随速度降低（磁带效果）
/// 插入 BGM 链路：WaveToSampleProvider → SpeedSampleProvider → 混音器
/// 切换倍速直接改 Speed 属性，即时生效、无需重建链路（播放中/暂停中均可）
/// </summary>
public class SpeedSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;

    // 滑动窗口：线性插值需要相邻两帧（f0 与 f0+1）；两个缓冲交替承载
    private float[] _prevFrame;
    private float[] _nextFrame;
    private long _windowBaseIndex = -1;  // _prevFrame 对应的源帧索引（-1 = 尚未初始化）

    private double _framePosition;       // 当前在源中的小数帧位置：每输出一帧前进 Speed
    private bool _sourceEnded;           // 源已读完：后续 Read 一律返回 0，混音器据此触发输入结束

    /// <summary> 播放倍速（1.0 / 0.5 / 0.25），随时可改、即时生效 </summary>
    public float Speed { get; set; } = 1.0f;

    /// <summary> 累计已产出的源帧数（按各时刻倍速累计；当前由时间轴负责位置，保留备用） </summary>
    public double SourceFramesConsumed { get; private set; }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public SpeedSampleProvider(ISampleProvider source)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;
        _prevFrame = new float[_channels];
        _nextFrame = new float[_channels];
    }

    public int Read(Span<float> buffer)
    {
        if (_channels == 0 || _sourceEnded || buffer.Length < _channels) return 0;

        int totalFrames = buffer.Length / _channels;
        int framesWritten = 0;

        while (framesWritten < totalFrames)
        {
            long f0 = (long)_framePosition;      // 左侧源帧索引
            double t = _framePosition - f0;      // 帧间小数位置（插值权重）

            if (!EnsureWindow(f0))
            {
                _sourceEnded = true;             // 源读尽：结束输出（源尾部不足一帧的余量丢弃）
                break;
            }

            // 每个声道独立插值：结果 = 左帧样本 × (1-t) + 右帧样本 × t
            int outBase = framesWritten * _channels;
            for (int c = 0; c < _channels; c++)
                buffer[outBase + c] = (float)(_prevFrame[c] * (1 - t) + _nextFrame[c] * t);

            _framePosition += Speed;
            SourceFramesConsumed += Speed;
            framesWritten++;
        }

        return framesWritten * _channels;
    }

    /// <summary>
    /// 保证插值窗口持有源帧 f0 与 f0+1；源读尽返回 false
    /// </summary>
    private bool EnsureWindow(long f0)
    {
        // 首次调用：读入起始两帧（构造时源位置已由外部 seek 好，这里从 0 号帧算起）
        if (_windowBaseIndex < 0)
        {
            if (!TryReadFrame(_prevFrame)) return false;
            if (!TryReadFrame(_nextFrame)) return false;
            _windowBaseIndex = 0;
            return true;
        }

        // 播放中 f0 单调不减：窗口顺序前移——丢弃左帧，把右帧换到左边，再读入新的右帧
        while (_windowBaseIndex < f0)
        {
            (_prevFrame, _nextFrame) = (_nextFrame, _prevFrame);
            _windowBaseIndex++;
            if (!TryReadFrame(_nextFrame)) return false;
        }

        return true;
    }

    /// <summary> 从源读满一帧样本（含所有声道）到目标缓冲；读不足返回 false </summary>
    private bool TryReadFrame(float[] target)
    {
        int read = 0;
        while (read < _channels)
        {
            int n = _source.Read(target.AsSpan(read, _channels - read));
            if (n == 0) return false;
            read += n;
        }
        return true;
    }
}
