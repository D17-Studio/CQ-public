
/// <summary>
/// 轨道输入枚举类型
/// </summary>
public enum LaneInputType
{
    Press, 
    Hold,   
    Release
}

public struct LaneInput 
{
    public readonly int LaneIndex;
    public readonly LaneInputType InputType;

    public LaneInput(int laneIndex, LaneInputType inputType)
    {
        LaneIndex = laneIndex;
        InputType = inputType;
    }
}
