using Avalonia;
using CQ_ChartMaker.Models;

namespace CQ_ChartMaker.Common;

/// <summary>
/// Inspector 面板的两套语言开关（均由设置-偏好控制）：
/// Inspector 界面汉化控制属性标签/按钮等固定文案，缓动曲线汉化单独控制缓动种类与方向下拉。
/// 固定文案写入应用资源，XAML 通过 DynamicResource 读取，切换时所有绑定自动刷新；
/// 代码拼接的文案（事件类型标题、Tune 行等）由 InspectorViewModel 监听设置变化后主动刷新。
/// </summary>
public static class InspectorLocalization
{
    /// <summary> Inspector 界面是否汉化（false = 按结构体属性成员原样显示） </summary>
    public static bool IsInspectorLocalized { get; private set; }

    /// <summary> 缓动曲线是否汉化（独立开关，false = 保持英文） </summary>
    public static bool IsEaseCurveLocalized { get; private set; }

    /// <summary> 按已加载的用户设置应用语言 </summary>
    public static void Apply(UserSettingsModel settings)
        => Apply(settings.IsInspectorLocalized, settings.IsEaseCurveLocalized);

    /// <summary> 应用两个语言开关并刷新动态资源 </summary>
    public static void Apply(bool inspectorLocalized, bool easeCurveLocalized)
    {
        IsInspectorLocalized = inspectorLocalized;
        IsEaseCurveLocalized = easeCurveLocalized;
        UpdateResources();
    }

    /// <summary> Inspector 固定文案：汉化开用中文，否则用英文原文 </summary>
    public static string Text(string chinese, string english)
        => IsInspectorLocalized ? chinese : english;

    /// <summary> 缓动曲线名字：曲线汉化开用中文，否则用英文原文 </summary>
    public static string CurveText(string chinese, string english)
        => IsEaseCurveLocalized ? chinese : english;

    /// <summary>
    /// 把 Inspector 全部固定文案写入应用资源。
    /// DynamicResource 会监听资源字典的变化，无需重建 InspectorView。
    /// </summary>
    private static void UpdateResources()
    {
        if (Application.Current is not { } app) return;

        var r = app.Resources;
        r["Inspector_Type"] = Text("类型：", "Type:");
        r["Inspector_LaneIndex"] = Text("轨道：", "LaneIndex:");
        r["Inspector_NoteTargetTime"] = Text("判定时间：", "TargetTime:");
        r["Inspector_SustainDur"] = Text("持续时长：", "SustainDur:");
        r["Inspector_AppearOffset"] = Text("出现偏移：", "AppearOffset:");
        r["Inspector_Fake"] = Text("Fake：", "Fake:");
        r["Inspector_TuneList"] = Text("Tune 列表：", "TuneList:");
        r["Inspector_AddTune"] = Text("＋ 添加 Tune", "AddTune+");
        r["Inspector_Back"] = Text("返回 ↩", "Back ↩");
        r["Inspector_TuneTargetTime"] = Text("目标时间：", "TargetTime:");
        r["Inspector_UseRelative"] = Text("使用相对坐标：", "UseRelative:");
        r["Inspector_TargetLane"] = Text("目标轨道：", "TargetLane:");
        r["Inspector_PosValue"] = Text("坐标值：", "PosValue:");
        r["Inspector_Duration"] = Text("持续时间：", "Duration:");
        r["Inspector_StartTime"] = Text("起始时间：", "StartTime:");
        r["Inspector_BPM"] = Text("BPM：", "BPM:");
        r["Inspector_Anchor"] = Text("锚点：", "Anchor:");
        r["Inspector_XCurve"] = Text("X 曲线", "X Curve");
        r["Inspector_YCurve"] = Text("Y 曲线", "Y Curve");
        r["Inspector_RotationCurve"] = Text("旋转曲线", "Rotation Curve");
        r["Inspector_Head"] = Text("头", "Head");
        r["Inspector_Line"] = Text("线条", "Line");
        r["Inspector_Label"] = Text("标签", "Label");
        r["Inspector_OpacityCurve"] = Text("不透明度曲线：", "Opacity Curve:");
        r["Inspector_LengthCurve"] = Text("长度曲线：", "Length Curve:");
        r["Inspector_SpeedMode"] = Text("流速模式：", "SpeedMode:");
        r["Inspector_SpeedCurve"] = Text("流速曲线：", "Speed Curve:");
        r["Inspector_TargetZone"] = Text("目标判定区：", "TargetZone:");
        r["Inspector_ParentLane"] = Text("父轨道：", "ParentLane:");
        r["Inspector_From"] = Text("起始值：", "From:");
        r["Inspector_To"] = Text("结束值：", "To:");
        r["Inspector_Ease"] = Text("缓动：", "Ease:");
    }
}
