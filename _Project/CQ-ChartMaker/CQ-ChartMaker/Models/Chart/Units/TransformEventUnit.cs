using CQMusicGame.Shared;

namespace CQ_ChartMaker.Models;

/// <summary> Transform 事件的编辑单元 </summary>
public sealed class TransformEventUnit : ILaneEventUnit
{
    public LaneEvent.Transform Data;

    public LaneEventKind Kind => LaneEventKind.Transform;
    public bool IsSelected { get; set; }

    public int LaneIndex
    {
        get => Data.LaneIndex;
        set => Data.LaneIndex = value;
    }

    public int StartTime
    {
        get => Data.StartTime;
        set => Data.StartTime = value;
    }

    public bool HasDuration => true;

    public int Duration
    {
        get => Data.Duration;
        set => Data.Duration = value;
    }

    public ILaneEventUnit CloneAt(int laneIndex, int startTime)
    {
        var data = Data;
        data.LaneIndex = laneIndex;
        data.StartTime = startTime;
        return new TransformEventUnit { Data = data };
    }

    public object CaptureData() => Data;

    public void RestoreData(object data) => Data = (LaneEvent.Transform)data;
}
