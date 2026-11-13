using System;
using UnityEngine;

/// <summary>
/// 音乐播放服务：单一播放通道，统一管理歌曲与预览的播放、暂停、停止和进度查询。
/// 同一时刻只允许一个音乐事件，<see cref="Play"/> / <see cref="PlayExternal"/> 会先停止当前播放。
/// </summary>
public class MusicService
{
    // 停止淡出时长（ms）：切歌 / 关闭试听等场景交叉淡化
    private const int StopFadeMs = 500;

    // PreviewStart/End 缺省（0/0）时的预览窗口长度
    private const int DefaultPreviewLengthMs = 15000;

    // 预览切换 / 循环接缝的淡入淡出时长
    private const int PreviewFadeMs = 200;

    // 本版本 Wwise 枚举未暴露该 flag；用于启用外部源的 GetSourcePlayPosition
    private const uint EnableGetSourcePlayPosition = 0x100000;

    private readonly AK.Wwise.Bank _musicBank;
    private readonly AK.Wwise.Event _externalMusicEvent;
    private readonly AkSegmentInfo _segmentInfo = new();

    private GameObject _emitter;
    private uint _playingId = AkUnitySoundEngine.AK_INVALID_PLAYING_ID;
    // 正在淡出的上一首：下一次 Stop 时直接切掉，避免快速切歌叠加多首
    private uint _fadingPlayingId = AkUnitySoundEngine.AK_INVALID_PLAYING_ID;
    private Action _onEnded;
    private bool _bankLoaded;
    private bool _isPaused;

    // 当前通道是否为外部源；外部源的位置与时长来自 TrackAudio
    private bool _usingExternal;
    private ExternalTrackAudio _externalAudio;

    // 正 StartOffset：延迟开播（常驻 ticker 驱动）
    private ExternalTrackAudio _pendingExternalAudio;
    private Action _pendingOnEnded;
    private int _pendingSeekMs;
    private float _pendingPostTime = -1f;
    // 延迟开播期间被暂停：记录剩余时长，恢复时重新排期
    private bool _pendingPaused;
    private int _pendingRemainingMs;

    // 正 StartOffset 开头计时：静音段墙钟 + 起播后校准交接
    private float _externalGapStartTime = -1f;
    private int _externalGapOffsetMs;
    private bool _externalTimelineCalibrated;
    private int _externalTimelineBiasMs;
    private int _lastExternalPositionMs;
    private float _lastExternalPositionTime;
    private float _externalGapPausedAt = -1f;

    // 预览循环：播放位置到达终点后 Seek 回起点
    private bool _previewLoopActive;
    private uint _previewLoopPlayingId;
    private uint _previewLoopEventId;
    private int _previewLoopSeekMs;
    private int _previewLoopEndMs;
    private bool _previewLoopUsesSourcePosition;
    private GameObject _previewLoopEmitter;
    private bool _previewLoopTransitionActive;

    // 预览通道：独立 emitter + 音量渐变（时长由 PreviewFadeMs 控制）
    private GameObject _previewEmitter;
    private bool _previewActive;
    private TrackData _pendingPreviewTrack;
    private bool _previewFadeActive;
    private float _previewVolume = 1f;
    private float _previewFadeFrom;
    private float _previewFadeTo;
    private float _previewFadeStartTime;
    private int _previewFadeDurationMs;
    private Action _previewFadeOnComplete;

    public MusicService(AK.Wwise.Bank musicBank, AK.Wwise.Event externalMusicEvent)
    {
        _musicBank = musicBank;
        _externalMusicEvent = externalMusicEvent;
    }

    /// <summary>幂等加载 Music 音库；音库常驻，不随场景卸载。</summary>
    public void EnsureBankLoaded()
    {
        if (_bankLoaded) return;

        if (_musicBank == null || !_musicBank.IsValid())
        {
            Debug.LogError("[MusicService] 未配置有效的 Music 音库");
            return;
        }

        _musicBank.Load();
        _bankLoaded = true;
    }

