/// <summary>
/// 输入收集用的判定区语义：按键配置中的七个互斥输入分区。
/// 与 JudgeZone 的差别：不含 Left/Right/All 聚合区，额外键按来源单独成区。
/// </summary>
public enum InputZone
{
    L1,
    L2,
    R1,
    R2,
    ExtraLeft,
    ExtraRight,
    ExtraAll
}

public enum ZoneInputType
{
    Press,
    Hold,
    Release
}

/// <summary> 单条区域输入：输入分区 + 输入类型 </summary>
public struct ZoneInput
{
    public static readonly InputZone[] AllZones =
    {
        InputZone.L1, InputZone.L2, InputZone.R1, InputZone.R2,
        InputZone.ExtraLeft, InputZone.ExtraRight, InputZone.ExtraAll
    };

    public readonly InputZone Zone;
    public readonly ZoneInputType Type;

    public ZoneInput(InputZone zone, ZoneInputType type)
    {
        Zone = zone;
        Type = type;
    }
}
