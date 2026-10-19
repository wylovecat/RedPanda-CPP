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
    private readonly DispatcherTimer _contestTimer;
    private readonly ObservableCollection<DiffLine> _diffLines = new();
    private readonly List<DocumentTab> _tabs = new();

    private AppSettings _settings;
    private int _activeTabIndex;
    private int _nextUntitled = 1;
    private bool _documentTabsUpdating;
    private bool _tabSwitchInProgress;
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
    private bool _ascSubmissionBusy;
    private CancellationTokenSource? _ascSubmissionCts;
    private int? _activeTimeLimitMs;
    private int? _activeMemoryLimitMb;
    private CancellationTokenSource? _operationCts;
    private bool _contestDialogOpen;

    public MainWindow()
        : this(new AppSettings())
    {
    }

    public MainWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        Title = $"{AppInfo.ProductName} v{AppInfo.Version}";
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

        _contestTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _contestTimer.Tick += ContestTimer_Tick;

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
        InitializeInitialTab();
        UpdateCompilerStatus();
        UpdateSelectAllState();
        StatusText.Text = "就绪";
        RestoreContestState();
        RebuildRecentFilesMenu();

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        Opened += OnFirstOpened;
    }

    private async void OnFirstOpened(object? sender, EventArgs e)
    {
        CodeEditor.Focus();
        await ShowWelcomeIfNeededAsync();

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

    private async Task ShowWelcomeIfNeededAsync()
    {
        if (_settings.WelcomeShown)
        {
            return;
        }

        _settings.WelcomeShown = true;
        SettingsService.Save(_settings);

        var gpp = ToolchainLocator.FindGpp(_settings.CompilerPath);
        var message = new StringBuilder();
        message.AppendLine($"欢迎使用 {AppInfo.ProductName} v{AppInfo.Version}。")
            .AppendLine()
            .AppendLine("常用入口：")
            .AppendLine("· “爱思创”登录后拉取班测/作业，导入题目和样例")
            .AppendLine("· F9 编译运行，Shift+F9 复用编译，F10 测试全部")
            .AppendLine("· Ctrl+J 打开代码片段，插入常用模板")
            .AppendLine("· “交题”直接提交当前文件，“提交详情”查看测试点")
            .AppendLine("· “计时”设置课堂倒计时，结束后可锁定在线交题");

        if (gpp is null)
        {
            message.AppendLine()
                .AppendLine("当前没有检测到 g++。需要先指定编译器才能编译。");

            var openSettings = await Dialogs.ConfirmAsync(
                this,
                "欢迎使用",
                message.ToString().TrimEnd(),
                "打开设置",
                "稍后");
            if (openSettings)
            {
                await OpenSettingsAsync();
            }

            return;
        }

        message.AppendLine()
            .Append("编译器：").Append(gpp);
        await Dialogs.ShowMessageAsync(this, "欢迎使用", message.ToString().TrimEnd());
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
        _ = OpenDocumentInNewTabAsync(path);
    }

    public async Task OpenPathAsync(string path)
    {
        await OpenDocumentInNewTabAsync(path);
        Activate();
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
        _ascSubmissionCts?.Cancel();

        if (_isBusy)
        {
            _operationCts?.Cancel();

            for (var i = 0; i < 20 && _isBusy; i++)
            {
                await Task.Delay(50);
            }
        }

        var canClose = await ConfirmAllTabsBeforeCloseAsync();
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

    private async Task<bool> ConfirmAllTabsBeforeCloseAsync()
    {
        var original = _activeTabIndex;
        for (var i = 0; i < _tabs.Count; i++)
        {
            if (i != _activeTabIndex)
            {
                await SwitchToTabAsync(i);
            }

            if (!await ConfirmDiscardAsync())
            {
                await SwitchToTabAsync(original);
                return false;
            }
        }

        return true;
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

        CaptureActiveTab();
        _draftTimer.Stop();
        RecoveryService.Delete();

        var reusable = _tabs.Count == 1 &&
                       CurrentTab is { Path: null } current &&
                       !current.IsDirty &&
                       !_isDirty &&
                       _viewModel.TestCases.All(testCase =>
                           string.IsNullOrEmpty(testCase.Input) &&
                           string.IsNullOrEmpty(testCase.ExpectedOutput));

        if (!reusable)
        {
            _tabs.Add(new DocumentTab { UntitledName = NextUntitledName() });
            _activeTabIndex = _tabs.Count - 1;
        }
        else if (CurrentTab is { } tab)
        {
            tab.UntitledName = NextUntitledName();
            tab.PendingText = null;
            tab.PendingTests = null;
        }

        ResetDocument(null, TemplateService.CreateCppTemplate(), "UTF-8", loadTests: false);
        if (CurrentTab is { } created)
        {
            created.Path = null;
            created.IsDirty = false;
            created.PendingText = null;
            created.PendingTests = null;
            created.CaretOffset = 0;
        }

        RefreshDocumentTabsCombo();
        StatusText.Text = "新建文件";
        await Task.CompletedTask;
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

        _draftTimer.Stop();
        RecoveryService.Delete();
        await OpenDocumentInNewTabAsync(path);
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
            if (CurrentTab is { } tab)
            {
                tab.Path = target;
                tab.IsDirty = false;
                tab.PendingText = null;
                tab.PendingTests = null;
            }

            FileEncodingText.Text = "UTF-8";
            ApplyAscLimitsForPath(target);
            UpdateTitle();
            AddRecentFile(target);
            RefreshDocumentTabsCombo();

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

    private DocumentTab? CurrentTab =>
        _activeTabIndex >= 0 && _activeTabIndex < _tabs.Count
            ? _tabs[_activeTabIndex]
            : null;

    private string NextUntitledName()
    {
        if (_nextUntitled <= 1)
        {
            _nextUntitled = 2;
            return "未命名.cpp";
        }

        return $"未命名{_nextUntitled++}.cpp";
    }

    private void InitializeInitialTab()
    {
        _tabs.Clear();
        _tabs.Add(new DocumentTab { UntitledName = NextUntitledName() });
        _activeTabIndex = 0;
        ResetDocument(null, TemplateService.CreateCppTemplate(), "UTF-8", loadTests: false);
        RefreshDocumentTabsCombo();
    }

    private void RefreshDocumentTabsCombo()
    {
        if (DocumentTabsCombo is null)
        {
            return;
        }

        _documentTabsUpdating = true;
        try
        {
            DocumentTabsCombo.ItemsSource = null;
            DocumentTabsCombo.ItemsSource = _tabs;
            DocumentTabsCombo.SelectedIndex = _activeTabIndex;
        }
        finally
        {
            _documentTabsUpdating = false;
        }
    }

    private void SyncActiveTabState()
    {
        var tab = CurrentTab;
        if (tab is null)
        {
            return;
        }

        tab.Path = _currentFilePath;
        tab.IsDirty = _isDirty;
        tab.PendingEncoding = FileEncodingText.Text ?? "UTF-8";
        if (_isDirty)
        {
            tab.PendingText = CodeEditor.Text ?? string.Empty;
        }
        else
        {
            tab.PendingText = null;
        }

        RefreshDocumentTabsCombo();
    }

    private List<TestCase> CloneTests()
    {
        return _viewModel.TestCases
            .Select(testCase => new TestCase
            {
                Name = testCase.Name,
                Input = testCase.Input,
                ExpectedOutput = testCase.ExpectedOutput,
                IsChecked = testCase.IsChecked
            })
            .ToList();
    }

    private void CaptureActiveTab()
    {
        var tab = CurrentTab;
        if (tab is null)
        {
            return;
        }

        tab.Path = _currentFilePath;
        tab.IsDirty = _isDirty;
        tab.PendingEncoding = FileEncodingText.Text ?? "UTF-8";
        tab.CaretOffset = CodeEditor.CaretOffset;
        tab.PendingText = _isDirty ? CodeEditor.Text ?? string.Empty : null;

        if (_currentFilePath is not null)
        {
            try
            {
                TestCaseStore.Save(_currentFilePath, _viewModel.TestCases);
                _testsDirty = false;
                tab.PendingTests = null;
            }
            catch (Exception ex)
            {
                AppendRunLog("测试点保存失败：" + ex.Message);
            }
        }
        else
        {
            tab.PendingTests = CloneTests();
        }
    }

    private void RestoreTests(List<TestCase>? tests)
    {
        if (tests is null)
        {
            return;
        }

        _suppressTestChanges = true;
        try
        {
            _viewModel.TestCases.Clear();
            foreach (var testCase in tests)
            {
                _viewModel.TestCases.Add(testCase);
            }

            _viewModel.SelectedCase = _viewModel.TestCases.Count > 0
                ? _viewModel.TestCases[0]
                : null;
        }
        finally
        {
            _suppressTestChanges = false;
        }

        _testsDirty = false;
        UpdateSelectAllState();
        UpdateDiff(_viewModel.SelectedCase);
    }

    private async Task SwitchToTabAsync(int index)
    {
        if (_tabSwitchInProgress ||
            _isBusy ||
            index < 0 ||
            index >= _tabs.Count ||
            index == _activeTabIndex)
        {
            RefreshDocumentTabsCombo();
            return;
        }

        _tabSwitchInProgress = true;
        try
        {
            CaptureActiveTab();
            _draftTimer.Stop();
            RecoveryService.Delete();

            var tab = _tabs[index];
            if (tab.Path is not null)
            {
                var targetLink = AscLinkStore.LoadOrInfer(tab.Path);
                if (ContestBlocksTask(targetLink?.TaskId))
                {
                    await Dialogs.ShowMessageAsync(
                        this,
                        "计时已锁定任务",
                        $"本轮计时锁定在“{_settings.ContestTaskName}”，不能切换到其他任务。");
                    RefreshDocumentTabsCombo();
                    return;
                }
            }

            if (tab.Path is not null && !File.Exists(tab.Path))            {
                _tabs.RemoveAt(index);
                if (_activeTabIndex >= _tabs.Count)
                {
                    _activeTabIndex = Math.Max(0, _tabs.Count - 1);
                }

                if (_tabs.Count == 0)
                {
                    _tabs.Add(new DocumentTab { UntitledName = NextUntitledName() });
                    _activeTabIndex = 0;
                }

                RefreshDocumentTabsCombo();
                StatusText.Text = "文件已不存在，标签已移除";
                await SwitchToTabAsync(_activeTabIndex);
                return;
            }

            _activeTabIndex = index;
            if (tab.PendingText is not null)
            {
                ResetDocument(
                    tab.Path,
                    tab.PendingText,
                    tab.PendingEncoding,
                    loadTests: tab.Path is not null);
                RestoreTests(tab.PendingTests);
                _isDirty = tab.IsDirty;
            }
            else if (tab.Path is not null)
            {
                var (text, encodingName) = FileEncodingService.Read(tab.Path);
                ResetDocument(tab.Path, text, encodingName, loadTests: true);
                _isDirty = false;
                tab.IsDirty = false;
            }
            else
            {
                ResetDocument(null, string.Empty, tab.PendingEncoding, loadTests: false);
                RestoreTests(tab.PendingTests);
                _isDirty = tab.IsDirty;
            }

            CodeEditor.CaretOffset = Math.Clamp(tab.CaretOffset, 0, CodeEditor.Text?.Length ?? 0);
            UpdateTitle();
            RefreshDocumentTabsCombo();
            StatusText.Text = $"已切换到 {tab.DisplayName.TrimStart('*')}";
        }
        finally
        {
            _tabSwitchInProgress = false;
        }
    }

    private async Task OpenDocumentInNewTabAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var fullPath = Path.GetFullPath(path);
        var targetLink = AscLinkStore.LoadOrInfer(fullPath);
        if (ContestBlocksTask(targetLink?.TaskId))
        {
            await Dialogs.ShowMessageAsync(
                this,
                "计时已锁定任务",
                $"本轮计时锁定在“{_settings.ContestTaskName}”，不能打开其他任务。\n\n" +
                "如需切换，请先暂停/交卷或清除计时。");
            return;
        }

        var existingIndex = _tabs.FindIndex(tab =>
            tab.Path is not null &&
            string.Equals(Path.GetFullPath(tab.Path), fullPath, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            await SwitchToTabAsync(existingIndex);
            return;
        }

        string text;
        string encodingName;
        try
        {
            (text, encodingName) = FileEncodingService.Read(fullPath);
        }
        catch (Exception ex)
        {
            await Dialogs.ShowMessageAsync(this, "打开失败", ex.Message);
            return;
        }

        var reusable = _tabs.Count == 1 &&
                       CurrentTab is { Path: null } current &&
                       !current.IsDirty &&
                       !_isDirty &&
                       _viewModel.TestCases.All(testCase =>
                           string.IsNullOrEmpty(testCase.Input) &&
                           string.IsNullOrEmpty(testCase.ExpectedOutput));

        if (!reusable)
        {
            CaptureActiveTab();
            _tabs.Add(new DocumentTab { UntitledName = NextUntitledName() });
            _activeTabIndex = _tabs.Count - 1;
        }

        ResetDocument(fullPath, text, encodingName, loadTests: true);
        _isDirty = false;
        var tab = CurrentTab;
        if (tab is not null)
        {
            tab.Path = fullPath;
            tab.IsDirty = false;
            tab.PendingText = null;
            tab.PendingTests = null;
            tab.CaretOffset = 0;
        }

        AddRecentFile(fullPath);
        RefreshDocumentTabsCombo();
        StatusText.Text = $"已打开 {Path.GetFileName(fullPath)}";
    }

    private void AddRecentFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        _settings.RecentFiles.RemoveAll(item =>
            string.Equals(item, fullPath, StringComparison.OrdinalIgnoreCase));
        _settings.RecentFiles.Insert(0, fullPath);
        if (_settings.RecentFiles.Count > 10)
        {
            _settings.RecentFiles.RemoveRange(10, _settings.RecentFiles.Count - 10);
        }

        SettingsService.Save(_settings);
        RebuildRecentFilesMenu();
    }

    private void RebuildRecentFilesMenu()
    {
        var items = new List<MenuItem>();
        var existing = _settings.RecentFiles
            .Where(File.Exists)
            .Take(10)
            .ToList();

        _settings.RecentFiles = existing;
        if (existing.Count == 0)
        {
            items.Add(new MenuItem { Header = "（暂无最近文件）", IsEnabled = false });
        }
        else
        {
            foreach (var path in existing)
            {
                var item = new MenuItem { Header = path };
                item.Click += async (_, _) => await OpenDocumentInNewTabAsync(path);
                items.Add(item);
            }
        }

        if (RecentFilesButton.Flyout is MenuFlyout flyout)
        {
            flyout.ItemsSource = items;
        }
    }

    private void DocumentTabsCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_documentTabsUpdating || DocumentTabsCombo.SelectedIndex < 0)
        {
            return;
        }

        _ = SwitchToTabAsync(DocumentTabsCombo.SelectedIndex);
    }

    private async void CloseTab_Click(object? sender, RoutedEventArgs e)
    {
        await CloseCurrentTabAsync();
    }

    private async Task CloseCurrentTabAsync()
    {
        if (_isBusy || _tabSwitchInProgress)
        {
            return;
        }

        if (!await ConfirmDiscardAsync())
        {
            return;
        }

        SaveTestsIfPossible();

        if (_tabs.Count <= 1)
        {
            CaptureActiveTab();
            var tab = CurrentTab;
            if (tab is not null)
            {
                tab.IsDirty = false;
                tab.PendingText = null;
                tab.PendingTests = null;
                tab.Path = null;
                tab.UntitledName = NextUntitledName();
            }

            ResetDocument(null, TemplateService.CreateCppTemplate(), "UTF-8", loadTests: false);
            RefreshDocumentTabsCombo();
            StatusText.Text = "已清空当前标签";
            return;
        }

        var index = _activeTabIndex;
        _tabs.RemoveAt(index);
        _activeTabIndex = Math.Clamp(index, 0, _tabs.Count - 1);
        var next = _tabs[_activeTabIndex];

        if (next.PendingText is not null)
        {
            ResetDocument(
                next.Path,
                next.PendingText,
                next.PendingEncoding,
                loadTests: next.Path is not null);
            RestoreTests(next.PendingTests);
            _isDirty = next.IsDirty;
        }
        else if (next.Path is not null)
        {
            var (text, encodingName) = FileEncodingService.Read(next.Path);
            ResetDocument(next.Path, text, encodingName, loadTests: true);
            _isDirty = false;
            next.IsDirty = false;
        }
        else
        {
            ResetDocument(null, string.Empty, next.PendingEncoding, loadTests: false);
            RestoreTests(next.PendingTests);
            _isDirty = next.IsDirty;
        }

        CodeEditor.CaretOffset = Math.Clamp(next.CaretOffset, 0, CodeEditor.Text?.Length ?? 0);
        RefreshDocumentTabsCombo();
        StatusText.Text = $"已关闭标签，切换到 {next.DisplayName.TrimStart('*')}";
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
            SyncActiveTabState();
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
            SyncActiveTabState();
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
            ? CurrentTab?.UntitledName ?? TemplateService.DefaultFileName
            : Path.GetFileName(_currentFilePath);

        Title = $"{(_isDirty ? "*" : string.Empty)}{name} - {AppInfo.ProductName} v{AppInfo.Version}";
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
        OpenAscWindow();
    }

    private void OpenAscWindow()
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

    private async void AscSubmit_Click(object? sender, RoutedEventArgs e)
    {
        await SubmitCurrentFileToAscAsync();
    }

    private async void AscDetail_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy || _ascSubmissionBusy)
        {
            StatusText.Text = "正在编译/运行或交题，稍后再看详情";
            return;
        }

        var link = AscLinkStore.Load(_currentFilePath);
        if (link is null || string.IsNullOrWhiteSpace(link.LastSubmissionId))
        {
            StatusText.Text = "当前文件还没有上次提交记录";
            return;
        }

        var settings = AscSettingsStore.Load();
        if (!settings.Session.IsValid)
        {
            await Dialogs.ShowMessageAsync(
                this,
                "需要登录",
                "查看提交详情需要爱思创登录。请先打开“爱思创”登录。");
            return;
        }

        try
        {
            using var client = new AscClient(baseUrl: settings.BaseUrl);
            client.SetSession(settings.Session);

            var detailId = link.LastSubmissionId;
            if (!long.TryParse(detailId, out _))
            {
                if (string.IsNullOrWhiteSpace(link.ProblemId))
                {
                    throw new AscApiException(
                        "这个旧记录只有提交请求号，缺少题号，无法自动转换。" +
                        "请在“爱思创 → 提交记录”中输入网站上的数字提交号。");
                }

                StatusText.Text = "正在解析旧提交号…";
                detailId = await client.ResolveSubmissionRecordIdAsync(
                    detailId,
                    link.ProblemId);
                link.LastSubmissionId = detailId;
                if (_currentFilePath is not null)
                {
                    AscLinkStore.Save(_currentFilePath, link);
                }

                UpdateAscStatusForCurrentFile(link);
                AppendRunLog($"爱思创：旧提交号已转换为记录 {detailId}");
            }

            StatusText.Text = $"正在读取提交 {detailId} 的详情…";
            var detail = await client.FetchSubmissionDetailAsync(detailId);
            await Dialogs.ShowDetailsAsync(
                this,
                $"提交详情 · {link.DisplayId}",
                AscSubmissionFormatter.Format(link, detail, detailId));
            StatusText.Text = "提交详情已加载";
        }
        catch (AscAuthException ex)
        {
            var stale = AscSettingsStore.Load();
            stale.Session = new AscSession { BaseUrl = stale.BaseUrl };
            AscSettingsStore.Save(stale);
            _ascWindow?.HandleSessionExpired();
            await Dialogs.ShowMessageAsync(
                this,
                "登录已过期",
                ex.Message + "\n\n请重新打开“爱思创”登录。");
        }
        catch (Exception ex)
        {
            StatusText.Text = "读取提交详情失败";
            await Dialogs.ShowMessageAsync(this, "读取提交详情失败", ex.Message);
        }
    }

    public async Task<bool> SubmitCurrentFileToAscAsync(Window? dialogOwner = null)
    {
        if (ContestSubmissionLocked)
        {
            StatusText.Text = "计时已结束，交题已锁定";
            await Dialogs.ShowMessageAsync(
                dialogOwner ?? this,
                "交题已锁定",
                "本轮课堂计时已经结束，在线交题被锁定。\n\n本地编译和测试不受影响。");
            return false;
        }

        if (_ascSubmissionBusy)
        {
            return false;
        }

        if (_isBusy)
        {
            StatusText.Text = "正在编译/运行，稍后再交题";
            return false;
        }

        _ascSubmissionBusy = true;
        AscSubmitButton.IsEnabled = false;
        CompileButton.IsEnabled = false;
        RunButton.IsEnabled = false;
        CompileRunButton.IsEnabled = false;
        RunAllButton.IsEnabled = false;
        StopButton.IsVisible = true;
        StopButton.IsEnabled = true;
        _ascSubmissionCts = new CancellationTokenSource();

        try
        {
            return await SubmitCurrentFileToAscCoreAsync(dialogOwner, _ascSubmissionCts.Token);
        }
        finally
        {
            _ascSubmissionBusy = false;
            _ascSubmissionCts?.Dispose();
            _ascSubmissionCts = null;
            RefreshAscSubmitEnabled();

            if (!_isBusy)
            {
                CompileButton.IsEnabled = true;
                RunButton.IsEnabled = true;
                CompileRunButton.IsEnabled = true;
                RunAllButton.IsEnabled = true;
                StopButton.IsVisible = false;
            }

            StopButton.IsEnabled = true;
        }
    }

    private async Task<bool> SubmitCurrentFileToAscCoreAsync(
        Window? dialogOwner,
        CancellationToken cancellationToken)
    {
        var ascSettings = AscSettingsStore.Load();
        var owner = dialogOwner ?? this;

        if (!ascSettings.Session.IsValid)
        {
            await Dialogs.ShowMessageAsync(owner, "需要登录爱思创", "请先在“爱思创”窗口登录，然后再交题。");
            OpenAscWindow();
            return false;
        }

        if (!ascSettings.EnableOnlineSubmit)
        {
            await Dialogs.ShowMessageAsync(
                owner,
                "在线提交已关闭",
                "请在“爱思创 → 账号与设置”中勾选“允许在线提交（会写入爱思创生产站点）”。");
            OpenAscWindow();
            return false;
        }

        var current = await GetCurrentCodeForAscSubmissionAsync();
        if (current is null)
        {
            StatusText.Text = "已取消交题";
            return false;
        }

        var (code, sourcePath) = current.Value;
        var codeHash = AscSubmissionGuard.HashCode(code);
        var link = AscLinkStore.LoadOrInfer(sourcePath);
        if (link is null || string.IsNullOrWhiteSpace(link.DisplayId))
        {
            await Dialogs.ShowMessageAsync(
                owner,
                "没有关联题目",
                "当前文件没有爱思创题目链接。请先导入题目，或把文件命名为题号（如 A1131.cpp）。");
            return false;
        }

        if (ContestBlocksTask(link.TaskId))
        {
            await Dialogs.ShowMessageAsync(
                owner,
                "计时已锁定任务",
                $"本轮计时锁定在“{_settings.ContestTaskName}”，不能提交其他任务。");
            return false;
        }

        try
        {
            using var client = new AscClient(baseUrl: ascSettings.BaseUrl);
            client.SetSession(ascSettings.Session);

            if (string.IsNullOrWhiteSpace(link.ProblemId))
            {
                StatusText.Text = $"正在解析 {link.DisplayId}…";
                var problem = await client.FindProblemAsync(link.DisplayId, cancellationToken);
                link.ProblemId = problem.ProblemId;
                if (string.IsNullOrWhiteSpace(link.Title))
                {
                    link.Title = problem.Title;
                }

                AscLinkStore.Save(sourcePath, link);
                AppendRunLog($"爱思创：{link.DisplayId} → 内部 id {link.ProblemId}");
            }

            var where = link.IsTask
                ? (link.Source == AscProblemSource.Work ? "作业" : "班测") + " " + link.TaskName
                : "题库";

            var contestMismatchMessage = string.Empty;
            if ((_settings.ContestActive || _settings.ContestPaused) &&
                !_settings.ContestFinished &&
                !string.IsNullOrWhiteSpace(_settings.ContestTaskId) &&
                link.IsTask &&
                !string.Equals(link.TaskId, _settings.ContestTaskId, StringComparison.Ordinal))
            {
                contestMismatchMessage =
                    $"注意：本轮计时绑定任务“{_settings.ContestTaskName}”，" +
                    $"当前文件属于“{link.TaskName}”。\n\n";
                AppendRunLog(
                    $"课堂计时提醒：当前文件属于 {link.TaskName}，本轮任务是 {_settings.ContestTaskName}。");
            }

            var duplicateMessage = string.Empty;
            if (AscSubmissionGuard.IsRecentDuplicate(link, codeHash, DateTime.UtcNow))
            {
                var previousTime = link.LastSubmittedAtUtc.ToLocalTime().ToString("HH:mm:ss");
                var previousVerdict = string.IsNullOrWhiteSpace(link.LastVerdict) ? "?" : link.LastVerdict;
                duplicateMessage =
                    $"这段代码在 {previousTime} 刚刚提交过（{previousVerdict}）。\n\n";
            }

            var confirmed = await Dialogs.ConfirmAsync(
                owner,
                "在线提交",
                contestMismatchMessage +
                duplicateMessage +
                $"将 {link.DisplayId}（{where}）提交到爱思创生产站点。\n\n这会写入线上记录，是否继续？",
                "提交",
                "取消");

            if (!confirmed)
            {
                StatusText.Text = "已取消交题";
                return false;
            }

            StatusText.Text = "正在提交…";
            AppendRunLog($"爱思创：提交 {link.DisplayId}…");

            var submissionId = await client.SubmitCodeAsync(
                code,
                ascSettings.SubmitLanguage,
                link.ProblemId,
                cancellationToken);

            AppendRunLog($"爱思创：submission {submissionId}");

            AscSubmissionResult result;
            var deadline = DateTime.UtcNow.AddMilliseconds(AscConstants.PollTimeoutMs);

            while (true)
            {
                await Task.Delay(AscConstants.PollIntervalMs, cancellationToken);
                result = await client.PollSubmissionAsync(
                    submissionId,
                    link.ProblemId,
                    cancellationToken);
                StatusText.Text = $"判题中：{result.VerdictText}";

                if (result.IsFinal)
                {
                    break;
                }

                if (DateTime.UtcNow > deadline)
                {
                    throw new AscApiException("判题超时，请稍后在网站上查看结果。");
                }
            }

            // The POST response id is only the polling handle (often a GUID).
            // The result page and history are keyed by the numeric row id from
            // the polling response.
            var detailSubmissionId = AscSubmissionGuard.ResolveDetailId(result, submissionId);
            if (!string.Equals(detailSubmissionId, submissionId, StringComparison.Ordinal))
            {
                AppendRunLog($"爱思创：判题记录 {detailSubmissionId}");
            }

            // /api/submission may stop at the verdict without runtime or memory.
            // The site's own result page uses this detail endpoint, so enrich
            // the result there before writing it back to the class task.
            if (result.TimeMs <= 0 || result.MemoryBytes <= 0)
            {
                try
                {
                    var detail = await client.FetchSubmissionDetailAsync(
                        detailSubmissionId,
                        cancellationToken);

                    if (result.TimeMs <= 0 && detail.TimeMs > 0)
                    {
                        result.TimeMs = detail.TimeMs;
                    }

                    if (result.MemoryBytes <= 0 && detail.MemoryBytes > 0)
                    {
                        result.MemoryBytes = detail.MemoryBytes;
                    }

                    if (string.IsNullOrWhiteSpace(result.Feedback) &&
                        !string.IsNullOrWhiteSpace(detail.Feedback))
                    {
                        result.Feedback = detail.Feedback;
                    }

                    if (result.Cases.Count == 0 && detail.Cases.Count > 0)
                    {
                        result.Cases = detail.Cases;
                    }

                    if (result.Score <= 0 && detail.Score > 0)
                    {
                        result.Score = detail.Score;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    AppendRunLog("爱思创：读取判题详情失败：" + ex.Message);
                }
            }

            if (link.IsTask && !string.IsNullOrWhiteSpace(link.TaskId))
            {
                try
                {
                    if (link.Source == AscProblemSource.Work)
                    {
                        await client.RegisterWorkAnswerAsync(
                            link.TaskId,
                            link.ProblemId,
                            code,
                            ascSettings.SubmitLanguage,
                            result,
                            cancellationToken);
                    }
                    else
                    {
                        await client.RegisterTestAnswerAsync(
                            link.TaskId,
                            link.ProblemId,
                            result,
                            cancellationToken);
                    }

                    AppendRunLog("爱思创：已回填任务状态。");
                }
                catch (AscAuthException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    AppendRunLog("爱思创：判题成功，但回填任务失败：" + ex.Message);
                }
            }

            link.LastVerdict = result.Verdict;
            link.LastSubmissionId = detailSubmissionId;
            link.LastSubmittedCodeHash = codeHash;
            link.LastSubmittedAtUtc = DateTime.UtcNow;
            AscLinkStore.Save(sourcePath, link);

            AscHistoryStore.Add(new AscSubmissionHistoryEntry
            {
                SubmissionId = detailSubmissionId,
                DisplayId = link.DisplayId,
                Title = link.Title,
                TaskId = link.TaskId,
                TaskName = link.TaskName,
                Source = link.Source,
                Verdict = result.Verdict,
                TimeMs = result.TimeMs,
                MemoryBytes = result.MemoryBytes,
                Score = result.Score,
                SubmittedAtUtc = link.LastSubmittedAtUtc,
                SourcePath = sourcePath
            });

            UpdateAscStatusForCurrentFile(link);
            _ascWindow?.NotifySubmissionResult(link, result);

            StatusText.Text = $"交题完成：{result.VerdictText} · {result.PerformanceText}";
            AppendRunLog(
                $"爱思创：{result.VerdictText}，{result.PerformanceText}，得分 {result.Score}");

            if (!string.IsNullOrWhiteSpace(result.Feedback))
            {
                AppendRunLog("反馈：" + result.Feedback);
            }

            if (!result.Passed || !string.IsNullOrWhiteSpace(result.Feedback))
            {
                await Dialogs.ShowMessageAsync(
                    owner,
                    result.Passed ? "提交完成" : "提交结果",
                    AscSubmissionFormatter.Format(link, result, detailSubmissionId));
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "交题已取消";
            AppendRunLog("爱思创：交题已取消。");
            return false;
        }
        catch (AscAuthException ex)
        {
            var stale = AscSettingsStore.Load();
            stale.Session = new AscSession { BaseUrl = stale.BaseUrl };
            AscSettingsStore.Save(stale);
            _ascWindow?.HandleSessionExpired();

            await Dialogs.ShowMessageAsync(
                owner,
                "登录已过期",
                ex.Message + "\n\n请重新打开“爱思创”登录。");
            OpenAscWindow();
            return false;
        }
        catch (Exception ex)
        {
            StatusText.Text = "交题失败";
            AppendRunLog("爱思创交题失败：" + ex.Message);
            await Dialogs.ShowMessageAsync(owner, "交题失败", ex.Message);
            return false;
        }
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

        if (ContestBlocksTask(task?.TaskId))
        {
            await Dialogs.ShowMessageAsync(
                this,
                "计时已锁定任务",
                $"本轮计时锁定在“{_settings.ContestTaskName}”，不能导入其他任务。");
            return null;
        }

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

            return await ActivateAscImportAsync(result);
        }
        catch (Exception ex)
        {
            await Dialogs.ShowMessageAsync(this, "导入爱思创题目失败", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Opens a problem that already exists on disk without touching the network
    /// or rewriting its statement/tests.
    /// </summary>
    public async Task<AscImportResult?> OpenLocalAscProblemAsync(AscImportResult local)
    {
        if (_isBusy)
        {
            StatusText.Text = "正在运行，稍后再打开题目";
            return null;
        }

        try
        {
            return await ActivateAscImportAsync(local);
        }
        catch (Exception ex)
        {
            await Dialogs.ShowMessageAsync(this, "打开本地题目失败", ex.Message);
            return null;
        }
    }

    private async Task<AscImportResult> ActivateAscImportAsync(AscImportResult result)
    {
        await OpenDocumentInNewTabAsync(result.SourcePath);
        ApplyAscLimitsForPath(result.SourcePath);

        var limits = _activeTimeLimitMs is not null || _activeMemoryLimitMb is not null
            ? $"（{_activeTimeLimitMs ?? _settings.TimeLimitMs} ms / {_activeMemoryLimitMb ?? _settings.MemoryLimitMb} MB）"
            : string.Empty;

        StatusText.Text = $"已打开 {result.Link.DisplayId}{limits}";
        var testState = result.SamplesFilled
            ? "已补样例"
            : result.CreatedTests ? "新建" : "保留本地";
        AppendRunLog(
            $"爱思创：已打开 {result.Link.DisplayId} {result.Link.Title}；" +
            $"源码 {(result.CreatedSource ? "新建" : "已存在")}，测试点 {testState}。");
        AppendRunLog("题面文件：" + result.StatementPath);

        return result;
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

    private void UpdateAscStatusForCurrentFile(AscProblemLink? link)
    {
        if (AscStatusText is null)
        {
            return;
        }

        AscDetailButton.IsEnabled =
            link is not null && !string.IsNullOrWhiteSpace(link.LastSubmissionId);

        if (link is null || !link.IsValid)
        {
            AscStatusText.Text = "爱思创：未关联";
            ToolTip.SetTip(
                AscStatusText,
                "当前文件没有关联爱思创题目。从“爱思创”导入，或把文件命名为题号（如 A1131.cpp）。");
            return;
        }

        var verdictSuffix = string.IsNullOrWhiteSpace(link.LastVerdict)
            ? string.Empty
            : " · " + link.LastVerdict;
        AscStatusText.Text = $"爱思创：{link.DisplayId}{verdictSuffix}";

        var tip = new StringBuilder();
        tip.Append(link.DisplayId);
        if (!string.IsNullOrWhiteSpace(link.Title))
        {
            tip.Append(' ').Append(link.Title);
        }

        tip.AppendLine();
        if (link.IsTask)
        {
            tip.Append("任务：").AppendLine(link.TaskName);
        }

        if (!string.IsNullOrWhiteSpace(link.LastVerdict))
        {
            tip.Append("上次提交：").Append(link.LastVerdict);
            if (!string.IsNullOrWhiteSpace(link.LastSubmissionId))
            {
                tip.Append("（").Append(link.LastSubmissionId).Append('）');
            }

            tip.AppendLine();
        }

        if (link.LastSubmittedAtUtc > DateTime.MinValue)
        {
            tip.Append("时间：")
                .Append(link.LastSubmittedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        }

        ToolTip.SetTip(AscStatusText, tip.ToString().TrimEnd());
    }

    private void ApplyAscLimitsForPath(string? path)
    {
        _activeTimeLimitMs = null;
        _activeMemoryLimitMb = null;

        var link = AscLinkStore.Load(path);
        UpdateAscStatusForCurrentFile(link);
        if (link is null)
        {
            return;
        }

        if ((_settings.ContestActive || _settings.ContestPaused) &&
            !_settings.ContestFinished &&
            !string.IsNullOrWhiteSpace(_settings.ContestTaskId) &&
            link.IsTask &&
            !string.Equals(link.TaskId, _settings.ContestTaskId, StringComparison.Ordinal))
        {
            var reminder =
                $"计时提醒：当前文件属于“{link.TaskName}”，本轮计时绑定“{_settings.ContestTaskName}”。";
            StatusText.Text = reminder;
            AppendRunLog(reminder);
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

    private async void Snippets_Click(object? sender, RoutedEventArgs e)
    {
        await OpenSnippetWindowAsync();
    }

    private async void SaveSelectionAsSnippet_Click(object? sender, RoutedEventArgs e)
    {
        var selected = CodeEditor.SelectedText;
        if (string.IsNullOrWhiteSpace(selected))
        {
            await Dialogs.ShowMessageAsync(
                this,
                "保存代码片段",
                "请先在编辑器里选中要保存的代码。");
            return;
        }

        var window = new SnippetWindow(selected, createNew: true);
        await window.ShowDialog(this);
        if (window.SavedSomething)
        {
            StatusText.Text = "选中代码已保存为片段";
        }
    }

    private async Task OpenSnippetWindowAsync()
    {
        var window = new SnippetWindow();
        await window.ShowDialog(this);
        if (window.InsertRequested is { } snippet)
        {
            InsertSnippet(snippet);
        }
    }

    private void InsertSnippet(CodeSnippet snippet)
    {
        var start = CodeEditor.SelectionLength > 0
            ? CodeEditor.SelectionStart
            : CodeEditor.CaretOffset;
        if (CodeEditor.SelectionLength > 0)
        {
            CodeEditor.Document.Remove(start, CodeEditor.SelectionLength);
        }

        CodeEditor.Document.Insert(start, snippet.Body);
        CodeEditor.CaretOffset = start + snippet.Body.Length;
        CodeEditor.Focus();
        StatusText.Text = $"已插入片段：{snippet.Name}";
        AppendRunLog($"代码片段：已插入 {snippet.Name}（{snippet.Body.Length} 字符）。");
    }

    private bool ContestSubmissionLocked =>
        _settings.ContestLockSubmission &&
        (_settings.ContestFinished ||
         (_settings.ContestActive && _settings.ContestEndsAtUtc <= DateTime.UtcNow));

    private bool ContestBlocksTask(string? taskId)
    {
        if (!_settings.ContestLockTask ||
            _settings.ContestFinished ||
            string.IsNullOrWhiteSpace(taskId) ||
            string.IsNullOrWhiteSpace(_settings.ContestTaskId))
        {
            return false;
        }

        if (!_settings.ContestActive && !_settings.ContestPaused)
        {
            return false;
        }

        return !string.Equals(taskId, _settings.ContestTaskId, StringComparison.Ordinal);
    }

    private void RefreshAscSubmitEnabled()
    {
        if (AscSubmitButton is null)
        {
            return;
        }

        AscSubmitButton.IsEnabled = !_isBusy && !_ascSubmissionBusy && !ContestSubmissionLocked;
        ToolTip.SetTip(
            AscSubmitButton,
            ContestSubmissionLocked
                ? "本轮计时已结束，在线交题被锁定；本地编译和测试不受影响"
                : "提交当前文件到爱思创（使用本地题目链接，不需要重开爱思创窗口）");
    }

    private void RestoreContestState()
    {
        if (_settings.ContestActive && _settings.ContestEndsAtUtc <= DateTime.UtcNow)
        {
            _settings.ContestActive = false;
            _settings.ContestFinished = true;
            _settings.ContestPaused = false;
            SettingsService.Save(_settings);
        }

        if (_settings.ContestActive)
        {
            _contestTimer.Start();
        }

        if (_settings.ContestFullscreen &&
            (_settings.ContestActive || _settings.ContestPaused))
        {
            WindowState = WindowState.FullScreen;
        }

        ApplyContestUi();
    }

    private void ApplyContestUi()
    {
        if (ContestStatusText is null || ContestButton is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var active = _settings.ContestActive && _settings.ContestEndsAtUtc > now;
        var paused = _settings.ContestPaused && !_settings.ContestFinished;
        var finished = _settings.ContestFinished || (_settings.ContestActive && !active);

        ContestStatusText.IsVisible = active || paused || finished;
        if (active)
        {
            ContestButton.Content = "计时中";
            UpdateContestStatus(_settings.ContestEndsAtUtc - now);
        }
        else if (paused)
        {
            ContestButton.Content = "已暂停";
            var remaining = TimeSpan.FromMilliseconds(
                Math.Max(0, _settings.ContestPausedRemainingMs));
            ContestStatusText.Text =
                $"{_settings.ContestTitle} · 已暂停 {FormatRemainingText(remaining)}";
            ToolTip.SetTip(ContestStatusText, "本轮计时已暂停，打开“计时”可继续。");
        }
        else if (finished)
        {
            ContestButton.Content = "已结束";
            ContestStatusText.Text = $"{_settings.ContestTitle} · 已结束";
            ToolTip.SetTip(
                ContestStatusText,
                _settings.ContestLockSubmission
                    ? "在线交题已锁定；清除计时后可重新开始。"
                    : "在线交题未锁定。");
        }
        else
        {
            ContestButton.Content = "计时";
            ContestStatusText.Text = string.Empty;
        }

        RefreshAscSubmitEnabled();
    }

    private static string FormatRemainingText(TimeSpan remaining)
    {
        var total = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
        var hours = total / 3600;
        var minutes = total % 3600 / 60;
        var seconds = total % 60;
        return hours > 0
            ? $"{hours}:{minutes:00}:{seconds:00}"
            : $"{minutes:00}:{seconds:00}";
    }

    private void UpdateContestStatus(TimeSpan remaining)
    {
        ContestStatusText.Text =
            $"{_settings.ContestTitle} · 剩余 {FormatRemainingText(remaining)}";
        var tip = new StringBuilder();
        tip.Append("结束时间：")
            .AppendLine(_settings.ContestEndsAtUtc.ToLocalTime().ToString("HH:mm"));
        if (!string.IsNullOrWhiteSpace(_settings.ContestTaskName))
        {
            tip.Append("任务：").AppendLine(_settings.ContestTaskName);
        }

        tip.Append(_settings.ContestLockSubmission ? "结束后锁定在线交题" : "结束后不锁定交题");
        ToolTip.SetTip(ContestStatusText, tip.ToString());
    }

    private async void Contest_Click(object? sender, RoutedEventArgs e)
    {
        if (_contestDialogOpen)
        {
            return;
        }

        if (_settings.ContestActive && _settings.ContestEndsAtUtc <= DateTime.UtcNow)
        {
            _settings.ContestActive = false;
            _settings.ContestFinished = true;
            SettingsService.Save(_settings);
            ApplyContestUi();
        }

        var currentLink = AscLinkStore.LoadOrInfer(_currentFilePath);
        var taskName = currentLink is { IsTask: true } ? currentLink.TaskName : string.Empty;
        var dialog = new ContestWindow(new ContestDialogState(
            _settings.ContestActive,
            _settings.ContestFinished,
            _settings.ContestPaused,
            _settings.ContestPausedRemainingMs,
            _settings.ContestTitle,
            _settings.ContestDurationMinutes,
            _settings.ContestLockSubmission,
            _settings.ContestLockTask,
            _settings.ContestFullscreen,
            _settings.ContestEndsAtUtc,
            taskName));

        _contestDialogOpen = true;
        try
        {
            await dialog.ShowDialog(this);
        }
        finally
        {
            _contestDialogOpen = false;
        }

        switch (dialog.Action)
        {
            case ContestDialogAction.Start:
                StartContest(dialog, currentLink);
                break;
            case ContestDialogAction.Extend:
                ExtendContest();
                break;
            case ContestDialogAction.Pause:
                PauseContest();
                break;
            case ContestDialogAction.Resume:
                ResumeContest();
                break;
            case ContestDialogAction.Finish:
                if (await Dialogs.ConfirmAsync(
                        this,
                        "交卷",
                        "确定交卷并结束本轮计时吗？\n\n" +
                        (_settings.ContestLockSubmission
                            ? "结束后将锁定在线交题。"
                            : "结束后不会锁定在线交题。"),
                        "交卷",
                        "取消"))
                {
                    await EndContestAsync();
                }
                break;
            case ContestDialogAction.Summary:
                await ShowContestSummaryAsync();
                break;
            case ContestDialogAction.Export:
                await ExportContestSummaryAsync();
                break;
            case ContestDialogAction.Clear:
                ResetContest();
                break;
            case ContestDialogAction.Restart:
                ResetContest();
                Contest_Click(null, e);
                break;
        }
    }

    private void StartContest(ContestWindow dialog, AscProblemLink? link)
    {
        var now = DateTime.UtcNow;
        _settings.ContestTitle = dialog.ContestTitle;
        _settings.ContestDurationMinutes = dialog.DurationMinutes;
        _settings.ContestLockSubmission = dialog.LockSubmission;
        _settings.ContestLockTask = dialog.LockTask;
        _settings.ContestFullscreen = dialog.Fullscreen;
        _settings.ContestRemind5Shown = false;
        _settings.ContestRemind1Shown = false;
        _settings.ContestActive = true;
        _settings.ContestFinished = false;
        _settings.ContestPaused = false;
        _settings.ContestPausedRemainingMs = 0;
        _settings.ContestStartedAtUtc = now;
        _settings.ContestEndsAtUtc = now.AddMinutes(dialog.DurationMinutes);

        if (link is { IsTask: true })
        {
            _settings.ContestTaskId = link.TaskId;
            _settings.ContestTaskName = link.TaskName;
            _settings.ContestTaskSource = link.Source;
        }
        else
        {
            _settings.ContestTaskId = string.Empty;
            _settings.ContestTaskName = string.Empty;
            _settings.ContestTaskSource = AscProblemSource.Bank;
        }

        SettingsService.Save(_settings);
        _contestTimer.Start();
        ApplyContestUi();
        if (_settings.ContestFullscreen)
        {
            WindowState = WindowState.FullScreen;
        }

        StatusText.Text = $"计时开始：{_settings.ContestTitle}，{dialog.DurationMinutes} 分钟";
        AppendRunLog(
            $"课堂计时：{_settings.ContestTitle}，{dialog.DurationMinutes} 分钟，" +
            (_settings.ContestLockSubmission ? "结束后锁定交题。" : "结束后不锁定交题。"));
    }

    private void ExtendContest()
    {
        if (_settings.ContestPaused)
        {
            _settings.ContestPausedRemainingMs += (long)TimeSpan.FromMinutes(5).TotalMilliseconds;
            SettingsService.Save(_settings);
            ApplyContestUi();
            StatusText.Text = "暂停中的计时已延长 5 分钟";
            AppendRunLog("课堂计时：暂停中延长 5 分钟。");
            return;
        }

        if (!_settings.ContestActive)
        {
            return;
        }

        var baseTime = _settings.ContestEndsAtUtc > DateTime.UtcNow
            ? _settings.ContestEndsAtUtc
            : DateTime.UtcNow;
        _settings.ContestEndsAtUtc = baseTime.AddMinutes(5);
        _settings.ContestFinished = false;
        _settings.ContestRemind5Shown = false;
        _settings.ContestRemind1Shown = false;
        SettingsService.Save(_settings);
        _contestTimer.Start();
        ApplyContestUi();
        StatusText.Text = "计时已延长 5 分钟";
        AppendRunLog("课堂计时：延长 5 分钟。");
    }

    private void PauseContest()
    {
        if (!_settings.ContestActive)
        {
            return;
        }

        var remaining = _settings.ContestEndsAtUtc - DateTime.UtcNow;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        _settings.ContestActive = false;
        _settings.ContestPaused = true;
        _settings.ContestPausedRemainingMs = (long)remaining.TotalMilliseconds;
        SettingsService.Save(_settings);
        _contestTimer.Stop();
        ApplyContestUi();
        StatusText.Text = "计时已暂停";
        AppendRunLog("课堂计时：已暂停。");
    }

    private void ResumeContest()
    {
        if (!_settings.ContestPaused)
        {
            return;
        }

        var remaining = TimeSpan.FromMilliseconds(
            Math.Max(0, _settings.ContestPausedRemainingMs));
        var now = DateTime.UtcNow;
        _settings.ContestPaused = false;
        _settings.ContestActive = true;
        _settings.ContestFinished = false;
        _settings.ContestPausedRemainingMs = 0;
        _settings.ContestEndsAtUtc = now.Add(remaining);
        _settings.ContestRemind5Shown = false;
        _settings.ContestRemind1Shown = false;
        SettingsService.Save(_settings);
        _contestTimer.Start();
        ApplyContestUi();
        StatusText.Text = "计时已继续";
        AppendRunLog("课堂计时：已继续。");
    }

    private async Task EndContestAsync()
    {
        if (_settings.ContestActive || _settings.ContestPaused)
        {
            _settings.ContestActive = false;
            _settings.ContestPaused = false;
            _settings.ContestFinished = true;
            SettingsService.Save(_settings);
        }

        _contestTimer.Stop();
        ApplyContestUi();
        if (_settings.ContestFullscreen)
        {
            WindowState = WindowState.Normal;
        }

        StatusText.Text = ContestSubmissionLocked ? "计时结束，交题已锁定" : "计时结束";
        AppendRunLog("课堂计时：已交卷。");
        await ShowContestSummaryAsync();
    }

    private async void ContestTimer_Tick(object? sender, EventArgs e)
    {
        if (!_settings.ContestActive)
        {
            _contestTimer.Stop();
            ApplyContestUi();
            return;
        }

        var remaining = _settings.ContestEndsAtUtc - DateTime.UtcNow;
        if (remaining <= TimeSpan.FromMinutes(5) && !_settings.ContestRemind5Shown)
        {
            _settings.ContestRemind5Shown = true;
            SettingsService.Save(_settings);
            StatusText.Text = "提醒：本轮计时还剩 5 分钟";
            AppendRunLog("课堂计时：剩余 5 分钟。");
        }

        if (remaining <= TimeSpan.FromMinutes(1) && !_settings.ContestRemind1Shown)
        {
            _settings.ContestRemind1Shown = true;
            SettingsService.Save(_settings);
            StatusText.Text = "提醒：本轮计时还剩 1 分钟";
            AppendRunLog("课堂计时：剩余 1 分钟。");
        }

        if (remaining > TimeSpan.Zero)
        {
            UpdateContestStatus(remaining);
            return;
        }

        _settings.ContestActive = false;
        _settings.ContestPaused = false;
        _settings.ContestFinished = true;
        SettingsService.Save(_settings);
        _contestTimer.Stop();
        ApplyContestUi();
        if (_settings.ContestFullscreen)
        {
            WindowState = WindowState.Normal;
        }

        StatusText.Text = ContestSubmissionLocked ? "计时结束，交题已锁定" : "计时结束";
        AppendRunLog("课堂计时：时间到。");

        if (_contestDialogOpen)
        {
            return;
        }

        await Dialogs.ShowMessageAsync(
            this,
            "课堂计时结束",
            _settings.ContestLockSubmission
                ? "时间到，在线交题已锁定。\n\n本地编译和测试不受影响。"
                : "时间到。\n\n在线交题未被锁定。");
        await ShowContestSummaryAsync();
    }

    private void ResetContest()
    {
        _settings.ContestActive = false;
        _settings.ContestFinished = false;
        _settings.ContestPaused = false;
        _settings.ContestPausedRemainingMs = 0;
        _settings.ContestStartedAtUtc = default;
        _settings.ContestEndsAtUtc = default;
        _settings.ContestTaskId = string.Empty;
        _settings.ContestTaskName = string.Empty;
        _settings.ContestTaskSource = AscProblemSource.Bank;
        SettingsService.Save(_settings);

        _contestTimer.Stop();
        ApplyContestUi();
        if (_settings.ContestFullscreen)
        {
            WindowState = WindowState.Normal;
        }

        StatusText.Text = "计时已清除";
        AppendRunLog("课堂计时：已清除。");
    }

    private string BuildContestSummary()
    {
        var since = _settings.ContestStartedAtUtc;
        var entries = AscHistoryStore.Load()
            .Where(entry => since == default || entry.SubmittedAtUtc >= since)
            .Where(entry => string.IsNullOrWhiteSpace(_settings.ContestTaskId) ||
                            string.Equals(entry.TaskId, _settings.ContestTaskId, StringComparison.Ordinal))
            .OrderBy(entry => entry.SubmittedAtUtc)
            .ToList();

        var builder = new StringBuilder();
        builder.AppendLine(_settings.ContestTitle);
        if (since != default)
        {
            builder.AppendLine($"开始：{since.ToLocalTime():MM-dd HH:mm:ss}");
        }

        if (_settings.ContestEndsAtUtc != default)
        {
            builder.AppendLine($"计划结束：{_settings.ContestEndsAtUtc.ToLocalTime():HH:mm}");
        }

        if (_settings.ContestPaused)
        {
            builder.AppendLine(
                "当前状态：已暂停，剩余 " +
                FormatRemainingText(TimeSpan.FromMilliseconds(_settings.ContestPausedRemainingMs)));
        }

        if (!string.IsNullOrWhiteSpace(_settings.ContestTaskName))
        {
            builder.AppendLine($"任务：{_settings.ContestTaskName}");
        }

        var accepted = entries.Count(entry =>
            string.Equals(entry.Verdict, "AC", StringComparison.OrdinalIgnoreCase));
        builder.AppendLine($"提交 {entries.Count} 次 · AC {accepted} 次");
        builder.AppendLine();

        if (entries.Count == 0)
        {
            builder.AppendLine("本次计时期间还没有在线提交记录。");
        }
        else
        {
            foreach (var entry in entries)
            {
                builder.Append(entry.SubmittedAtUtc.ToLocalTime().ToString("HH:mm:ss"))
                    .Append("  ")
                    .Append(entry.DisplayId.PadRight(8))
                    .Append(entry.VerdictText.PadRight(4))
                    .Append("  ")
                    .AppendLine(entry.PerformanceText);
            }
        }

        return builder.ToString().TrimEnd();
    }

    private async Task ShowContestSummaryAsync()
    {
        await Dialogs.ShowDetailsAsync(this, "比赛结果汇总", BuildContestSummary());
    }

    private async Task ExportContestSummaryAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出比赛汇总",
            SuggestedFileName =
                SanitizeFileName(_settings.ContestTitle, "比赛汇总") +
                "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt",
            DefaultExtension = "txt",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("文本报告")
                {
                    Patterns = new[] { "*.txt" }
                }
            }
        });

        if (file is null)
        {
            return;
        }

        try
        {
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream, new UTF8Encoding(true));
            await writer.WriteAsync(BuildContestSummary());
            StatusText.Text = "比赛汇总已导出";
            AppendRunLog("课堂计时：汇总已导出到 " + file.Path);
        }
        catch (Exception ex)
        {
            await Dialogs.ShowMessageAsync(this, "导出汇总失败", ex.Message);
        }
    }

    private static string SanitizeFileName(string? value, string fallback)
    {
        var text = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            text = text.Replace(ch, '_');
        }

        return text.Length == 0 ? fallback : text;
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

    private async void Stress_Click(object? sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        if (_currentFilePath is null || _isDirty)
        {
            if (!await SaveFileAsync())
            {
                return;
            }
        }

        if (_currentFilePath is null)
        {
            return;
        }

        var window = new StressWindow(_currentFilePath, _settings);
        await window.ShowDialog(this);
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
            var completed = 0;
            var parallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
            using var gate = new SemaphoreSlim(parallelism);

            var tasks = cases.Select(async testCase =>
            {
                try
                {
                    await gate.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                try
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    await RunCaseAsync(
                        testCase,
                        compile.ExecutablePath!,
                        cancellationToken,
                        refreshDiff: false);

                    completed++;
                    slowest = Math.Max(slowest, testCase.ElapsedMs);

                    if (!string.IsNullOrWhiteSpace(testCase.ExpectedOutput))
                    {
                        withExpected++;
                        if (testCase.Status == "通过")
                        {
                            passed++;
                        }
                    }

                    StatusText.Text =
                        $"进度 {completed}/{cases.Count} · {testCase.Name}：{testCase.Status}";
                }
                finally
                {
                    gate.Release();
                }
            });

            await Task.WhenAll(tasks);

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
            result = new RunResult(RunOutcome.StartError, -1, string.Empty, string.Empty, 0, 0, 0, ex.Message);
        }

        testCase.ElapsedMs = result.ElapsedMs;
        testCase.StartupMs = result.StartupMs;
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
            $"{testCase.Name}：{testCase.Status}，用时 {result.ElapsedMs} ms" +
            $"（启动 {result.StartupMs} ms），" +
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
        SyncActiveTabState();
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
        AscSubmitButton.IsEnabled = false;
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
        RefreshAscSubmitEnabled();
        SelectAllCheckBox.IsEnabled = true;
        StopButton.IsVisible = false;
        StopButton.IsEnabled = true;
        UpdateSelectAllState();
    }

    private void Stop_Click(object? sender, RoutedEventArgs e)
    {
        _operationCts?.Cancel();
        _ascSubmissionCts?.Cancel();
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
        else if (control && e.Key == Key.J)
        {
            e.Handled = true;
            await OpenSnippetWindowAsync();
        }
        else if (control && !shift && e.Key == Key.W)
        {
            e.Handled = true;
            await CloseCurrentTabAsync();
        }
        else if (control && e.Key == Key.Tab && _tabs.Count > 1)
        {
            e.Handled = true;
            var delta = shift ? -1 : 1;
            var next = (_activeTabIndex + delta + _tabs.Count) % _tabs.Count;
            await SwitchToTabAsync(next);
        }
    }
}
