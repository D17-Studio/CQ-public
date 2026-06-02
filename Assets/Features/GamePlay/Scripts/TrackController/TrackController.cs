using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using CQMusicGame.Shared;
using CriWare;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Zenject;

public class TrackController : MonoBehaviour
{
    [Inject] private readonly ChartSheet _chart;
    [Inject] private readonly NoteJudge _noteJudge;

    private void Start()
    {
        string n = "";
        foreach (var note in _chart.GetNotes())
        {
            n += $"{note.Type.ToString()}:{note.TargetTime}\n";
            if (note.LinkedTunes != null)
            {
                foreach (var tune in note.LinkedTunes)
                {
                    n += $"- Tune:{tune.TargetTime}\n";
                }
            }
        }
        Debug.Log(n);
        
        PlayTrack().Forget();
    }

    public int TrackTimeMs
    {
        get
        {
            if (_fmodCorePlayer == null)
                return 0;
            return (int)_fmodCorePlayer.GetCurrentPositionMs();
        }
    }
    
    private FMODCorePlayer _fmodCorePlayer;
    
    private readonly Queue<LaneInput> _inputQueue = new Queue<LaneInput>();
    private readonly List<LaneInput> _inputList = new List<LaneInput>();
    
    private bool isPaused = false;

    /// <summary>
    /// 开始乐曲
    /// </summary>
    public async UniTaskVoid PlayTrack()
    {
        _fmodCorePlayer = new FMODCorePlayer();
        
        _fmodCorePlayer.PlayAudio(GetFullMusicPath("Euraidd - Kusua"));//播放乐曲
        
        uint totalLengthMs = _fmodCorePlayer.GetTotalLengthMs();//获取乐曲总长度
  
        while (TrackTimeMs < totalLengthMs)//核心循环
        {
            if (isPaused == false)
            {
                TrackUpdate();
            }
            await UniTask.Delay(5);
        }
    }

    //乐曲进行时的更新
    private void TrackUpdate()
    {
        _noteJudge.AutoPlayUpdate(TrackTimeMs);
        //_noteJudge.NotesUpdate(GetPendingInputs(),TrackTimeMs);//更新Note判定
    }

    //添加Note
 
    
    void Update()
    {
        //更新输入队列
        for (int i = -3; i <= 3; i++)
        {
            if (InputSystem.Instance.LaneDown(i))
            {
                _inputQueue.Enqueue(new LaneInput(i,LaneInputType.Press));
            }

            if (InputSystem.Instance.LaneUp(i))
            {
                _inputQueue.Enqueue(new LaneInput(i,LaneInputType.Release));
            }
        }
    }
    
    //获取输入列表 
    private List<LaneInput> GetPendingInputs()
    {
        _inputList.Clear();
        while (_inputQueue.Count > 0)
            _inputList.Add(_inputQueue.Dequeue());
        for (int i = -3; i <= 3; i++)
        {
            if (InputSystem.Instance.LaneHold(i))
            {
                _inputList.Add(new LaneInput(i,LaneInputType.Hold));
            }
        }
        return _inputList;
    }

    //获取完整乐曲wav文件路径
    private string GetFullMusicPath(string audioPath)
    {
        audioPath += ".wav";
        return Path.Combine(Application.streamingAssetsPath,"TracksWav", audioPath); 
    }
}
