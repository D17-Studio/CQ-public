using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CriWare;

public class CriSfxPlayer
{
    
    private CriAtomExPlayer sfxExPlayer;
    private CriAtomExAcb acb;
    
    //全局音量系数
    private float globalVolume = 1.0f;
    
    //构造函数，自动初始化
    public CriSfxPlayer()
    {
        Initialize();
    }
    
    /// <summary>
    /// 初始化，创建ExPlayer
    /// </summary>
    public void Initialize()
    {
        acb = null;
        //创建ExPlayer
        sfxExPlayer = new CriAtomExPlayer();
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
    /// 播放Cue（按id查找），可指定独立音量（默认为1），此音量与全局音量乘算
    /// </summary>
    public void Play(int cueId = 0, float volume = 1.0f)
    {
        if (acb == null || sfxExPlayer == null) return;
    
        sfxExPlayer.SetCue(acb, cueId);
        CriAtomExPlayback playback = sfxExPlayer.Start();   // 获取播放句柄
    
        float finalVol = globalVolume * volume;
        sfxExPlayer.SetVolume(finalVol);
        sfxExPlayer.Update(playback);
    }
    
    /// <summary>
    /// 播放Cue（按名称查找），可指定独立音量（默认为1），此音量与全局音量乘算
    /// </summary>
    public void Play(string name, float volume = 1.0f)
    {
        if (acb == null || sfxExPlayer == null) return;
    
        sfxExPlayer.SetCue(acb, name);
        CriAtomExPlayback playback = sfxExPlayer.Start();
    
        float finalVol = globalVolume * volume;
        sfxExPlayer.SetVolume(finalVol);
        sfxExPlayer.Update(playback);
    }
    
    /// <summary>
    /// 停止所有正在播放的音效
    /// </summary>
    public void StopAll()
    {
        if (sfxExPlayer == null) return;
        sfxExPlayer.Stop();
    }
    
    /// <summary>
    /// 暂停所有正在播放的音效
    /// </summary>
    public void PauseAll()
    {
        if (sfxExPlayer == null) return;
        sfxExPlayer.Pause(true);
    }
    
    /// <summary>
    /// 恢复所有被暂停的音效
    /// </summary>
    public void ResumeAll()
    {
        if (sfxExPlayer == null) return;
        sfxExPlayer.Pause(false);
    }
    
    /// <summary>
    /// 设置全局音量系数（只影响修改后播放的音效音量）
    /// </summary>
    public void SetVolume(float volume)
    {
        globalVolume = volume;
    }
    
    /// <summary>
    /// 手动释放资源
    /// </summary>
    public void Dispose()
    {
        sfxExPlayer?.Dispose();
        sfxExPlayer = null;
    
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
