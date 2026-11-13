using System;

/// <summary>单谱面成绩记录（磁盘数据单元）：Key = 谱面索引，IsFullCombo = 全连成就（曾经达成）。</summary>
[Serializable]
public class ScoreRecord
{
    public string Key;
    public int BestScore;
    public bool IsFullCombo;
}
