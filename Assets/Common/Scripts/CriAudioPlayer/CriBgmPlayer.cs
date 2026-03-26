using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CriWare;

public class CriBgmPlayer
{
    
    private CriAtomExPlayer bgmExPlayer;
    private CriAtomExAcb acb;
    private CriAtomExPlayback playback;
    //用于支持时间拉伸的专用语音池
    private CriAtomExStandardVoicePool timeStretchVoicePool;
    
    //构造函数，自动初始化
    public CriBgmPlayer()
    {
        Initialize();
    }
    
    /// <summary>
    /// 初始化，创建ExPlayer和Fader并归零淡出淡入时间
    /// </summary>
    public void Initialize()
    {
        
        acb = null;
        //创建ExPlayer
        bgmExPlayer = new CriAtomExPlayer(true);
        
        //创建Fader并归零淡出淡入时间
        bgmExPlayer.AttachFader();
        SetFadeInTime(0);
        SetFadeOutTime(0);
        
        // 创建并配置支持时间拉伸的专用语音池
        // 参数依次为：
        //   - maxPlaybacks: 同时播放数，BGM通常1个就够了
        //   - maxChannels: 最大声道数，立体声为2
        //   - maxSamplingRate: 最大采样率，必须是BGM采样率的两倍以上！(假设你的BGM是44100Hz，这里设置成88200或更高)
        //   - streamingFlag: 是否流播放，根据你的BGM设置 (如果你的BGM是“零延迟流”，这里应为true)
        //   - identifier: 一个唯一的ID，用于标识这个语音池，确保不和其他的冲突，比如用 2104
        timeStretchVoicePool = new CriAtomExStandardVoicePool(1, 2, 96000, true, 2104);
    
        // 3. 为这个语音池附加时间拉伸DSP功能（关键步骤！）
        timeStretchVoicePool.AttachDspTimeStretch();
    
        // 4. 将播放器绑定到这个专用语音池
        bgmExPlayer.SetVoicePoolIdentifier(timeStretchVoicePool.identifier);
        
        // 订阅退出事件，确保程序退出时释放资源
        Application.quitting += OnApplicationQuit;
    }
    
    /// <summary>
    /// 载入CueSheet
    /// </summary>
    /// <param name="name"></param>
    public void SetCueSheet(string name)
    {
        acb = CriAtom.GetAcb(name);
    }
    
    /// <summary>
    /// 播放Cue（按id查找）
    /// </summary>
    /// <param name="cueId"></param>
    public void Play(int cueId = 0)
    {
        if (acb == null) return;
        if (bgmExPlayer == null) return;
        bgmExPlayer.Stop();
        bgmExPlayer.SetStartTime(0);
        bgmExPlayer.SetCue(acb, cueId);
        playback = bgmExPlayer.Start();
    }
    
    /// <summary>
    /// 播放Cue（按名称查找）
    /// </summary>
    /// <param name="name"></param>
    public void Play(string name)
    {
        if (acb == null) return;
        if (bgmExPlayer == null) return;
        bgmExPlayer.Stop();
        bgmExPlayer.SetStartTime(0);
        bgmExPlayer.SetCue(acb, name);
        playback = bgmExPlayer.Start();
    }
    
    /// <summary>
    /// 停止播放
    /// </summary>
    public void Stop()
    {
        if (bgmExPlayer == null) return;
        bgmExPlayer.Stop();
    }
    
    /// <summary>
    /// 从指定毫秒位置开始播放（按id查找）
    /// </summary>
    /// <param name="startTimeMs">开始时间（毫秒）</param>
    /// <param name="cueId">Cue ID</param>
    public void PlayAtTime(long startTimeMs , int cueId = 0)
    {
        if (acb == null) return;
        if (bgmExPlayer == null) return;
        bgmExPlayer.Stop();
        bgmExPlayer.SetCue(acb, cueId);
        bgmExPlayer.SetStartTime(startTimeMs);
        playback = bgmExPlayer.Start();
    }
    