    /// <summary>播放音乐（歌曲或预览），先停止当前正在播放的内容。</summary>
    /// <param name="evt">Wwise 播放事件</param>
    /// <param name="onEnded">自然播放结束回调；主动 Stop / 换曲不触发</param>
    public void Play(AK.Wwise.Event evt, Action onEnded = null)
    {
        if (evt == null || !evt.IsValid())
        {
            Debug.LogError("[MusicService] Play 收到无效事件");
            return;
        }

        Stop();
        EnsureBankLoaded();

        _onEnded = onEnded;

        // 预览循环需要段落位置，因此始终带上位置 / 曲终回调
        _playingId = AkUnitySoundEngine.PostEvent(
            evt.Id,
            Emitter,
            (uint)(AkCallbackType.AK_EnableGetMusicPlayPosition | AkCallbackType.AK_EndOfEvent),
            OnMusicCallback,
            null);

        if (_playingId == AkUnitySoundEngine.AK_INVALID_PLAYING_ID)
        {
            _onEnded = null;
            Debug.LogError($"[MusicService] 播放失败：{evt.Name}");
        }
    }

    /// <summary>播放自制谱外部源；正 StartOffset 延迟开播，负偏移 Seek 裁头。</summary>
    /// <param name="onEnded">自然播放结束回调；主动 Stop / 换曲不触发</param>
    public void PlayExternal(ExternalTrackAudio audio, Action onEnded = null)
    {
        if (audio == null || string.IsNullOrEmpty(audio.FilePath))
        {
            Debug.LogError("[MusicService] PlayExternal 收到无效音频");
            return;
        }

        if (!ValidateExternalEvent()) return;

        Stop();
        EnsureBankLoaded();
        _ = Emitter; // 延迟开播依赖常驻 ticker

        int delayMs = Math.Max(0, audio.StartOffsetMs);
        int seekMs = Math.Max(0, -audio.StartOffsetMs);

        if (delayMs > 0)
        {
            _usingExternal = true;
            _externalAudio = audio;
            _externalGapStartTime = Time.realtimeSinceStartup;
            _externalGapOffsetMs = delayMs;
            _externalTimelineCalibrated = false;
            _externalTimelineBiasMs = 0;
            _pendingExternalAudio = audio;
            _pendingOnEnded = onEnded;
            _pendingSeekMs = seekMs;
            _pendingPostTime = _externalGapStartTime + delayMs / 1000f;
            return;
        }

        _externalTimelineCalibrated = true;
        _lastExternalPositionTime = Time.realtimeSinceStartup;
        StartExternalPost(audio, seekMs, onEnded, Emitter);
    }

    /// <summary>统一预览：切换时先淡出旧曲，再淡入并循环新曲。</summary>
    public void PlayPreview(TrackData track)
    {
        if (track?.Audio == null)
        {
            Stop();
            return;
        }

        if (_previewActive)
        {
            _pendingPreviewTrack = track;
            CancelPreviewFade();
            CancelPreviewLoop();
            StartPreviewFade(0f, CompletePreviewSwitch);
            return;
        }

        StartPreviewNow(track);
    }

    // 切换预览：旧曲淡出完成后启动最新选中的曲目
    private void CompletePreviewSwitch()
    {
        StopPreviewPlayback();
        _previewActive = false;

        var track = _pendingPreviewTrack;
        _pendingPreviewTrack = null;
        if (track != null) StartPreviewNow(track);
    }

    private void StartPreviewNow(TrackData track)
    {
        _ = Emitter; // 预览同样依赖常驻 ticker 驱动渐变与循环
        _ = PreviewEmitter;
        EnsureBankLoaded();

        StopPreviewPlayback();
        _previewActive = true;
        _pendingPreviewTrack = null;
        SetPreviewVolume(0f);

        int previewStart = Math.Max(0, track.PreviewStartMs);
        int previewEnd = track.PreviewEndMs > previewStart
            ? track.PreviewEndMs
            : previewStart + DefaultPreviewLengthMs;

        switch (track.Audio)
        {
            case WwiseTrackAudio wwise:
            {
                _usingExternal = false;
                _externalAudio = null;
                _onEnded = null;
                _playingId = AkUnitySoundEngine.PostEvent(
                    wwise.Config.PlayMusic.Id,
                    PreviewEmitter,
                    (uint)(AkCallbackType.AK_EnableGetMusicPlayPosition | AkCallbackType.AK_EndOfEvent),
                    OnMusicCallback,
                    null);

                if (_playingId == AkUnitySoundEngine.AK_INVALID_PLAYING_ID)
                {
                    Debug.LogError($"[MusicService] 预览播放失败：{wwise.Config.PlayMusic.Name}");
                    EndPreview();
                    return;
                }

                if (previewStart > 0)
                {
                    AkUnitySoundEngine.SeekOnEvent(
                        wwise.Config.PlayMusic.Id, PreviewEmitter, previewStart, false, _playingId);
                }

                StartPreviewLoop(wwise.Config.PlayMusic.Id, previewStart, previewEnd, false, PreviewEmitter);
                break;
            }
            case ExternalTrackAudio external:
            {
                if (!ValidateExternalEvent())
                {
                    EndPreview();
                    return;
                }

                // 预览按文件时间播放，不带 StartOffset 的头部静音
                int fileStart = Math.Max(0, previewStart - external.StartOffsetMs);
                int fileEnd = fileStart + (previewEnd - previewStart);
                if (external.DurationMs > 0) fileEnd = Math.Min(fileEnd, external.DurationMs);
                if (fileEnd <= fileStart)
                {
                    EndPreview();
                    return;
                }

                _externalTimelineCalibrated = true;
                _lastExternalPositionTime = Time.realtimeSinceStartup;
                StartExternalPost(external, fileStart, null, PreviewEmitter);
                if (_playingId == AkUnitySoundEngine.AK_INVALID_PLAYING_ID)
                {
                    EndPreview();
                    return;
                }

                StartPreviewLoop(_externalMusicEvent.Id, fileStart, fileEnd, true, PreviewEmitter);
                break;
            }
            default:
                EndPreview();
                return;
        }

        StartPreviewFade(1f, null);
    }

