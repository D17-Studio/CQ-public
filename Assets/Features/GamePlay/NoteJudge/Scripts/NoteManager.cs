using System.Collections.Generic;
using System.Linq;
using UnityEngine;


public class NoteManager
{
    private NoteJudgeConfig _noteJudgeConfig;
    
    /// <summary>
    /// 存放头部还未点击判定的Note
    /// </summary>
    private readonly List<Note> _pendingHeadNotes = new List<Note>();

    /// <summary>
    /// 存放需要参与长按判定的Note
    /// </summary>
    private readonly List<Note> _sustainedNotes =  new List<Note>();

    /// <summary>
    /// 存放头部还未判定的Dash
    /// </summary>
    private readonly List<Note> _specialHeadNotes =  new List<Note>();

    //用于计算dash的宽松判定
    private readonly Dictionary<int, int> _dashTimer = new Dictionary<int, int>()
    {
        { -3, 0 },
        { -2, 0 },
        { -1, 0 },
        { 0, 0 },
        { 1, 0 },
        { 2, 0 },
        { 3, 0 },
    };

    //长按输入
    private readonly Dictionary<int, bool> _holdInput = new Dictionary<int, bool>()
    {
        { -3, false },
        { -2, false },
        { -1, false },
        { 0, false },
        { 1, false },
        { 2, false },
        { 3, false },
    };

    //用于AutoPlay输入
    private readonly Dictionary<int, bool> _autoHoldInput = new Dictionary<int, bool>
    {
        { -3, true },
        { -2, true },
        { -1, true },
        { 0, true },
        { 1, true },
        { 2, true },
        { 3, true },
    };
    
    private int _lastUpdateTime = -1;

    /// <summary>
    /// 添加Note
    /// </summary>
    /// <param name="data"></param>
    public void AddNote(NoteData data)
    {
        var note = new Note(data , _noteJudgeConfig);
        switch (note.GetNoteType())
        {
            case NoteType.Dash:
                _specialHeadNotes.Add(note);
                break;
            default:
                _pendingHeadNotes.Add(note);
                break;
        }
    }
    
    /// <summary>
    /// 当前轨道更新逻辑，需要传入输入类型（此方法需要频繁调用）
    /// </summary>
    /// <param name="inputs"></param>
    /// <param name="time"></param>
    public void NotesUpdate(List<LaneInput> inputs , int time)
    {
        HoldInputUpdate(inputs);

        UpdatePendingHeads(inputs, time);
        
        UpdateSpecialHeads(_holdInput, time);
        
        UpdateSustainedHeads(_holdInput, time);
    }

    //更新Hold输入字典
    private void HoldInputUpdate(List<LaneInput> inputs)
    {
        foreach (var pair in _holdInput)
        {
            _holdInput[pair.Key] =  false;
        }

        foreach (var input in inputs)
        {
            if (input.InputType == LaneInputType.Hold)
            {
                _holdInput[input.LaneIndex] = true;
            }
        }
    }

    /// <summary>
    /// AutoPlay轨道更新
    /// </summary>
    /// <param name="time"></param>
    public void AutoPlayUpdate(int time)
    {
        TryAutoHit(time);
        
        UpdateSpecialHeads(_autoHoldInput, time);
        
        UpdateSustainedHeads(_autoHoldInput, time);
    }

    private void UpdatePendingHeads(List<LaneInput> inputs, int time)
    {
        //尝试击打
        foreach (var input in inputs)
        {
            if (input.InputType == LaneInputType.Press)
            {
                TryHit(input,time);
            }
        }
        
        //判断Miss
        while (_pendingHeadNotes.Count != 0 && _pendingHeadNotes[0].IsMissed(time) == true)
        {
            _pendingHeadNotes[0].Miss();//使Note Miss
            _pendingHeadNotes.RemoveAt(0);
        }
    }

