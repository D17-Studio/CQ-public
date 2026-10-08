using System;
using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace CQ_ChartMaker.Models;

/// <summary>
/// 音频播放模块：封装 NAudio 常驻播放管线，提供单个 BGM 的加载与播放控制
/// 常驻管线：所有声音统一经过 MixingSampleProvider → 唯一的 WasapiPlayer 输出到声卡，
/// 创建后一直运行，播放 = 往混音器加输入
/// 本类不认识时间轴，播放/暂停由外部（ChartViewModel）根据时间轴状态驱动
/// </summary>
public class AudioModel : IDisposable
{
    // 常驻混音器与输出设备（构造时创建，全程复用；_mixer 为 null 表示管线不可用）
    private readonly MixingSampleProvider? _mixer;
    private IWavePlayer? _output;

    // 当前 BGM 在混音器中的输入（用于暂停时移除；null 表示没有在播的 BGM）
    private ISampleProvider? _bgmProvider;

    // 当前 BGM 链路中的变速插值器（仅播放中存在；切换倍速时改它的 Speed 属性）
    private SpeedSampleProvider? _speedProvider;

    // 当前播放倍速（重建链路时用它初始化新的插值器）
    private double _speed = 1.0;

    // 整首 BGM 缓存为设备格式（float32、设备采样率）的原始字节，播放时零解码零转换
    private byte[]? _bgmData;
    private WaveFormat? _bgmFormat;

    /// <summary> 是否已加载 BGM </summary>
    public bool HasBgm => _bgmData != null;

    /// <summary> BGM 总时长（毫秒），未加载时为 0 </summary>
    public int TotalDurationMs { get; private set; }

    /// <summary> BGM 自然播放结束时触发 </summary>
    public event Action? BgmEnded;

    public AudioModel()
    {
        try
        {
            // 查询默认播放设备的混音格式（MixFormat 即设备当前使用的采样率/声道数）
            using var device = new MMDeviceEnumerator()
                .GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            using var audioClient = device.CreateAudioClient();
            var mixFormat = audioClient.MixFormat;

            // 统一的目标格式：设备采样率 + 设备声道数 + float32
            // 音频数据与设备格式一致是启用 WASAPI 低延迟共享模式的前提
            var deviceFormat = WaveFormat.CreateIeeeFloatWaveFormat(mixFormat.SampleRate, mixFormat.Channels);

            // 混音器：所有输入（BGM、将来的音效）都加到这里
            // ReadFully = true 表示无输入时也持续输出静音，保证设备不断流
            _mixer = new MixingSampleProvider(deviceFormat) { ReadFully = true };
            _mixer.MixerInputEnded += OnMixerInputEnded;

            // 常驻输出设备：请求低延迟共享模式（不可用则自动回退到普通共享模式），
            // 并提升音频线程到 "Pro Audio" 优先级以减少卡顿
            _output = new WasapiPlayerBuilder()
                .WithLowLatency(false)
                .WithMmcssThreadPriority("Pro Audio")
                .Build();
            _output.Init(_mixer.ToWaveProvider());
            _output.Play();
        }
        catch
        {
            // 无音频设备等异常：整体降级为无音频模式（_mixer 为 null，所有方法变空操作）
            _mixer = null;
            _output = null;
        }
    }

