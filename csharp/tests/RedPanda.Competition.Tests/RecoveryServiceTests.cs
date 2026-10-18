using RedPanda.Competition.Models;
using RedPanda.Competition.Services;

namespace RedPanda.Competition.Tests;

public sealed class RecoveryServiceTests
{
    [Fact]
    public void SaveLoadDelete_RoundTripsChineseDraft()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "draft.json");

        try
        {
            var state = new DraftState
            {
                SourcePath = @"C:\题目\A+B.cpp",
                EncodingName = "UTF-8",
                Text = "// 中文草稿\nint main() {}\n",
                Tests = new List<TestCaseFile>
                {
                    new()
                    {
                        Name = "边界",
                        Input = "1 2\n",
                        ExpectedOutput = "3\n",
                        Enabled = false
                    }
                }
            };

            RecoveryService.Save(state, path);
            Assert.True(RecoveryService.Exists(path));

            var loaded = RecoveryService.Load(out var error, path);

            Assert.Null(error);
            Assert.NotNull(loaded);
            Assert.Equal(state.Text, loaded!.Text);
            Assert.Equal(state.SourcePath, loaded.SourcePath);
            Assert.Single(loaded.Tests);
            Assert.False(loaded.Tests[0].Enabled);

            RecoveryService.Delete(path);
            Assert.False(RecoveryService.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Load_ReturnsErrorForBrokenJson()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "broken.json");

        try
        {
            File.WriteAllText(path, "{ not json");

            var loaded = RecoveryService.Load(out var error, path);

            Assert.Null(loaded);
            Assert.NotNull(error);
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
