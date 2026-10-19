using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RedPanda.Competition.Models;
using RedPanda.Competition.Services;

namespace RedPanda.Competition.Views;

public partial class StressWindow : Window
{
    private static readonly FilePickerFileType CppFileType = new("C++ 源文件")
    {
        Patterns = new[] { "*.cpp", "*.cc", "*.cxx", "*.c" }
    };

    private readonly string _solutionPath;
    private readonly AppSettings _settings;
    private readonly CompilerService _compiler = new();
    private CancellationTokenSource? _cts;
    private bool _busy;

    public StressWindow()
        : this(string.Empty, new AppSettings())
    {
    }

    public StressWindow(string solutionPath, AppSettings settings)
    {
        _solutionPath = solutionPath;
        _settings = settings.Clone();
        InitializeComponent();

        SolutionPathText.Text = solutionPath;
        StatusText.Text = "选择生成器和暴力程序后开始。";
        Closing += (_, _) => _cts?.Cancel();
    }

    private async void BrowseGenerator_Click(object? sender, RoutedEventArgs e)
    {
        var path = await PickSourceAsync("选择数据生成器");
        if (path is not null)
        {
            GeneratorBox.Text = path;
        }
    }

    private async void BrowseBrute_Click(object? sender, RoutedEventArgs e)
    {
        var path = await PickSourceAsync("选择暴力程序");
        if (path is not null)
        {
            BruteBox.Text = path;
        }
    }

    private async Task<string?> PickSourceAsync(string title)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { CppFileType }
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private async void Start_Click(object? sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_solutionPath) || !File.Exists(_solutionPath))
        {
            StatusText.Text = "当前程序尚未保存。";
            return;
        }

        var generator = GeneratorBox.Text?.Trim() ?? string.Empty;
        var brute = BruteBox.Text?.Trim() ?? string.Empty;

        if (!File.Exists(generator) || !File.Exists(brute))
        {
            StatusText.Text = "请选择存在的数据生成器和暴力程序。";
            return;
        }

        var rounds = (int)Math.Clamp(RoundsBox.Value ?? 200, 1, 10000);
        _busy = true;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        SetControlsEnabled(false);
        LogBox.Text = string.Empty;
        StatusText.Text = "正在编译…";

        try
        {
            LogBox.Text += "编译当前程序…\n";
            var solution = await _compiler.CompileAsync(_solutionPath, _settings, token);
            if (!solution.Success || solution.ExecutablePath is null)
            {
                LogCompileFailure(solution);
                return;
            }

            LogBox.Text += "编译数据生成器…\n";
            var generatorResult = await _compiler.CompileAsync(generator, _settings, token);
            if (!generatorResult.Success || generatorResult.ExecutablePath is null)
            {
                LogCompileFailure(generatorResult);
                return;
            }

            LogBox.Text += "编译暴力程序…\n";
            var bruteResult = await _compiler.CompileAsync(brute, _settings, token);
            if (!bruteResult.Success || bruteResult.ExecutablePath is null)
            {
                LogCompileFailure(bruteResult);
                return;
            }

            var workingDirectory = Path.GetDirectoryName(_solutionPath) ?? Environment.CurrentDirectory;
            var progress = new Progress<string>(message =>
            {
                LogBox.Text += message + "\n";
                LogBox.CaretIndex = LogBox.Text?.Length ?? 0;
            });

            LogBox.Text += $"开始对拍 {rounds} 轮…\n";
            var result = await StressTester.RunAsync(
                _solutionPath,
                solution.ExecutablePath,
                generatorResult.ExecutablePath,
                bruteResult.ExecutablePath,
                workingDirectory,
                rounds,
                _settings,
                progress,
                token);

            LogBox.Text += result.Message + "\n";
            if (result.InputPath is not null)
            {
                LogBox.Text += "反例输入：" + result.InputPath + "\n";
                LogBox.Text += "暴力答案：" + result.ExpectedPath + "\n";
                LogBox.Text += "当前输出：" + result.ActualPath + "\n";
            }

            StatusText.Text = result.Success
                ? result.Message
                : $"第 {result.Round} 轮失败：{result.Message}";
            LogBox.CaretIndex = LogBox.Text?.Length ?? 0;
        }
        catch (OperationCanceledException)
        {
            LogBox.Text += "已停止。\n";
            StatusText.Text = "已停止";
        }
        catch (Exception ex)
        {
            LogBox.Text += "对拍失败：" + ex.Message + "\n";
            StatusText.Text = "对拍失败";
        }
        finally
        {
            _busy = false;
            _cts?.Dispose();
            _cts = null;
            SetControlsEnabled(true);
        }
    }

    private void LogCompileFailure(CompileResult result)
    {
        LogBox.Text += "编译失败。\n";
        if (!string.IsNullOrWhiteSpace(result.Output))
        {
            LogBox.Text += result.Output + "\n";
        }

        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            LogBox.Text += result.ErrorMessage + "\n";
        }

        StatusText.Text = "编译失败";
        LogBox.CaretIndex = LogBox.Text?.Length ?? 0;
    }

    private void Stop_Click(object? sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        StatusText.Text = "正在停止…";
    }

    private void SetControlsEnabled(bool enabled)
    {
        StartButton.IsEnabled = enabled;
        StopButton.IsEnabled = !enabled;
        GeneratorBox.IsEnabled = enabled;
        BruteBox.IsEnabled = enabled;
        RoundsBox.IsEnabled = enabled;
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        Close();
    }
}
