using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RedPanda.Competition.Models;
using RedPanda.Competition.Services;

namespace RedPanda.Competition.Views;

public partial class AscWindow : Window
{
    private readonly MainWindow? _owner;
    private readonly AscSettings _settings;
    private readonly AscClient _client;
    private readonly List<AscClassInfo> _classes = new();
    private readonly List<AscTaskInfo> _tasks = new();
    private readonly List<AscTaskProblem> _taskProblems = new();
    private readonly List<AscProblemRef> _bankProblems = new();

    private AscTaskInfo? _currentTask;
    private AscClassInfo? _currentClass;
    private string? _currentStatementPath;
    private CancellationTokenSource? _cts;
    private bool _busy;
    private bool _opened;
    private bool _closed;
    private bool _lastTaskRestoreAttempted;

    public AscWindow()
        : this(null)
    {
    }

    public AscWindow(MainWindow? owner)
    {
        _owner = owner;
        _settings = AscSettingsStore.Load();
        _client = new AscClient(baseUrl: _settings.BaseUrl);
        _client.SetSession(_settings.Session);

        InitializeComponent();

        PhoneBox.Text = _settings.LastPhone;
        BaseUrlBox.Text = _settings.BaseUrl;
        TaskRootBox.Text = _settings.TaskRootDir;
        OnlineSubmitCheckBox.IsChecked = _settings.EnableOnlineSubmit;

        LanguageBox.ItemsSource = new[] { "C++" };
        LanguageBox.SelectedIndex = 0;

        TaskKindCombo.ItemsSource = new[] { "班测", "作业" };
        TaskKindCombo.SelectedIndex = _settings.LastTaskKind == 1 ? 1 : 0;

        OperationStatusText.Text = "就绪";
        UpdateUi();

        Opened += OnOpened;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (_opened)
        {
            return;
        }

        _opened = true;

        if (!_settings.Session.IsValid)
        {
            UpdateUi();
            return;
        }

        await RunOperationAsync("正在验证登录…", async cancellationToken =>
        {
            _client.SetBaseUrl(_settings.BaseUrl);
            _client.SetSession(_settings.Session);
            await _client.ProbeSessionAsync(cancellationToken);
            Log("会话有效。");
            await TryLoadClassesAsync(cancellationToken);
            SetStatus("登录状态已恢复");
            await TryRestoreLastTaskAsync(cancellationToken);
        });
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        _cts?.Cancel();
        SaveSettingsFromUi();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _closed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _client.Dispose();
    }

    private void SetStatus(string text)
    {
        OperationStatusText.Text = text;
    }

    private void Log(string text)
    {
        var current = LogBox.Text ?? string.Empty;
        if (current.Length > 20_000)
        {
            current = current[^20_000..];
        }

        LogBox.Text = current.Length == 0 ? text : current + Environment.NewLine + text;
        LogBox.CaretIndex = LogBox.Text?.Length ?? 0;
    }

