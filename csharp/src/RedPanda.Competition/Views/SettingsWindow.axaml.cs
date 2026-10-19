using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using RedPanda.Competition.Models;
using RedPanda.Competition.Services;

namespace RedPanda.Competition.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _working;

    public SettingsWindow()
        : this(new AppSettings(), null)
    {
    }

    public SettingsWindow(AppSettings settings, string? detectedCompiler)
    {
        _working = settings.Clone();
        InitializeComponent();

        VersionText.Text = $"{AppInfo.ProductName} v{AppInfo.Version}";
        StandardBox.ItemsSource = new[] { "c++11", "c++14", "c++17", "c++20", "c++23" };
        StandardBox.SelectedItem = _working.CppStandard;
        if (StandardBox.SelectedItem is null)
        {
            StandardBox.ItemsSource = StandardBox.ItemsSource.Cast<string>().Append(_working.CppStandard).ToArray();
            StandardBox.SelectedItem = _working.CppStandard;
        }

        OptimizationBox.ItemsSource = new[] { "-O0", "-O1", "-O2", "-O3", "-Os" };
        OptimizationBox.SelectedItem = _working.Optimization;
        if (OptimizationBox.SelectedItem is null)
        {
            OptimizationBox.ItemsSource = OptimizationBox.ItemsSource.Cast<string>().Append(_working.Optimization).ToArray();
            OptimizationBox.SelectedItem = _working.Optimization;
        }

        ThemeBox.ItemsSource = new[] { "浅色", "深色" };
        ThemeBox.SelectedIndex = _working.Theme == "Dark" ? 1 : 0;

        CompilerPathBox.Text = _working.CompilerPath;
        if (string.IsNullOrWhiteSpace(_working.CompilerPath) && !string.IsNullOrEmpty(detectedCompiler))
        {
            CompilerPathBox.PlaceholderText = "自动检测：" + detectedCompiler;
        }

        ExtraArgsBox.Text = _working.ExtraCompilerArgs;
        TimeLimitBox.Value = _working.TimeLimitMs;
        MemoryLimitBox.Value = _working.MemoryLimitMb;
        OutputLimitBox.Value = _working.OutputLimitKb;
        DisplayLimitBox.Value = _working.OutputDisplayLimitKb;
        FontSizeBox.Value = (decimal)_working.EditorFontSize;
        ShowLineNumbersBox.IsChecked = _working.ShowLineNumbers;
    }

    private async void Browse_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择 g++.exe",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("g++ 编译器")
                {
                    Patterns = new[] { "g++.exe", "*.exe" }
                }
            }
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            CompilerPathBox.Text = path;
        }
    }

    private void Save_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _working.CompilerPath = CompilerPathBox.Text?.Trim() ?? string.Empty;
        _working.CppStandard = StandardBox.SelectedItem as string ?? "c++17";
        _working.Optimization = OptimizationBox.SelectedItem as string ?? "-O2";
        _working.ExtraCompilerArgs = ExtraArgsBox.Text?.Trim() ?? string.Empty;
        _working.TimeLimitMs = (int)(TimeLimitBox.Value ?? 2000);
        _working.MemoryLimitMb = (int)(MemoryLimitBox.Value ?? 256);
        _working.OutputLimitKb = (int)(OutputLimitBox.Value ?? 1024);
        _working.OutputDisplayLimitKb = (int)(DisplayLimitBox.Value ?? 64);
        _working.EditorFontSize = (double)(FontSizeBox.Value ?? 14);
        _working.Theme = ThemeBox.SelectedIndex == 1 ? "Dark" : "Light";
        _working.ShowLineNumbers = ShowLineNumbersBox.IsChecked != false;

        _working.TimeLimitMs = Math.Clamp(_working.TimeLimitMs, 100, 60_000);
        _working.MemoryLimitMb = Math.Clamp(_working.MemoryLimitMb, 16, 8192);
        _working.OutputLimitKb = Math.Clamp(_working.OutputLimitKb, 16, 16_384);
        _working.OutputDisplayLimitKb = Math.Clamp(_working.OutputDisplayLimitKb, 4, 1024);
        _working.EditorFontSize = Math.Clamp(_working.EditorFontSize, 9, 40);

        Close(_working);
    }

    private void Cancel_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(null);
    }

    private async void ExportDiagnostics_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出诊断包",
            SuggestedFileName = $"RedPandaCP-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
            DefaultExtension = "zip",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("ZIP 压缩包")
                {
                    Patterns = new[] { "*.zip" }
                }
            }
        });

        if (file?.TryGetLocalPath() is not { } path)
        {
            return;
        }

        try
        {
            DiagnosticsService.Export(path, _working);
            await Dialogs.ShowMessageAsync(
                this,
                "诊断包",
                "已导出到：\n" + path + "\n\n不包含登录令牌。");
        }
        catch (Exception ex)
        {
            await Dialogs.ShowMessageAsync(this, "导出诊断包失败", ex.Message);
        }
    }

    private void OpenLogs_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(CrashLogger.LogDirectory);
            Process.Start(new ProcessStartInfo(CrashLogger.LogDirectory)
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // Opening a folder is a convenience; ignore shell failures.
        }
    }
}
