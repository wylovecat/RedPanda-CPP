namespace RedPanda.Competition.Models;

public sealed class AppSettings
{
    public string CompilerPath { get; set; } = string.Empty;

    public string CppStandard { get; set; } = "c++17";

    public string Optimization { get; set; } = "-O2";

    public string ExtraCompilerArgs { get; set; } = string.Empty;

    public int TimeLimitMs { get; set; } = 2000;

    public int MemoryLimitMb { get; set; } = 256;

    public int OutputLimitKb { get; set; } = 1024;

    public int OutputDisplayLimitKb { get; set; } = 64;

    public double EditorFontSize { get; set; } = 14;

    public string Theme { get; set; } = "Light";

    public bool ShowLineNumbers { get; set; } = true;

    public string LastDirectory { get; set; } = string.Empty;

    public List<string> RecentFiles { get; set; } = new();

    public bool WelcomeShown { get; set; }

    public string ContestTitle { get; set; } = "课堂测试";

    public int ContestDurationMinutes { get; set; } = 90;

    public bool ContestLockSubmission { get; set; } = true;

    public bool ContestLockTask { get; set; }

    public bool ContestFullscreen { get; set; }

    public bool ContestRemind5Shown { get; set; }

    public bool ContestRemind1Shown { get; set; }

    public bool ContestActive { get; set; }

    public bool ContestFinished { get; set; }

    public bool ContestPaused { get; set; }

    public long ContestPausedRemainingMs { get; set; }

    public DateTime ContestStartedAtUtc { get; set; }

    public DateTime ContestEndsAtUtc { get; set; }

    public string ContestTaskId { get; set; } = string.Empty;

    public string ContestTaskName { get; set; } = string.Empty;

    public AscProblemSource ContestTaskSource { get; set; } = AscProblemSource.Bank;

    public AppSettings Clone()
    {
        return (AppSettings)MemberwiseClone();
    }
}
