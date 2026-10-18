using RedPanda.Competition.Models;
using RedPanda.Competition.Services;

namespace RedPanda.Competition.Tests;

public sealed class AscLinkStoreTests
{
    [Fact]
    public void SaveLoad_RoundTripsTaskLink()
    {
        var directory = CreateTempDirectory();
        var sourcePath = Path.Combine(directory, "A1131.cpp");

        try
        {
            File.WriteAllText(sourcePath, "// source");

            var link = new AscProblemLink
            {
                DisplayId = "A1131",
                ProblemId = "123",
                Title = "加法",
                Source = AscProblemSource.Work,
                TaskId = "55",
                TaskName = "作业一",
                ClassId = "9",
                ClassName = "算法班",
                TimeLimitMs = 1000,
                MemoryLimitMb = 256,
                LastVerdict = "AC",
                LastSubmissionId = "999"
            };

            AscLinkStore.Save(sourcePath, link);
            var loaded = AscLinkStore.Load(sourcePath);

            Assert.NotNull(loaded);
            Assert.Equal("A1131", loaded!.DisplayId);
            Assert.Equal(AscProblemSource.Work, loaded.Source);
            Assert.Equal("55", loaded.TaskId);
            Assert.Equal(1000, loaded.TimeLimitMs);
            Assert.Equal("AC", loaded.LastVerdict);
            Assert.True(File.Exists(sourcePath + ".aisichuang.json"));

            AscLinkStore.Delete(sourcePath);
            Assert.Null(AscLinkStore.Load(sourcePath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LoadOrInfer_TreatsProblemLikeFileNamesAsBankProblem()
    {
        var directory = CreateTempDirectory();
        var sourcePath = Path.Combine(directory, "A1131.cpp");

        try
        {
            File.WriteAllText(sourcePath, "// source");

            var link = AscLinkStore.LoadOrInfer(sourcePath);

            Assert.NotNull(link);
            Assert.Equal("A1131", link!.DisplayId);
            Assert.Equal(AscProblemSource.Bank, link.Source);
            Assert.Equal(string.Empty, link.ProblemId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LoadOrInfer_ReturnsNullForOrdinaryNames()
    {
        var directory = CreateTempDirectory();
        var sourcePath = Path.Combine(directory, "main.cpp");

        try
        {
            File.WriteAllText(sourcePath, "// source");
            Assert.Null(AscLinkStore.LoadOrInfer(sourcePath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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
