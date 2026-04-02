using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NoteJudgeConfig", menuName = "CQConfig/NoteJudgeConfig")]
public class NoteJudgeConfig : ScriptableObject
{
    /// <summary>
    /// Perfect判定偏差（正负）
    /// </summary>
    public int PerfectOffset => _perfectOffset;
    [Header("Perfect判定偏差（正负）")]
    [SerializeField]private int _perfectOffset = 60;
    
    /// <summary>
    /// Good判定偏差（正负）
    /// </summary>
    public int GoodOffset => _goodOffset;
    [Header("Good判定偏差（正负）")]
    [SerializeField]private int _goodOffset = 100;
    
    /// <summary>
    /// Bad判定偏差（正负）
    /// </summary>
    public int BadOffset => _badOffset;
    [Header("Bad判定偏差（正负）")]
    [SerializeField]private int _badOffset = 150;
    
    /// <summary>
    /// hold可以提前松开的判定时间
    /// </summary>
    public int HoldEarlyReleaseWindow => _holdEarlyReleaseWindow;
    [Header("hold可以提前松开的判定时间")]
    [SerializeField]private int _holdEarlyReleaseWindow = 300;
}
