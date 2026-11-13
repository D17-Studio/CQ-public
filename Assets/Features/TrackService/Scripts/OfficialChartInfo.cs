using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public enum ChartDif
{
    EZ,
    ST,
    HD,
    EX
}

[System.Serializable]
public class OfficialChartInfo
{
    [Header("难度")]
    public ChartDif ChartDifficulty;
    [Header("谱师")]
    public string ChartArtist;
    [Header("定数")]
    public int ChartConstant;
    [Header("谱面文件")]
    public TextAsset ChartText;
}
