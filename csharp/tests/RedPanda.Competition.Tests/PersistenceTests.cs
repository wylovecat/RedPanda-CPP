using System.Text;
using RedPanda.Competition.Models;
using RedPanda.Competition.Services;

namespace RedPanda.Competition.Tests;

public sealed class PersistenceTests
{
    [Fact]
    public void TestCaseStore_RoundTripsChineseContentAndSidecar()
    {
        var directory = CreateTempDirectory();
        try
        {
            var sourcePath = Path.Combine(directory, "A+B 题.cpp");
            var cases = new List<TestCase>
            {
                new()
                {
                    Name = "样例 1",
                    Input = "2 3\n",
                    ExpectedOutput = "5\n"
                },
                new()
                {
                    Name = "边界",
                    Input = "1000000000 1000000000\n",
                    ExpectedOutput = "2000000000\n",
                    IsChecked = false
                }
            };

            TestCaseStore.Save(sourcePath, cases);

            Assert.True(File.Exists(TestCaseStore.GetSidecarPath(sourcePath)));

            var loaded = TestCaseStore.Load(sourcePath, out var error);

            Assert.Null(error);
            Assert.Equal(2, loaded.Count);
            Assert.Equal("样例 1", loaded[0].Name);
            Assert.Equal("2 3\n", loaded[0].Input);
            Assert.Equal("2000000000\n", loaded[1].ExpectedOutput);
            Assert.True(loaded[0].IsChecked);
            Assert.False(loaded[1].IsChecked);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TestCaseStore_MissingEnabledDefaultsToChecked()
    {
        var directory = CreateTempDirectory();
        try
        {
            var sourcePath = Path.Combine(directory, "legacy.cpp");
            File.WriteAllText(
                TestCaseStore.GetSidecarPath(sourcePath),
                """
                {
                  "Version": 1,
                  "Cases": [
                    { "Name": "样例 1", "Input": "1\n", "ExpectedOutput": "1\n" }
                  ]
                }
                """);

            var loaded = TestCaseStore.Load(sourcePath, out var error);

            Assert.Null(error);
            Assert.Single(loaded);
            Assert.True(loaded[0].IsChecked);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TestCaseStore_ReturnsErrorForBrokenJson()
    {
        var directory = CreateTempDirectory();
        try
        {
            var sourcePath = Path.Combine(directory, "broken.cpp");
            File.WriteAllText(TestCaseStore.GetSidecarPath(sourcePath), "{ not json");

            var loaded = TestCaseStore.Load(sourcePath, out var error);

            Assert.Empty(loaded);
            Assert.NotNull(error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FileEncoding_ReadsUtf8AndWritesWithoutBom()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "utf8.cpp");
            FileEncodingService.Write(path, "// 中文注释\nint main() {}\n");

            var bytes = File.ReadAllBytes(path);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);

            var (text, encodingName) = FileEncodingService.Read(path);
            Assert.Equal("// 中文注释\nint main() {}\n", text);
            Assert.Equal("UTF-8", encodingName);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FileEncoding_ReadsUtf8BomAndGb18030()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var directory = CreateTempDirectory();
        try
        {
            var bomPath = Path.Combine(directory, "bom.cpp");
            File.WriteAllText(bomPath, "// 字节顺序标记", new UTF8Encoding(true));

            var (bomText, bomEncoding) = FileEncodingService.Read(bomPath);
            Assert.Equal("// 字节顺序标记", bomText);
            Assert.Equal("UTF-8 BOM", bomEncoding);

            var gbkPath = Path.Combine(directory, "gbk.cpp");
            File.WriteAllBytes(gbkPath, Encoding.GetEncoding("GB18030").GetBytes("// 旧编码"));

            var (gbkText, gbkEncoding) = FileEncodingService.Read(gbkPath);
            Assert.Equal("// 旧编码", gbkText);
            Assert.Equal("GB18030", gbkEncoding);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TestCase_PerformanceTextReflectsRunData()
    {
        var testCase = new TestCase
        {
            HasRun = true,
            ElapsedMs = 125,
            PeakMemoryMb = 3.5
        };

        Assert.Equal("125 ms · 3.5 MB", testCase.PerformanceText);
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
