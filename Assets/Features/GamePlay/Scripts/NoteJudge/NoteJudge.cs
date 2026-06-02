using System.Collections.Generic;
using CQMusicGame.Shared;
using UnityEngine;

/// <summary>
/// NoteJudge，管理Note的判定
/// </summary>
public class NoteJudge
{
    private readonly NoteJudgeConfig _config;
    private readonly ScoringSystem _scoring;

    // 四个判定池（平铺列表，无需按轨道分组）
    private readonly List<Note> _dotDashHeadPool = new();   // Dot、Dash头部判定池
    private readonly List<Note> _muteHeadPool = new();      // Mute头部判定池
    private readonly List<Note> _sustainPool = new();       // 长按判定池（Dash/Mute头部判定后）
    private readonly List<Note> _tunePool = new();          // Tune判定池

    // Mute宽松判定计时器，每个轨道一个（索引 = laneIndex + 3）
    private readonly int[] _muteTimer = new int[7];

    // NoteData → Note 映射（Dot / Dash / Mute），供 NoteViewManager 绑定视觉
    private readonly Dictionary<NoteData, Note> _noteMap = new();
    // Tune 映射，key = (所属Dash的NoteData, TuneData)
    private readonly Dictionary<(NoteData dashData, TuneData tuneData), TuneNote> _tuneMap = new();

    private int _lastUpdateTime = -1;

    public NoteJudge(NoteJudgeConfig config, ScoringSystem scoring, List<NoteData> noteDataList)
    {
        _config = config;
        _scoring = scoring;

        foreach (var data in noteDataList)
        {
            AddNote(data);
        }
    }

    /// <summary>
    /// 根据 NoteData 查询 Dot / Dash / Mute（供 NoteViewManager 绑定回调）
    /// </summary>
    public Note GetNote(NoteData data)
    {
        _noteMap.TryGetValue(data, out var note);
        return note;
    }

    /// <summary>
    /// 根据所属 Dash 的 NoteData 与 TuneData 查询 Tune
    /// </summary>
    public TuneNote GetTune(NoteData dashData, TuneData tuneData)
    {
        _tuneMap.TryGetValue((dashData, tuneData), out var tune);
        return tune;
    }

    #region 添加Note

    /// <summary>
    /// 添加Note，根据类型分配到对应判定池。Dash会同时创建关联的Tune
    /// </summary>
    private void AddNote(NoteData data)
    {
        switch (data.Type)
        {
            case NoteType.Dot:
                _dotDashHeadPool.Add(CreateNote(data, _config, _scoring));
                _noteMap[data] = _dotDashHeadPool[^1];
                break;
            case NoteType.Mute:
                _muteHeadPool.Add(CreateNote(data, _config,  _scoring));
                _noteMap[data] = _muteHeadPool[^1];
                break;
            case NoteType.Dash:
            {
                DashNote dash = (DashNote)CreateNote(data, _config,  _scoring);
                _dotDashHeadPool.Add(dash);
                _noteMap[data] = dash;

                // 为Dash创建关联的Tune
                if (data.LinkedTunes != null)
                {
                    foreach (var tuneData in data.LinkedTunes)
                    {
                        var tuneNoteData = new NoteData(NoteType.Tune, laneIndex: data.LaneIndex, targetTime: tuneData.TargetTime);
                        TuneNote tune = (TuneNote)CreateNote(tuneNoteData, _config, _scoring);
                        tune.SetLinkedDash(dash, tuneData.Direction);
                        dash.AddLinkedTune(tune);
                        _tunePool.Add(tune);
                        _tuneMap[(data, tuneData)] = tune;
                    }
                }
                break;
            }
        }
    }
    
    /// <summary>
    /// 根据NoteData创建对应的Note实例
    /// 注意：Tune需要在创建后调用SetLinkedDash设置关联的Dash
    /// </summary>
    private static Note CreateNote(NoteData noteData, NoteJudgeConfig config , ScoringSystem scoring)
    {
        return noteData.Type switch
        {
            NoteType.Dot => new DotNote(noteData, config, scoring),
            NoteType.Dash => new DashNote(noteData, config, scoring),
            NoteType.Mute => new MuteNote(noteData, config, scoring),
            NoteType.Tune => new TuneNote(noteData, config, scoring),
            _ => throw new System.NotSupportedException($"不支持的Note类型: {noteData.Type}")
        };
    }

    #endregion

    #region 帧更新

    /// <summary>
    /// 常规更新（需要频繁调用）
    /// </summary>
    public void NotesUpdate(List<LaneInput> inputs, int time)
    {
        // 更新Mute计时器
        UpdateMuteTimer(inputs, CalcDeltaTime(time));

        // Mute头部判定（宽松判定生效时自动击中）
        UpdateMuteHeadPool(time);

        // Dot/Dash头部判定 + Tune判定（两个池并行检查）
        foreach (var input in inputs)
        {
            if (input.InputType == LaneInputType.Press)
            {
                TryHitDotDash(input, time);
                TryHitTune(input, time);
            }
        }

        // 长按判定
        UpdateSustainPool(inputs, time);

        // 检查所有池的超时Miss
        CheckMissAll(time);
    }

