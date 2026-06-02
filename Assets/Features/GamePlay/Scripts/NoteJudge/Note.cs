using System;
using CQMusicGame.Shared;
using UnityEngine;

/// <summary>
/// Note抽象基类，定义所有Note类型的通用接口和共享逻辑
/// </summary>
public abstract class Note
{
    protected readonly NoteData _noteData;
    protected readonly NoteJudgeConfig _config;
    protected JudgeRecord _judgeRecord;
    private readonly ScoringSystem _scoring;
    private Action<JudgeResult> _onHit;
    private Action<JudgeResult> _onMiss;

    protected Note(NoteData noteData, NoteJudgeConfig config , ScoringSystem scoring)
    {
        _noteData = noteData;
        _config = config;
        _scoring = scoring;
        _judgeRecord = new JudgeRecord(JudgeResult.Unknown, 0);
    }

    public void SetOnHit(Action<JudgeResult> callback) => _onHit = callback;
    public void SetOnMiss(Action<JudgeResult> callback) => _onMiss = callback;

    protected void NotifyHit()
    {
        _onHit?.Invoke(_judgeRecord.Result);
    }

    #region 状态查询（外部调用）

    /// <summary>
    /// 判断Note是否可以被击中（子类实现不同的判定窗口）
    /// </summary>
    public abstract bool CanBeHit(LaneInput input, int time);

    /// <summary>
    /// 判断Note是否已经Miss（基类实现，逻辑相同）
    /// </summary>
    public bool IsMissed(int time)
    {
        return time - _noteData.TargetTime > _config.GoodOffset;
    }

    /// <summary>
    /// 判断长按Note的持续时间是否结束
    /// </summary>
    public bool IsSustainEnded(int time)
    {
        return time > _noteData.TargetTime + _noteData.SustainDuration - _config.HoldEarlyReleaseWindow;
    }

    /// <summary>
    /// 当前时间是否还没进入判定窗口（Bad之前），用于遍历时break
    /// </summary>
    public bool IsBeforeJudgmentWindow(int time)
    {
        return time < _noteData.TargetTime - _config.BadOffset;
    }

    /// <summary>
    /// AutoPlay的判定检测
    /// </summary>
    public bool CanAutoHit(int time)
    {
        return time - _noteData.TargetTime >= 0;
    }

    #endregion

    #region 判定操作（外部调用）

    /// <summary>
    /// 击中Note时的处理（子类实现不同的判定行为）
    /// </summary>
    public abstract void OnHit(int time);
    
    /// <summary>
    /// Miss时的处理
    /// </summary>
    public void OnMiss()
    {
        SetResult(JudgeResult.Miss);
        SubmitResult();
        _onMiss?.Invoke(JudgeResult.Miss);
    }
    
    /// <summary>
    /// 提交最终结果（子类按需实现，默认为空）
    /// </summary>
    public void SubmitFinal()
    {
        SubmitResult();
    }

    #endregion

    #region 内部方法（子类共享）

    /// <summary>
    /// 计算判定结果并记录偏移（共享逻辑）
    /// </summary>
    protected JudgeRecord CalcJudgeResult(int time)
    {
        int offset = time - _noteData.TargetTime;
        JudgeResult result;

        if (Mathf.Abs(offset) <= _config.PerfectOffset)
            result = JudgeResult.Perfect;
        else if (offset >= -_config.GoodOffset && offset < -_config.PerfectOffset)
            result = JudgeResult.Early;
        else if (offset <= _config.GoodOffset && offset >= _config.PerfectOffset)
            result = JudgeResult.Late;
        else if (offset >= -_config.BadOffset && offset < -_config.GoodOffset)
            result = JudgeResult.Bad;
        else if (offset > _config.GoodOffset)
            result = JudgeResult.Miss;
        else
        {
            Debug.LogError($"判定异常！偏移量超出预期范围：{offset}");
            result = JudgeResult.Unknown;
        }

        return new JudgeRecord(result, offset);
    }

    /// <summary>
    /// 设置判定记录
    /// </summary>
    protected void SetResult(JudgeRecord record)
    {
        _judgeRecord = record;
    }

    /// <summary>
    /// 设置判定记录（直接传结果，偏移默认为0）
    /// </summary>
    protected void SetResult(JudgeResult result, int offset = 0)
    {
        _judgeRecord = new JudgeRecord(result, offset);
    }

    /// <summary>
    /// 提交结果到计分系统
    /// </summary>
    protected void SubmitResult()
    {
        _scoring.AddRecord(_judgeRecord, _noteData.Type);
    }

    /// <summary>
    /// 获取Note类型
    /// </summary>
    public NoteType GetNoteType() =>  _noteData.Type;

    /// <summary>
    /// 获取当前判定结果
    /// </summary>
    public JudgeResult GetResult() => _judgeRecord.Result;

    /// <summary>
    /// 获取当前判定记录
    /// </summary>
    public JudgeRecord GetRecord() => _judgeRecord;

    /// <summary>
    /// 获取Note数据中的轨道序号
    /// </summary>
    public int GetLaneIndex() => _noteData.LaneIndex;

    /// <summary>
    /// 获取Note正解时间
    /// </summary>
    public int GetTargetTime() => _noteData.TargetTime;

    /// <summary>
    /// 获取持续时间
    /// </summary>
    public int GetSustainDuration() => _noteData.SustainDuration;

    #endregion
}