    /// <summary>
    /// 加载 BGM 文件（WAV），解码并转换为设备格式后整首缓存，供后续播放
    /// </summary>
    /// <returns>是否加载成功（失败不抛异常，保持无音频状态）</returns>
    public bool LoadBgm(string path)
    {
        if (_mixer == null) return false;

        try
        {
            using var reader = new WaveFileReader(path);
            // 先转成 16bit PCM 采样流，再按需重采样到设备采样率（重采样不改变总时长）
            ISampleProvider samples = new Pcm16BitToSampleProvider(reader);
            if (samples.WaveFormat.SampleRate != _mixer.WaveFormat.SampleRate)
                samples = new WdlResamplingSampleProvider(samples, _mixer.WaveFormat.SampleRate);

            // 转回 WaveProvider 以便按字节整首读出（此时已是 float32 设备格式）
            var resampled = new SampleToWaveProvider(samples);
            using var buffer = new MemoryStream();
            var chunk = new byte[resampled.WaveFormat.AverageBytesPerSecond]; // 1 秒大小的中转缓冲
            int read;
            // NAudio 3 的读取接口是 Span 版本：每次尽量填满整个缓冲，返回实际读取的字节数
            while ((read = resampled.Read(chunk.AsSpan())) > 0)
                buffer.Write(chunk, 0, read);

            _bgmData = buffer.ToArray();
            _bgmFormat = resampled.WaveFormat;

            // 总时长直接用缓存数据计算：总字节数 ÷ 每秒字节数（与源文件时长严格一致）
            TotalDurationMs = (int)(_bgmData.Length * 1000L / _bgmFormat.AverageBytesPerSecond);
            return true;
        }
        catch
        {
            // 文件不存在、非 WAV、非 16bit 等情况：清空 BGM 状态但不影响时间轴使用
            _bgmData = null;
            _bgmFormat = null;
            TotalDurationMs = 0;
            return false;
        }
    }

    /// <summary>
    /// 从指定位置开始播放 BGM（内部重建播放链路并定位）
    /// </summary>
    public void PlayFrom(int timeMs)
    {
        if (_mixer == null || _bgmData == null || _bgmFormat == null) return;

        RemoveCurrentBgmInput();

        // 用缓存字节重建播放链路：内存原始字节流 → 采样流 → 变速插值器 → 混音器
        // 每次重建都是纯内存操作、成本极低，因此跳转/恢复统一走这条路，无需维护旧输入的位置
        var stream = new RawSourceWaveStream(new MemoryStream(_bgmData), _bgmFormat);
        // 手动定位到目标时间：字节位置 = 秒数 × 每秒字节数，并按 BlockAlign 对齐避免落在采样帧中间
        long bytePos = (long)(timeMs / 1000.0 * _bgmFormat.AverageBytesPerSecond);
        bytePos -= bytePos % _bgmFormat.BlockAlign;
        stream.Position = Math.Clamp(bytePos, 0, _bgmData.Length);

        // 采样流外面包一层变速插值器：实现倍速播放，且切换倍速无需重建链路
        _speedProvider = new SpeedSampleProvider(new WaveToSampleProvider(stream))
        {
            Speed = (float)_speed
        };
        _bgmProvider = _speedProvider;
        _mixer.AddMixerInput(_bgmProvider);
    }

    /// <summary>
    /// 暂停 BGM（移出混音器），位置由调用方（时间轴）记录，恢复时用 PlayFrom 重新定位
    /// </summary>
    public void Pause() => RemoveCurrentBgmInput();

    private void RemoveCurrentBgmInput()
    {
        if (_bgmProvider == null || _mixer == null) return;
        _mixer.RemoveMixerInput(_bgmProvider);
        _bgmProvider = null;
        _speedProvider = null; // 输入已移除，插值器一并废弃（恢复播放时按当前倍速重建）
    }

    /// <summary>
    /// 设置播放倍速（1.0 / 0.5 / 0.25）：直接改插值器属性即时生效（播放中/暂停中均可），不重建链路
    /// </summary>
    public void SetSpeed(double speed)
    {
        _speed = speed;
        if (_speedProvider != null)
            _speedProvider.Speed = (float)speed;
    }

    /// <summary> 混音器输入结束回调：仅当前 BGM 自然播完时对外通知 </summary>
    private void OnMixerInputEnded(object? sender, SampleProviderEventArgs e)
    {
        if (!ReferenceEquals(e.SampleProvider, _bgmProvider)) return;
        _bgmProvider = null; // 播完后混音器已自动移除该输入
        BgmEnded?.Invoke();
    }

    /// <summary> 释放音频资源：停止输出并关闭设备 </summary>
    public void Dispose()
    {
        Pause();
        _output?.Dispose();
        _output = null;
    }
}
