using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

// namespace CQ_ChartMaker; // 原命名空间
namespace CQ_ChartMaker.Common;

public class BoolToPlayPauseIconConverter : IValueConverter
{
    // 定义两个静态几何图形，避免重复创建
    private static readonly StreamGeometry PlayIcon = StreamGeometry.Parse("M 8,5 L 19,12 L 8,19 Z");
    private static readonly StreamGeometry PauseIcon = StreamGeometry.Parse("M 6,5 H 10 V 19 H 6 Z M 14,5 H 18 V 19 H 14 Z");

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isPaused)
        {
            // 暂停时显示“播放”图标（三角形）
            return isPaused ? PlayIcon : PauseIcon;
        }
        return PlayIcon;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}