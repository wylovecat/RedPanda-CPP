using RedPanda.Competition.Models;
using RedPanda.Competition.Services;

namespace RedPanda.Competition.Tests;

public sealed class AscProblemImporterTests
{
    [Fact]
    public void Import_CreatesSourceStatementSamplesAndLink()
    {
        var directory = CreateTempDirectory();

        try
        {
            var settings = new AscSettings { TaskRootDir = directory };
            var statement = Statement("A1131", "加法", "2 3\n", "5\n");
            var problem = new AscProblemRef { DisplayId = "A1131", ProblemId = "123", Title = "加法" };

            var result = AscProblemImporter.Import(
                statement,
                problem,
                AscProblemSource.Bank,
                null,
                null,
                null,
                settings);

            Assert.True(File.Exists(result.SourcePath));
            Assert.True(File.Exists(result.StatementPath));
            Assert.True(File.Exists(TestCaseStore.GetSidecarPath(result.SourcePath)));
            Assert.True(result.CreatedSource);
            Assert.True(result.CreatedTests);
            Assert.Single(result.TestCases);
            Assert.Equal("2 3\n", result.TestCases[0].Input);
            Assert.Equal("5\n", result.TestCases[0].ExpectedOutput);

            var link = AscLinkStore.Load(result.SourcePath);
            Assert.NotNull(link);
            Assert.Equal("123", link!.ProblemId);
            Assert.Equal(1000, link.TimeLimitMs);
            Assert.Equal(256, link.MemoryLimitMb);

            Assert.True(File.Exists(Path.Combine(directory, "题库", "A1131_1.in")));
            Assert.True(File.Exists(Path.Combine(directory, "题库", "A1131_1.out")));
            Assert.Contains("# A1131", File.ReadAllText(result.StatementPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Import_PreservesExistingCodeAndTests()
    {
        var directory = CreateTempDirectory();

        try
        {
            var settings = new AscSettings { TaskRootDir = directory };
            var statement = Statement("A1131", "加法", "2 3\n", "5\n");
            var problem = new AscProblemRef { DisplayId = "A1131", ProblemId = "123", Title = "加法" };

            var first = AscProblemImporter.Import(
                statement, problem, AscProblemSource.Bank, null, null, null, settings);

            File.WriteAllText(first.SourcePath, "// my code");
            var custom = new List<TestCase>
            {
                new() { Name = "我的用例", Input = "1 1\n", ExpectedOutput = "2\n" }
            };
            TestCaseStore.Save(first.SourcePath, custom);

            var second = AscProblemImporter.Import(
                statement, problem, AscProblemSource.Bank, null, null, null, settings);

            Assert.False(second.CreatedSource);
            Assert.False(second.CreatedTests);
            Assert.Equal("// my code", File.ReadAllText(second.SourcePath));
            Assert.Single(second.TestCases);
            Assert.Equal("我的用例", second.TestCases[0].Name);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Import_UsesTaskFolderAndSanitizesNames()
    {
        var directory = CreateTempDirectory();

        try
        {
            var settings = new AscSettings { TaskRootDir = directory };
            var statement = Statement("B7", "B", "1\n", "2\n");
            var task = new AscTaskInfo
            {
                Source = AscProblemSource.Work,
                TaskId = "55",
                TaskName = "作业/一"
            };
            var problem = new AscProblemRef { DisplayId = "B7", ProblemId = "8", Title = "B" };

            var result = AscProblemImporter.Import(
                statement,
                problem,
                AscProblemSource.Work,
                task,
                "9",
                "算法班",
                settings);

            Assert.Contains(Path.Combine("作业", "作业_一"), result.SourcePath);
            var link = AscLinkStore.Load(result.SourcePath);
            Assert.NotNull(link);
            Assert.Equal("55", link!.TaskId);
            Assert.Equal("作业/一", link.TaskName);
            Assert.Equal("9", link.ClassId);
            Assert.Equal(AscProblemSource.Work, link.Source);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SanitizeName_ReplacesForbiddenCharactersAndFallsBack()
    {
        Assert.Equal("A_B_C", AscProblemImporter.SanitizeName("A/B:C", "x"));
        Assert.Equal("fallback", AscProblemImporter.SanitizeName("...", "fallback"));
        Assert.Equal("fallback", AscProblemImporter.SanitizeName("   ", "fallback"));
    }

    private static AscStatement Statement(string displayId, string title, string input, string output)
    {
        return new AscStatement
        {
            DisplayId = displayId,
            ProblemId = "123",
            Title = title,
            RawMarkdown = $"# {displayId} {title}\n",
            TimeLimitMs = 1000,
            MemoryLimitMb = 256,
            Samples =
            {
                new AscSample { Input = input, Output = output }
            }
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
