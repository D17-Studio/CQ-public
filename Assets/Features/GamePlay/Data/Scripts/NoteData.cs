using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Note类型枚举
/// </summary>
public enum NoteType
{
    Tap,
    Hold,
    Dash,
    Left,
    Right,
    Up,
    Down,
}

/// <summary>
/// Note数据结构体
/// </summary>
public struct NoteData
{
    /// <summary>
    /// Note类型
    /// </summary>
    public NoteType Type;
    
    /// <summary>
    /// Note判定轨道序号
    /// </summary>
    public int LaneIndex;
    
    /// <summary>
    /// Note正解时间
    /// </summary>
    public int TargetTime;
    
    /// <summary>
    /// Note长按时长
    /// </summary>
    public int SustainDuration;

    /// <summary>
    /// Note结构体的构造函数
    /// </summary>
    /// <param name="type">Note类型</param>
    /// <param name="laneIndex">Note判定轨道序号</param>
    /// <param name="targetTime">Note正解时间</param>
    /// <param name="sustainDuration">Note长按时长</param>
    public NoteData(NoteType type , int laneIndex , int targetTime, int sustainDuration = 0)
    {
        Type = type;
        LaneIndex = laneIndex;
        TargetTime = targetTime;
        SustainDuration = sustainDuration;
    }
}
