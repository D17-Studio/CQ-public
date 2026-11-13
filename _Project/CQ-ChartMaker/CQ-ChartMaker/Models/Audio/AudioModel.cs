using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace CQ_ChartMaker.Models;

/// <summary>
/// 音频播放模块：封装 NAudio 常驻播放管线，提供单个 BGM 的加载与播放控制
/// 常驻管线：BGM 与打击音效 → MixingSampleProvider → 总音量 → LimiterSampleProvider → 唯一的 WasapiPlayer。
/// 限幅器只是兜底：BGM 固定留 −2 dB 余量、打击音效载入时归一化到 −10 dBFS，
/// 正常叠加不会触发限幅，只有总输出真要超过 0 dBFS 时才短暂整体压一点，避免设备端硬削波
/// 本类不认识时间轴，播放/暂停由外部（ChartViewModel）根据时间轴状态驱动
/// </summary>
public class AudioModel : IDisposable
{
    // 常驻混音器与输出设备（构造时创建，全程复用；_mixer 为 null 表示管线不可用）
    private readonly MixingSampleProvider? _mixer;
    private readonly LimiterSampleProvider? _limiter;
    private IWavePlayer? _output;

    // 当前 BGM 在混音器中的输入（用于暂停时移除；null 表示没有在播的 BGM）
    // 该字段及 _speedProvider 只在 UI 线程读写（PlayFrom/Pause/结束处理）
    private ISampleProvider? _bgmProvider;

    // 音量链路：总音量包在混音器外（常驻，控制最终输出），
    // 音乐音量包在 BGM 链路内，打击音效音量包在每个音效输入内
    private VolumeSampleProvider? _masterVolumeProvider;
    private VolumeSampleProvider? _bgmVolumeProvider;
    private float _masterGain = 1f;
    private float _musicGain = 1f;
    private float _hitGain = 1f;

    // 打击音效静音开关：只拦截打击音效，BGM 链路完全不受影响
    private bool _hitMuted;

    // 创建本对象时捕获的同步上下文（通常是 Avalonia UI 线程）：
    // 混音器的输入结束回调在音频线程触发，必须 Post 回这里再改时间轴/UI
    private readonly SynchronizationContext? _syncContext;

    // 已释放标志：阻止 Post 迟到的结束回调在 Dispose 之后继续执行
    private bool _disposed;

    // 当前 BGM 链路中的变速插值器（仅播放中存在；切换倍速时改它的 Speed 属性）
    private SpeedSampleProvider? _speedProvider;

    // 当前播放倍速（重建链路时用它初始化新的插值器）
    private double _speed = 1.0;

    // 整首 BGM 缓存为设备格式（float32、设备采样率）的原始字节，播放时零解码零转换
    private byte[]? _bgmData;
    private WaveFormat? _bgmFormat;

    // 当前 BGM 路径：起始偏移变化时按同一文件重新解析（不保留未处理的原始音频缓存）
    private string? _bgmPath;

    // 打击音效缓存（同样是设备格式原始字节）：加载一次，每次触发复用
    private byte[]? _hitData;
    private WaveFormat? _hitFormat;

    // 打击音效最长保留时长：超过 5 秒的部分在加载时截断
    private const int HitSoundMaxDurationMs = 5000;

    // BGM 固定余量：−4.4 dB（0.6）。BGM 母带通常已经顶满 0 dBFS，
    // 留出空间给打击音效叠加，使总线限幅器很少触发 → 音量稳定、不随音效起伏
    private const float BgmHeadroom = 0.6f;

    // 打击音效载入时把峰值归一化到约 −4.4 dBFS（0.6），统一不同音效文件的响度；
    // 目标值越高音效越响，但叠加后越容易触发总线限幅
    private const float HitSoundTargetPeak = 0.6f;

    // 总线限幅上限：约 −0.5 dBFS（0.944），给浮点运算留一点余量，避免转换到设备格式时削波
    private const float LimiterCeiling = 0.944f;