    // 停止预览通道并清理渐变 / 循环状态（不走 500ms 淡出）
    private void StopPreviewPlayback()
    {
        CancelPreviewFade();
        CancelPreviewLoop();
        ResetExternalTimeline();

        if (_fadingPlayingId != AkUnitySoundEngine.AK_INVALID_PLAYING_ID)
        {
            ExecuteActionOnPlayingId(_fadingPlayingId, AkActionOnEventType.AkActionOnEventType_Stop, 0);
            _fadingPlayingId = AkUnitySoundEngine.AK_INVALID_PLAYING_ID;
        }

        if (_playingId != AkUnitySoundEngine.AK_INVALID_PLAYING_ID)
        {
            ExecuteActionOnPlayingId(_playingId, AkActionOnEventType.AkActionOnEventType_Stop, 0);
            _playingId = AkUnitySoundEngine.AK_INVALID_PLAYING_ID;
            _onEnded = null;
        }

        _usingExternal = false;
        _externalAudio = null;
    }

    private void EndPreview()
    {
        StopPreviewPlayback();
        _previewActive = false;
        _pendingPreviewTrack = null;
    }

    /// <summary>停止当前音乐（500ms 淡出）。</summary>
    public void Stop()
    {
        CancelPreviewFade();
        CancelPreviewLoop();
        CancelPendingExternal();
        ResetExternalTimeline();
        _previewActive = false;
        _pendingPreviewTrack = null;
        _usingExternal = false;
        _externalAudio = null;

        // 上一首还在淡出：直接切掉，保证同一时刻最多一层交叉淡化
        if (_fadingPlayingId != AkUnitySoundEngine.AK_INVALID_PLAYING_ID)
        {
            ExecuteActionOnPlayingId(_fadingPlayingId, AkActionOnEventType.AkActionOnEventType_Stop, 0);
            _fadingPlayingId = AkUnitySoundEngine.AK_INVALID_PLAYING_ID;
        }

        if (_playingId == AkUnitySoundEngine.AK_INVALID_PLAYING_ID) return;

        // 暂停态直接 Stop 可能不终止事件，先恢复再停止
        if (_isPaused)
        {
            ExecuteAction(AkActionOnEventType.AkActionOnEventType_Resume);
            _isPaused = false;
        }

        ExecuteAction(AkActionOnEventType.AkActionOnEventType_Stop, StopFadeMs);

        _fadingPlayingId = _playingId;
        _playingId = AkUnitySoundEngine.AK_INVALID_PLAYING_ID;
        _onEnded = null;
    }

    /// <summary>暂停当前音乐，播放位置保留。</summary>
    public void Pause()
    {
        // 静音段 / 交接前的墙钟同步挂起，恢复时顺延
        if (_usingExternal && !_externalTimelineCalibrated && _externalGapStartTime >= 0f && _externalGapPausedAt < 0f)
            _externalGapPausedAt = Time.realtimeSinceStartup;

        // 延迟开播阶段还没有 playingId：挂起排队，恢复时续排
        if (_pendingPostTime >= 0f)
        {
            _pendingRemainingMs = Mathf.Max(0, Mathf.RoundToInt((_pendingPostTime - Time.realtimeSinceStartup) * 1000f));
            _pendingPaused = true;
            _pendingPostTime = -1f;
            return;
        }

        if (_playingId == AkUnitySoundEngine.AK_INVALID_PLAYING_ID || _isPaused) return;

        ExecuteAction(AkActionOnEventType.AkActionOnEventType_Pause);
        _isPaused = true;
    }

