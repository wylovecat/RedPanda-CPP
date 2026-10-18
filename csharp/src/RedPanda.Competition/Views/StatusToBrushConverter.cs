using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace RedPanda.Competition.Views;

public sealed class StatusToBrushConverter : IValueConverter
{
    private static readonly IBrush Success = new SolidColorBrush(Color.Parse("#16A34A"));
    private static readonly IBrush Error = new SolidColorBrush(Color.Parse("#DC2626"));
    private static readonly IBrush Warning = new SolidColorBrush(Color.Parse("#D97706"));
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#6B7280"));
    private static readonly IBrush Running = new SolidColorBrush(Color.Parse("#2563EB"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value as string) switch
        {
            "通过" => Success,
            "答案错误" => Error,
            "运行错误" => Error,
            "超时" => Warning,
            "输出超限" => Warning,
            "已停止" => Muted,
            "运行中" => Running,
            _ => Muted
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
