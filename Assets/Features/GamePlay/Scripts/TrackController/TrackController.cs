using System;
using System.Collections.Generic;
using CQMusicGame.Shared;
using UnityEngine;
using Zenject;

public class TrackController : MonoBehaviour
{
    [Inject] private readonly ChartSheet _chart;
    [Inject] private readonly NoteJudge _noteJudge;
    [Inject] private readonly PlaySessionService _playSession;
    [Inject] private readonly PlayerConfigStore _playerConfigStore;
    [Inject] private readonly InputService _inputService;
    [Inject] private readonly MusicService _musicService;

    private void Start()
    {
        if (_playSession.SelectedTrack == null)
        {
            Debug.LogError("PlaySessionService 未写入选曲：不能直接 Play 打歌场景，请从 MenuScene 进入。");
            return;
        }

        if (_playSession.SelectedTrack.Audio == null)
        {
            Debug.LogError($"[TrackController] 曲目缺少音频配置：{_playSession.SelectedTrack.MusicTitle}");
            return;
        }

        _audio = _playSession.SelectedTrack.Audio;
        IsAutoPlay = _playSession.AutoPlay;
        PlayTrack();
    }

    public int TrackTimeMs { get; private set; }

    /// <summary>乐曲总时长（ms）：首次拿到有效音乐时长后确定（进度条用）；未确定前为 0。</summary>
    public int TrackDurationMs { get; private set; }

    /// <summary>本局是否 AutoPlay（开局快照，整局不变）。</summary>
    public bool IsAutoPlay { get; private set; }

    /// <summary>当前阶段（显式状态机；暂停面板显隐 / 输入路由的单一依据）。</summary>
    public TrackPhase Phase { get; private set; } = TrackPhase.Loading;

    /// <summary>恢复倒数剩余秒数（仅 Resuming 阶段有效；UI 显示取 CeilToInt）。</summary>
    public float ResumeCountdownRemaining { get; private set; }

    private readonly Queue<ZoneInput> _inputQueue = new();
    private readonly List<ZoneInput> _inputList = new();

    // 恢复倒数时长（秒）：Resuming 阶段按真实时间倒计时，归零即续播
    private const float ResumeCountdownSeconds = 3f;
    private int _lastCountdownDisplay;

    private TrackAudio _audio;

    /// <summary>乐曲自然播放结束时触发（主动 Stop 不触发）。</summary>
    public event Action TrackEnded;

    /// <summary>开始乐曲：交给 MusicService 播放（音库由服务保证已加载）。</summary>
    public void PlayTrack()
    {
        switch (_audio)
        {
            case WwiseTrackAudio wwise:
                _musicService.Play(wwise.Config.PlayMusic, OnMusicEnded);
                break;
            case ExternalTrackAudio external:
                _musicService.PlayExternal(external, OnMusicEnded);
                break;
            default:
                Debug.LogError("[TrackController] 未配置可播放的音频");
                return;
        }

        Phase = TrackPhase.Playing;
    }

    // 曲终（自然播放结束）：进入结算
    private void OnMusicEnded()
    {
        if (Phase != TrackPhase.Playing) return;

        Phase = TrackPhase.Result;
        TrackEnded?.Invoke();
    }