    /// <summary>从暂停位置继续播放。</summary>
    public void Resume()
    {
        // 静音段 / 交接前暂停：把墙钟起点顺延暂停时长
        if (_externalGapPausedAt >= 0f)
        {
            _externalGapStartTime += Time.realtimeSinceStartup - _externalGapPausedAt;
            _externalGapPausedAt = -1f;
        }

        if (_pendingPaused)
        {
            _pendingPostTime = Time.realtimeSinceStartup + _pendingRemainingMs / 1000f;
            _pendingPaused = false;
            _pendingRemainingMs = 0;
            return;
        }

        if (_playingId == AkUnitySoundEngine.AK_INVALID_PLAYING_ID || !_isPaused) return;

        ExecuteAction(AkActionOnEventType.AkActionOnEventType_Resume);
        _isPaused = false;
    }

    /// <summary>取当前音乐段落的原始信息（官谱）；无有效播放或查询失败时返回 false。</summary>
    public bool TryGetSegmentInfo(out AkSegmentInfo info)
    {
        info = _segmentInfo;

        if (_playingId == AkUnitySoundEngine.AK_INVALID_PLAYING_ID) return false;

        return AkUnitySoundEngine.GetPlayingSegmentInfo(_playingId, _segmentInfo) == AKRESULT.AK_Success;
    }

    /// <summary>统一位置查询：官谱取段落信息，自制谱取外部源位置 + StartOffset。</summary>
    public bool TryGetPosition(out int currentMs, out int durationMs)
    {
        if (_usingExternal)
        {
            durationMs = _externalAudio != null
                ? Math.Max(0, _externalAudio.DurationMs + _externalAudio.StartOffsetMs)
                : 0;
            currentMs = 0;

            float now = Time.realtimeSinceStartup;

            // 正 StartOffset 静音段：墙钟推进；音频起播后首次拿到位置时校准交接
            if (!_externalTimelineCalibrated)
            {
                float gapElapsedMs = _externalGapStartTime >= 0f
                    ? Math.Max(0f, (now - _externalGapStartTime) * 1000f)
                    : 0f;

                if (_playingId != AkUnitySoundEngine.AK_INVALID_PLAYING_ID &&
                    AkUnitySoundEngine.GetSourcePlayPosition(_playingId, out int firstPositionMs, true) == AKRESULT.AK_Success)
                {
                    int expectedMs = _externalGapOffsetMs + firstPositionMs;
                    _externalTimelineBiasMs = Mathf.Max(0, Mathf.RoundToInt(gapElapsedMs - expectedMs));
                    _externalTimelineCalibrated = true;
                    _lastExternalPositionMs = expectedMs + _externalTimelineBiasMs;
                    _lastExternalPositionTime = now;
                    currentMs = _lastExternalPositionMs;
                    return true;
                }

                currentMs = durationMs > 0
                    ? Math.Min(Mathf.RoundToInt(gapElapsedMs), durationMs)
                    : Mathf.RoundToInt(gapElapsedMs);
                return true;
            }

            if (_playingId != AkUnitySoundEngine.AK_INVALID_PLAYING_ID &&
                AkUnitySoundEngine.GetSourcePlayPosition(_playingId, out int positionMs, true) == AKRESULT.AK_Success)
            {
                currentMs = positionMs + (_externalAudio?.StartOffsetMs ?? 0) + _externalTimelineBiasMs;
                _lastExternalPositionMs = currentMs;
                _lastExternalPositionTime = now;
                return true;
            }

            // 位置查询暂时失败：按真实时间推进，避免时间源掉回 0
            if (_lastExternalPositionTime <= 0f)
            {
                _lastExternalPositionTime = now;
                currentMs = _lastExternalPositionMs;
                return true;
            }

            currentMs = _lastExternalPositionMs + Mathf.RoundToInt((now - _lastExternalPositionTime) * 1000f);
            if (durationMs > 0) currentMs = Math.Min(currentMs, durationMs);
            _lastExternalPositionMs = currentMs;
            _lastExternalPositionTime = now;
            return true;
        }

        if (!TryGetSegmentInfo(out var info))
        {
            currentMs = 0;
            durationMs = 0;
            return false;
        }

        currentMs = info.iCurrentPosition;
        durationMs = info.iPreEntryDuration + info.iActiveDuration;
        return true;
    }