    private void UpdateSustainedHeads(Dictionary<int, bool> holdInputs, int time)
    {
        for (int i = _sustainedNotes.Count - 1; i >= 0; i--)
        {
            //判断长按是否结束
            if (_sustainedNotes[i].IsSustainEnded(time) == true)
            {
                _sustainedNotes[i].SubmitResult();
                _sustainedNotes.RemoveAt(i);
                continue;
            }
            
            //判断长按是否miss
            if (_sustainedNotes[i].IsHeld(holdInputs) == false)
            {
                _sustainedNotes[i].Miss();
                _sustainedNotes.RemoveAt(i);
                continue;
            }
        }
    }

    private void UpdateSpecialHeads(Dictionary<int , bool> holdInputs, int time)
    {
        //更新Dash宽松判断时间
        foreach (var pair in holdInputs)
        {
            if (pair.Value == true)
            {
                _dashTimer[pair.Key] = _noteJudgeConfig.GoodOffset;
            }
        }
        
        // 计算时间差（毫秒）
        int deltaTime = 0;
        if (_lastUpdateTime != -1)
        {
            deltaTime = time - _lastUpdateTime;
            if (deltaTime < 0) deltaTime = 0; // 防止时间倒退
        }
        _lastUpdateTime = time;

        // 更新 Dash 宽松判断时间（使用 deltaTime 递减）
        foreach (var pair in _dashTimer)
        {
            if (pair.Value > 0) // 只对正数计时器递减
            {
                _dashTimer[pair.Key] = Mathf.Max(0, pair.Value - deltaTime);
            }
        }

        //判断Dash
        foreach (var pair in _dashTimer)
        {
            if (pair.Value >= 0)
            {
                // _dashTimer[pair.Key] -=  (int)(Time.deltaTime*1000);
                for (int i = _specialHeadNotes.Count - 1; i >= 0; i--)
                {
                    if (_specialHeadNotes[i].CanBeHit(new LaneInput(pair.Key , LaneInputType.Press),time) == true)
                    {
                        _specialHeadNotes[i].OnHit(time);//击中Note的调用
                        _sustainedNotes.Add(_specialHeadNotes[i]);
                        _specialHeadNotes.RemoveAt(i);
                    }
                }
            }
        }
        
        //判断Miss
        while (_specialHeadNotes.Count != 0 && _specialHeadNotes[0].IsMissed(time) == true)
        {
            _specialHeadNotes[0].Miss();//使Note Miss
            _specialHeadNotes.RemoveAt(0);
        }
    }
    
    /// <summary>
    /// 尝试击打Note
    /// </summary>
    private void TryHit(LaneInput input , int time)
    {
        for (int i = _pendingHeadNotes.Count - 1; i >= 0; i--)
        {
            if (_pendingHeadNotes[i].CanBeHit(input,time) == true)
            {
                _pendingHeadNotes[i].OnHit(time);//击中Note的调用
                if (_pendingHeadNotes[i].GetNoteType() == NoteType.Hold)
                {
                    _sustainedNotes.Add(_pendingHeadNotes[i]);
                }
                _pendingHeadNotes.RemoveAt(i);
                break;
            }
        }
    }

    /// <summary>
    /// AutoPlay尝试打击
    /// </summary>
    /// <param name="time"></param>
    private void TryAutoHit(int time)
    {
        for (int i = _pendingHeadNotes.Count - 1; i >= 0; i--)
        {
            if (_pendingHeadNotes[i].CanAutoHit(time) == true)
            {
                _pendingHeadNotes[i].OnHit(time);//击中Note的调用
                if (_pendingHeadNotes[i].GetNoteType() == NoteType.Hold)
                {
                    _sustainedNotes.Add(_pendingHeadNotes[i]);
                }
                _pendingHeadNotes.RemoveAt(i);
            }
        }
    }
    
    /// <summary>
    /// 清空
    /// </summary>
    public void Clear()
    {
        foreach (Note note in _pendingHeadNotes)
        {
            note.Destroy();
        }
        foreach (Note note in _sustainedNotes)
        {
            note.Destroy();
        }
        foreach (Note note in _specialHeadNotes)
        {
            note.Destroy();
        }
        
        _pendingHeadNotes.Clear();
        _sustainedNotes.Clear();
        _specialHeadNotes.Clear();
    }
}
    
