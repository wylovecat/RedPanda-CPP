using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Search;
using RedPanda.Competition.Models;
using RedPanda.Competition.Services;
using RedPanda.Competition.ViewModels;
using RedPanda.Competition.Views;

namespace RedPanda.Competition;

public partial class MainWindow : Window
{
    private static readonly FilePickerFileType CppFileType = new("C++ 源文件")
    {
        Patterns = new[] { "*.cpp", "*.cc", "*.cxx", "*.c", "*.h", "*.hpp" }
    };

    private static readonly FilePickerFileType AllFilesType = new("所有文件")
    {
        Patterns = new[] { "*" }
    };

    private static readonly FilePickerFileType TestDataFileType = new("测试数据")
    {
        Patterns = new[] { "*.in", "*.out", "*.ans" }
    };

    private readonly MainViewModel _viewModel = new();
    private readonly CompilerService _compilerService = new();
    private readonly DispatcherTimer _testSaveTimer;
    private readonly DispatcherTimer _draftTimer;
    private readonly ObservableCollection<DiffLine> _diffLines = new();

    private AppSettings _settings;
    private string? _currentFilePath;
    private string? _lastExecutablePath;
    private string? _lastCompiledSourcePath;
    private string? _lastCompiledSourceHash;
    private bool _isDirty;
    private bool _isLoadingDocument;
    private bool _suppressTestChanges;
    private bool _testsDirty;
    private bool _isBusy;
    private bool _forceClose;
    private bool _closePromptOpen;
    private bool _settingsDialogOpen;
    private bool _selectAllUpdating;
    private bool _suppressCaseReset;
    private bool _restorePromptShown;
    private AscWindow? _ascWindow;
    private int? _activeTimeLimitMs;
    private int? _activeMemoryLimitMb;
    private CancellationTokenSource? _operationCts;

    public MainWindow()
        : this(new AppSettings())
    {
    }

    public MainWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        DataContext = _viewModel;

