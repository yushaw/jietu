using System;
using Avalonia.Data.Converters;
using Avalonia.Layout;

namespace SnapDescribe.App.Models;

public sealed class ChatMessageRoleToAlignmentConverter : IValueConverter
{
    public static ChatMessageRoleToAlignmentConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        var role = value as string;
        return role switch
        {
            "user" => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Left
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
