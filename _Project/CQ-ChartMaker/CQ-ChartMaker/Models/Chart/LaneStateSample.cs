using CQMusicGame.Shared;

namespace CQ_ChartMaker.Models;

/// <summary>
/// 某一时刻的轨道视觉状态采样值。
/// 启用动画初始值时，Inspector 用它作为 StartPos/StartAngle/StartOpacity 的默认值，
/// 保证勾选瞬间画面与"从上一动画状态采样"的结果一致，不发生跳变。
/// </summary>
public readonly record struct LaneStateSample(
    Vec2 Position,
    float Rotation,
    float HeadOpacity,
    float LineOpacity,
    float LabelOpacity,
    float NoteOpacity);