    private async Task RunOperationAsync(string status, Func<CancellationToken, Task> action)
    {
        if (_busy || _closed)
        {
            return;
        }

        _busy = true;
        SetStatus(status);
        _cts = new CancellationTokenSource();
        UpdateUi();

        try
        {
            await action(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            SetStatus("操作已取消");
        }
        catch (AscAuthException ex)
        {
            await HandleAuthExpiredAsync(ex.Message);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
            Log("错误：" + ex.Message);
        }
        finally
        {
            _busy = false;
            _cts?.Dispose();
            _cts = null;
            if (!_closed)
            {
                UpdateUi();
            }
        }
    }

    private Task HandleAuthExpiredAsync(string message)
    {
        _client.ClearSession();
        _settings.Session = new AscSession { BaseUrl = _settings.BaseUrl };
        AscSettingsStore.Save(_settings);
        _classes.Clear();
        _tasks.Clear();
        _taskProblems.Clear();
        _bankProblems.Clear();
        ClassCombo.ItemsSource = null;
        TaskCombo.ItemsSource = null;
        TaskProblemList.ItemsSource = null;
        BankList.ItemsSource = null;
        SetStatus("登录已过期，请重新登录");
        Log("登录已过期：" + message);
        return Task.CompletedTask;
    }

    private bool IsTeacher =>
        _client.IsLoggedIn &&
        !string.IsNullOrWhiteSpace(_client.Session.Role) &&
        !string.Equals(_client.Session.Role, "student", StringComparison.OrdinalIgnoreCase);

    private void UpdateUi()
    {
        var loggedIn = _client.IsLoggedIn;
        var busy = _busy;

        if (loggedIn)
        {
            AccountStatusText.Text =
                $"爱思创：已登录 {AscJson.MaskAccount(_client.Session.Phone)}（{_client.Session.Role}）";
            AccountHintText.Text = "可拉取班级测验/作业和题库题目；密码不会保存。";
        }
        else
        {
            AccountStatusText.Text = "爱思创：未登录";
            AccountHintText.Text = "登录后可拉取班级测验/作业和题库题目；密码不会保存。";
        }

        LoginButton.IsEnabled = !loggedIn && !busy;
        LogoutButton.IsVisible = loggedIn;
        LogoutButton.IsEnabled = loggedIn && !busy;
        PhoneBox.IsEnabled = !loggedIn && !busy;
        PasswordBox.IsEnabled = !loggedIn && !busy;
        SubmitButton.IsEnabled = loggedIn && !busy;

        BaseUrlBox.IsEnabled = !busy;
        TaskRootBox.IsEnabled = !busy;
        OnlineSubmitCheckBox.IsEnabled = !busy;
        LanguageBox.IsEnabled = !busy;

        MainTabs.IsEnabled = !busy;

        var teacher = IsTeacher;
        ClassRow.IsVisible = teacher;
        TaskPickRow.IsVisible = teacher;
        RefreshClassesButton.IsVisible = teacher;
        LoadTasksButton.IsVisible = teacher;

        RefreshClassesButton.IsEnabled = teacher && !busy;
        LoadTasksButton.IsEnabled = teacher && !busy;
        OpenTaskButton.IsEnabled = teacher && !busy && TaskCombo.SelectedItem is AscTaskInfo;
        LoadTaskByIdButton.IsEnabled = loggedIn && !busy;
        TaskKindCombo.IsEnabled = !busy;

        ImportTaskButton.IsEnabled = loggedIn && !busy;
        StatementTaskButton.IsEnabled = loggedIn && !busy;
        SearchBankButton.IsEnabled = loggedIn && !busy;
        ImportBankButton.IsEnabled = loggedIn && !busy;
        StatementBankButton.IsEnabled = loggedIn && !busy;

        OpenStatementFileButton.IsEnabled = _currentStatementPath is not null;
    }

    private void PasswordBox_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key != Avalonia.Input.Key.Enter)
        {
            return;
        }

