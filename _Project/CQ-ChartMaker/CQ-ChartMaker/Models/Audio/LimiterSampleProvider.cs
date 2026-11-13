using System;
using NAudio.Wave;

namespace CQ_ChartMaker.Models;

/// <summary>
/// 总线限幅器（lookahead limiter）：把最终混音限制在 ceiling 以内，防止设备端硬削波。
/// 工作方式：
/// 1. 音频先进入一条长度 = 前视时间的延迟线，增益根据"刚读到的未来样本"计算，
///    因此增益下压发生在峰值到达输出之前，不会把波形削平；
/// 2. 用单调队列维护最近 前视+1 帧所需的最小增益，保证延迟线里的样本输出时一定不超上限；
/// 3. 增益瞬时下压、按约 100ms 指数释放，听感是整体短暂地"让一下"，不会持续抽动。
/// 该限幅器放在总音量之后、WasapiPlayer 之前，只在上游总和真要超过 0dBFS 时才动作。
/// </summary>
public class LimiterSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;

    // 输出上限（线性值，1.0 = 0dBFS）
    private readonly float _ceiling;

    // 释放系数：每帧增益向目标靠近的比例（指数释放）
    private readonly float _releaseCoef;

    // 延迟线（环形帧缓冲）：输出比输入晚 lookaheadFrames 帧
    private readonly float[] _delayBuffer;
    private readonly int _lookaheadFrames;
    private int _delayIndex;

    // 单调递增队列：保存最近 lookaheadFrames + 1 帧的"所需增益"，
    // 队首就是窗口内最小值 → 延迟样本输出时的增益上限
    private readonly float[] _gainQueue;
    private readonly long[] _gainQueueIndex;
    private readonly int _queueCapacity;
    private int _queueHead;
    private int _queueCount;
    private long _frameIndex;

    // 上游数据的中转缓冲（复用，避免每帧分配）
    private float[] _inputBuffer = Array.Empty<float>();

    // 当前施加的增益（1 = 不压）
    private float _gain = 1f;

    public WaveFormat WaveFormat => _source.WaveFormat;

    /// <summary>
    /// 创建总线限幅器
    /// </summary>
    /// <param name="source">上游样本（应已包含所有混音与音量处理）</param>
    /// <param name="ceiling">输出上限（线性值，1.0 = 0dBFS，0.944 ≈ −0.5dBFS）</param>
    /// <param name="lookaheadMs">前视时间（毫秒）：越大越平滑，但输出延迟越高</param>
    /// <param name="releaseMs">增益释放时间（毫秒）</param>
    public LimiterSampleProvider(ISampleProvider source, float ceiling,
        double lookaheadMs = 5, double releaseMs = 100)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;
        _ceiling = Math.Clamp(ceiling, 0.001f, 1f);

        int sampleRate = source.WaveFormat.SampleRate;
        _lookaheadFrames = Math.Max(1, (int)(sampleRate * lookaheadMs / 1000.0));
        _delayBuffer = new float[_lookaheadFrames * _channels];
        _queueCapacity = _lookaheadFrames + 2;
        _gainQueue = new float[_queueCapacity];
        _gainQueueIndex = new long[_queueCapacity];
        _releaseCoef = 1f - MathF.Exp(-1f / MathF.Max(1f, (float)(sampleRate * releaseMs / 1000.0)));
    }

    /// <summary>
    /// 读取并限幅；始终保持满缓冲输出（上游不足的部分按静音处理）
    /// </summary>
    public int Read(Span<float> buffer)
    {
        if (buffer.Length == 0) return 0;

        if (_inputBuffer.Length < buffer.Length)
            _inputBuffer = new float[buffer.Length];

        int read = _source.Read(_inputBuffer.AsSpan(0, buffer.Length));
        if (read < buffer.Length)
            _inputBuffer.AsSpan(read, buffer.Length - read).Clear();

        int frameCount = buffer.Length / _channels;
        for (int frame = 0; frame < frameCount; frame++)
        {
            int offset = frame * _channels;

            // 1) 未来样本的峰值 → 本帧所需增益
            float peak = 0f;
            for (int c = 0; c < _channels; c++)
                peak = MathF.Max(peak, MathF.Abs(_inputBuffer[offset + c]));
            float required = peak > _ceiling ? _ceiling / peak : 1f;

            // 2) 先淘汰窗口外的旧增益，再压入本帧（窗口 = 最近 前视+1 帧）
            EvictOldGains();
            PushGain(required);
            float windowMin = _gainQueue[_queueHead];

            // 3) 增益包络：需要更小就瞬时下压，否则向窗口最小值缓慢释放。
            //    增益始终 ≤ 窗口最小值，保证延迟样本输出时不超过上限
            if (windowMin < _gain)
                _gain = windowMin;
            else
                _gain += (windowMin - _gain) * _releaseCoef;

            // 4) 输出延迟线里的旧帧（乘当前增益），再把本帧写入延迟线
            int delayOffset = _delayIndex * _channels;
            for (int c = 0; c < _channels; c++)
            {
                buffer[offset + c] = _delayBuffer[delayOffset + c] * _gain;
                _delayBuffer[delayOffset + c] = _inputBuffer[offset + c];
            }
            _delayIndex = (_delayIndex + 1) % _lookaheadFrames;

            _frameIndex++;
        }

        // 尾部不足一帧的样本（正常不会出现，WASAPI 缓冲都是整帧的）：按当前增益直接输出
        for (int i = frameCount * _channels; i < buffer.Length; i++)
            buffer[i] = _inputBuffer[i] * _gain;

        return buffer.Length;
    }

    /// <summary> 单调递增队列压入：弹出队尾所有不小于新值的元素，队首始终最小 </summary>
    private void PushGain(float value)
    {
        while (_queueCount > 0)
        {
            int back = (_queueHead + _queueCount - 1) % _queueCapacity;
            if (_gainQueue[back] <= value) break;
            _queueCount--;
        }

        int index = (_queueHead + _queueCount) % _queueCapacity;
        _gainQueue[index] = value;
        _gainQueueIndex[index] = _frameIndex;
        _queueCount++;
    }

    /// <summary> 淘汰滑出窗口的旧增益（窗口下界 = 当前帧 − 前视帧数） </summary>
    private void EvictOldGains()
    {
        long oldest = _frameIndex - _lookaheadFrames;
        while (_queueCount > 0 && _gainQueueIndex[_queueHead] < oldest)
        {
            _queueHead = (_queueHead + 1) % _queueCapacity;
            _queueCount--;
        }
    }
}
