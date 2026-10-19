using RedPanda.Competition.Models;
using RedPanda.Competition.Services;

namespace RedPanda.Competition.Tests;

public sealed class StressTesterTests
{
    [Fact]
    public async Task RunAsync_CompletesWhenOutputsMatch()
    {
        var directory = CreateTempDirectory();
        var source = Path.Combine(directory, "A1.cpp");
        File.WriteAllText(source, "int main(){}");

        try
        {
            var runner = FakeRunner((exe, input) => exe switch
            {
                "gen" => "1 2\n",
                "brute" => "3\n",
                _ => "3\n"
            });

            var result = await StressTester.RunAsync(
                source, "sol", "gen", "brute", directory, 3,
                new AppSettings(), runner: runner);

            Assert.True(result.Success);
            Assert.Equal(3, result.Round);
            Assert.Null(result.InputPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_SavesFirstMismatch()
    {
        var directory = CreateTempDirectory();
        var source = Path.Combine(directory, "A1.cpp");
        File.WriteAllText(source, "int main(){}");

        try
        {
            var round = 0;
            var runner = FakeRunner((exe, input) =>
            {
                if (exe == "gen")
                {
                    round++;
                    return round + " 2\n";
                }

                if (exe == "brute")
                {
                    return "3\n";
                }

                return round == 2 ? "4\n" : "3\n";
            });

            var result = await StressTester.RunAsync(
                source, "sol", "gen", "brute", directory, 5,
                new AppSettings(), runner: runner);

            Assert.False(result.Success);
            Assert.Equal(2, result.Round);
            Assert.NotNull(result.InputPath);
            Assert.True(File.Exists(result.InputPath));
            Assert.Equal("2 2\n", File.ReadAllText(result.InputPath));
            Assert.Equal("3\n", File.ReadAllText(result.ExpectedPath!));
            Assert.Equal("4\n", File.ReadAllText(result.ActualPath!));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static StressProcessRunner FakeRunner(Func<string, string, string> output)
    {
        return (exe, directory, input, timeLimit, outputLimit, token) =>
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new RunResult(
                RunOutcome.Normal,
                0,
                output(exe, input),
                string.Empty,
                10,
                2,
                1.0));
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
