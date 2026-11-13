namespace CQ_ChartMaker.Models;

/// <summary>
/// 六类 LaneEvent 编辑单元的统一接口。
/// 只声明跨类型操作需要的成员，具体数据仍由六个独立 Unit 类各自持有。
/// </summary>
public interface ILaneEventUnit
{
    /// <summary> 事件类型 </summary>
    LaneEventKind Kind { get; }

    /// <summary> 渲染层选中镜像标记，仅由 ChartViewModel.ApplySelection 写入 </summary>
    bool IsSelected { get; set; }

    /// <summary> 视觉轨道编号 </summary>
    int LaneIndex { get; set; }

    /// <summary> 开始时间（毫秒） </summary>
    int StartTime { get; set; }

    /// <summary> 是否带持续时间；Zone / Parent 为 false </summary>
    bool HasDuration { get; }

    /// <summary> 持续时间；HasDuration 为 false 时固定为 0，赋值忽略 </summary>
    int Duration { get; set; }

    /// <summary> 复制：值拷贝全部字段，只改写轨道与开始时间 </summary>
    ILaneEventUnit CloneAt(int laneIndex, int startTime);

    /// <summary> 捕获当前结构体快照（装箱），供编辑命令撤销使用 </summary>
    object CaptureData();

    /// <summary> 还原结构体快照 </summary>
    void RestoreData(object data);
}
