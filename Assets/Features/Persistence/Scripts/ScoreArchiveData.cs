using System;
using System.Collections.Generic;

/// <summary>分数存档的序列化根（JsonUtility 要求对象作根）；Version = 格式迁移钩子。</summary>
[Serializable]
public class ScoreArchiveData
{
    public int Version;
    public List<ScoreRecord> Records = new();
}
