namespace RedPanda.Competition.Models;

public sealed record CompileResult(
    bool Success,
    string Output,
    string? ExecutablePath,
    long ElapsedMs,
    string? ErrorMessage,
    string CommandLine);
