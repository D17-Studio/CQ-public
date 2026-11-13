using System;
using System.Collections.Generic;
using System.Text;
using CQMusicGame.Shared;
using UnityEngine;

public class ScoringSystem
{
    #region 常量

    public const int FullMarks = 1000000;

    /// <summary> 评级定义：分数阈值 → 评级名称（从高到低） </summary>
    public static readonly (int Threshold, string Name)[] Ranks =
    {
        (1000000, "P"),
        (990000, "S"),
        (970000, "A"),
        (950000, "B"),
        (930000, "C"),
        (900000, "D"),
        (800000, "E"),
        (0, "F")
    };

    #endregion

    #region 权重

    /// <summary> 不同种类Note的评分权重（×100） </summary>
    public readonly Dictionary<NoteType, int> NoteWeights = new()
    {
        { NoteType.Dot, 100 },
        { NoteType.Dash, 100 },
        { NoteType.Mute, 50 }
    };

    /// <summary> 不同判定结果的评分权重（×100） </summary>
    public readonly Dictionary<JudgeResult, int> ResultWeights = new()
    {
        { JudgeResult.Perfect, 100 },
        { JudgeResult.Early, 80 },
        { JudgeResult.Late, 80 },
        { JudgeResult.Bad, 0 },
        { JudgeResult.Miss, 0 },
        { JudgeResult.Unknown, -1000000 }
    };

    #endregion

    #region 统计数据

    // 判定计数 [NoteType, JudgeResult]
    private readonly int[,] _counts;

    // 偏移记录
    private readonly List<int> _dotOffsets = new();
    private readonly List<int> _dashOffsets = new();
    private readonly List<int> _muteOffsets = new();

    // 分母（初始化时确定）
    private readonly long _denominator;

    // Combo
    private int _combo;
    private int _maxCombo;

    #endregion

    #region 公开属性

    /// <summary> 产生判定时触发：Note类型、判定结果、偏移量（毫秒） </summary>
    public event Action<NoteType, JudgeResult, int> NoteJudged;

    public int Score => CalcScore();
    public int ComboCount => _combo;
    public int MaxCombo => _maxCombo;

    public bool IsAllPerfect =>
        GetCount(JudgeResult.Early) == 0 &&
        GetCount(JudgeResult.Late) == 0 &&
        GetCount(JudgeResult.Bad) == 0 &&
        GetCount(JudgeResult.Miss) == 0 &&
        GetCount(JudgeResult.Unknown) == 0;

    public bool IsFullCombo =>
        GetCount(JudgeResult.Bad) == 0 &&
        GetCount(JudgeResult.Miss) == 0 &&
        GetCount(JudgeResult.Unknown) == 0;

    #endregion

    /// <summary>
    /// 传入谱面数据，初始化统计数组和分母
    /// </summary>
    public ScoringSystem(ChartSheet chart)
    {
        _counts = new int[3, 6];

        // 计算分母 = Σ(NoteWeight × 100)，Fake 不参与判定/计分
        long denominator = 0;
        foreach (var note in chart.GetNoteList())
        {
            if (note.Fake.HasValue)
                continue;

            int weight = NoteWeights[note.Type];
            denominator += weight * ResultWeights[JudgeResult.Perfect]; // Perfect = 100
        }
        _denominator = denominator;

        Debug.Log($"[ScoringSystem] 初始化完成，判定Note数={chart.TotalCount}，分母={_denominator}");
    }

    /// <summary>
    /// 添加一条判定记录
    /// </summary>
    public void AddRecord(JudgeRecord record, NoteType type)
    {
        // 计数
        int typeIdx = TypeToIndex(type);
        int resultIdx = ResultToIndex(record.Result);
        _counts[typeIdx, resultIdx]++;

        // 偏移
        GetOffsetList(type).Add(record.Offset);

        // Combo
        switch (record.Result)
        {
            case JudgeResult.Perfect:
            case JudgeResult.Early:
            case JudgeResult.Late:
                _combo++;
                if (_combo > _maxCombo) _maxCombo = _combo;
                break;
            case JudgeResult.Bad:
            case JudgeResult.Miss:
                _combo = 0;
                break;
        }

        //Debug.Log(BuildLog(record, type));

        NoteJudged?.Invoke(type, record.Result, record.Offset);
    }

    /// <summary>
    /// 查询所有Note的指定判定结果的总数量
    /// </summary>
    public int GetCount(JudgeResult result)
    {
        int r = ResultToIndex(result);
        int total = 0;
        for (int t = 0; t < 3; t++)
            total += _counts[t, r];
        return total;
    }

    /// <summary>
    /// 获取评级
    /// </summary>
    public string GetEvaluate() => GetEvaluateStatic(Score);

    /// <summary>
    /// 静态获取评级
    /// </summary>
    public static string GetEvaluateStatic(int score)
    {
        foreach (var rank in Ranks)
        {
            if (score >= rank.Threshold)
                return rank.Name;
        }
        return "";
    }

    #region 内部计算

    /// <summary>
    /// 从统计数组计算分数（整数运算，四舍五入）
    /// </summary>
    private int CalcScore()
    {
        if (_denominator == 0) return 0;

        long numerator = 0;
        for (int t = 0; t < 3; t++)
        {
            int weight = NoteWeights[IndexToType(t)];
            for (int r = 0; r < 6; r++)
            {
                var result = IndexToResult(r);
                numerator += (long)_counts[t, r] * weight * ResultWeights[result];
            }
        }

        return (int)((FullMarks * numerator + _denominator / 2) / _denominator);
    }

    #endregion

    #region 工具方法

    private static int TypeToIndex(NoteType type) => type switch
    {
        NoteType.Dot => 0,
        NoteType.Dash => 1,
        NoteType.Mute => 2,
        _ => 0
    };

    private static NoteType IndexToType(int idx) => idx switch
    {
        0 => NoteType.Dot,
        1 => NoteType.Dash,
        2 => NoteType.Mute,
        _ => NoteType.Dot
    };

    private static int ResultToIndex(JudgeResult result) => result switch
    {
        JudgeResult.Perfect => 0,
        JudgeResult.Early => 1,
        JudgeResult.Late => 2,
        JudgeResult.Bad => 3,
        JudgeResult.Miss => 4,
        JudgeResult.Unknown => 5,
        _ => 5
    };

    private static JudgeResult IndexToResult(int idx) => idx switch
    {
        0 => JudgeResult.Perfect,
        1 => JudgeResult.Early,
        2 => JudgeResult.Late,
        3 => JudgeResult.Bad,
        4 => JudgeResult.Miss,
        5 => JudgeResult.Unknown,
        _ => JudgeResult.Unknown
    };

    private List<int> GetOffsetList(NoteType type) => type switch
    {
        NoteType.Dot => _dotOffsets,
        NoteType.Dash => _dashOffsets,
        NoteType.Mute => _muteOffsets,
        _ => _dotOffsets
    };

    private string BuildLog(JudgeRecord record, NoteType type)
    {
        var sb = new StringBuilder();
        sb.Append("[判定] ");
        sb.Append(type.ToString());
        sb.Append(" | Result:");
        sb.Append(record.Result.ToString());
        sb.Append(" | Offset:");
        sb.Append(record.Offset);
        sb.Append("ms | Score:");
        sb.Append(Score);
        sb.Append(" | Combo:");
        sb.Append(_combo);
        return sb.ToString();
    }

    #endregion
}