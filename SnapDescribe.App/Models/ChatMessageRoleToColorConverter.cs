using System;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SnapDescribe.App.Models;

public sealed class ChatMessageRoleToColorConverter : IValueConverter
{
    public static ChatMessageRoleToColorConverter Instance { get; } = new();

    private static readonly SolidColorBrush AssistantBrush = new(Color.Parse("#EEF3FF"));
    private static readonly SolidColorBrush UserBrush = new(Color.Parse("#DCFCE7"));
    private static readonly SolidColorBrush ToolBrush = new(Color.Parse("#F3F4F6"));

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        return value switch
        {
            "user" => UserBrush,
            "tool" => ToolBrush,
            _ => AssistantBrush
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
