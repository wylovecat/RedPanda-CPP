namespace RedPanda.Competition.Models;

/// <summary>
/// Recovery snapshot for the single open document. It deliberately stores the
/// text separately from the source file so a crash cannot destroy the original.
/// </summary>
public sealed class DraftState
{
    public int Version { get; set; } = 1;

    public string? SourcePath { get; set; }

    public string EncodingName { get; set; } = "UTF-8";

    public string Text { get; set; } = string.Empty;

    public DateTime SavedAtUtc { get; set; } = DateTime.UtcNow;

    public List<TestCaseFile> Tests { get; set; } = new();
}