    /// <summary>
    /// 重构PlayAtTime（按id查找），使其可以传入float（s）
    /// </summary>
    /// <param name="startTime"></param>
    /// <param name="cueId"></param>
    public void PlayAtTime(float startTime, int cueId = 0)
    {
        PlayAtTime((long)(startTime * 1000), cueId);
    }
    
    /// <summary>
    /// 从指定毫秒位置开始播放（按名称查找）
    /// </summary>
    /// <param name="startTimeMs">开始时间（毫秒）</param>
    /// <param name="name">名称</param>
    public void PlayAtTime(long startTimeMs , string name)
    {
        if (acb == null) return;
        if (bgmExPlayer == null) return;
        bgmExPlayer.Stop();
        bgmExPlayer.SetCue(acb, name);
        bgmExPlayer.SetStartTime(startTimeMs);
        playback = bgmExPlayer.Start();
    }
    
    /// <summary>
    /// 重构PlayAtTime（按名称查找），使其可以传入float（s）
    /// </summary>
    /// <param name="startTime"></param>
    /// <param name="name"></param>
    public void PlayAtTime(float startTime, string name)
    {
        PlayAtTime((long)(startTime * 1000), name);
    }
    
    /// <summary>
    /// 暂停播放
    /// </summary>
    public void Pause()
    {
        bgmExPlayer?.Pause(true);
    }
    
    /// <summary>
    /// 恢复播放
    /// </summary>
    public void Resume()
    {
        bgmExPlayer?.Resume(CriAtomEx.ResumeMode.PausedPlayback);
    }
    
    /// <summary>
    /// 获取当前时间（s或ms）
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    /// <exception cref="NotSupportedException"></exception>
    public T GetCurrentTime<T>() where T : struct
    {
        if (bgmExPlayer == null)
            return typeof(T) == typeof(float) ? (T)(object)-1f : (T)(object)(-1L);

        long ms = playback.GetTimeSyncedWithAudio();

        if (typeof(T) == typeof(long))
            return (T)(object)ms;
        else if (typeof(T) == typeof(float))
            return (T)(object)(ms / 1000f);
        else
            throw new NotSupportedException("仅支持 long 和 float 类型");
    }
    
    /// <summary>
    /// 重构GetCurrentTime，默认返回s
    /// </summary>
    /// <returns></returns>
    public float GetCurrentTime() 
    {
        return GetCurrentTime<float>();
    }
    
    /// <summary>
    /// 设置音量（0.0f 静音，1.0f 原声）
    /// </summary>
    public void SetVolume(float volume)
    {
        if (bgmExPlayer == null) return;
        bgmExPlayer.SetVolume(volume);
        bgmExPlayer?.Update(playback);
    }
    
    /// <summary>
    /// 获取指定Cue的总时长（毫秒或秒）
    /// </summary>
    /// <typeparam name="T">long（毫秒）或 float（秒）</typeparam>
    /// <param name="cueId">Cue ID</param>
    /// <returns>时长，失败返回 -1（对应类型）</returns>
    /// <exception cref="NotSupportedException">当T不是long或float时抛出</exception>
    public T GetCueLength<T>(int cueId = 0) where T : struct
    {
        if (acb == null)
            return typeof(T) == typeof(float) ? (T)(object)-1f : (T)(object)(-1L);

        CriAtomEx.CueInfo cueInfo;
        if (!acb.GetCueInfo(cueId, out cueInfo))
            return typeof(T) == typeof(float) ? (T)(object)-1f : (T)(object)(-1L);

        if (typeof(T) == typeof(long))
            return (T)(object)cueInfo.length;
        else if (typeof(T) == typeof(float))
            return (T)(object)(cueInfo.length / 1000f);
        else
            throw new NotSupportedException("仅支持 long 和 float 类型");
    }

