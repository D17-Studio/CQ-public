using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FMODUnity;
using FMOD;
using Debug = UnityEngine.Debug;

public class FMODCorePlayer
{
    // FMOD 对象
    private Sound sound;      // 音频资源
    private Channel channel;  // 播放通道

    /// <summary>
    /// 播放指定路径的音频文件
    /// </summary>
    /// <param name="filePath">文件完整路径</param>
    public void PlayAudio(string filePath)
    {
        StopAudio();

        // 组合模式标志：流式加载 + 关闭循环
        MODE mode = MODE.CREATESTREAM | MODE.LOOP_OFF;
        RESULT result = RuntimeManager.CoreSystem.createStream(filePath, mode, out sound);

        if (result != RESULT.OK)
        {
            Debug.LogError($"加载音频失败: {result}, 路径: {filePath}");
            return;
        }

        // 创建一个 ChannelGroup 结构体，并将其句柄清空，作为"空"参数传递
        ChannelGroup emptyGroup = new ChannelGroup();
        emptyGroup.clearHandle();

        // 播放并获取 Channel 句柄，这里传入 emptyGroup 即可
        result = RuntimeManager.CoreSystem.playSound(sound, emptyGroup, false, out channel);
        if (result != RESULT.OK)
        {
            Debug.LogError($"播放失败: {result}");
            if (sound.hasHandle())
            {
                sound.release();
                sound.clearHandle();
            }
            return;
        }

        //绑定回调
        channel.setCallback(OnChannelEnd);
        
        Debug.Log($"开始播放: {filePath}");
    }
    [AOT.MonoPInvokeCallback(typeof(FMOD.CHANNELCONTROL_CALLBACK))]
    static FMOD.RESULT OnChannelEnd(IntPtr channelControl, CHANNELCONTROL_TYPE controlType, CHANNELCONTROL_CALLBACK_TYPE callbackType, IntPtr commandData1, IntPtr commandData2)
    {
        if (callbackType == CHANNELCONTROL_CALLBACK_TYPE.END)
        {
            // 播放结束的处理逻辑
            Debug.Log("音频播放结束");
        }
        return FMOD.RESULT.OK;
    }

    /// <summary>
    /// 暂停播放
    /// </summary>
    public void Pause()
    {
        if (channel.hasHandle())
        {
            channel.setPaused(true);
            Debug.Log("已暂停");
        }
    }

    /// <summary>
    /// 恢复播放
    /// </summary>
    public void Resume()
    {
        if (channel.hasHandle())
        {
            channel.setPaused(false);
            Debug.Log("已恢复");
        }
    }

    /// <summary>
    /// 停止播放（不可恢复）
    /// </summary>
    public void StopAudio()
    {
        if (channel.hasHandle())
        {
            channel.stop();
            channel.clearHandle();
        }
        if (sound.hasHandle())
        {
            sound.release();
            sound.clearHandle();
        }
        Debug.Log("已停止并释放资源");
    }

    /// <summary>
    /// 设置音量（0.0 - 1.0，可大于1.0增益）
    /// </summary>
    public void SetVolume(float volume)
    {
        if (channel.hasHandle())
        {
            channel.setVolume(Mathf.Clamp(volume, 0f, 2f));
        }
    }

    /// <summary>
    /// 设置音调/速度倍率（1.0 = 原始）
    /// </summary>
    public void SetPitch(float pitch)
    {
        if (channel.hasHandle())
        {
            channel.setPitch(Mathf.Clamp(pitch, 0.5f, 2f));
        }
    }

    /// <summary>
    /// 跳转到指定位置（毫秒）
    /// </summary>
    public void SeekToMilliseconds(uint ms)
    {
        if (channel.hasHandle())
        {
            channel.setPosition(ms, TIMEUNIT.MS);
            Debug.Log($"跳转到 {ms} ms");
        }
    }

    /// <summary>
    /// 跳转到百分比（0-1）
    /// </summary>
    public void SeekToPercent(float percent)
    {
        if (channel.hasHandle() && sound.hasHandle())
        {
            // 获取音频总长度（毫秒）
            sound.getLength(out uint length, TIMEUNIT.MS);
            uint targetMs = (uint)(length * Mathf.Clamp01(percent));
            SeekToMilliseconds(targetMs);
        }
    }

    /// <summary>
    /// 获取当前播放位置（毫秒）
    /// </summary>
    public uint GetCurrentPositionMs()
    {
        if (channel.hasHandle())
        {
            channel.getPosition(out uint pos, TIMEUNIT.MS);
            return pos;
        }
        return 0;
    }

    /// <summary>
    /// 获取音频总长度（毫秒）
    /// </summary>
    public uint GetTotalLengthMs()
    {
        if (sound.hasHandle())
        {
            sound.getLength(out uint length, TIMEUNIT.MS);
            return length;
        }
        return 0;
    }

    /// <summary>
    /// 是否正在播放（未暂停且未停止）
    /// </summary>
    public bool IsPlaying()
    {
        if (channel.hasHandle())
        {
            channel.isPlaying(out bool playing);
            return playing;
        }
        return false;
    }
    
}
