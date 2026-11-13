using System;
using Avalonia;
using Avalonia.Media;

namespace CQ_ChartMaker.Common;

/// <summary>
/// 应用主题色管理。
/// 基础色集中定义在此处，启动时写入 Avalonia 的强调色资源；
/// 所有通过 DynamicResource 引用 SystemAccentColor 系列的控件（Fluent 默认控件与自定义样式）会一起跟随。
/// </summary>
public static class AppTheme
{
    /// <summary> 当前应用主题色（后续如需支持切换，调用 <see cref="ApplyAccent"/> 传入新颜色即可） </summary>
    public static Color AccentColor { get; private set; } = Color.Parse("#8E8CD8");

    /// <summary>
    /// 应用主题色：覆盖 Fluent 主题的强调色资源。
    /// 除基础色外还需写入 Light1~3 / Dark1~3 变体，否则悬浮、按下等状态仍会使用主题默认的蓝色。
    /// </summary>
    /// <param name="color">新的主题色</param>
    public static void ApplyAccent(Color color)
    {
        AccentColor = color;

        // Application.Current.Resources 是应用级资源字典；
        // 在此覆盖同名资源后，界面上 {DynamicResource ...} 的引用会重新解析到新值。
        var resources = Application.Current!.Resources;
        resources["SystemAccentColor"] = color;
        resources["SystemAccentColorLight1"] = MixWith(color, Colors.White, 0.10);
        resources["SystemAccentColorLight2"] = MixWith(color, Colors.White, 0.20);
        resources["SystemAccentColorLight3"] = MixWith(color, Colors.White, 0.30);
        resources["SystemAccentColorDark1"] = MixWith(color, Colors.Black, 0.10);
        resources["SystemAccentColorDark2"] = MixWith(color, Colors.Black, 0.20);
        resources["SystemAccentColorDark3"] = MixWith(color, Colors.Black, 0.30);
    }

    /// <summary>
    /// 按比例把颜色向目标色混合，用于生成 Fluent 主题要求的明暗变体。
    /// t = 0 保持原色，t = 1 完全变为 target。
    /// </summary>
    private static Color MixWith(Color color, Color target, double t)
    {
        return Color.FromRgb(
            (byte)Math.Round(color.R + (target.R - color.R) * t),
            (byte)Math.Round(color.G + (target.G - color.G) * t),
            (byte)Math.Round(color.B + (target.B - color.B) * t));
    }
}
