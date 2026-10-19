using RedPanda.Competition.Models;
using RedPanda.Competition.Services;

namespace RedPanda.Competition.Tests;

public sealed class AscHistoryFilterTests
{
    private static readonly List<AscSubmissionHistoryEntry> Entries = new()
    {
        new()
        {
            SubmissionId = "1",
            DisplayId = "A1",
            Title = "A+B",
            TaskName = "班测 55",
            Source = AscProblemSource.Test,
            Verdict = "AC",
            TimeMs = 12,
            MemoryBytes = 1048576,
            SubmittedAtUtc = new DateTime(2026, 10, 19, 8, 0, 0, DateTimeKind.Utc)
        },
        new()
        {
            SubmissionId = "2",
            DisplayId = "A2",
            Title = "A-B",
            TaskName = "班测 55",
            Source = AscProblemSource.Test,
            Verdict = "WA",
            TimeMs = 4,
            MemoryBytes = 512 * 1024,
            SubmittedAtUtc = new DateTime(2026, 10, 19, 8, 5, 0, DateTimeKind.Utc)
        },
        new()
        {
            SubmissionId = "3",
            DisplayId = "B1",
            Title = "字符串",
            TaskName = "作业 7",
            Source = AscProblemSource.Work,
            Verdict = string.Empty,
            SubmittedAtUtc = new DateTime(2026, 10, 19, 8, 10, 0, DateTimeKind.Utc)
        }
    };

    [Fact]
    public void Filter_MatchesTextAndVerdict()
    {
        Assert.Equal(3, AscHistoryFilter.Filter(Entries, "", AscHistoryVerdictFilter.All).Count);

        var accepted = AscHistoryFilter.Filter(Entries, "", AscHistoryVerdictFilter.Accepted);
        Assert.Single(accepted);
        Assert.Equal("A1", accepted[0].DisplayId);

        var notAccepted = AscHistoryFilter.Filter(Entries, "", AscHistoryVerdictFilter.NotAccepted);
        Assert.Equal(2, notAccepted.Count);

        var unjudged = AscHistoryFilter.Filter(Entries, "", AscHistoryVerdictFilter.Unjudged);
        Assert.Single(unjudged);
        Assert.Equal("B1", unjudged[0].DisplayId);

        var byTask = AscHistoryFilter.Filter(Entries, "班测", AscHistoryVerdictFilter.All);
        Assert.Equal(2, byTask.Count);

        var byId = AscHistoryFilter.Filter(Entries, "3", AscHistoryVerdictFilter.All);
        Assert.Single(byId);
        Assert.Equal("B1", byId[0].DisplayId);
    }

    [Fact]
    public void BuildCsv_EscapesAndFormatsPerformance()
    {
        var csv = AscHistoryFilter.BuildCsv(Entries.Take(2));
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith("时间,提交号", lines[0]);
        Assert.Contains("\"A1\"", lines[1]);
        Assert.Contains("\"AC\"", lines[1]);
        Assert.Contains(",12,1.00,", lines[1]);
        Assert.Contains("\"WA\"", lines[2]);
    }
}