    /// <summary>常驻 ticker 每帧调用：处理延迟开播与预览循环。</summary>
    public void Tick()
    {
        TickPreviewFade();

        if (_pendingPostTime >= 0f && Time.realtimeSinceStartup >= _pendingPostTime)
        {
            var audio = _pendingExternalAudio;
            var onEnded = _pendingOnEnded;
            int seekMs = _pendingSeekMs;
            CancelPendingExternal();
            if (audio != null) StartExternalPost(audio, seekMs, onEnded, Emitter);
        }

        if (!_previewLoopActive || _previewLoopTransitionActive) return;

        if (_playingId != _previewLoopPlayingId)
        {
            EndPreview();
            return;
        }

        if (!TryGetPreviewPosition(out int positionMs)) return;

        int fadeTriggerMs = Math.Max(_previewLoopSeekMs, _previewLoopEndMs - PreviewFadeMs);
        if (positionMs < fadeTriggerMs) return;

        _previewLoopTransitionActive = true;
        StartPreviewFade(0f, () =>
        {
            AkUnitySoundEngine.SeekOnEvent(
                _previewLoopEventId, _previewLoopEmitter, _previewLoopSeekMs, false, _previewLoopPlayingId);
            StartPreviewFade(1f, () => _previewLoopTransitionActive = false);
        });
    }

    // 常驻发射器：Wwise 只接受已注册的 Game Object；首次播放时创建，跨场景存活
    private GameObject Emitter
    {
        get
        {
            if (_emitter != null) return _emitter;

            _emitter = new GameObject("[MusicService]");
            UnityEngine.Object.DontDestroyOnLoad(_emitter);
            _emitter.AddComponent<AkGameObj>();
            _emitter.AddComponent<MusicServiceTicker>().Service = this;
            return _emitter;
        }
    }

    // 预览专用发射器：独立音量通道，避免淡入淡出影响打歌音乐
    private GameObject PreviewEmitter
    {
        get
        {
            if (_previewEmitter != null) return _previewEmitter;

            _previewEmitter = new GameObject("[MusicService Preview]");
            UnityEngine.Object.DontDestroyOnLoad(_previewEmitter);
            _previewEmitter.AddComponent<AkGameObj>();
            AkUnitySoundEngine.SetGameObjectOutputBusVolume(_previewEmitter, null, _previewVolume);
            return _previewEmitter;
        }
    }

    private void SetPreviewVolume(float volume)
    {
        _previewVolume = volume;
        if (_previewEmitter != null)
            AkUnitySoundEngine.SetGameObjectOutputBusVolume(_previewEmitter, null, volume);
    }

    private void StartPreviewFade(float target, Action onComplete)
    {
        _previewFadeFrom = _previewVolume;
        _previewFadeTo = target;
        _previewFadeStartTime = Time.realtimeSinceStartup;
        _previewFadeDurationMs = PreviewFadeMs;
        _previewFadeOnComplete = onComplete;
        _previewFadeActive = true;
    }

    private void TickPreviewFade()
    {
        if (!_previewFadeActive) return;

        float progress = _previewFadeDurationMs > 0
            ? Mathf.Clamp01((Time.realtimeSinceStartup - _previewFadeStartTime) * 1000f / _previewFadeDurationMs)
            : 1f;
        SetPreviewVolume(Mathf.Lerp(_previewFadeFrom, _previewFadeTo, progress));

        if (progress < 1f) return;

        _previewFadeActive = false;
        var complete = _previewFadeOnComplete;
        _previewFadeOnComplete = null;
        complete?.Invoke();
    }

    private void CancelPreviewFade()
    {
        _previewFadeActive = false;
        _previewFadeOnComplete = null;
    }