    //乐曲进行时的更新
    private void TrackUpdate()
    {
        if (IsAutoPlay)
        {
            _noteJudge.AutoPlayUpdate(TrackTimeMs);
        }
        else
        {
            _noteJudge.NotesUpdate(GetPendingInputs(),TrackTimeMs);//更新Note判定
        }
    }
    
    
    void Update()
    {
        // 显式阶段路由：只有 Playing 推进时间源与输入采集
        switch (Phase)
        {
            case TrackPhase.Paused:
                // 冻结 TrackTimeMs 时间源：Note 视觉 / 轨道动画 / 判定 / HUD 全部派生自它，一并停摆；也不吃输入
                return;
            case TrackPhase.Resuming:
                // 恢复倒数期间音乐仍暂停（Wwise 驻留），输入继续冻结，只推倒数
                TickResumeCountdown();
                return;
            case TrackPhase.Loading:
            case TrackPhase.Result:
                // 未开始 / 曲终：时间源保持不动（曲终冻结在曲末，残留 Note 不再回跳）
                return;
        }

        TrackTimeMs = GetTrackTime();
        //Debug.Log("TrackTimeMs: " + TrackTimeMs);

        TrackUpdate();//更新Note判定

        //更新输入队列（AutoPlay 不采集输入）
        if (!IsAutoPlay)
        {
            foreach (InputZone zone in ZoneInput.AllZones)
            {
                if (_inputService.InputZoneDown(zone))
                {
                    _inputQueue.Enqueue(new ZoneInput(zone, ZoneInputType.Press));
                }

                if (_inputService.InputZoneUp(zone))
                {
                    _inputQueue.Enqueue(new ZoneInput(zone, ZoneInputType.Release));
                }
            }
        }
    }

    void OnDestroy()
    {
        StopTrack();
    }

    //获取输入列表
    private List<ZoneInput> GetPendingInputs()
    {
        _inputList.Clear();
        while (_inputQueue.Count > 0)
            _inputList.Add(_inputQueue.Dequeue());

        foreach (InputZone zone in ZoneInput.AllZones)
        {
            if (_inputService.InputZoneHold(zone))
            {
                _inputList.Add(new ZoneInput(zone, ZoneInputType.Hold));
            }
        }

        return _inputList;
    }

    private int GetTrackTime()
    {
        if (!_musicService.TryGetPosition(out int currentMs, out int durationMs)) return 0;

        // 首次拿到有效时长后固定（官谱含段落前导；自制谱 = WAV 时长 + StartOffset）
        if (TrackDurationMs <= 0 && durationMs > 0)
            TrackDurationMs = durationMs;

        return currentMs - _playerConfigStore.InputOffsetMs;
    }

    /// <summary>暂停乐曲与判定：Playing → Paused（音乐位置保留）。</summary>
    public void Pause()
    {
        if (Phase != TrackPhase.Playing) return;

        Phase = TrackPhase.Paused;
        _musicService.Pause();
        Debug.Log("[暂停] 音乐与判定已暂停");
    }

    /// <summary>请求恢复：Paused → Resuming，开始 3-2-1 倒数（音乐保持暂停）；倒数归零自动续播。</summary>
    public void RequestResume()
    {
        if (Phase != TrackPhase.Paused) return;

        Phase = TrackPhase.Resuming;
        ResumeCountdownRemaining = ResumeCountdownSeconds;
        _lastCountdownDisplay = (int)ResumeCountdownSeconds;
        Debug.Log($"[暂停] 恢复倒数 {_lastCountdownDisplay}");
    }

    // 恢复倒数走真实时间；每个整秒变化时打一条日志（UI 接入后由面板显示数字）
    private void TickResumeCountdown()
    {
        ResumeCountdownRemaining -= Time.deltaTime;

        if (ResumeCountdownRemaining <= 0f)
        {
            FinishResume();
            return;
        }

        int display = Mathf.CeilToInt(ResumeCountdownRemaining);
        if (display != _lastCountdownDisplay)
        {
            _lastCountdownDisplay = display;
            Debug.Log($"[暂停] 恢复倒数 {display}");
        }
    }

    // 倒数归零：Resuming → Playing，从暂停位置续播
    private void FinishResume()
    {
        ResumeCountdownRemaining = 0f;
        Phase = TrackPhase.Playing;
        _musicService.Resume();
        Debug.Log("[暂停] 已恢复播放");
    }

    /// <summary>停止乐曲（含暂停 / 倒数状态）。音库常驻，不随场景卸载。</summary>
    public void StopTrack()
    {
        _musicService.Stop();
    }
}