        e.Handled = true;
        Login_Click(sender, new RoutedEventArgs());
    }

    private async void Login_Click(object? sender, RoutedEventArgs e)
    {
        SaveSettingsFromUi();

        var phone = PhoneBox.Text?.Trim() ?? string.Empty;
        var password = PasswordBox.Text ?? string.Empty;

        if (phone.Length == 0 || password.Length == 0)
        {
            SetStatus("请输入手机号和密码");
            return;
        }

        var baseUrl = BaseUrlBox.Text?.Trim() ?? AscSettings.DefaultBaseUrl;

        var previousPhone = _settings.LastPhone;

        await RunOperationAsync("正在登录…", async cancellationToken =>
        {
            _client.SetBaseUrl(baseUrl);
            var session = await _client.LoginAsync(phone, password, cancellationToken);

            if (!string.Equals(previousPhone, phone, StringComparison.OrdinalIgnoreCase))
            {
                _settings.LastTaskId = string.Empty;
                _settings.LastTaskName = string.Empty;
                _settings.LastTaskKind = 0;
                _settings.LastClassId = string.Empty;
                _settings.LastClassName = string.Empty;
                _lastTaskRestoreAttempted = false;
            }

            _settings.BaseUrl = _client.BaseUrl;
            _settings.Session = session;
            _settings.LastPhone = phone;
            AscSettingsStore.Save(_settings);

            PasswordBox.Text = string.Empty;
            Log($"登录成功：{AscJson.MaskAccount(phone)}（{session.Role}）。");
            await TryLoadClassesAsync(cancellationToken);
            SetStatus("登录成功");
            await TryRestoreLastTaskAsync(cancellationToken);
        });
    }

    private void Logout_Click(object? sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        _client.ClearSession();
        _settings.Session = new AscSession { BaseUrl = _settings.BaseUrl };
        AscSettingsStore.Save(_settings);
        _lastTaskRestoreAttempted = false;

        _classes.Clear();
        _tasks.Clear();
        _taskProblems.Clear();
        _bankProblems.Clear();
        ClassCombo.ItemsSource = null;
        TaskCombo.ItemsSource = null;
        TaskProblemList.ItemsSource = null;
        BankList.ItemsSource = null;
        _currentTask = null;
        _currentClass = null;
        SetStatus("已退出登录");
        Log("已退出登录。");
        UpdateUi();
    }

    private async Task TryLoadClassesAsync(CancellationToken cancellationToken)
    {
        if (!IsTeacher)
        {
            return;
        }

        try
        {
            var classes = await _client.ListClassesAsync(cancellationToken);
            _classes.Clear();
            _classes.AddRange(classes);
            ClassCombo.ItemsSource = null;
            ClassCombo.ItemsSource = _classes;

            var lastIndex = _classes.FindIndex(item =>
                string.Equals(item.ClassId, _settings.LastClassId, StringComparison.Ordinal));
            if (_classes.Count > 0)
            {
                ClassCombo.SelectedIndex = lastIndex >= 0 ? lastIndex : 0;
            }

            Log($"班级列表：{_classes.Count} 个。");
            SetStatus($"已加载 {_classes.Count} 个班级");
            TaskStatusText.Text = _classes.Count == 0
                ? "当前账号没有返回班级；可直接用任务 ID 加载。"
                : $"已加载 {_classes.Count} 个班级。";
        }
        catch (AscAuthException)
        {
            throw;
        }
        catch (Exception ex)
        {
            TaskStatusText.Text = "班级列表加载失败：" + ex.Message;
            Log("班级列表加载失败：" + ex.Message);
        }
    }

    private async void RefreshClasses_Click(object? sender, RoutedEventArgs e)
    {
        await RunOperationAsync("正在刷新班级…", TryLoadClassesAsync);
    }

    private void ClassCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        ClearTaskSelection();
    }

    private void TaskKindCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        ClearTaskSelection();
    }

    private void ClearTaskSelection()
    {
        _tasks.Clear();
        TaskCombo.ItemsSource = null;
        TaskCombo.SelectedItem = null;
        _currentTask = null;
        _taskProblems.Clear();
        TaskProblemList.ItemsSource = null;
        TaskProblemList.SelectedItem = null;
        TaskIdBox.Text = string.Empty;
        TaskStatusText.Text = string.Empty;
        UpdateUi();
    }

    private async void LoadTasks_Click(object? sender, RoutedEventArgs e)
    {
        var classInfo = ClassCombo.SelectedItem as AscClassInfo;
        if (classInfo is null)
        {
            SetStatus("请先选择班级；学生账号可直接输入任务 ID。");
            return;
        }

        var source = TaskKindCombo.SelectedIndex == 1 ? AscProblemSource.Work : AscProblemSource.Test;

        await RunOperationAsync(source == AscProblemSource.Work ? "正在加载作业…" : "正在加载班测…",
            async cancellationToken =>
            {
                var tasks = await _client.ListTasksAsync(source, classInfo.ClassId, cancellationToken);
                _tasks.Clear();
                _tasks.AddRange(tasks);
                TaskCombo.ItemsSource = null;
                TaskCombo.ItemsSource = _tasks;
                if (_tasks.Count > 0)
                {
                    TaskCombo.SelectedIndex = 0;
                }

                _currentClass = classInfo;
                _settings.LastClassId = classInfo.ClassId;
                _settings.LastClassName = classInfo.DisplayName;
                _settings.LastTaskKind = source == AscProblemSource.Work ? 1 : 0;
                AscSettingsStore.Save(_settings);

                Log($"任务列表：{_tasks.Count} 个。");
                SetStatus($"已加载 {_tasks.Count} 个任务");
                TaskStatusText.Text = _tasks.Count == 0
                    ? "没有返回任务；可直接用任务 ID 加载。"
                    : $"已加载 {_tasks.Count} 个任务。";
            });
    }

    private async void OpenTask_Click(object? sender, RoutedEventArgs e)
    {
        if (TaskCombo.SelectedItem is not AscTaskInfo task)
        {
            SetStatus("请先选择一个任务");
            return;
        }

        await OpenTaskAsync(task, ClassCombo.SelectedItem as AscClassInfo);
    }

    private void TaskIdBox_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key != Avalonia.Input.Key.Enter)
        {
            return;
        }

        e.Handled = true;
        LoadTaskById_Click(sender, new RoutedEventArgs());
    }

    private async void LoadTaskById_Click(object? sender, RoutedEventArgs e)
    {
        var taskId = TaskIdBox.Text?.Trim() ?? string.Empty;
        if (taskId.Length == 0 || !taskId.All(char.IsDigit))
        {
            SetStatus("任务 ID 必须是数字");
            return;
        }

        var source = TaskKindCombo.SelectedIndex == 1 ? AscProblemSource.Work : AscProblemSource.Test;
        var classInfo = ClassCombo.SelectedItem as AscClassInfo;
        var task = new AscTaskInfo
        {
            Source = source,
            TaskId = taskId,
            TaskName = (source == AscProblemSource.Work ? "作业 " : "班测 ") + taskId
        };

        await OpenTaskAsync(task, classInfo);
    }

    private async Task OpenTaskAsync(AscTaskInfo task, AscClassInfo? classInfo)
    {
        await RunOperationAsync(
            "正在加载任务题目…",
            cancellationToken => LoadTaskProblemsCoreAsync(task, classInfo, cancellationToken));
    }

    private async Task LoadTaskProblemsCoreAsync(
        AscTaskInfo task,
        AscClassInfo? classInfo,
        CancellationToken cancellationToken)
    {
        var problems = await _client.ListTaskProblemsAsync(task.Source, task.TaskId, cancellationToken);

        _currentTask = task;
        _currentClass = classInfo;
        _taskProblems.Clear();
        _taskProblems.AddRange(problems);
        TaskProblemList.ItemsSource = null;
        TaskProblemList.ItemsSource = _taskProblems;
        if (_taskProblems.Count > 0)
        {
            TaskProblemList.SelectedIndex = 0;
        }

        TaskIdBox.Text = task.TaskId;

        _settings.LastTaskKind = task.Source == AscProblemSource.Work ? 1 : 0;
        _settings.LastTaskId = task.TaskId;
        _settings.LastTaskName = task.TaskName;
        if (classInfo is not null)
        {
            _settings.LastClassId = classInfo.ClassId;
            _settings.LastClassName = classInfo.DisplayName;
        }

        AscSettingsStore.Save(_settings);

        Log($"任务 {task.TaskId}：{problems.Count} 道编程题。");
        TaskStatusText.Text = $"{task.TaskName}：{problems.Count} 道编程题";
        SetStatus($"已加载 {task.TaskName}");
    }

    private async Task TryRestoreLastTaskAsync(CancellationToken cancellationToken)
    {
        if (_lastTaskRestoreAttempted)
        {
            return;
        }

        _lastTaskRestoreAttempted = true;

        if (string.IsNullOrWhiteSpace(_settings.LastTaskId))
        {
            return;
        }

        try
        {
            var source = _settings.LastTaskKind == 1 ? AscProblemSource.Work : AscProblemSource.Test;
            var taskName = string.IsNullOrWhiteSpace(_settings.LastTaskName)
                ? (source == AscProblemSource.Work ? "作业 " : "班测 ") + _settings.LastTaskId
                : _settings.LastTaskName;

            var task = new AscTaskInfo
            {
                Source = source,
                TaskId = _settings.LastTaskId,
                TaskName = taskName
            };

            AscClassInfo? classInfo = null;
            if (!string.IsNullOrWhiteSpace(_settings.LastClassId))
            {
                var classIndex = _classes.FindIndex(item =>
                    string.Equals(item.ClassId, _settings.LastClassId, StringComparison.Ordinal));
                classInfo = classIndex >= 0
                    ? _classes[classIndex]
                    : new AscClassInfo
                    {
                        ClassId = _settings.LastClassId,
                        ClassName = _settings.LastClassName
                    };

                if (classIndex >= 0)
                {
                    ClassCombo.SelectedIndex = classIndex;
                }
            }

            TaskKindCombo.SelectedIndex = source == AscProblemSource.Work ? 1 : 0;
            SetStatus($"正在恢复上次任务 {taskName}…");
            await LoadTaskProblemsCoreAsync(task, classInfo, cancellationToken);
            Log($"已恢复上次任务：{taskName}");
            SetStatus($"已恢复上次任务 {taskName}");
        }
        catch (AscAuthException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log("恢复上次任务失败：" + ex.Message);
            SetStatus("上次任务恢复失败，可手动加载");
        }
    }

    private void BankSearchBox_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key != Avalonia.Input.Key.Enter)
        {
            return;
        }

        e.Handled = true;
        SearchBank_Click(sender, new RoutedEventArgs());
    }

    private async void SearchBank_Click(object? sender, RoutedEventArgs e)
    {
        var keyword = BankSearchBox.Text?.Trim() ?? string.Empty;
        if (keyword.Length == 0)
        {
            SetStatus("请输入题号或关键字");
            return;
        }

        await RunOperationAsync("正在搜索题库…", async cancellationToken =>
        {
            var (problems, total) = await _client.SearchProblemsAsync(keyword, 1, cancellationToken);
            _bankProblems.Clear();
            _bankProblems.AddRange(problems);
            BankList.ItemsSource = null;
            BankList.ItemsSource = _bankProblems;
            if (_bankProblems.Count > 0)
            {
                BankList.SelectedIndex = 0;
            }

            Log($"题库搜索：显示 {problems.Count} / {total}。");
            BankStatusText.Text = $"显示 {problems.Count} / {total}";
            SetStatus($"题库搜索完成：{problems.Count} 条");
        });
    }

    private async void ImportTask_Click(object? sender, RoutedEventArgs e)
    {
        if (TaskProblemList.SelectedItem is not AscTaskProblem selected || _currentTask is null)
        {
            SetStatus("请先选择一道题");
            return;
        }

        await ImportProblemAsync(selected.DisplayId, selected.Title, _currentTask.Source, _currentTask, _currentClass);
    }

    private async void StatementTask_Click(object? sender, RoutedEventArgs e)
    {
        if (TaskProblemList.SelectedItem is not AscTaskProblem selected)
        {
            SetStatus("请先选择一道题");
            return;
        }

        await ShowStatementOnlyAsync(selected.DisplayId, selected.Title);
    }

    private async void ImportBank_Click(object? sender, RoutedEventArgs e)
    {
        if (BankList.SelectedItem is not AscProblemRef selected)
        {
            SetStatus("请先选择一道题");
            return;
        }

        await ImportProblemAsync(selected.DisplayId, selected.Title, AscProblemSource.Bank, null, null);
    }

    private async void StatementBank_Click(object? sender, RoutedEventArgs e)
    {
        if (BankList.SelectedItem is not AscProblemRef selected)
        {
            SetStatus("请先选择一道题");
            return;
        }

        await ShowStatementOnlyAsync(selected.DisplayId, selected.Title);
    }

    private async Task ImportProblemAsync(
        string displayId,
        string title,
        AscProblemSource source,
        AscTaskInfo? task,
        AscClassInfo? classInfo)
    {
        if (_owner is null)
        {
            return;
        }

        SaveSettingsFromUi();

        await RunOperationAsync("正在下载样例…", async cancellationToken =>
        {
            // The statement and samples share one endpoint; the samples are
            // required for local testing, but the statement is only shown on
            // demand and never forces the Statement tab open.
            var statement = await _client.FetchStatementAsync(displayId, title, cancellationToken);

            var problem = new AscProblemRef
            {
                DisplayId = statement.DisplayId,
                ProblemId = statement.ProblemId,
                Title = statement.Title
            };

            var result = await _owner.ImportAscProblemAsync(
                statement,
                problem,
                source,
                task,
                classInfo?.ClassId,
                classInfo?.DisplayName,
                _settings);

            if (result is null)
            {
                SetStatus("已取消导入");
                return;
            }

            ShowStatement(statement, result.StatementPath, switchToTab: false);

            Log($"已导入 {result.Link.DisplayId}：{result.SourcePath}");
            Log($"已写入 {result.TestCases.Count} 个样例；题面可在“题面”标签按需查看。");
            SetStatus($"已导入并打开 {result.Link.DisplayId}");
            TaskStatusText.Text = result.CreatedSource ? "已新建源码并打开" : "已打开已有源码";
            BankStatusText.Text = "已导入并打开";
            _owner.Activate();
        });
    }

    private async Task ShowStatementOnlyAsync(string displayId, string title)
    {
        await RunOperationAsync("正在读取题面…", async cancellationToken =>
        {
            var statement = await _client.FetchStatementAsync(displayId, title, cancellationToken);
            ShowStatement(statement, null);
            SetStatus($"已显示 {statement.DisplayId} 的题面");
        });
    }

    private void ShowStatement(AscStatement statement, string? path, bool switchToTab = true)
    {
        _currentStatementPath = path;
        StatementTitleText.Text =
            $"{statement.DisplayId} {statement.Title} · {statement.TimeLimitMs} ms / {statement.MemoryLimitMb} MB";
        StatementBox.Text = string.IsNullOrWhiteSpace(statement.RawMarkdown)
            ? AscJson.StatementToMarkdown(statement)
            : statement.RawMarkdown;
        StatementBox.CaretIndex = 0;
        OpenStatementFileButton.IsEnabled = path is not null;
        if (switchToTab)
        {
            MainTabs.SelectedIndex = 3;
        }
    }

    private void OpenStatementFile_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentStatementPath is null || !File.Exists(_currentStatementPath))
        {
            SetStatus("还没有保存的题面文件");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(_currentStatementPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus("打开题面失败：" + ex.Message);
        }
    }

    private async void Submit_Click(object? sender, RoutedEventArgs e)
    {
        if (_owner is null)
        {
            return;
        }

        SaveSettingsFromUi();
        await _owner.SubmitCurrentFileToAscAsync(this);
    }

    public void NotifySubmissionResult(AscProblemLink link, AscSubmissionResult result)
    {
        var row = _taskProblems.FirstOrDefault(problem =>
            string.Equals(problem.DisplayId, link.DisplayId, StringComparison.OrdinalIgnoreCase));
        if (row is not null)
        {
            row.Verdict = result.Verdict;
            var selected = TaskProblemList.SelectedItem;
            TaskProblemList.ItemsSource = null;
            TaskProblemList.ItemsSource = _taskProblems;
            TaskProblemList.SelectedItem = selected;
        }

        Log($"提交 {link.DisplayId}：{result.VerdictText}，{result.PerformanceText}");
        SetStatus($"提交完成：{result.VerdictText}");
        TaskStatusText.Text = $"{link.DisplayId}：{result.VerdictText}";
    }

    public void HandleSessionExpired()
    {
        _client.ClearSession();
        _settings.Session = new AscSession { BaseUrl = _settings.BaseUrl };
        AscSettingsStore.Save(_settings);
        UpdateUi();
        SetStatus("登录已过期，请重新登录");
        Log("登录已过期，请重新登录。");
    }

    private async void BrowseTaskRoot_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择爱思创题目根目录",
            AllowMultiple = false
        });

        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            TaskRootBox.Text = path;
            _settings.TaskRootDir = path;
            AscSettingsStore.Save(_settings);
        }
    }

    private void OnlineSubmitCheckBox_Click(object? sender, RoutedEventArgs e)
    {
        SaveSettingsFromUi();
    }

    private void SaveSettingsFromUi()
    {
        if (BaseUrlBox is null)
        {
            return;
        }

        _settings.BaseUrl = BaseUrlBox.Text?.Trim() ?? AscSettings.DefaultBaseUrl;
        _settings.TaskRootDir = TaskRootBox.Text?.Trim() ?? string.Empty;
        _settings.EnableOnlineSubmit = OnlineSubmitCheckBox.IsChecked == true;
        _settings.SubmitLanguage = "C++";
        _settings.LastTaskKind = TaskKindCombo.SelectedIndex == 1 ? 1 : 0;
        AscSettingsStore.Save(_settings);
    }
}
