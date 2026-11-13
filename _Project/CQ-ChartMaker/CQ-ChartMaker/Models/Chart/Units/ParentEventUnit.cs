using CQMusicGame.Shared;

namespace CQ_ChartMaker.Models;

/// <summary> Parent 事件（瞬时，无时长）的编辑单元 </summary>
public sealed class ParentEventUnit : ILaneEventUnit
{
    public LaneEvent.Parent Data;

    public LaneEventKind Kind => LaneEventKind.Parent;
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

    public bool HasDuration => false;

    public int Duration
    {
        get => 0;
        set { }
    }

    public ILaneEventUnit CloneAt(int laneIndex, int startTime)
    {
        var data = Data;
        data.LaneIndex = laneIndex;
        data.StartTime = startTime;
        return new ParentEventUnit { Data = data };
    }

    public object CaptureData() => Data;

    public void RestoreData(object data) => Data = (LaneEvent.Parent)data;
}
