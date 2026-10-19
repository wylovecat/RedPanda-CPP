using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace RedPanda.Competition.Views;

public enum ContestDialogAction
{
    None,
    Start,
    Extend,
    Pause,
    Resume,
    Finish,
    Summary,
    Export,
    Clear,
    Restart
}

public sealed record ContestDialogState(
    bool Active,
    bool Finished,
    bool Paused,
    long PausedRemainingMs,
    string Title,
    int DurationMinutes,
    bool LockSubmission,
    DateTime EndsAtUtc,
    string TaskName);

public partial class ContestWindow : Window
{
    private readonly ContestDialogState _state;
    private readonly DispatcherTimer _timer;
    private bool _running;
    private bool _paused;
    private bool _finished;

    public ContestWindow()
        : this(new ContestDialogState(
            false, false, false, 0, "课堂测试", 90, true,
            DateTime.MinValue, string.Empty))
    {
    }

    public ContestWindow(ContestDialogState state)
    {
        _state = state;
        InitializeComponent();

        _running = state.Active && state.EndsAtUtc > DateTime.UtcNow;
        _paused = state.Paused && !state.Finished;
        _finished = state.Finished || (state.Active && !_running);

        TitleBox.Text = state.Title;
        MinutesBox.Value = Math.Clamp(state.DurationMinutes, 1, 600);
        LockCheckBox.IsChecked = state.LockSubmission;

        SetupPanel.IsVisible = !_running && !_paused && !_finished;
        RunningPanel.IsVisible = _running;
        PausedPanel.IsVisible = _paused;
        FinishedPanel.IsVisible = _finished;
        StartButton.IsVisible = !_running && !_paused && !_finished;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += (_, _) => UpdateRemaining();
        _timer.Start();

        UpdateRemaining();
        UpdateHeader();
        Closed += (_, _) => _timer.Stop();
    }

    public ContestDialogAction Action { get; private set; } = ContestDialogAction.None;

    public string ContestTitle => TitleBox.Text?.Trim() is { Length: > 0 } title
        ? title
        : "课堂测试";

    public int DurationMinutes => (int)Math.Clamp(MinutesBox.Value ?? 90, 1, 600);

    public bool LockSubmission => LockCheckBox.IsChecked == true;

    private void UpdateHeader()
    {
        if (_running)
        {
            HeaderText.Text = _state.Title + " · 进行中";
            HintText.Text = BuildRunningHint();
        }
        else if (_paused)
        {
            HeaderText.Text = _state.Title + " · 已暂停";
            HintText.Text =
                $"剩余 {FormatRemaining(TimeSpan.FromMilliseconds(_state.PausedRemainingMs))}。" +
                " 继续后按剩余时间倒计时。";
        }
        else if (_finished)
        {
            HeaderText.Text = _state.Title + " · 已结束";
            HintText.Text =
                (_state.LockSubmission ? "交题已锁定。" : "交题未锁定。") +
                " 可以查看本次汇总，或清除计时后重新开始。";
        }
        else
        {
            HeaderText.Text = "课堂计时";
            HintText.Text = string.IsNullOrWhiteSpace(_state.TaskName)
                ? "将绑定当前文件所属任务；没有任务时按时间范围汇总。"
                : "绑定任务：" + _state.TaskName;
        }
    }

    private string BuildRunningHint()
    {
        var lockText = _state.LockSubmission ? "结束后锁定交题" : "结束后不锁定交题";
        var taskText = string.IsNullOrWhiteSpace(_state.TaskName)
            ? "未绑定任务"
            : "任务：" + _state.TaskName;
        return $"结束时间 {_state.EndsAtUtc.ToLocalTime():HH:mm} · {lockText}\n{taskText}";
    }

    private void UpdateRemaining()
    {
        if (_paused)
        {
            RemainingText.Text = FormatRemaining(
                TimeSpan.FromMilliseconds(_state.PausedRemainingMs));
            return;
        }

        if (!_running)
        {
            RemainingText.Text = _finished ? "已结束" : FormatDuration(DurationMinutes);
            return;
        }

        var remaining = _state.EndsAtUtc - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            _running = false;
            _finished = true;
            RunningPanel.IsVisible = false;
            FinishedPanel.IsVisible = true;
            StartButton.IsVisible = false;
            UpdateHeader();
            RemainingText.Text = "已结束";
            return;
        }

        RemainingText.Text = FormatRemaining(remaining);
    }

    private void Start_Click(object? sender, RoutedEventArgs e)
    {
        if (DurationMinutes <= 0)
        {
            HintText.Text = "请输入 1 到 600 之间的分钟数。";
            return;
        }

        Action = ContestDialogAction.Start;
        Close();
    }

    private void Extend_Click(object? sender, RoutedEventArgs e)
    {
        Action = ContestDialogAction.Extend;
        Close();
    }

    private void Pause_Click(object? sender, RoutedEventArgs e)
    {
        Action = ContestDialogAction.Pause;
        Close();
    }

    private void Resume_Click(object? sender, RoutedEventArgs e)
    {
        Action = ContestDialogAction.Resume;
        Close();
    }

    private void Export_Click(object? sender, RoutedEventArgs e)
    {
        Action = ContestDialogAction.Export;
        Close();
    }

    private void Finish_Click(object? sender, RoutedEventArgs e)
    {
        Action = ContestDialogAction.Finish;
        Close();
    }

    private void Summary_Click(object? sender, RoutedEventArgs e)
    {
        Action = ContestDialogAction.Summary;
        Close();
    }

    private void Clear_Click(object? sender, RoutedEventArgs e)
    {
        Action = ContestDialogAction.Clear;
        Close();
    }

    private void Restart_Click(object? sender, RoutedEventArgs e)
    {
        Action = ContestDialogAction.Restart;
        Close();
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private static string FormatRemaining(TimeSpan remaining)
    {
        var total = (int)Math.Ceiling(remaining.TotalSeconds);
        var hours = total / 3600;
        var minutes = total % 3600 / 60;
        var seconds = total % 60;
        return hours > 0
            ? $"{hours}:{minutes:00}:{seconds:00}"
            : $"{minutes:00}:{seconds:00}";
    }

    private static string FormatDuration(int minutes)
    {
        return minutes >= 60
            ? $"{minutes / 60}:{minutes % 60:00}"
            : $"{minutes:00}:00";
    }
}
