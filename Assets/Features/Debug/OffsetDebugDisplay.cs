using System.Collections.Generic;
using CQMusicGame.Shared;
using TMPro;
using UnityEngine;
using Zenject;

/// <summary>
/// 判定偏移调试显示（测试用，无 MVP）：订阅 ScoringSystem 判定事件，仅统计 Dot，
/// 显示最近若干次偏移与累计平均值。UGUI 由场景手拖，本脚本只负责写入两个 TMP 文本。
/// </summary>
public class OffsetDebugDisplay : MonoBehaviour
{
    [Inject] private readonly ScoringSystem _scoring;

    [SerializeField] private TMP_Text _historyText;
    [SerializeField] private TMP_Text _averageText;

    private const int MaxLines = 10;

    private readonly Queue<string> _lines = new();

    private long _sum;
    private int _count;

    private void Start()
    {
        _scoring.NoteJudged += OnNoteJudged;
    }

    private void OnDestroy()
    {
        if (_scoring != null)
            _scoring.NoteJudged -= OnNoteJudged;
    }

    private void OnNoteJudged(NoteType type, JudgeResult result, int offset)
    {
        if (type != NoteType.Dot) return;
        if (result == JudgeResult.Miss) return;

        _lines.Enqueue($"{offset:+0;-0;0} ms");
        while (_lines.Count > MaxLines)
            _lines.Dequeue();

        _historyText.text = string.Join("\n", _lines);

        _sum += offset;
        _count++;
        float average = (float)_sum / _count;
        _averageText.text = $"AVG {average:+0.0;-0.0;0.0} ms";
    }
}
