using CommunityToolkit.Mvvm.ComponentModel;

namespace RedPanda.Competition.Models;

public partial class TestCase : ObservableObject
{
    [ObservableProperty]
    private string _name = "样例 1";

    [ObservableProperty]
    private string _input = string.Empty;

    [ObservableProperty]
    private string _expectedOutput = string.Empty;

    [ObservableProperty]
    private string _actualOutput = string.Empty;

    [ObservableProperty]
    private string _status = "未运行";

    [ObservableProperty]
    private long _elapsedMs;

    [ObservableProperty]
    private double _peakMemoryMb;

    [ObservableProperty]
    private int _exitCode;

    [ObservableProperty]
    private bool _hasRun;

    [ObservableProperty]
    private bool _isChecked = true;

    /// <summary>
    /// Full stdout of the last run. <see cref="ActualOutput"/> may be truncated
    /// for display, while this value is kept intact for "copy to expected".
    /// </summary>
    public string RawActualOutput { get; set; } = string.Empty;

    public string PerformanceText => HasRun
        ? $"{ElapsedMs} ms · {PeakMemoryMb:F1} MB"
        : string.Empty;

    partial void OnElapsedMsChanged(long value)
    {
        OnPropertyChanged(nameof(PerformanceText));
    }

    partial void OnPeakMemoryMbChanged(double value)
    {
        OnPropertyChanged(nameof(PerformanceText));
    }

    partial void OnHasRunChanged(bool value)
    {
        OnPropertyChanged(nameof(PerformanceText));
    }
}
