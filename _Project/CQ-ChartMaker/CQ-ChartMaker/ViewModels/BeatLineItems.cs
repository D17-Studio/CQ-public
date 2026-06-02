namespace CQ_ChartMaker.ViewModels;

/// <summary> 节拍线的一个轨道宽度的线段（值类型） </summary>
public struct BeatLineSegmentItem
{
    public double X;
    public double Y;
    public double Width;
}

/// <summary> 节拍线左侧编号（值类型） </summary>
public struct BeatLineLabelItem
{
    public double Y;
    public string Label;  // string 是引用类型，但靠 List 复用稳定
}
