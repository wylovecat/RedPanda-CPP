namespace RedPanda.Competition.Models;

public enum RunOutcome
{
    Normal,
    Timeout,
    OutputLimit,
    RuntimeError,
    StartError,
    Cancelled
}

public sealed record RunResult(
    RunOutcome Outcome,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    long ElapsedMs,
    long StartupMs,
    double PeakMemoryMb,
    string? ErrorMessage = null)
{
    public bool Succeeded => Outcome == RunOutcome.Normal && ExitCode == 0;
}
