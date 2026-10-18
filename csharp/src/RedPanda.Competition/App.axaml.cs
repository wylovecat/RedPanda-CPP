using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using RedPanda.Competition.Services;

namespace RedPanda.Competition;

public partial class App : Application
{
    public Models.AppSettings Settings { get; private set; } = new();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Settings = SettingsService.Load();
            ThemeService.Apply(Settings.Theme);

            var window = new MainWindow(Settings);
            desktop.MainWindow = window;

            if (desktop.Args is { Length: > 0 } args && File.Exists(args[0]))
            {
                window.OpenInitialFile(args[0]);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