        _testSaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(800)
        };
        _testSaveTimer.Tick += (_, _) =>
        {
            _testSaveTimer.Stop();
            SaveTestsIfPossible();
        };

        _draftTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(3000)
        };
        _draftTimer.Tick += (_, _) => SaveDraft();

        DiffList.ItemsSource = _diffLines;
        _diffLines.Add(new DiffLine("i", "运行测试点后在这里查看差异", DiffLine.Note));

        CodeEditor.Options.IndentationSize = 4;
        CodeEditor.Options.ConvertTabsToSpaces = true;
        CodeEditor.Options.EnableHyperlinks = false;
        CodeEditor.Options.EnableEmailHyperlinks = false;
        CodeEditor.Options.HighlightCurrentLine = true;
        CodeEditor.Options.EnableTextDragDrop = true;
        CodeEditor.TextChanged += Editor_TextChanged;
        CodeEditor.TextArea.Caret.PositionChanged += Caret_PositionChanged;

        if (CodeEditor.SearchPanel is null)
        {
            SearchPanel.Install(CodeEditor);
        }

        AddHandler(KeyDownEvent, Window_KeyDown, RoutingStrategies.Tunnel);

        ApplyAppearance();
        ResetDocument(null, TemplateService.CreateCppTemplate(), "UTF-8", loadTests: false);
        UpdateCompilerStatus();
        UpdateSelectAllState();
        StatusText.Text = "就绪";

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        Opened += OnFirstOpened;
    }

    private async void OnFirstOpened(object? sender, EventArgs e)
    {
        CodeEditor.Focus();

        if (_restorePromptShown || !RecoveryService.Exists())
        {
            return;
        }

        _restorePromptShown = true;

        try
        {
            var draft = RecoveryService.Load(out var error);

            if (draft is null)
            {
                RecoveryService.Delete();
                if (error is not null)
                {
                    AppendRunLog(error);
                }

                return;
            }

            if (string.IsNullOrWhiteSpace(draft.Text) && (draft.Tests?.Count ?? 0) == 0)
            {
                RecoveryService.Delete();
                return;
            }

            var name = draft.SourcePath is null
                ? TemplateService.DefaultFileName
                : Path.GetFileName(draft.SourcePath);

            var restore = await Dialogs.AskRestoreDraftAsync(this, name);
            if (!restore)
            {
                RecoveryService.Delete();
                return;
            }

            ApplyDraft(draft);
        }
        catch (Exception ex)
        {
            RecoveryService.Delete();
            AppendRunLog("恢复草稿失败：" + ex.Message);
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedCase))
        {
            UpdateDiff(_viewModel.SelectedCase);
        }
    }

    public void OpenInitialFile(string path)
    {
        try
        {
            var (text, encodingName) = FileEncodingService.Read(path);
            ResetDocument(path, text, encodingName, loadTests: true);
            StatusText.Text = $"已打开 {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "打开失败：" + ex.Message;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_forceClose)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;

        if (_closePromptOpen)
        {
            return;
        }

        _closePromptOpen = true;
        _ = HandleCloseAsync();
    }

    private async Task HandleCloseAsync()
    {
        if (_isBusy)
        {
            _operationCts?.Cancel();

            for (var i = 0; i < 20 && _isBusy; i++)
            {
                await Task.Delay(50);
            }
        }

        var canClose = await ConfirmDiscardAsync();
        if (!canClose)
        {
            _closePromptOpen = false;
            return;
        }

        _testSaveTimer.Stop();
        _draftTimer.Stop();
        SaveTestsIfPossible();
        RecoveryService.Delete();
        SettingsService.Save(_settings);

        _forceClose = true;
        _closePromptOpen = false;
        Close();
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        var hasUntitledTests = _currentFilePath is null &&
                               _viewModel.TestCases.Any(c =>
                                   !string.IsNullOrEmpty(c.Input) ||
                                   !string.IsNullOrEmpty(c.ExpectedOutput));

        if (!_isDirty && !hasUntitledTests)
        {
            return true;
        }

        var displayName = _currentFilePath is null
            ? TemplateService.DefaultFileName
            : Path.GetFileName(_currentFilePath);

        var choice = await Dialogs.AskSaveChangesAsync(this, displayName);

        if (choice == SaveChangesChoice.Save)
        {
            return await SaveFileAsync();
        }

        return choice == SaveChangesChoice.Discard;
    }

    private async void NewFile_Click(object? sender, RoutedEventArgs e)
    {
        await NewFileAsync();
    }

    private async Task NewFileAsync()
    {
        if (_isBusy)
        {
            return;
        }

        if (!await ConfirmDiscardAsync())
        {
            return;
        }

        SaveTestsIfPossible();
        _draftTimer.Stop();
        RecoveryService.Delete();
        ResetDocument(null, TemplateService.CreateCppTemplate(), "UTF-8", loadTests: false);
        StatusText.Text = "新建文件";
    }

    private async void OpenFile_Click(object? sender, RoutedEventArgs e)
    {
        await OpenFileAsync();
    }

    private async Task OpenFileAsync()
    {
        if (_isBusy)
        {
            return;
        }

        if (!await ConfirmDiscardAsync())
        {
            return;
        }

        SaveTestsIfPossible();

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "打开 C++ 源文件",
            AllowMultiple = false,
            FileTypeFilter = new[] { CppFileType, AllFilesType }
        });

        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path)
        {
            return;
        }

        try
        {
            var (text, encodingName) = FileEncodingService.Read(path);
            _draftTimer.Stop();
            RecoveryService.Delete();
            ResetDocument(path, text, encodingName, loadTests: true);
            StatusText.Text = $"已打开 {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            await Dialogs.ShowMessageAsync(this, "打开失败", ex.Message);
        }
    }

    private async void SaveFile_Click(object? sender, RoutedEventArgs e)
    {
        if (!_isBusy)
        {
            await SaveFileAsync();
        }
    }

    private async void SaveAs_Click(object? sender, RoutedEventArgs e)
    {
        if (!_isBusy)
        {
            await SaveFileAsync(saveAs: true);
        }
    }

    private async Task<bool> SaveFileAsync(bool saveAs = false)
    {
        var target = _currentFilePath;

        if (saveAs || target is null)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "保存 C++ 源文件",
                SuggestedFileName = Path.GetFileName(target ?? "A.cpp"),
                DefaultExtension = "cpp",
                ShowOverwritePrompt = true,
                FileTypeChoices = new[] { CppFileType }
            });

            if (file?.TryGetLocalPath() is not { } picked)
            {
                return false;
            }

            target = picked;
        }

        if (string.IsNullOrEmpty(Path.GetExtension(target)))
        {
            target += ".cpp";
        }

        try
        {
            FileEncodingService.Write(target, CodeEditor.Text ?? string.Empty);

            _currentFilePath = target;
            _settings.LastDirectory = Path.GetDirectoryName(target) ?? _settings.LastDirectory;
            _isDirty = false;
            FileEncodingText.Text = "UTF-8";
            ApplyAscLimitsForPath(target);
            UpdateTitle();

            try
            {
                TestCaseStore.Save(target, _viewModel.TestCases);
                _testsDirty = false;
            }
            catch (Exception ex)
            {
                AppendRunLog("测试点保存失败：" + ex.Message);
            }

            InvalidateCompilation();
            ClearDraftIfClean();
            StatusText.Text = $"已保存 {Path.GetFileName(target)}";
            return true;
        }
        catch (Exception ex)
        {
            await Dialogs.ShowMessageAsync(this, "保存失败", ex.Message);
            return false;
        }
    }

    private void ResetDocument(string? path, string text, string encodingName, bool loadTests)
    {
        _isLoadingDocument = true;
        try
        {
            CodeEditor.Text = text;
        }
        finally
        {
            _isLoadingDocument = false;
        }

        try
        {
            CodeEditor.Document.UndoStack.ClearAll();
        }
        catch
        {
            // The undo stack is an optional nicety during document switches.
        }

        _currentFilePath = path;
        _isDirty = false;
        ApplyAscLimitsForPath(path);
        FileEncodingText.Text = encodingName;
        _testSaveTimer.Stop();
        _draftTimer.Stop();
        CompileOutputBox.Text = string.Empty;
        RunLogBox.Text = string.Empty;
        InvalidateCompilation();
        UpdateDiff(null);

        if (!string.IsNullOrEmpty(path))
        {
            _settings.LastDirectory = Path.GetDirectoryName(path) ?? _settings.LastDirectory;
        }

        LoadTests(loadTests);

        UpdateTitle();
        CodeEditor.CaretOffset = 0;
        UpdateCaret();
        CodeEditor.Focus();
    }

    private void LoadTests(bool fromFile)
    {
        List<TestCase> loaded;
        string? error = null;

        if (fromFile && _currentFilePath is not null)
        {
            loaded = TestCaseStore.Load(_currentFilePath, out error);
        }
        else
        {
            loaded = new List<TestCase>();
        }

        if (loaded.Count == 0)
        {
            loaded.Add(new TestCase { Name = "样例 1" });
        }

        ApplyTestCases(loaded, markDirty: false);
        _testsDirty = false;

        if (error is not null)
        {
            AppendRunLog(error);
        }
    }

    private void ApplyTestCases(IEnumerable<TestCase> testCases, bool markDirty)
    {
        _suppressTestChanges = true;
        try
        {
            foreach (var existing in _viewModel.TestCases)
            {
                existing.PropertyChanged -= TestCase_PropertyChanged;
            }

            _viewModel.TestCases.Clear();

            foreach (var testCase in testCases)
            {
                testCase.PropertyChanged += TestCase_PropertyChanged;
                _viewModel.TestCases.Add(testCase);
            }

            if (_viewModel.TestCases.Count == 0)
            {
                var fallback = new TestCase { Name = "样例 1" };
                fallback.PropertyChanged += TestCase_PropertyChanged;
                _viewModel.TestCases.Add(fallback);
            }

            _viewModel.SelectedCase = _viewModel.TestCases[0];
            _testsDirty = markDirty;
        }
        finally
        {
            _suppressTestChanges = false;
        }

        UpdateSelectAllState();
        UpdateDiff(_viewModel.SelectedCase);

        if (markDirty && _currentFilePath is null)
        {
            _isDirty = true;
            UpdateTitle();
        }
    }

    private void Editor_TextChanged(object? sender, EventArgs e)
    {
        if (_isLoadingDocument)
        {
            return;
        }

        if (!_isDirty)
        {
            _isDirty = true;
            UpdateTitle();
        }

        ScheduleDraftSave();
    }

    private void Caret_PositionChanged(object? sender, EventArgs e)
    {
        UpdateCaret();
    }

    private void UpdateCaret()
    {
        CaretText.Text = $"行 {CodeEditor.TextArea.Caret.Line}，列 {CodeEditor.TextArea.Caret.Column}";
    }

    private void UpdateTitle()
    {
        var name = _currentFilePath is null
            ? TemplateService.DefaultFileName
            : Path.GetFileName(_currentFilePath);

        Title = $"{(_isDirty ? "*" : string.Empty)}{name} - RedPanda CP · 算法竞赛版";
    }

    private void ApplyAppearance()
    {
        ThemeButton.Content = ThemeService.IsDark ? "浅色" : "深色";
        CodeEditor.FontSize = _settings.EditorFontSize;
        CodeEditor.ShowLineNumbers = _settings.ShowLineNumbers;

        try
        {
            CodeEditor.SyntaxHighlighting = SyntaxHighlightingService.GetCppDefinition(ThemeService.IsDark);
        }
        catch
        {
            CodeEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("C++");
        }
    }

    private void UpdateCompilerStatus()
    {
        var gpp = ToolchainLocator.FindGpp(_settings.CompilerPath);

        if (gpp is null)
        {
            CompilerStatusText.Text = "未找到 g++";
            ToolTip.SetTip(CompilerStatusText, "请在“设置”中指定 g++.exe，或把 MinGW 的 bin 目录加入 PATH。");
            return;
        }

        var compilerDirectory = Path.GetDirectoryName(gpp);
        var toolchainName = compilerDirectory is null
            ? null
            : Path.GetFileName(Path.GetDirectoryName(compilerDirectory));

        CompilerStatusText.Text = string.IsNullOrEmpty(toolchainName)
            ? "g++ 已就绪"
            : "g++ · " + toolchainName;
        ToolTip.SetTip(CompilerStatusText, gpp);
    }

    private void Aisichuang_Click(object? sender, RoutedEventArgs e)
    {
        if (_ascWindow is { IsVisible: true })
        {
            _ascWindow.Activate();
            return;
        }

        _ascWindow = new AscWindow(this);
        _ascWindow.Closed += (_, _) => _ascWindow = null;
        _ascWindow.Show(this);
    }

    public async Task<AscImportResult?> ImportAscProblemAsync(
        AscStatement statement,
        AscProblemRef problem,
        AscProblemSource source,
        AscTaskInfo? task,
        string? classId,
        string? className,
        AscSettings ascSettings)
    {
        if (_isBusy)
        {
            StatusText.Text = "正在运行，稍后再导入题目";
            return null;
        }

        if (!await ConfirmDiscardAsync())
        {
            return null;
        }

        SaveTestsIfPossible();
        _draftTimer.Stop();
        RecoveryService.Delete();

        try
        {
            var result = AscProblemImporter.Import(
                statement,
                problem,
                source,
                task,
                classId,
                className,
                ascSettings);

            var (text, encodingName) = FileEncodingService.Read(result.SourcePath);
            ResetDocument(result.SourcePath, text, encodingName, loadTests: true);
            ApplyAscLimitsForPath(result.SourcePath);

            var limits = _activeTimeLimitMs is not null || _activeMemoryLimitMb is not null
                ? $"（{_activeTimeLimitMs ?? _settings.TimeLimitMs} ms / {_activeMemoryLimitMb ?? _settings.MemoryLimitMb} MB）"
                : string.Empty;

            StatusText.Text = $"已导入 {result.Link.DisplayId}{limits}";
            AppendRunLog(
                $"爱思创：已导入 {result.Link.DisplayId} {result.Link.Title}；" +
                $"源码 {(result.CreatedSource ? "新建" : "已存在")}，测试点 {(result.CreatedTests ? "新建" : "保留本地")}。");
            AppendRunLog("题面文件：" + result.StatementPath);

            return result;
        }
        catch (Exception ex)
        {
            await Dialogs.ShowMessageAsync(this, "导入爱思创题目失败", ex.Message);
            return null;
        }
    }

    public async Task<(string Code, string SourcePath)?> GetCurrentCodeForAscSubmissionAsync()
    {
        if (_currentFilePath is null || _isDirty)
        {
            if (!await SaveFileAsync())
            {
                return null;
            }
        }

        if (_currentFilePath is null)
        {
            return null;
        }

        return (CodeEditor.Text ?? string.Empty, _currentFilePath);
    }

    private void ApplyAscLimitsForPath(string? path)
    {
        _activeTimeLimitMs = null;
        _activeMemoryLimitMb = null;

        var link = AscLinkStore.Load(path);
        if (link is null)
        {
            return;
        }

        if (link.TimeLimitMs > 0)
        {
            _activeTimeLimitMs = (int)Math.Clamp(link.TimeLimitMs, 100, 60_000);
        }

        if (link.MemoryLimitMb > 0)
        {
            _activeMemoryLimitMb = (int)Math.Clamp(link.MemoryLimitMb, 16, 8192);
        }
    }

    private void ToggleTheme_Click(object? sender, RoutedEventArgs e)
    {
        _settings.Theme = ThemeService.IsDark ? "Light" : "Dark";
        ThemeService.Apply(_settings.Theme);
        SettingsService.Save(_settings);
        ApplyAppearance();
    }

    private async void Settings_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        await OpenSettingsAsync();
    }

    private async Task OpenSettingsAsync()
    {
        if (_settingsDialogOpen)
        {
            return;
        }

        _settingsDialogOpen = true;
        try
        {
            var detectedCompiler = ToolchainLocator.FindGpp(_settings.CompilerPath);
            var dialog = new SettingsWindow(_settings, detectedCompiler);
            var result = await dialog.ShowDialog<AppSettings?>(this);

            if (result is null)
            {
                return;
            }

            _settings = result;
            SettingsService.Save(_settings);
            ThemeService.Apply(_settings.Theme);
            ApplyAppearance();
            UpdateCompilerStatus();
            InvalidateCompilation();
            StatusText.Text = "设置已保存";
        }
        finally
        {
            _settingsDialogOpen = false;
        }
    }

    private async void Compile_Click(object? sender, RoutedEventArgs e)
    {
        await CompileOnlyAsync();
    }

    private async Task CompileOnlyAsync()
    {
        if (_isBusy)
        {
            return;
        }

        if (!await EnsureSavedForRunAsync())
        {
            StatusText.Text = "已取消";
            return;
        }

        BeginOperation();
        var cancellationToken = _operationCts!.Token;

        try
        {
            RunLogBox.Text = string.Empty;
            var compile = await EnsureCompiledAsync(forceCompile: true, cancellationToken);

            if (compile.Success)
            {
                BottomTabs.SelectedIndex = 0;
                StatusText.Text = $"编译成功，用时 {compile.ElapsedMs / 1000.0:F2} s";
            }
        }
        finally
        {
            EndOperation();
        }
    }

    private async void Run_Click(object? sender, RoutedEventArgs e)
    {
        await RunSelectedAsync(forceCompile: false);
    }

    private async void CompileRun_Click(object? sender, RoutedEventArgs e)
    {
        await RunSelectedAsync(forceCompile: true);
    }

    private async Task RunSelectedAsync(bool forceCompile)
    {
        if (_isBusy)
        {
            return;
        }

        if (!await EnsureSavedForRunAsync())
        {
            StatusText.Text = "已取消";
            return;
        }

        BeginOperation();
        var cancellationToken = _operationCts!.Token;

        try
        {
            RunLogBox.Text = string.Empty;

            var compile = await EnsureCompiledAsync(forceCompile, cancellationToken);
            if (!compile.Success)
            {
                return;
            }

            var testCase = _viewModel.SelectedCase;
            if (testCase is null)
            {
                testCase = AddCaseInternal();
                _viewModel.SelectedCase = testCase;
                SaveTestsIfPossible();
            }

            BottomTabs.SelectedIndex = 1;
            StatusText.Text = "正在运行…";

            await RunCaseAsync(testCase, compile.ExecutablePath!, cancellationToken, refreshDiff: true);

            StatusText.Text = $"运行完成：{testCase.Status}";
        }
        finally
        {
            EndOperation();
        }
    }

    private async void RunAll_Click(object? sender, RoutedEventArgs e)
    {
        await RunAllAsync();
    }

    private async Task RunAllAsync()
    {
        if (_isBusy)
        {
            return;
        }

        if (!await EnsureSavedForRunAsync())
        {
            StatusText.Text = "已取消";
            return;
        }

        var cases = _viewModel.TestCases.Where(testCase => testCase.IsChecked).ToList();
        if (cases.Count == 0)
        {
            StatusText.Text = "没有勾选的测试点";
            return;
        }

        BeginOperation();
        var cancellationToken = _operationCts!.Token;

        try
        {
            RunLogBox.Text = string.Empty;

            var compile = await EnsureCompiledAsync(forceCompile: false, cancellationToken);
            if (!compile.Success)
            {
                return;
            }

            BottomTabs.SelectedIndex = 1;

            var passed = 0;
            var withExpected = 0;
            var slowest = 0L;

            foreach (var testCase in cases)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                StatusText.Text = $"正在运行 {testCase.Name}…";
                await RunCaseAsync(testCase, compile.ExecutablePath!, cancellationToken, refreshDiff: false);
                slowest = Math.Max(slowest, testCase.ElapsedMs);

                if (!string.IsNullOrWhiteSpace(testCase.ExpectedOutput))
                {
                    withExpected++;
                    if (testCase.Status == "通过")
                    {
                        passed++;
                    }
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                StatusText.Text = withExpected > 0
                    ? $"已停止，通过 {passed}/{withExpected}"
                    : "已停止";
            }
            else if (withExpected > 0)
            {
                StatusText.Text = passed == withExpected
                    ? $"全部通过 {passed}/{withExpected}，最慢 {slowest} ms"
                    : $"通过 {passed}/{withExpected}，最慢 {slowest} ms";
            }
            else
            {
                StatusText.Text = $"运行完成 {cases.Count} 个测试点，最慢 {slowest} ms";
            }

            var firstFailure = cases.FirstOrDefault(testCase =>
                !string.IsNullOrWhiteSpace(testCase.ExpectedOutput) &&
                testCase.Status != "通过");

            if (firstFailure is not null)
            {
                _viewModel.SelectedCase = firstFailure;
                UpdateDiff(firstFailure);
                BottomTabs.SelectedIndex = 2;
            }
            else
            {
                UpdateDiff(_viewModel.SelectedCase);
            }
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task<CompileResult> EnsureCompiledAsync(
        bool forceCompile,
        CancellationToken cancellationToken)
    {
        if (!forceCompile && IsCompilationCacheValid())
        {
            StatusText.Text = "源码未变化，复用上次编译结果";
            AppendRunLog("使用上次编译结果（源码未修改）。");
            return new CompileResult(
                true,
                string.Empty,
                _lastExecutablePath,
                0,
                null,
                "（复用上次编译结果）");
        }

        StatusText.Text = "正在编译…";
        CompileOutputBox.Text = string.Empty;

        var result = await _compilerService.CompileAsync(_currentFilePath!, _settings, cancellationToken);
        ShowCompileResult(result);

        if (result.Success)
        {
            _lastExecutablePath = result.ExecutablePath;
            _lastCompiledSourcePath = _currentFilePath;
            _lastCompiledSourceHash = ComputeFileHash(_currentFilePath);
            return result;
        }

        InvalidateCompilation();
        BottomTabs.SelectedIndex = 0;

        if (result.ErrorMessage?.Contains("未找到 g++", StringComparison.Ordinal) == true)
        {
            await Dialogs.ShowMessageAsync(
                this,
                "未找到编译器",
                result.ErrorMessage + "\n\n可以在接下来的设置窗口中指定 g++.exe。");
            await OpenSettingsAsync();
        }

        return result;
    }

    private bool IsCompilationCacheValid()
    {
        if (string.IsNullOrEmpty(_lastExecutablePath) ||
            !File.Exists(_lastExecutablePath) ||
            string.IsNullOrEmpty(_lastCompiledSourceHash) ||
            !string.Equals(_lastCompiledSourcePath, _currentFilePath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var currentHash = ComputeFileHash(_currentFilePath);
        return currentHash is not null &&
               string.Equals(currentHash, _lastCompiledSourceHash, StringComparison.OrdinalIgnoreCase);
    }

    private void InvalidateCompilation()
    {
        _lastExecutablePath = null;
        _lastCompiledSourcePath = null;
        _lastCompiledSourceHash = null;
    }

    private static string? ComputeFileHash(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch
        {
            return null;
        }
    }

    private void ShowCompileResult(CompileResult result)
    {
        var builder = new StringBuilder();
        builder.Append("> ").AppendLine(result.CommandLine);

        if (!string.IsNullOrWhiteSpace(result.Output))
        {
            builder.AppendLine(result.Output.TrimEnd());
        }

        if (!result.Success &&
            !string.IsNullOrWhiteSpace(result.ErrorMessage) &&
            !(result.Output?.Contains(result.ErrorMessage, StringComparison.Ordinal) ?? false))
        {
            builder.AppendLine(result.ErrorMessage);
        }

        builder.Append(result.Success
            ? $"编译成功，用时 {result.ElapsedMs / 1000.0:F2} s"
            : $"编译失败，用时 {result.ElapsedMs / 1000.0:F2} s");

        var text = TextTruncation.ForDisplay(
            builder.ToString(),
            _settings.OutputDisplayLimitKb * 1024);

        CompileOutputBox.Text = text;
        CompileOutputBox.CaretIndex = text.Length;
        StatusText.Text = result.Success ? "编译成功" : "编译失败";
    }

    private async Task RunCaseAsync(
        TestCase testCase,
        string executablePath,
        CancellationToken cancellationToken,
        bool refreshDiff)
    {
        testCase.Status = "运行中";
        testCase.HasRun = false;
        testCase.ActualOutput = string.Empty;
        testCase.RawActualOutput = string.Empty;
        testCase.ElapsedMs = 0;
        testCase.PeakMemoryMb = 0;

        var workingDirectory = Path.GetDirectoryName(_currentFilePath!) ?? Environment.CurrentDirectory;
        var timeLimitMs = _activeTimeLimitMs ?? _settings.TimeLimitMs;
        var memoryLimitMb = _activeMemoryLimitMb ?? _settings.MemoryLimitMb;

        RunResult result;
        try
        {
            result = await ProcessRunner.RunAsync(
                executablePath,
                workingDirectory,
                testCase.Input,
                timeLimitMs,
                memoryLimitMb,
                _settings.OutputLimitKb * 1024,
                cancellationToken);
        }
        catch (Exception ex)
        {
            result = new RunResult(RunOutcome.StartError, -1, string.Empty, string.Empty, 0, 0, ex.Message);
        }

        testCase.ElapsedMs = result.ElapsedMs;
        testCase.PeakMemoryMb = result.PeakMemoryMb;
        testCase.ExitCode = result.ExitCode;
        testCase.HasRun = true;
        testCase.RawActualOutput = result.StandardOutput;

        testCase.Status = result.Outcome switch
        {
            RunOutcome.Cancelled => "已停止",
            RunOutcome.Timeout => "超时",
            RunOutcome.OutputLimit => "输出超限",
            RunOutcome.StartError => "运行错误",
            RunOutcome.RuntimeError => "运行错误",
            _ => string.IsNullOrWhiteSpace(testCase.ExpectedOutput)
                ? "已运行"
                : OutputComparer.AreEqual(testCase.ExpectedOutput, result.StandardOutput)
                    ? "通过"
                    : "答案错误"
        };

        var actual = new StringBuilder(result.StandardOutput);

        if (result.Outcome == RunOutcome.OutputLimit)
        {
            actual.AppendLine();
            actual.Append("[输出超过上限，已截断]");
        }

        if (!string.IsNullOrEmpty(result.StandardError))
        {
            actual.AppendLine();
            actual.AppendLine("[stderr]");
            actual.Append(result.StandardError.TrimEnd());
        }

        testCase.ActualOutput = TextTruncation.ForDisplay(
            actual.ToString(),
            _settings.OutputDisplayLimitKb * 1024);

        AppendRunLog(
            $"{testCase.Name}：{testCase.Status}，用时 {result.ElapsedMs} ms，" +
            $"峰值内存 {result.PeakMemoryMb:F1} MB，退出码 {result.ExitCode}");

        if (result.Outcome == RunOutcome.Timeout)
        {
            AppendRunLog($"超过时限 {timeLimitMs} ms，程序已终止。");
        }
        else if (result.Outcome == RunOutcome.OutputLimit)
        {
            AppendRunLog($"输出超过 {_settings.OutputLimitKb} KB，程序已终止。");
        }

        if (testCase.Status == "答案错误")
        {
            AppendRunLog(OutputComparer.DescribeDifference(testCase.ExpectedOutput, result.StandardOutput));
        }

        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            AppendRunLog(result.ErrorMessage);
        }

        if (refreshDiff)
        {
            UpdateDiff(testCase);
            if (testCase.Status == "答案错误")
            {
                BottomTabs.SelectedIndex = 2;
            }
        }
    }

    private void AddCase_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        var testCase = AddCaseInternal();
        _viewModel.SelectedCase = testCase;
        MarkTestsDirty();
        InputBox.Focus();
    }

    private TestCase AddCaseInternal()
    {
        var index = 1;
        while (_viewModel.TestCases.Any(testCase => testCase.Name == $"样例 {index}"))
        {
            index++;
        }

        var created = new TestCase { Name = $"样例 {index}", IsChecked = true };
        created.PropertyChanged += TestCase_PropertyChanged;
        _viewModel.TestCases.Add(created);
        UpdateSelectAllState();
        return created;
    }

    private void RemoveCase_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        var current = _viewModel.SelectedCase;
        if (current is null)
        {
            return;
        }

        if (_viewModel.TestCases.Count <= 1)
        {
            current.Input = string.Empty;
            current.ExpectedOutput = string.Empty;
            current.ActualOutput = string.Empty;
            current.RawActualOutput = string.Empty;
            current.Status = "未运行";
            current.HasRun = false;
            UpdateDiff(current);
            StatusText.Text = "至少保留一个测试点";
            return;
        }

        var index = _viewModel.TestCases.IndexOf(current);
        current.PropertyChanged -= TestCase_PropertyChanged;
        _viewModel.TestCases.Remove(current);
        _viewModel.SelectedCase = _viewModel.TestCases[Math.Clamp(index, 0, _viewModel.TestCases.Count - 1)];
        UpdateSelectAllState();
        MarkTestsDirty();
        StatusText.Text = "测试点已删除";
    }

    private void TestCase_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not TestCase testCase)
        {
            return;
        }

        if (e.PropertyName == nameof(TestCase.IsChecked))
        {
            if (!_selectAllUpdating)
            {
                UpdateSelectAllState();
                MarkTestsDirty();
            }

            return;
        }

        if (e.PropertyName is nameof(TestCase.Input) or nameof(TestCase.ExpectedOutput))
        {
            if (!_suppressCaseReset && testCase.Status != "运行中")
            {
                testCase.Status = "未运行";
                testCase.ActualOutput = string.Empty;
                testCase.RawActualOutput = string.Empty;
                testCase.HasRun = false;
            }

            if (ReferenceEquals(testCase, _viewModel.SelectedCase))
            {
                UpdateDiff(testCase);
            }
        }

        if (e.PropertyName is nameof(TestCase.Name) or nameof(TestCase.Input) or nameof(TestCase.ExpectedOutput))
        {
            MarkTestsDirty();
        }
    }

    private void MarkTestsDirty()
    {
        if (_suppressTestChanges)
        {
            return;
        }

        _testsDirty = true;
        ScheduleDraftSave();

        if (_currentFilePath is null)
        {
            if (!_isDirty)
            {
                _isDirty = true;
                UpdateTitle();
            }

            return;
        }

        _testSaveTimer.Stop();
        _testSaveTimer.Start();
    }

    private void SaveTestsIfPossible()
    {
        if (!_testsDirty || _currentFilePath is null)
        {
            return;
        }

        try
        {
            TestCaseStore.Save(_currentFilePath, _viewModel.TestCases);
            _testsDirty = false;
            ClearDraftIfClean();
        }
        catch (Exception ex)
        {
            AppendRunLog("测试点保存失败：" + ex.Message);
            ScheduleDraftSave();
        }
    }

    private async Task<bool> EnsureSavedForRunAsync()
    {
        if (_currentFilePath is null || _isDirty)
        {
            return await SaveFileAsync();
        }

        SaveTestsIfPossible();
        return true;
    }

    private void UpdateDiff(TestCase? testCase)
    {
        _diffLines.Clear();

        if (testCase is null)
        {
            _diffLines.Add(new DiffLine("i", "没有选中的测试点", DiffLine.Note));
            return;
        }

        if (!testCase.HasRun)
        {
            _diffLines.Add(new DiffLine("i", "还没有运行结果", DiffLine.Note));
            return;
        }

        if (string.IsNullOrWhiteSpace(testCase.ExpectedOutput))
        {
            _diffLines.Add(new DiffLine("i", "未设置期望输出，本次只运行、不比对", DiffLine.Note));
            return;
        }

        foreach (var line in DiffService.Build(
                     testCase.ExpectedOutput,
                     testCase.RawActualOutput,
                     _settings.OutputDisplayLimitKb * 1024))
        {
            _diffLines.Add(line);
        }
    }

    private void UpdateSelectAllState()
    {
        if (SelectAllCheckBox is null)
        {
            return;
        }

        _selectAllUpdating = true;
        try
        {
            SelectAllCheckBox.IsEnabled = _viewModel.TestCases.Count > 0;
            SelectAllCheckBox.IsChecked = _viewModel.TestCases.Count > 0 &&
                                          _viewModel.TestCases.All(testCase => testCase.IsChecked);
        }
        finally
        {
            _selectAllUpdating = false;
        }
    }

    private void SelectAllCases_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectAllUpdating)
        {
            return;
        }

        SetAllCasesChecked(SelectAllCheckBox.IsChecked == true);
    }

    private void CheckAllCases_Click(object? sender, RoutedEventArgs e)
    {
        SetAllCasesChecked(true);
    }

    private void UncheckAllCases_Click(object? sender, RoutedEventArgs e)
    {
        SetAllCasesChecked(false);
    }

    private void InvertCases_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy || _viewModel.TestCases.Count == 0)
        {
            return;
        }

        _selectAllUpdating = true;
        try
        {
            foreach (var testCase in _viewModel.TestCases)
            {
                testCase.IsChecked = !testCase.IsChecked;
            }
        }
        finally
        {
            _selectAllUpdating = false;
        }

        UpdateSelectAllState();
        MarkTestsDirty();
    }

    private void SetAllCasesChecked(bool value)
    {
        if (_isBusy || _viewModel.TestCases.Count == 0)
        {
            return;
        }

        _selectAllUpdating = true;
        try
        {
            foreach (var testCase in _viewModel.TestCases)
            {
                testCase.IsChecked = value;
            }
        }
        finally
        {
            _selectAllUpdating = false;
        }

        UpdateSelectAllState();
        MarkTestsDirty();
    }

    private void CopyOutputToExpected_Click(object? sender, RoutedEventArgs e)
    {
        var testCase = _viewModel.SelectedCase;
        if (testCase is null || !testCase.HasRun)
        {
            return;
        }

        if (testCase.Status is not ("通过" or "答案错误" or "已运行"))
        {
            StatusText.Text = "只有正常结束的运行结果才能设为期望";
            return;
        }

        var rawOutput = testCase.RawActualOutput;

        _suppressCaseReset = true;
        try
        {
            testCase.ExpectedOutput = rawOutput;
        }
        finally
        {
            _suppressCaseReset = false;
        }

        testCase.Status = string.IsNullOrEmpty(rawOutput) ? "已运行" : "通过";
        MarkTestsDirty();
        UpdateDiff(testCase);
        StatusText.Text = string.IsNullOrEmpty(rawOutput)
            ? "实际输出为空，已清空期望输出"
            : "已把实际输出复制到期望输出";
    }

    private void DeleteEmptyCases_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        var emptyCases = _viewModel.TestCases
            .Where(testCase =>
                string.IsNullOrEmpty(testCase.Input) &&
                string.IsNullOrEmpty(testCase.ExpectedOutput))
            .ToList();

        if (emptyCases.Count == 0)
        {
            StatusText.Text = "没有空测试点";
            return;
        }

        foreach (var testCase in emptyCases)
        {
            testCase.PropertyChanged -= TestCase_PropertyChanged;
            _viewModel.TestCases.Remove(testCase);
        }

        if (_viewModel.TestCases.Count == 0)
        {
            var fallback = new TestCase { Name = "样例 1", IsChecked = true };
            fallback.PropertyChanged += TestCase_PropertyChanged;
            _viewModel.TestCases.Add(fallback);
            _viewModel.SelectedCase = fallback;
        }
        else if (_viewModel.SelectedCase is null ||
                 !_viewModel.TestCases.Contains(_viewModel.SelectedCase))
        {
            _viewModel.SelectedCase = _viewModel.TestCases[0];
        }

        UpdateSelectAllState();
        MarkTestsDirty();
        StatusText.Text = $"已删除 {emptyCases.Count} 个空测试点";
    }

    private async void ImportCasesFromFiles_Click(object? sender, RoutedEventArgs e)
    {
        await ImportCasesFromFilesAsync();
    }

    private async Task ImportCasesFromFilesAsync()
    {
        if (_isBusy)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择测试数据（.in / .out / .ans）",
            AllowMultiple = true,
            FileTypeFilter = new[] { TestDataFileType, AllFilesType }
        });

        var paths = files
            .Select(file => file.TryGetLocalPath())
            .Where(path => path is not null)
            .Cast<string>()
            .ToList();

        if (paths.Count == 0)
        {
            return;
        }

        try
        {
            var (cases, warnings) = CaseFileService.LoadFromFiles(paths);
            await ApplyImportedCasesAsync(cases, warnings);
        }
        catch (Exception ex)
        {
            await Dialogs.ShowMessageAsync(this, "导入失败", ex.Message);
        }
    }

    private async void ImportCasesFromFolder_Click(object? sender, RoutedEventArgs e)
    {
        await ImportCasesFromFolderAsync();
    }

    private async Task ImportCasesFromFolderAsync()
    {
        if (_isBusy)
        {
            return;
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择包含 .in/.out/.ans 的文件夹",
            AllowMultiple = false
        });

        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } folder)
        {
            return;
        }

        try
        {
            var (cases, warnings) = CaseFileService.LoadFromFolder(folder);
            await ApplyImportedCasesAsync(cases, warnings);
        }
        catch (Exception ex)
        {
            await Dialogs.ShowMessageAsync(this, "导入失败", ex.Message);
        }
    }

    private async Task ApplyImportedCasesAsync(
        List<ImportedTestCase> imported,
        List<string> warnings)
    {
        if (imported.Count == 0)
        {
            await Dialogs.ShowMessageAsync(
                this,
                "导入测试点",
                warnings.FirstOrDefault() ?? "没有找到可导入的 .in/.out 文件。");
            return;
        }

        var existingCount = _viewModel.TestCases.Count(testCase =>
            !string.IsNullOrEmpty(testCase.Input) ||
            !string.IsNullOrEmpty(testCase.ExpectedOutput));

        var mode = existingCount == 0
            ? ImportModeChoice.Replace
            : await Dialogs.AskImportModeAsync(this, imported.Count, existingCount);

        if (mode == ImportModeChoice.Cancel)
        {
            return;
        }

        if (mode == ImportModeChoice.Replace)
        {
            foreach (var existing in _viewModel.TestCases)
            {
                existing.PropertyChanged -= TestCase_PropertyChanged;
            }

            _viewModel.TestCases.Clear();
        }

        var added = new List<TestCase>();

        foreach (var item in imported)
        {
            var testCase = new TestCase
            {
                Name = MakeUniqueCaseName(item.Name),
                Input = item.Input,
                ExpectedOutput = item.ExpectedOutput,
                IsChecked = true
            };

            testCase.PropertyChanged += TestCase_PropertyChanged;
            _viewModel.TestCases.Add(testCase);
            added.Add(testCase);
        }

        if (added.Count > 0)
        {
            _viewModel.SelectedCase = added[0];
        }

        foreach (var warning in warnings)
        {
            AppendRunLog("导入测试点：" + warning);
        }

        UpdateSelectAllState();
        MarkTestsDirty();
        SaveTestsIfPossible();
        UpdateDiff(_viewModel.SelectedCase);
        StatusText.Text = $"已导入 {added.Count} 个测试点";
    }

    private string MakeUniqueCaseName(string? baseName)
    {
        var name = string.IsNullOrWhiteSpace(baseName) ? "测试点" : baseName.Trim();
        var candidate = name;
        var index = 2;

        while (_viewModel.TestCases.Any(testCase => testCase.Name == candidate))
        {
            candidate = $"{name} ({index++})";
        }

        return candidate;
    }

    private async void ExportCases_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择导出测试点的文件夹",
            AllowMultiple = false
        });

        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } folder)
        {
            return;
        }

        var baseName = _currentFilePath is null
            ? "tests"
            : Path.GetFileNameWithoutExtension(_currentFilePath);

        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "tests";
        }

        try
        {
            var count = CaseFileService.Export(folder, baseName, _viewModel.TestCases);
            StatusText.Text = count == 0
                ? "没有可导出的测试点"
                : $"已导出 {count} 组测试点到 {folder}";
        }
        catch (Exception ex)
        {
            await Dialogs.ShowMessageAsync(this, "导出失败", ex.Message);
        }
    }

    private void ScheduleDraftSave()
    {
        if (_isLoadingDocument || _suppressTestChanges || (!_isDirty && !_testsDirty))
        {
            return;
        }

        // Periodic rather than pure debounce: a student may type for a long
        // time without a pause, so always keep the last 3 seconds recoverable.
        if (!_draftTimer.IsEnabled)
        {
            _draftTimer.Start();
        }
    }

    private void SaveDraft()
    {
        if (!_isDirty && !_testsDirty)
        {
            ClearDraftIfClean();
            return;
        }

        try
        {
            var state = new DraftState
            {
                SourcePath = _currentFilePath,
                EncodingName = FileEncodingText.Text ?? "UTF-8",
                Text = CodeEditor.Text ?? string.Empty,
                Tests = _viewModel.TestCases.Select(testCase => new TestCaseFile
                {
                    Name = testCase.Name,
                    Input = testCase.Input,
                    ExpectedOutput = testCase.ExpectedOutput,
                    Enabled = testCase.IsChecked
                }).ToList()
            };

            RecoveryService.Save(state);
        }
        catch (Exception ex)
        {
            AppendRunLog("自动恢复草稿保存失败：" + ex.Message);
        }
    }

    private void ClearDraftIfClean()
    {
        if (_isDirty || _testsDirty)
        {
            return;
        }

        _draftTimer.Stop();
        RecoveryService.Delete();
    }

    private void ApplyDraft(DraftState draft)
    {
        _isLoadingDocument = true;
        try
        {
            CodeEditor.Text = draft.Text ?? string.Empty;
        }
        finally
        {
            _isLoadingDocument = false;
        }

        try
        {
            CodeEditor.Document.UndoStack.ClearAll();
        }
        catch
        {
            // Optional.
        }

        _currentFilePath = draft.SourcePath is not null && File.Exists(draft.SourcePath)
            ? draft.SourcePath
            : null;
        FileEncodingText.Text = draft.EncodingName;
        _isDirty = true;
        InvalidateCompilation();

        var draftTests = draft.Tests ?? new List<TestCaseFile>();
        var cases = draftTests.Select((item, index) => new TestCase
        {
            Name = string.IsNullOrWhiteSpace(item.Name) ? $"样例 {index + 1}" : item.Name,
            Input = item.Input ?? string.Empty,
            ExpectedOutput = item.ExpectedOutput ?? string.Empty,
            IsChecked = item.Enabled
        }).ToList();

        if (cases.Count == 0)
        {
            cases.Add(new TestCase { Name = "样例 1" });
        }

        ApplyTestCases(cases, markDirty: true);
        _testsDirty = true;
        UpdateTitle();
        CodeEditor.CaretOffset = 0;
        UpdateCaret();
        CodeEditor.Focus();
        StatusText.Text = "已恢复上次未保存的草稿，请检查并保存";
    }

    private void BeginOperation()
    {
        _isBusy = true;
        _operationCts = new CancellationTokenSource();
        CompileButton.IsEnabled = false;
        RunButton.IsEnabled = false;
        CompileRunButton.IsEnabled = false;
        RunAllButton.IsEnabled = false;
        SelectAllCheckBox.IsEnabled = false;
        StopButton.IsVisible = true;
        StopButton.IsEnabled = true;
    }

    private void EndOperation()
    {
        _isBusy = false;
        _operationCts?.Dispose();
        _operationCts = null;
        CompileButton.IsEnabled = true;
        RunButton.IsEnabled = true;
        CompileRunButton.IsEnabled = true;
        RunAllButton.IsEnabled = true;
        SelectAllCheckBox.IsEnabled = true;
        StopButton.IsVisible = false;
        StopButton.IsEnabled = true;
        UpdateSelectAllState();
    }

    private void Stop_Click(object? sender, RoutedEventArgs e)
    {
        _operationCts?.Cancel();
        StopButton.IsEnabled = false;
        StatusText.Text = "正在停止…";
    }

    private void AppendRunLog(string text)
    {
        var current = RunLogBox.Text ?? string.Empty;
        RunLogBox.Text = current.Length == 0
            ? text
            : current + Environment.NewLine + text;
        RunLogBox.CaretIndex = RunLogBox.Text?.Length ?? 0;
    }

    private async void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (control && e.Key == Key.F9)
        {
            e.Handled = true;
            await CompileOnlyAsync();
        }
        else if (shift && e.Key == Key.F9)
        {
            e.Handled = true;
            await RunSelectedAsync(forceCompile: false);
        }
        else if (e.Key == Key.F9)
        {
            e.Handled = true;
            await RunSelectedAsync(forceCompile: true);
        }
        else if (e.Key == Key.F10)
        {
            e.Handled = true;
            await RunAllAsync();
        }
        else if (control && !shift && e.Key == Key.S)
        {
            e.Handled = true;
            if (!_isBusy)
            {
                await SaveFileAsync();
            }
        }
        else if (control && shift && e.Key == Key.S)
        {
            e.Handled = true;
            if (!_isBusy)
            {
                await SaveFileAsync(saveAs: true);
            }
        }
        else if (control && e.Key == Key.O)
        {
            e.Handled = true;
            await OpenFileAsync();
        }
        else if (control && e.Key == Key.N)
        {
            e.Handled = true;
            await NewFileAsync();
        }
        else if (control && e.Key == Key.F)
        {
            e.Handled = true;
            if (CodeEditor.SearchPanel is { } searchPanel)
            {
                if (!string.IsNullOrEmpty(CodeEditor.SelectedText))
                {
                    searchPanel.SearchPattern = CodeEditor.SelectedText;
                }

                searchPanel.Open();
            }
        }
        else if (control && e.Key == Key.H)
        {
            e.Handled = true;
            if (CodeEditor.SearchPanel is { } searchPanel)
            {
                searchPanel.IsReplaceMode = true;
                searchPanel.Open();
            }
        }
    }
}
