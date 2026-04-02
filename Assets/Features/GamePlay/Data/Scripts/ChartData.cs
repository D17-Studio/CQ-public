using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum ChartDif
{
    EZ,
    ST,
    HD,
    EX
}

[CreateAssetMenu(fileName = "ChartData" , menuName = "Data/ChartData")]
public class ChartData : ScriptableObject
{
    [Header("谱师")]
    public string ChartArtist;
    [Header("难度")]
    public ChartDif ChartDifficulty;
    [Header("定数")]
    public int ChartDifficultyNumber;
    [Header("谱面文件")]
    public TextAsset ChartText;
    [Header("分数存档的存档名")]
    public string ScoreSave;
    
    
    /// <summary>
    /// 谱面文本
    /// </summary>
    [HideInInspector] public string ChartContent;
}