    /// <summary>
    /// AutoPlay更新
    /// </summary>
    public void AutoPlayUpdate(int time)
    {
        // AutoPlay下所有轨道视为按住
        for (int i = 0; i < 7; i++)
        {
            _muteTimer[i] = _config.GoodOffset;
        }

        // Mute头部判定
        UpdateMuteHeadPool(time);

        // Dot/Dash自动击中
        TryAutoHitDotDash(time);

        // Tune自动击中
        TryAutoHitTune(time);

        // 长按判定（AutoPlay下全按住）
        UpdateSustainAuto(time);

        // Miss检查
        CheckMissAll(time);
    }

    #endregion

    #region Mute计时器

    /// <summary>
    /// 按住时刷新计时器，松开后递减
    /// </summary>
    private void UpdateMuteTimer(List<LaneInput> inputs, int deltaTime)
    {
        // 按住：重置计时器
        foreach (var input in inputs)
        {
            if (input.InputType == LaneInputType.Hold)
            {
                int idx = input.LaneIndex + 3;
                _muteTimer[idx] = _config.GoodOffset;//计时时间
            }
        }

        // 递减
        for (int i = 0; i < 7; i++)
        {
            if (_muteTimer[i] > 0)
                _muteTimer[i] = Mathf.Max(0, _muteTimer[i] - deltaTime);
        }
    }

    #endregion

    #region 判定池更新

    /// <summary>
    /// Mute头部判定：宽松计时器生效时自动击中
    /// </summary>
    private void UpdateMuteHeadPool(int time)
    {
        for (int laneIdx = 0; laneIdx < 7; laneIdx++)
        {
            if (_muteTimer[laneIdx] <= 0) continue;

            int laneIndex = laneIdx - 3;
            var input = new LaneInput(laneIndex, LaneInputType.Press);

            for (int i = 0; i < _muteHeadPool.Count; i++)
            {
                if (_muteHeadPool[i].IsBeforeJudgmentWindow(time))
                    break;

                if (_muteHeadPool[i].CanBeHit(input, time))
                {
                    _muteHeadPool[i].OnHit(time);
                    _sustainPool.Add(_muteHeadPool[i]);
                    _muteHeadPool.RemoveAt(i);
                    i--;  // 补偿移除导致的索引偏移
                }
            }
        }
    }

    /// <summary>
    /// 尝试击中Dot/Dash
    /// </summary>
    private void TryHitDotDash(LaneInput input, int time)
    {
        for (int i = 0; i < _dotDashHeadPool.Count; i++)
        {
            if (_dotDashHeadPool[i].IsBeforeJudgmentWindow(time))
                break;

            if (_dotDashHeadPool[i].CanBeHit(input, time))
            {
                _dotDashHeadPool[i].OnHit(time);

                // Dash击中后移入长按池
                if (_dotDashHeadPool[i].GetNoteType() == NoteType.Dash)
                {
                    _sustainPool.Add(_dotDashHeadPool[i]);
                }
                _dotDashHeadPool.RemoveAt(i);
                break;  // 一次点击只判一个
            }
        }
    }

    /// <summary>
    /// 尝试击中Tune（与DotDashHeadPool并行，互不影响）
    /// </summary>
    private void TryHitTune(LaneInput input, int time)
    {
        for (int i = 0; i < _tunePool.Count; i++)
        {
            if (_tunePool[i].IsBeforeJudgmentWindow(time))
                break;

            if (_tunePool[i].CanBeHit(input, time))
            {
                _tunePool[i].OnHit(time);
                _tunePool.RemoveAt(i);
                break;  // 一次点击只判一个Tune
            }
        }
    }

