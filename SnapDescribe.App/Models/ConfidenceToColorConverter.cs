using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SnapDescribe.App.Models;

public class ConfidenceToColorConverter : IValueConverter
{
    public static readonly ConfidenceToColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double confidence)
        {
            return Colors.Gray;
        }

        // High confidence (>= 80): Green
        if (confidence >= 80)
        {
            return Color.FromRgb(34, 197, 94); // green-500
        }

        // Medium confidence (60-80): Yellow
        if (confidence >= 60)
        {
            return Color.FromRgb(234, 179, 8); // yellow-500
        }

        // Low confidence (< 60): Red
        return Color.FromRgb(239, 68, 68); // red-500
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
