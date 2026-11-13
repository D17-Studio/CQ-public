/// <summary>
/// 打歌场景的显式阶段（由 TrackController 单一持有、单一推进）。
/// 流程：Loading → Playing ⇄ Paused →（Resuming 倒数）→ Playing，曲终 = Result。
/// 消费者 = 暂停面板显隐与输入路由；状态判断不要再用面板 SetActive 互猜。
/// </summary>
public enum TrackPhase
{
    /// <summary>SoundBank 加载与曲目 Post 前：不推进时间源、不采集输入。</summary>
    Loading,

    /// <summary>正常打歌：时间源推进，判定与输入采集运行。</summary>
    Playing,

    /// <summary>暂停：音乐 Wwise Pause 驻留，时间源冻结、输入不采集。</summary>
    Paused,

    /// <summary>恢复倒数（3-2-1）中：音乐仍暂停，倒数走真实时间；归零自动续播回 Playing。</summary>
    Resuming,

    /// <summary>曲终（自然结束）：时间源冻结在曲末，结算面板接管；主动 Stop 不进入本阶段。</summary>
    Result,
}
