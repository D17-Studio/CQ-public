using System.Collections.Generic;
using UnityEngine;

public class Note
{
    private readonly NoteData _noteData;
    
    private JudgeResult _judgeResult;

    private NoteJudgeConfig _noteJudgeConfig;//这里后续会使用依赖注入框架
    
   
    //Note视觉效果的引用
    // private NoteVE noteVE;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="noteData">Note数据结构体</param>
    /// <param name="config"></param>
    public Note(NoteData noteData , NoteJudgeConfig config)
    {
        _noteData = noteData;
        _noteJudgeConfig =  config;
        
        // //生成Note视觉效果
        // noteVE = NoteVEPool.Instance.PlaceNoteVE(noteInfo);
    }

    /// <summary>
    /// 击中Note时的方法
    /// </summary>
    /// <param name="time"></param>
    public void OnHit(int time)
    {
        switch (_noteData.Type)
        {
            case NoteType.Hold:
                EvaluateResult(time);
                break;
            case  NoteType.Dash:
                EvaluateResult(JudgeResult.Perfect);
                break;
            default:
                JudgeAndSubmit(time);
                break;
        }
        // noteVE.OnHit(result);
    }

    /// <summary>
    /// 使Note Miss的方法
    /// </summary>
    public void Miss()
    {
        JudgeAndSubmit(JudgeResult.Miss);
        // noteVE.HoldMiss();
    }

    /// <summary>
    /// 计算并保存判定结果
    /// </summary>
    /// <param name="time"></param>
    private void EvaluateResult(int time)
    {
        _judgeResult = GetJudgeResult(time);
    }
    
    /// <summary>
    /// 设定并保存判定结果
    /// </summary>
    /// <param name="result"></param>
    private void EvaluateResult(JudgeResult result)
    {
        _judgeResult = result;
    }

    /// <summary>
    /// 提交结果
    /// </summary>
    public void SubmitResult()
    {
        //这里是执行调用计分系统提交结果的语句
    }

    /// <summary>
    /// 计算并提交结果
    /// </summary>
    /// <param name="time"></param>
    private void JudgeAndSubmit(int time)
    {
        EvaluateResult(time);
        SubmitResult();
    }

    /// <summary>
    /// 设定并提交结果
    /// </summary>
    /// <param name="result"></param>
    private void JudgeAndSubmit(JudgeResult result)
    {
        EvaluateResult(result);
        SubmitResult();
    }
    
    /// <summary>
    /// 计算判定结果
    /// </summary>
    /// <param name="time">当前时间轴时间</param>
    /// <returns>判定结果</returns>
    private JudgeResult GetJudgeResult(int time)
    {
        int offset = time - _noteData.TargetTime;
        
        if (Mathf.Abs(offset) <= _noteJudgeConfig.PerfectOffset)
            return JudgeResult.Perfect;
        if (offset >= -_noteJudgeConfig.GoodOffset && offset < -_noteJudgeConfig.PerfectOffset)
            return JudgeResult.Early;
        if (offset <= _noteJudgeConfig.GoodOffset  && offset >= _noteJudgeConfig.PerfectOffset)
            return JudgeResult.Late;
        if (offset >= -_noteJudgeConfig.BadOffset && offset < -_noteJudgeConfig.GoodOffset)
            return JudgeResult.Bad;
        if (offset > _noteJudgeConfig.GoodOffset)
            return JudgeResult.Miss;
        
        Debug.LogError($"判定异常！偏移量超出预期范围：{offset}");
        return JudgeResult.Unknown;
    }

    /// <summary>
    /// 判断Note是否可以被击中的方法
    /// </summary>
    /// <param name="input">轨道输入结构体</param>
    /// <param name="time">时间轨时间</param>
    /// <returns></returns>
    public bool CanBeHit(LaneInput input , int time)
    {
        if (input.LaneIndex != _noteData.LaneIndex)
        {
            return false;
        }
        
        //判定是否处于miss~bad的可判定状态逻辑
        switch (_noteData.Type)
        {
            case NoteType.Hold:
                return time-_noteData.TargetTime >= -_noteJudgeConfig.GoodOffset && time-_noteData.TargetTime <= _noteJudgeConfig.GoodOffset;
            case NoteType.Dash:
                return time-_noteData.TargetTime >= 0 && time-_noteData.TargetTime <= _noteJudgeConfig.GoodOffset;
            default:
                return time-_noteData.TargetTime >= -_noteJudgeConfig.BadOffset && time-_noteData.TargetTime <= _noteJudgeConfig.GoodOffset;
        }
    }

    /// <summary>
    /// AutoPlay的判定检测
    /// </summary>
    /// <param name="time"></param>
    /// <returns></returns>
    public bool CanAutoHit(int time)
    {
        return time - _noteData.TargetTime >= 0;
    }
    
    /// <summary>
    /// 判断Note是否miss的方法
    /// </summary>
    /// <returns></returns>
    public bool IsMissed(int time)
    {
        //判定是否miss的逻辑
        return time-_noteData.TargetTime > _noteJudgeConfig.GoodOffset;
    }
    
    /// <summary>
    /// 判定长按类Note的持续时间是否结束
    /// </summary>
    /// <returns></returns>
    public bool IsSustainEnded(int time)
    {
        return time > _noteData.TargetTime+_noteData.SustainDuration - _noteJudgeConfig.HoldEarlyReleaseWindow;
    }

    /// <summary>
    /// 判断Hold是否按住的方法
    /// </summary>
    /// <param name="inputs"></param>
    /// <returns></returns>
    public bool IsHeld(Dictionary<int, bool> inputs)
    {
        return inputs[_noteData.LaneIndex];
    }

    public NoteType GetNoteType()
    {
        return _noteData.Type;
    }
    
    /// <summary>
    /// 销毁Note的方法
    /// </summary>
    public void Destroy()
    {
        // if (noteVE)
        //     noteVE.ReturnNote();
    }
}