    /// <summary>
    /// 获取指定Cue的总时长（默认返回秒）
    /// </summary>
    /// <param name="cueId">Cue ID</param>
    /// <returns>时长（秒），失败返回 -1f</returns>
    public float GetCueLength(int cueId = 0)
    {
        return GetCueLength<float>(cueId);
    }
    
    /// <summary>
    /// 获取指定Cue的总时长（毫秒或秒）
    /// </summary>
    /// <typeparam name="T">long（毫秒）或 float（秒）</typeparam>
    /// <param name="name">Cue名称</param>
    /// <returns>时长，失败返回 -1（对应类型）</returns>
    /// <exception cref="NotSupportedException">当T不是long或float时抛出</exception>
    public T GetCueLength<T>(string name) where T : struct
    {
        if (acb == null)
            return typeof(T) == typeof(float) ? (T)(object)-1f : (T)(object)(-1L);

        CriAtomEx.CueInfo cueInfo;
        if (!acb.GetCueInfo(name, out cueInfo))
            return typeof(T) == typeof(float) ? (T)(object)-1f : (T)(object)(-1L);

        if (typeof(T) == typeof(long))
            return (T)(object)cueInfo.length;
        else if (typeof(T) == typeof(float))
            return (T)(object)(cueInfo.length / 1000f);
        else
            throw new NotSupportedException("仅支持 long 和 float 类型");
    }

    /// <summary>
    /// 获取指定Cue的总时长（默认返回秒）
    /// </summary>
    /// <param name="name">Cue名称</param>
    /// <returns>时长（秒），失败返回 -1f</returns>
    public float GetCueLength(string name)
    {
        return GetCueLength<float>(name);
    }
    
    /// <summary>
    /// 设置是否循环播放（对无循环点的波形有效）
    /// </summary>
    public void SetLoop(bool loop)
    {
        if (bgmExPlayer == null) return;
        bgmExPlayer.Loop(loop);
        // 如果正在播放，需要更新才能立即生效
        bgmExPlayer.Update(playback);
    }
    
    /// <summary>
    /// 设置淡入时间（毫秒）
    /// </summary>
    public void SetFadeInTime(int ms)
    {
        bgmExPlayer?.SetFadeInTime(ms);
    }
    
    /// <summary>
    /// 重构SetFadeInTime，使其可以传入float（s）
    /// </summary>
    /// <param name="s"></param>
    public void SetFadeInTime(float s)
    {
        SetFadeInTime((int)(s * 1000));
    }

    /// <summary>
    /// 设置淡出时间（毫秒）
    /// </summary>
    public void SetFadeOutTime(int ms)
    {
        bgmExPlayer?.SetFadeOutTime(ms);
    }
    
    /// <summary>
    /// 重构SetFadeOutTime，使其可以传入float（s）
    /// </summary>
    /// <param name="s"></param>
    public void SetFadeOutTime(float s)
    {
        SetFadeOutTime((int)(s * 1000));
    }
    
    /// <summary>
    /// 设置播放速度（0.25 0.5 2倍速等）
    /// </summary>
    /// <param name="speedRatio"></param>
    public void SetSpeed(float speedRatio)
    {
        if (bgmExPlayer == null) return;
        float stretchRatio = 1f / speedRatio;
        bgmExPlayer.SetDspTimeStretchRatio(stretchRatio);
        bgmExPlayer.UpdateAll();
    }
    
    /// <summary>
    /// 手动释放资源
    /// </summary>
    public void Dispose()
    {
        // 释放非托管资源
        bgmExPlayer?.Dispose();
        bgmExPlayer = null;
        timeStretchVoicePool?.Dispose();
        timeStretchVoicePool = null;

        // 取消订阅（避免万一事件被再次触发）
        Application.quitting -= OnApplicationQuit;
    }

    /// <summary>
    /// 退出释放资源
    /// </summary>
    private void OnApplicationQuit()
    {
        Dispose();
    }
    
}