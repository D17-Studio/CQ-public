using System.Text.Json.Serialization;

namespace CQ_ChartMaker.Models;

/// <summary>
/// 长条（Dash/Mute/Tune/Motion）的放置操作方式。
/// 用 JsonStringEnumConverter 以字符串写入 settings.json，避免枚举顺序变化导致旧配置错位。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LongNotePlaceMode
{
    /// <summary> 两次单击：第一次点击定头，第二次点击定长 </summary>
    TwoClicks,

    /// <summary> 长按：按住左键定头并拖动，松开时定长 </summary>
    Hold
}
