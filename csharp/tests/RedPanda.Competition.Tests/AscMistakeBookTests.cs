using RedPanda.Competition.Models;
using RedPanda.Competition.Services;

namespace RedPanda.Competition.Tests;

public sealed class AscMistakeBookTests
{
    [Fact]
    public void Build_KeepsUnsolvedAndDropsSolvedProblems()
    {
        var now = new DateTime(2026, 10, 19, 8, 0, 0, DateTimeKind.Utc);
        var entries = new List<AscSubmissionHistoryEntry>
        {
            Entry("1", "A1", "WA", now.AddMinutes(-30), @"C:\p\A1.cpp"),
            Entry("2", "A1", "AC", now.AddMinutes(-20), @"C:\p\A1.cpp"),
            Entry("3", "A2", "WA", now.AddMinutes(-40), @"C:\p\A2.cpp"),
            Entry("4", "A2", "TLE", now.AddMinutes(-10), @"C:\p\A2.cpp"),
            Entry("5", "A3", "CE", now.AddMinutes(-50), @"C:\p\A3.cpp")
        };

        var mistakes = AscMistakeBook.Build(entries);

        Assert.Equal(2, mistakes.Count);
        Assert.Equal("A2", mistakes[0].DisplayId);
        Assert.Equal("TLE", mistakes[0].Verdict);
        Assert.Equal(2, mistakes[0].FailCount);
        Assert.Equal("4", mistakes[0].LastSubmissionId);
        Assert.Equal("A3", mistakes[1].DisplayId);
        Assert.Equal(1, mistakes[1].FailCount);
    }

    private static AscSubmissionHistoryEntry Entry(
        string id,
        string displayId,
        string verdict,
        DateTime time,
        string path)
    {
        return new AscSubmissionHistoryEntry
        {
            SubmissionId = id,
            DisplayId = displayId,
            Title = displayId,
            TaskName = "班测 55",
            Source = AscProblemSource.Test,
            Verdict = verdict,
            SubmittedAtUtc = time,
            SourcePath = path
        };
    }
}
