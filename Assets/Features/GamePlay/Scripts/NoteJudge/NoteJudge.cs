using System;
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
    private readonly JudgeZoneCatalog _zoneCatalog;
    private readonly LaneState _laneState;

    // 三个判定池（平铺列表，无需按轨道分组）
    private readonly List<Note> _dotDashHeadPool = new();   // Dot、Dash头部判定池
    private readonly List<Note> _muteHeadPool = new();      // Mute头部判定池
    private readonly List<Note> _sustainPool = new();       // 长按判定池（Dash/Mute头部判定后）

    // Mute宽松判定计时器，按输入分区索引
    private readonly int[] _muteTimer;

    // Miss检查的复用缓冲，避免每帧分配
    private readonly List<Note> _missedNotes = new();

    // View 生成单元（Data + Note），由 ViewSpawner 取走并按 SpawnTime 排序
    private List<NoteSpawnEntry> _spawnEntries = new();

    private int _lastUpdateTime = -1;

    public NoteJudge(NoteJudgeConfig config, ScoringSystem scoring, List<NoteData> noteDataList, JudgeZoneCatalog zoneCatalog, LaneState laneState)
    {
        _config = config;
        _scoring = scoring;
        _zoneCatalog = zoneCatalog ?? throw new ArgumentNullException(nameof(zoneCatalog));
        _muteTimer = new int[ZoneInput.AllZones.Length];
        _laneState = laneState;

        foreach (var data in noteDataList)
        {
            AddNote(data);
        }
    }

    /// <summary>
    /// 一次性转移生成列表所有权；调用后 NoteJudge 不再持有全量生成列表
    /// </summary>
    public List<NoteSpawnEntry> TakeSpawnEntries()
    {
        if (_spawnEntries == null)
            throw new InvalidOperationException("生成列表已被取走");

        var entries = _spawnEntries;
        _spawnEntries = null;
        return entries;
    }

    #region 添加Note

    /// <summary>
    /// 添加Note，根据类型分配到对应判定池，并记录生成单元
    /// </summary>
    private void AddNote(NoteData data)
    {
        var note = CreateNote(data);
        _spawnEntries.Add(new NoteSpawnEntry(data, note));

        // Fake 只供 View 加载，不进入任何判定池
        if (data.Fake.HasValue)
            return;

        switch (data.Type)
        {
            case NoteType.Dot:
            case NoteType.Dash:
                _dotDashHeadPool.Add(note);
                break;
            case NoteType.Mute:
                _muteHeadPool.Add(note);
                break;
        }
    }

    /// <summary>
    /// 根据NoteData创建对应的Note实例
    /// </summary>
    private Note CreateNote(NoteData noteData)
    {
        return noteData.Type switch
        {
            NoteType.Dot => new DotNote(noteData, _config, _scoring, _zoneCatalog, _laneState),
            NoteType.Dash => new DashNote(noteData, _config, _scoring, _zoneCatalog, _laneState),
            NoteType.Mute => new MuteNote(noteData, _config, _scoring, _zoneCatalog, _laneState),
            _ => throw new System.NotSupportedException($"不支持的Note类型: {noteData.Type}")
        };
    }

    #endregion

    #region 帧更新

    /// <summary>
    /// 常规更新（需要频繁调用）
    /// </summary>
    public void NotesUpdate(List<ZoneInput> inputs, int time)
    {
        // 更新Mute计时器
        UpdateMuteTimer(inputs, CalcDeltaTime(time));

        // Mute头部判定（宽松判定生效时自动击中）
        UpdateMuteHeadPool(time);

        // Dot/Dash头部判定
        foreach (var input in inputs)
        {
            if (input.Type == ZoneInputType.Press)
                TryHitDotDash(input, time);
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
        // AutoPlay下所有输入分区视为按住
        for (int i = 0; i < _muteTimer.Length; i++)
        {
            _muteTimer[i] = _config.GoodOffset;
        }

        // Mute头部判定
        UpdateMuteHeadPool(time);

        // Dot/Dash自动击中
        TryAutoHitDotDash(time);

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
    private void UpdateMuteTimer(List<ZoneInput> inputs, int deltaTime)
    {
        // 按住：重置计时器
        foreach (var input in inputs)
        {
            if (input.Type == ZoneInputType.Hold)
                _muteTimer[(int)input.Zone] = _config.GoodOffset;//计时时间
        }

        // 递减
        for (int i = 0; i < _muteTimer.Length; i++)
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
        for (int zoneIndex = 0; zoneIndex < _muteTimer.Length; zoneIndex++)
        {
            if (_muteTimer[zoneIndex] <= 0) continue;

            var input = new ZoneInput((InputZone)zoneIndex, ZoneInputType.Press);

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
    private void TryHitDotDash(ZoneInput input, int time)
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
    /// 长按池更新：检查松开 → Miss，检查结束 → 结算
    /// </summary>
    private void UpdateSustainPool(List<ZoneInput> inputs, int time)
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

            // 松开：判Miss（任意一个可命中的输入分区按住即可）
            if (!IsZoneHeld(inputs, note.GetJudgeZone()))
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

    #region Miss检查

    /// <summary>
    /// 检查两个头部池的超时Miss。池按TargetTime升序，Miss项一定在队首，扫到第一个未Miss即可停
    /// </summary>
    private void CheckMissAll(int time)
    {
        CheckMissPool(_dotDashHeadPool, time);
        CheckMissPool(_muteHeadPool, time);
    }

    private void CheckMissPool(List<Note> pool, int time)
    {
        _missedNotes.Clear();
        
        foreach (var note in pool)
        {
            if (note.IsMissed(time))
                _missedNotes.Add(note);
            else
                break;
        }

        foreach (var note in _missedNotes)
        { 
            HandleMiss(note);
            pool.Remove(note);
        }
    }

    /// <summary>
    /// 处理单个Note的Miss
    /// </summary>
    private void HandleMiss(Note note)
    {
        note.OnMiss();
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
    /// 检查是否存在能命中指定判定区的按住输入
    /// </summary>
    private bool IsZoneHeld(List<ZoneInput> inputs, JudgeZone zone)
    {
        foreach (var input in inputs)
        {
            if (input.Type != ZoneInputType.Hold)
                continue;

            if (_zoneCatalog.Matches(zone, input.Zone))
                return true;
        }
        return false;
    }

    #endregion
}

/// <summary>
/// View 生成单元：视觉数据 + 对应判定对象 + 预定生成时间
/// </summary>
public readonly struct NoteSpawnEntry
{
    public readonly NoteData Data;
    public readonly Note Note;
    public readonly int SpawnTime;

    public NoteSpawnEntry(NoteData data, Note note)
    {
        Data = data;
        Note = note;
        SpawnTime = data.TargetTime + data.AppearOffset;
    }
}
