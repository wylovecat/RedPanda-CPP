using Avalonia;
using RedPanda.Competition.Services;

namespace RedPanda.Competition;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        CrashLogger.Install();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
