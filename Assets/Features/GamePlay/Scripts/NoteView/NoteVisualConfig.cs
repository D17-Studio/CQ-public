using System;
using UnityEngine;

/// <summary>
/// Note 视觉配置（形状 + Miss/Bad 表现）：按 NoteType 分组，由 NoteConfigInstaller 注入。
/// </summary>
[Serializable]
public class NoteVisualConfig
{
    [Header("Dot 视觉")]
    public NoteDotShape Dot = new();

    [Header("Dash 视觉")]
    public NoteDashShape Dash = new();

    [Header("Mute 视觉")]
    public NoteMuteShape Mute = new();

    [Header("Miss / Bad 表现")]
    public NoteFeedbackConfig Feedback = new();
}

/// <summary>Dot 形状参数（圆形）。</summary>
[Serializable]
public class NoteDotShape
{
    [Header("圆半径（世界单位）")]
    public float CircleRadius = 0.3f;

    [Header("圆分段数（越大越圆滑）")]
    public int CircleSegments = 32;

    [Header("描边宽度（世界单位）")]
    public float StrokeWidth = 0.08f;
}

/// <summary>Dash 形状参数（头部圆形 + 主体折线）。</summary>
[Serializable]
public class NoteDashShape
{
    [Header("头部圆半径（世界单位）")]
    public float CircleRadius = 0.3f;

    [Header("头部圆分段数（越大越圆滑）")]
    public int CircleSegments = 32;

    [Header("主体宽度（世界单位）")]
    public float BodyWidth = 0.3f;

    [Header("描边宽度（世界单位）")]
    public float StrokeWidth = 0.08f;
}

/// <summary>Mute 形状参数（仅主体折线）。</summary>
[Serializable]
public class NoteMuteShape
{
    [Header("主体宽度（世界单位）")]
    public float BodyWidth = 0.3f;

    [Header("描边宽度（世界单位）")]
    public float StrokeWidth = 0.05f;
}

/// <summary>Miss / Bad 的淡出表现。</summary>
[Serializable]
public class NoteFeedbackConfig
{
    [Header("Miss 淡出时长（毫秒）")]
    public float MissFadeMs = 70f;

    [Header("Bad 淡出时长（毫秒）")]
    public float BadFadeMs = 100f;

    [Header("Miss 颜色（作用于填充）")]
    public Color MissColor = new(0.5f, 0.5f, 0.5f, 1f);

    [Header("Bad 颜色（作用于填充）")]
    public Color BadColor = Color.red;
}