    private void StartExternalPost(ExternalTrackAudio audio, int seekMs, Action onEnded, GameObject emitter)
    {
        _usingExternal = true;
        _externalAudio = audio;
        _onEnded = onEnded;

        using var source = new AkExternalSourceInfo(
            audio.FilePath,
            AkUnitySoundEngine.GetIDFromString("External_Source"),
            (uint)AkUnitySoundEngine.AKCODECID_PCM_WAV);
        using var sources = new AkExternalSourceInfoArray(1);
        sources[0] = source;

        uint flags = EnableGetSourcePlayPosition | (uint)AkCallbackType.AK_EndOfEvent;
        _playingId = AkUnitySoundEngine.PostEvent(
            _externalMusicEvent.Id,
            emitter,
            flags,
            OnMusicCallback,
            null,
            1,
            sources);

        if (_playingId == AkUnitySoundEngine.AK_INVALID_PLAYING_ID)
        {
            _onEnded = null;
            _usingExternal = false;
            _externalAudio = null;
            Debug.LogError($"[MusicService] 外部源播放失败：{audio.FilePath}");
            return;
        }

        if (seekMs > 0)
        {
            AkUnitySoundEngine.SeekOnEvent(
                _externalMusicEvent.Id, emitter, seekMs, false, _playingId);
        }
    }

    private void StartPreviewLoop(uint eventId, int seekMs, int endMs, bool usesSourcePosition, GameObject emitter)
    {
        _previewLoopActive = true;
        _previewLoopPlayingId = _playingId;
        _previewLoopEventId = eventId;
        _previewLoopSeekMs = seekMs;
        _previewLoopEndMs = endMs;
        _previewLoopUsesSourcePosition = usesSourcePosition;
        _previewLoopEmitter = emitter;
        _previewLoopTransitionActive = false;
    }

    private void CancelPreviewLoop()
    {
        _previewLoopActive = false;
        _previewLoopTransitionActive = false;
        _previewLoopPlayingId = AkUnitySoundEngine.AK_INVALID_PLAYING_ID;
        _previewLoopEndMs = 0;
        _previewLoopEmitter = null;
    }

    private void CancelPendingExternal()
    {
        _pendingExternalAudio = null;
        _pendingOnEnded = null;
        _pendingSeekMs = 0;
        _pendingPostTime = -1f;
        _pendingPaused = false;
        _pendingRemainingMs = 0;
    }

    private void ResetExternalTimeline()
    {
        _externalGapStartTime = -1f;
        _externalGapOffsetMs = 0;
        _externalTimelineCalibrated = false;
        _externalTimelineBiasMs = 0;
        _lastExternalPositionMs = 0;
        _lastExternalPositionTime = 0f;
        _externalGapPausedAt = -1f;
    }

    private bool TryGetPreviewPosition(out int positionMs)
    {
        if (_previewLoopUsesSourcePosition)
        {
            positionMs = 0;
            if (_playingId == AkUnitySoundEngine.AK_INVALID_PLAYING_ID) return false;
            return AkUnitySoundEngine.GetSourcePlayPosition(_playingId, out positionMs, true) == AKRESULT.AK_Success;
        }

        if (!TryGetSegmentInfo(out var info))
        {
            positionMs = 0;
            return false;
        }

        positionMs = info.iCurrentPosition;
        return true;
    }

    private bool ValidateExternalEvent()
    {
        if (_externalMusicEvent == null || !_externalMusicEvent.IsValid())
        {
            Debug.LogError("[MusicService] 未配置外部源播放事件（MusicInstaller）");
            return false;
        }

        return true;
    }

    private void ExecuteAction(AkActionOnEventType actionType, int transitionMs = 0)
    {
        ExecuteActionOnPlayingId(_playingId, actionType, transitionMs);
    }

    private static void ExecuteActionOnPlayingId(uint playingId, AkActionOnEventType actionType, int transitionMs)
    {
        AkUnitySoundEngine.ExecuteActionOnPlayingID(
            actionType,
            playingId,
            transitionMs,
            AkCurveInterpolation.AkCurveInterpolation_Linear);
    }

    // 曲终回调：校验 playingID，屏蔽主动 Stop / 换曲产生的残留回调
    private void OnMusicCallback(object in_cookie, AkCallbackType in_type, AkCallbackInfo in_info)
    {
        if (in_type != AkCallbackType.AK_EndOfEvent) return;
        if (in_info is not AkEventCallbackInfo eventInfo || eventInfo.playingID != _playingId) return;

        _playingId = AkUnitySoundEngine.AK_INVALID_PLAYING_ID;
        if (_pendingPreviewTrack == null) CancelPreviewFade();
        CancelPreviewLoop();
        _previewActive = false;
        _usingExternal = false;
        _externalAudio = null;
        ResetExternalTimeline();

        var onEnded = _onEnded;
        _onEnded = null;
        onEnded?.Invoke();
    }
}
