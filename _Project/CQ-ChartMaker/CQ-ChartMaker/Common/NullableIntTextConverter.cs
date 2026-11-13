using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace CQ_ChartMaker.Common;

/// <summary>
/// 可空整数与文本框之间的转换器。
/// 空字符串按 null 处理（例如 ParentLane 的 null 表示解绑），
/// 其余输入交给 Avalonia 默认转换器，非法文本仍会产生校验错误并在失焦时还原旧值。
/// </summary>
public sealed class NullableIntTextConverter : IValueConverter
{
    /// <summary>
    /// 源值 → 文本框文字。默认转换器会把 null 变成空字符串、把数字变成数字文本。
    /// </summary>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => DefaultValueConverter.Instance.Convert(value, targetType, parameter, culture);

    /// <summary>
    /// 文本框文字 → 源值。清空输入框时返回 null（解绑），其余文本沿用默认转换与校验逻辑。
    /// </summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string text && string.IsNullOrWhiteSpace(text))
            return null;

        return DefaultValueConverter.Instance.ConvertBack(value, targetType, parameter, culture);
    }
}
