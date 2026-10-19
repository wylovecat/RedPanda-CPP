using System.Text;
using RedPanda.Competition.Models;

namespace RedPanda.Competition.Services;

public sealed record StressResult(
    bool Success,
    string Message,
    int Round,
    string? InputPath = null,
    string? ExpectedPath = null,
    string? ActualPath = null);

public delegate Task<RunResult> StressProcessRunner(
    string executablePath,
    string workingDirectory,
    string standardInput,
    int timeLimitMs,
    int outputLimitBytes,
    CancellationToken cancellationToken);

public static class StressTester
{
    public static async Task<StressResult> RunAsync(
        string solutionSourcePath,
        string solutionExecutable,
        string generatorExecutable,
        string bruteExecutable,
        string workingDirectory,
        int rounds,
        AppSettings settings,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default,
        StressProcessRunner? runner = null)
    {
        runner ??= (exe, directory, input, timeLimitMs, outputLimitBytes, token) =>
            ProcessRunner.RunAsync(
                exe,
                directory,
                input,
                timeLimitMs,
                settings.MemoryLimitMb,
                outputLimitBytes,
                token);

        rounds = Math.Clamp(rounds, 1, 10_000);
        var outputLimitBytes = Math.Max(1024, settings.OutputLimitKb * 1024);

        for (var round = 1; round <= rounds; round++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"第 {round}/{rounds} 轮：生成数据…");

            var input = await runner(
                generatorExecutable,
                workingDirectory,
                string.Empty,
                Math.Max(5000, settings.TimeLimitMs * 5),
                outputLimitBytes,
                cancellationToken);

            if (input.Outcome != RunOutcome.Normal)
            {
                return new StressResult(
                    false,
                    $"生成器失败（{input.Outcome}）：{input.ErrorMessage}".Trim(),
                    round);
            }

            progress?.Report($"第 {round}/{rounds} 轮：运行暴力程序…");
            var expected = await runner(
                bruteExecutable,
                workingDirectory,
                input.StandardOutput,
                settings.TimeLimitMs,
                outputLimitBytes,
                cancellationToken);

            if (expected.Outcome != RunOutcome.Normal)
            {
                return new StressResult(
                    false,
                    $"暴力程序失败（{expected.Outcome}）：{expected.ErrorMessage}".Trim(),
                    round);
            }

            progress?.Report($"第 {round}/{rounds} 轮：运行当前程序…");
            var actual = await runner(
                solutionExecutable,
                workingDirectory,
                input.StandardOutput,
                settings.TimeLimitMs,
                outputLimitBytes,
                cancellationToken);

            if (actual.Outcome != RunOutcome.Normal)
            {
                var paths = SaveFailure(
                    solutionSourcePath,
                    input.StandardOutput,
                    expected.StandardOutput,
                    actual.StandardOutput);
                return new StressResult(
                    false,
                    $"当前程序失败（{actual.Outcome}）：{actual.ErrorMessage}".Trim(),
                    round,
                    paths.Input,
                    paths.Expected,
                    paths.Actual);
            }

            if (!OutputComparer.AreEqual(expected.StandardOutput, actual.StandardOutput))
            {
                var paths = SaveFailure(
                    solutionSourcePath,
                    input.StandardOutput,
                    expected.StandardOutput,
                    actual.StandardOutput);
                return new StressResult(
                    false,
                    "输出不一致，已保存反例数据。",
                    round,
                    paths.Input,
                    paths.Expected,
                    paths.Actual);
            }

            progress?.Report($"第 {round}/{rounds} 轮：通过");
        }

        return new StressResult(true, $"完成 {rounds} 轮对拍，未发现差异。", rounds);
    }

    private static (string Input, string Expected, string Actual) SaveFailure(
        string solutionSourcePath,
        string input,
        string expected,
        string actual)
    {
        var directory = Path.GetDirectoryName(solutionSourcePath) ?? Environment.CurrentDirectory;
        var stem = Path.GetFileNameWithoutExtension(solutionSourcePath);
        var inputPath = Path.Combine(directory, stem + ".stress.in");
        var expectedPath = Path.Combine(directory, stem + ".stress.ans");
        var actualPath = Path.Combine(directory, stem + ".stress.out");

        File.WriteAllText(inputPath, input, new UTF8Encoding(false));
        File.WriteAllText(expectedPath, expected, new UTF8Encoding(false));
        File.WriteAllText(actualPath, actual, new UTF8Encoding(false));
        return (inputPath, expectedPath, actualPath);
    }
}
