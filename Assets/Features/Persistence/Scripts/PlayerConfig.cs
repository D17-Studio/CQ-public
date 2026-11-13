using System;

/// <summary>
/// 玩家配置数据（磁盘数据单元）：全局流速 / 输入偏移；Version = 格式迁移钩子。
/// </summary>
[Serializable]
public class PlayerConfig
{
    public int Version;
    public float ScrollSpeed = DefaultScrollSpeed;
    public int InputOffsetMs;

    /// <summary>默认全局流速。</summary>
    public const float DefaultScrollSpeed = 1000f;
}