    /// <summary> 是否已加载 BGM </summary>
    public bool HasBgm => _bgmData != null;

    /// <summary> 处理后 BGM 的总时长（毫秒），未加载时为 0 </summary>
    public int TotalDurationMs { get; private set; }

    /// <summary> 当前 BGM 的起始偏移（毫秒） </summary>
    public int MusicStartOffsetMs { get; private set; }

    /// <summary> 处理后音频不足 1ms（已替换为 1ms 空白音频） </summary>
    public bool IsAudioShorterThanOneMs { get; private set; }

    /// <summary> BGM 自然播放结束时触发（在创建本对象时的同步上下文上，通常是 UI 线程） </summary>
    public event Action? BgmEnded;

    public AudioModel()
    {
        // 必须在 UI 线程构造（当前由 ChartViewModel 在项目打开时创建），才能捕获到 UI 同步上下文
        _syncContext = SynchronizationContext.Current;

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

            // 总音量包在混音器外面：统一缩放 BGM 与所有打击音效的最终输出
            _masterVolumeProvider = new VolumeSampleProvider(_mixer) { Volume = _masterGain };

            // 总线限幅器：兜住 BGM + 音效叠加后的过冲，防止设备端硬削波。
            // 放在总音量之后，总音量调低时它基本不会触发
            _limiter = new LimiterSampleProvider(_masterVolumeProvider, LimiterCeiling);

            // 常驻输出设备：请求低延迟共享模式（不可用则自动回退到普通共享模式），
            // 并提升音频线程到 "Pro Audio" 优先级以减少卡顿
            _output = new WasapiPlayerBuilder()
                .WithLowLatency(false)
                .WithMmcssThreadPriority("Pro Audio")
                .Build();
            _output.Init(_limiter.ToWaveProvider());
            _output.Play();
        }
        catch
        {
            // 无音频设备等异常：整体降级为无音频模式（_mixer 为 null，所有方法变空操作）
            _mixer = null;
            _limiter = null;
            _output = null;
        }
    }

    /// <summary>
    /// 加载 BGM 文件（WAV），解码并转换为设备格式后整首缓存，供后续播放。
    /// 解码时直接应用起始偏移：正偏移在开头补静音，负偏移裁掉开头
    /// </summary>
    /// <returns>是否加载成功（失败不抛异常，保持无音频状态）</returns>
    public bool LoadBgm(string path)
    {
        if (_mixer == null) return false;

        // 记住路径：之后修改起始偏移时按同一文件重新解析
        _bgmPath = string.IsNullOrWhiteSpace(path) ? null : path;

        // BGM 不做归一化（targetPeak = 0），保持原始电平和整体余量策略
        if (!TryDecodeToDeviceFormat(path, 0, 0f, MusicStartOffsetMs, out var data, out var format))
        {
            // 文件不存在、非 WAV、不支持的编码/位深等情况：清空 BGM 状态但不影响时间轴使用
            _bgmData = null;
            _bgmFormat = null;
            TotalDurationMs = 0;
            IsAudioShorterThanOneMs = false;
            return false;
        }

        _bgmData = data;
        _bgmFormat = format;

        // 总时长直接用缓存数据计算：总字节数 ÷ 每秒字节数（此时数据已按偏移处理过）
        TotalDurationMs = (int)(_bgmData.Length * 1000L / _bgmFormat.AverageBytesPerSecond);

        // 处理后的音频不足 1ms（例如负偏移裁掉了几乎全部音频）：
        // 替换为 1ms 静音，保证时间轴至少能走 1ms；同时记录状态供界面提示
        if (TotalDurationMs < 1)
        {
            int minFrames = (int)Math.Ceiling(_bgmFormat.SampleRate / 1000.0);
            _bgmData = new byte[minFrames * _bgmFormat.BlockAlign];
            TotalDurationMs = (int)(_bgmData.Length * 1000L / _bgmFormat.AverageBytesPerSecond);
            IsAudioShorterThanOneMs = true;
        }
        else
        {
            IsAudioShorterThanOneMs = false;
        }

        return true;
    }

    /// <summary>
    /// 设置起始偏移（毫秒）并重新解析当前 BGM：
    /// 正偏移在音频开头补静音，负偏移裁掉开头；不保留原始音频数据，直接重新读取文件
    /// </summary>
    public void SetMusicStartOffset(int offsetMs)
    {
        if (MusicStartOffsetMs == offsetMs) return;
        MusicStartOffsetMs = offsetMs;

        if (!string.IsNullOrWhiteSpace(_bgmPath))
            LoadBgm(_bgmPath);
    }

    /// <summary>
    /// 加载打击音效（WAV）：解码为设备格式并缓存，超过 5 秒的部分在加载时截断。
    /// 路径为空或加载失败时清空音效（静音）；不抛异常
    /// </summary>
    /// <returns>是否加载成功</returns>
    public bool LoadHitSound(string path)
    {
        if (_mixer == null || string.IsNullOrWhiteSpace(path))
        {
            _hitData = null;
            _hitFormat = null;
            return false;
        }

        // 音效归一化到固定峰值：不同来源的音效文件响度差异很大，统一后再叠加
        if (!TryDecodeToDeviceFormat(path, HitSoundMaxDurationMs, HitSoundTargetPeak, 0,
                out var data, out var format))
        {
            _hitData = null;
            _hitFormat = null;
            return false;
        }

        _hitData = data;
        _hitFormat = format;
        return true;
    }

    /// <summary>
    /// 播放一次打击音效：每次触发都新建一路独立的混音输入，
    /// 多押时多个音效同时输出、自然叠加；播放结束由混音器自动移除该输入
    /// </summary>
    public void PlayHitSound()
    {
        if (_hitMuted || _mixer == null || _hitData == null || _hitFormat == null) return;

        var stream = new RawSourceWaveStream(new MemoryStream(_hitData), _hitFormat);
        // 每个音效输入自带音量缩放，多押叠加时也按同一增益输出
        var volumeProvider = new VolumeSampleProvider(new WaveToSampleProvider(stream))
        {
            Volume = _hitGain
        };
        _mixer.AddMixerInput(volumeProvider);
    }

    /// <summary>
    /// 把 WAV 文件解码并转换为设备格式（float32、设备采样率、设备声道数）的字节缓存。
    /// maxDurationMs &gt; 0 时只读取前 maxDurationMs 毫秒（用于限制打击音效长度）；
    /// targetPeak &gt; 0 时把数据峰值归一化到该值（用于统一打击音效响度）；
    /// musicStartOffsetMs &gt; 0 时在开头补静音，&lt; 0 时裁掉开头（仅 BGM 使用，打击音效传 0）。
    /// 失败不抛异常，返回 false
    /// </summary>
    private bool TryDecodeToDeviceFormat(string path, int maxDurationMs, float targetPeak,
        int musicStartOffsetMs, out byte[] data, out WaveFormat format)
    {
        data = Array.Empty<byte>();
        var mixer = _mixer;
        if (mixer == null)
        {
            format = null!;
            return false;
        }
        format = mixer.WaveFormat;

        try
        {
            using var reader = new WaveFileReader(path);
            // 按文件真实格式选择采样转换器：PCM 8/16/24/32 位和 32 位 IEEE float 都支持，
            // 也兼容 WAVE_FORMAT_EXTENSIBLE（DAW 导出的 24 位 WAV 常用这种封装）
            // 不能写死 Pcm16BitToSampleProvider：它不检查源位深、固定按每样本 2 字节切分，
            // 24 位数据会被错切成无效波形，样本数还会变成 1.5 倍导致时长错误
            ISampleProvider samples = reader.ToSampleProvider();
            // 再按需重采样到设备采样率（重采样不改变总时长）
            if (samples.WaveFormat.SampleRate != mixer.WaveFormat.SampleRate)
                samples = new WdlResamplingSampleProvider(samples, mixer.WaveFormat.SampleRate);

            // 声道数也必须和设备一致：MixingSampleProvider 要求所有输入格式完全相同，
            // 单声道音效加进立体声设备会在 AddMixerInput 抛异常。
            // 单/立体声互转用专门转换器（立体声转单声道会做左右平均），
            // 其余声道数用通道映射按需循环补齐/截取
            int targetChannels = mixer.WaveFormat.Channels;
            if (samples.WaveFormat.Channels != targetChannels)
            {
                if (samples.WaveFormat.Channels == 1 && targetChannels == 2)
                    samples = new MonoToStereoSampleProvider(samples);
                else if (samples.WaveFormat.Channels == 2 && targetChannels == 1)
                    samples = new StereoToMonoSampleProvider(samples);
                else
                    samples = new MultiplexingSampleProvider(new[] { samples }, targetChannels);
            }

            // 转回 WaveProvider 以便按字节整段读出（此时已是 float32 设备格式）
            var resampled = new SampleToWaveProvider(samples);
            format = resampled.WaveFormat;

            // 起始偏移直接作用在解码后的音频数据上（按设备格式字节计算，并按 BlockAlign 对齐）：
            // 正偏移在开头补静音，负偏移从开头裁掉对应时长
            long padBytes = 0;
            long skipBytes = 0;
            if (musicStartOffsetMs > 0)
            {
                padBytes = (long)musicStartOffsetMs * format.AverageBytesPerSecond / 1000;
                padBytes -= padBytes % format.BlockAlign;
            }
            else if (musicStartOffsetMs < 0)
            {
                skipBytes = (long)(-musicStartOffsetMs) * format.AverageBytesPerSecond / 1000;
                skipBytes -= skipBytes % format.BlockAlign;
            }

            // 需要限长时换算成字节上限，并按 BlockAlign 向下对齐，避免截断在半个采样帧上
            long maxBytes = long.MaxValue;
            if (maxDurationMs > 0)
            {
                maxBytes = (long)resampled.WaveFormat.AverageBytesPerSecond * maxDurationMs / 1000;
                maxBytes -= maxBytes % resampled.WaveFormat.BlockAlign;
            }

            using var buffer = new MemoryStream();

            // 正偏移：先把开头要补的静音长度撑出来（MemoryStream 扩展区域自动为 0）
            if (padBytes > 0)
            {
                buffer.SetLength(padBytes);
                buffer.Position = padBytes;
            }

            var chunk = new byte[resampled.WaveFormat.AverageBytesPerSecond]; // 1 秒大小的中转缓冲
            int read;
            long skipRemaining = skipBytes;
            // NAudio 3 的读取接口是 Span 版本：每次尽量填满整个缓冲，返回实际读取的字节数
            while (buffer.Length < maxBytes && (read = resampled.Read(chunk.AsSpan())) > 0)
            {
                int start = 0;
                // 负偏移：先丢弃开头对应时长的解码数据
                if (skipRemaining > 0)
                {
                    int skipNow = (int)Math.Min(skipRemaining, read);
                    skipRemaining -= skipNow;
                    start = skipNow;
                }

                int count = read - start;
                if (count <= 0) continue;

                // 最后一块可能超出上限：只写入需要的那部分，保证总长不超过 maxBytes
                long remaining = maxBytes - buffer.Length;
                buffer.Write(chunk, start, (int)Math.Min(count, remaining));
            }

            data = buffer.ToArray();

            // 需要统一响度时按峰值等比缩放（设备格式固定是 IEEE float，可按 float 解释字节）
            if (targetPeak > 0f)
                NormalizePeak(data, targetPeak);

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 把 float32 样本数据的峰值等比缩放到 targetPeak，用于统一不同音效文件的响度。
    /// 设备格式固定为 IEEE float，所以可以直接把字节按 float 解释
    /// </summary>
    private static void NormalizePeak(byte[] data, float targetPeak)
    {
        var samples = MemoryMarshal.Cast<byte, float>(data.AsSpan());
        float peak = 0f;
        for (int i = 0; i < samples.Length; i++)
            peak = MathF.Max(peak, MathF.Abs(samples[i]));
        if (peak <= 1e-6f) return; // 全静音：不做处理

        float scale = targetPeak / peak;
        for (int i = 0; i < samples.Length; i++)
            samples[i] *= scale;
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
        // 音乐音量 × 固定余量：余量只作用于 BGM，给打击音效留出叠加空间
        _bgmVolumeProvider = new VolumeSampleProvider(_speedProvider) { Volume = _musicGain * BgmHeadroom };
        _bgmProvider = _bgmVolumeProvider;
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
        _bgmVolumeProvider = null;
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

    /// <summary> 设置总音量（0~100）：即时缩放 BGM 与打击音效的最终输出 </summary>
    public void SetMasterVolume(int volume)
    {
        _masterGain = ToGain(volume);
        if (_masterVolumeProvider != null)
            _masterVolumeProvider.Volume = _masterGain;
    }

    /// <summary> 设置音乐（BGM）音量（0~100）：即时生效，不影响打击音效 </summary>
    public void SetMusicVolume(int volume)
    {
        _musicGain = ToGain(volume);
        if (_bgmVolumeProvider != null)
            _bgmVolumeProvider.Volume = _musicGain * BgmHeadroom;
    }

    /// <summary> 设置打击音效音量（0~100）：对之后触发的每个音效生效 </summary>
    public void SetHitSoundVolume(int volume) => _hitGain = ToGain(volume);

    /// <summary> 设置打击音效是否静音：静音时直接跳过音效播放，不影响 BGM </summary>
    public void SetHitSoundMuted(bool muted) => _hitMuted = muted;

    /// <summary> 0~100 的界面音量 → 0~1 的线性增益 </summary>
    private static float ToGain(int volume) => Math.Clamp(volume, 0, 100) / 100f;

    /// <summary>
    /// 混音器输入结束回调：仅当前 BGM 自然播完时对外通知。
    /// 该回调在 NAudio 音频播放线程上、混音器 Read 内部同步触发，不能在这里访问时间轴/UI；
    /// 这里只把结束的输入引用 Post 回 UI 线程，引用判定与状态清理统一在 UI 线程完成，
    /// 既避免非 UI 线程操作 Avalonia 控件，也避免与 PlayFrom/Pause 并发修改 _bgmProvider
    /// </summary>
    private void OnMixerInputEnded(object? sender, SampleProviderEventArgs e)
    {
        var ended = e.SampleProvider;
        if (_syncContext != null)
            _syncContext.Post(_ => HandleBgmEnded(ended), null);
        else
            HandleBgmEnded(ended); // 无同步上下文（非 UI 环境）时退化为同步通知
    }

    /// <summary> 在 UI 线程处理 BGM 自然结束；期间已暂停/重新播放时丢弃这个迟到事件 </summary>
    private void HandleBgmEnded(ISampleProvider ended)
    {
        if (_disposed || !ReferenceEquals(ended, _bgmProvider)) return;
        _bgmProvider = null; // 播完后混音器已自动移除该输入
        _speedProvider = null;
        BgmEnded?.Invoke();
    }

    /// <summary> 释放音频资源：停止输出并关闭设备 </summary>
    public void Dispose()
    {
        _disposed = true;
        BgmEnded = null; // 丢弃订阅，避免 Post 迟到的结束回调触达已销毁的 ViewModel
        Pause();
        _output?.Dispose();
        _output = null;
    }
}
