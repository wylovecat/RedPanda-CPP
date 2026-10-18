using Avalonia;
using Avalonia.Styling;

namespace RedPanda.Competition.Services;

public static class ThemeService
{
    public static bool IsDark { get; private set; }

    public static void Apply(string theme)
    {
        IsDark = string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase);

        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        }
    }
}
