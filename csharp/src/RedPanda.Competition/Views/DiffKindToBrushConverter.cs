using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using RedPanda.Competition.Models;
using RedPanda.Competition.Services;

namespace RedPanda.Competition.Views;

public sealed class DiffKindToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var dark = ThemeService.IsDark;
        var color = (value as string) switch
        {
            DiffLine.Deleted => dark ? "#F87171" : "#DC2626",
            DiffLine.Inserted => dark ? "#4ADE80" : "#15803D",
            DiffLine.Note => dark ? "#93C5FD" : "#2563EB",
            _ => dark ? "#9AA3AE" : "#667085"
        };

        return new SolidColorBrush(Color.Parse(color));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
