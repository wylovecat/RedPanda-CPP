using RedPanda.Competition.Services;

namespace RedPanda.Competition.Tests;

public sealed class CrashLoggerTests
{
    [Fact]
    public void Write_CreatesDailyLogWithContextAndException()
    {
        var directory = CreateTempDirectory();

        try
        {
            var path = CrashLogger.Write(
                "unit-test",
                new InvalidOperationException("boom"),
                directory);

            Assert.NotNull(path);
            Assert.True(File.Exists(path));
            var text = File.ReadAllText(path);
            Assert.Contains("unit-test", text);
            Assert.Contains("InvalidOperationException", text);
            Assert.Contains("boom", text);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Write_AppendsToTheSameDailyFile()
    {
        var directory = CreateTempDirectory();

        try
        {
            var first = CrashLogger.Write("one", new Exception("first"), directory);
            var second = CrashLogger.Write("two", new Exception("second"), directory);

            Assert.Equal(first, second);
            var text = File.ReadAllText(first!);
            Assert.Contains("first", text);
            Assert.Contains("second", text);
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
