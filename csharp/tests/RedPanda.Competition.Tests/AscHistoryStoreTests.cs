using RedPanda.Competition.Models;
using RedPanda.Competition.Services;

namespace RedPanda.Competition.Tests;

public sealed class AscHistoryStoreTests
{
    [Fact]
    public void Add_KeepsNewestFirstAndDeduplicatesSubmissionId()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "history.json");

        try
        {
            AscHistoryStore.Add(Entry("1", "A1"), path);
            AscHistoryStore.Add(Entry("2", "A2"), path);
            AscHistoryStore.Add(Entry("1", "A1-again"), path);

            var items = AscHistoryStore.Load(path);

            Assert.Equal(2, items.Count);
            Assert.Equal("1", items[0].SubmissionId);
            Assert.Equal("A1-again", items[0].DisplayId);
            Assert.Equal("2", items[1].SubmissionId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Add_CapsHistoryAtLimit()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "history.json");

        try
        {
            for (var i = 0; i < AscHistoryStore.Limit + 10; i++)
            {
                AscHistoryStore.Add(Entry(i.ToString(), "A" + i), path);
            }

            var items = AscHistoryStore.Load(path);

            Assert.Equal(AscHistoryStore.Limit, items.Count);
            Assert.Equal((AscHistoryStore.Limit + 9).ToString(), items[0].SubmissionId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Load_ReturnsEmptyForMissingOrInvalidFile()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "history.json");

        try
        {
            Assert.Empty(AscHistoryStore.Load(path));

            File.WriteAllText(path, "{not json");
            Assert.Empty(AscHistoryStore.Load(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static AscSubmissionHistoryEntry Entry(string id, string displayId)
    {
        return new AscSubmissionHistoryEntry
        {
            SubmissionId = id,
            DisplayId = displayId,
            Title = "题",
            TaskName = "班测",
            Source = AscProblemSource.Test,
            Verdict = "AC",
            TimeMs = 12,
            MemoryBytes = 1024 * 1024,
            SubmittedAtUtc = DateTime.UtcNow
        };
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "RedPandaCPTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);
        return directory;
    }
}
