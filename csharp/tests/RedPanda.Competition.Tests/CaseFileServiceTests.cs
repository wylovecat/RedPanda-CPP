using RedPanda.Competition.Models;
using RedPanda.Competition.Services;

namespace RedPanda.Competition.Tests;

public sealed class CaseFileServiceTests
{
    [Fact]
    public void LoadFromFolder_PairsInWithOutOrAns()
    {
        var directory = CreateTempDirectory();
        try
        {
            FileEncodingService.Write(Path.Combine(directory, "sample1.in"), "2 3\n");
            FileEncodingService.Write(Path.Combine(directory, "sample1.out"), "5\n");
            FileEncodingService.Write(Path.Combine(directory, "sample2.in"), "10 20\n");
            FileEncodingService.Write(Path.Combine(directory, "sample2.ans"), "30\n");
            FileEncodingService.Write(Path.Combine(directory, "sample3.in"), "1 1\n");

            var (cases, warnings) = CaseFileService.LoadFromFolder(directory);

            Assert.Equal(3, cases.Count);
            Assert.Equal("2 3\n", cases[0].Input);
            Assert.Equal("5\n", cases[0].ExpectedOutput);
            Assert.Equal("30\n", cases[1].ExpectedOutput);
            Assert.Equal(string.Empty, cases[2].ExpectedOutput);
            Assert.Contains(warnings, warning => warning.Contains("sample3.in"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LoadFromFiles_DeduplicatesAndFindsSiblingInput()
    {
        var directory = CreateTempDirectory();
        try
        {
            var input1 = Path.Combine(directory, "a.in");
            var output1 = Path.Combine(directory, "a.out");
            var input2 = Path.Combine(directory, "b.in");
            FileEncodingService.Write(input1, "a\n");
            FileEncodingService.Write(output1, "A\n");
            FileEncodingService.Write(input2, "b\n");
            FileEncodingService.Write(Path.Combine(directory, "b.out"), "B\n");

            var (cases, warnings) = CaseFileService.LoadFromFiles(new[] { output1, input2, input1 });

            Assert.Empty(warnings);
            Assert.Equal(2, cases.Count);
            Assert.Contains(cases, item => item.Input == "a\n" && item.ExpectedOutput == "A\n");
            Assert.Contains(cases, item => item.Input == "b\n" && item.ExpectedOutput == "B\n");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Export_WritesUtf8PairsAndSkipsEmptyCases()
    {
        var directory = CreateTempDirectory();
        try
        {
            var cases = new List<TestCase>
            {
                new() { Input = "2 3\n", ExpectedOutput = "5\n" },
                new() { Input = string.Empty, ExpectedOutput = string.Empty },
                new() { Input = "中文输入\n", ExpectedOutput = "中文输出\n" }
            };

            var count = CaseFileService.Export(directory, "A", cases);

            Assert.Equal(2, count);
            Assert.True(File.Exists(Path.Combine(directory, "A_1.in")));
            Assert.True(File.Exists(Path.Combine(directory, "A_1.out")));
            Assert.True(File.Exists(Path.Combine(directory, "A_2.in")));
            Assert.False(File.Exists(Path.Combine(directory, "A_3.in")));

            var (imported, _) = CaseFileService.LoadFromFolder(directory);
            Assert.Equal(2, imported.Count);
            Assert.Contains(imported, item => item.Input == "中文输入\n" && item.ExpectedOutput == "中文输出\n");
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
