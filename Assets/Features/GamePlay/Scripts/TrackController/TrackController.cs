using System;
using System.Collections.Generic;
using CQMusicGame.Shared;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Zenject;

public class TrackController : MonoBehaviour
{
    [Inject] private readonly ChartSheet _chart;
    [Inject] private readonly NoteJudge _noteJudge;
    [Inject] private readonly TrackSelectionService _trackSelection;

    private void Start()
    {
        _wwiseConfig = _trackSelection.SelectedTrack.WwiseConfig;
        PlayTrack().Forget();
    }

    public int TrackTimeMs { get; private set; }

    private readonly Queue<LaneInput> _inputQueue = new();
    private readonly List<LaneInput> _inputList = new();
    
    private bool _isStarted = false;
    private bool _isPaused = false;
    
    private uint _playingID = AkUnitySoundEngine.AK_INVALID_PLAYING_ID;
    private AkSegmentInfo _segmentInfo = new();
    
    private WwiseSongPlayConfig _wwiseConfig;

    /// <summary>
    /// 开始乐曲
    /// </summary>
    public async UniTaskVoid PlayTrack()
    {
        var tcs = new UniTaskCompletionSource<AKRESULT>();
        
        _wwiseConfig.SoundBank.LoadAsync((uint in_bankID, IntPtr in_InMemoryBankPtr, AKRESULT in_eLoadResult, object in_Cookie) =>
        {
            tcs.TrySetResult(in_eLoadResult);
        });
        
        AKRESULT loadResult = await tcs.Task;
        
        if (loadResult != AKRESULT.AK_Success)
        {
            Debug.LogError($"SoundBank 加载失败: {loadResult}");
            return;
        }
        
        float minDuration = 0f;
        float maxDuration = 0f;
        AkUtilities.GetEventDurations(_wwiseConfig.PlayMusic.Id,  ref maxDuration, ref minDuration);
        
        uint totalLengthMs = (uint)(1000 * minDuration);
        
        _playingID = _wwiseConfig.PlayMusic.Post(
            gameObject,
            (uint)(AkCallbackType.AK_EnableGetMusicPlayPosition),
            (in_cookie, in_type, in_info) => { }, // 空回调
            null
        );
        
  
        _isStarted = true;//标记开始乐曲
    }

    //乐曲进行时的更新
    private void TrackUpdate()
    {
        //_noteJudge.AutoPlayUpdate(TrackTimeMs);
        _noteJudge.NotesUpdate(GetPendingInputs(),TrackTimeMs);//更新Note判定
    }
    
    
    void Update()
    {
        TrackTimeMs = GetTrackTime();
        Debug.Log("TrackTimeMs: " + TrackTimeMs);

        if (_isStarted && !_isPaused)
        {
            TrackUpdate();
        }
        
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

    void OnDestroy()
    {
        StopAndUnloadTrack();
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

    private int GetTrackTime()
    {
        if (_playingID == AkUnitySoundEngine.AK_INVALID_PLAYING_ID) return 0;
        
        // 查询当前音乐段落的详细信息
        AKRESULT result = AkUnitySoundEngine.GetPlayingSegmentInfo(_playingID, _segmentInfo);
        
        if (result == AKRESULT.AK_Success)
        {
            //当前播放位置，单位：毫秒
            return _segmentInfo.iCurrentPosition;
        }
        
        return 0;
    }
    
    public void StopAndUnloadTrack()
    {
        // 1. 停止当前音乐事件
        if (_playingID != AkUnitySoundEngine.AK_INVALID_PLAYING_ID)
        {
            AkUnitySoundEngine.ExecuteActionOnEvent(
                _playingID,
                AkActionOnEventType.AkActionOnEventType_Stop,
                gameObject,
                0,
                AkCurveInterpolation.AkCurveInterpolation_Linear
            );
            _playingID = AkUnitySoundEngine.AK_INVALID_PLAYING_ID;
        }

        // 2. 卸载 SoundBank
        if (_wwiseConfig.SoundBank != null)
        {
            _wwiseConfig.SoundBank.Unload();
        }
    }
}