    /// <summary>
    /// AutoPlay自动击打Dot/Dash
    /// </summary>
    private void TryAutoHitDotDash(int time)
    {
        for (int i = _dotDashHeadPool.Count - 1; i >= 0; i--)
        {
            if (_dotDashHeadPool[i].CanAutoHit(time))
            {
                _dotDashHeadPool[i].OnHit(time);

                if (_dotDashHeadPool[i].GetNoteType() == NoteType.Dash)
                {
                    _sustainPool.Add(_dotDashHeadPool[i]);
                }
                _dotDashHeadPool.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// AutoPlay自动击打Tune
    /// </summary>
    private void TryAutoHitTune(int time)
    {
        for (int i = _tunePool.Count - 1; i >= 0; i--)
        {
            if (_tunePool[i].CanAutoHit(time))
            {
                _tunePool[i].OnHit(time);
                _tunePool.RemoveAt(i);
            }
        }
    }
    
    /// <summary>
    /// 长按池更新：检查松开 → Miss，检查结束 → 结算
    /// </summary>
    private void UpdateSustainPool(List<LaneInput> inputs, int time)
    {
        for (int i = _sustainPool.Count - 1; i >= 0; i--)
        {
            var note = _sustainPool[i];

            // 长按结束：提交结果
            if (note.IsSustainEnded(time))
            {
                note.SubmitFinal();
                _sustainPool.RemoveAt(i);
                continue;
            }

            // 松开：判Miss
            if (!IsLaneHeld(inputs, note.GetLaneIndex()))
            {
                HandleMiss(note);
                _sustainPool.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// AutoPlay长按更新（全按住）
    /// </summary>
    private void UpdateSustainAuto(int time)
    {
        for (int i = _sustainPool.Count - 1; i >= 0; i--)
        {
            if (_sustainPool[i].IsSustainEnded(time))
            {
                _sustainPool[i].SubmitFinal();
                _sustainPool.RemoveAt(i);
            }
        }
    }

    #endregion

    #region Miss检查与连锁

    /// <summary>
    /// 检查所有池的超时Miss，处理连锁。先收集再处理，避免遍历时修改列表
    /// </summary>
    private void CheckMissAll(int time)
    {
        // 先收集所有超时的Note
        var missedNotes = new List<Note>();

        for (int i = _dotDashHeadPool.Count - 1; i >= 0; i--)
        {
            if (_dotDashHeadPool[i].IsMissed(time))
                missedNotes.Add(_dotDashHeadPool[i]);
        }
        for (int i = _muteHeadPool.Count - 1; i >= 0; i--)
        {
            if (_muteHeadPool[i].IsMissed(time))
                missedNotes.Add(_muteHeadPool[i]);
        }
        for (int i = _tunePool.Count - 1; i >= 0; i--)
        {
            if (_tunePool[i].IsMissed(time))
                missedNotes.Add(_tunePool[i]);
        }

        // 再统一处理（只处理仍在池中的Note，连锁可能已将其移除）
        foreach (var note in missedNotes)
        {
            if (IsInAnyPool(note))
            {
                HandleMiss(note);
                // 从所有可能的池中移除（HandleMiss已处理连锁，这里移除自身）
                _dotDashHeadPool.Remove(note);
                _muteHeadPool.Remove(note);
                _tunePool.Remove(note);
            }
        }
    }

    /// <summary>
    /// 检查Note是否还在任意池中
    /// </summary>
    private bool IsInAnyPool(Note note)
    {
        return _dotDashHeadPool.Contains(note) ||
               _muteHeadPool.Contains(note) ||
               _sustainPool.Contains(note) ||
               _tunePool.Contains(note);
    }

    /// <summary>
    /// 处理单个Note的Miss（含连锁）
    /// </summary>
    private void HandleMiss(Note note)
    {
        // 先处理自身Miss
        note.OnMiss();

        switch (note.GetNoteType())
        {
            case NoteType.Dash:
            {
                DashNote dash = (DashNote)note;
                // 连锁所有关联Tune
                foreach (var tune in dash.GetLinkedTunes())
                {
                    if (_tunePool.Contains(tune))  // 只Miss还没判定过的Tune
                    {
                        tune.OnMiss();
                        _tunePool.Remove(tune);
                    }
                }
                break;
            }
            case NoteType.Tune:
            {
                TuneNote tune = (TuneNote)note;
                DashNote linkedDash = tune.GetLinkedDash();
                // 只有Dash还在池中时才连锁（已结算的不受影响）
                if (linkedDash != null
                    && linkedDash.GetResult() != JudgeResult.Miss
                    && IsInAnyPool(linkedDash))
                {
                    // 连锁Dash
                    linkedDash.OnMiss();
                    // 连锁Dash关联的其他Tune
                    foreach (var otherTune in linkedDash.GetLinkedTunes())
                    {
                        if (otherTune != tune && _tunePool.Contains(otherTune))
                        {
                            otherTune.OnMiss();
                            _tunePool.Remove(otherTune);
                        }
                    }
                    // 从对应池中移除Dash
                    _dotDashHeadPool.Remove(linkedDash);
                    _sustainPool.Remove(linkedDash);
                }
                break;
            }
        }
    }

    #endregion

    #region 工具方法

    /// <summary>
    /// 计算时间差（毫秒）
    /// </summary>
    private int CalcDeltaTime(int time)
    {
        int delta = 0;
        if (_lastUpdateTime != -1)
        {
            delta = time - _lastUpdateTime;
            if (delta < 0) delta = 0;
        }
        _lastUpdateTime = time;
        return delta;
    }

    /// <summary>
    /// 检查某轨道是否按住
    /// </summary>
    private bool IsLaneHeld(List<LaneInput> inputs, int laneIndex)
    {
        foreach (var input in inputs)
        {
            if (input.LaneIndex == laneIndex && input.InputType == LaneInputType.Hold)
                return true;
        }
        return false;
    }

    #endregion
}