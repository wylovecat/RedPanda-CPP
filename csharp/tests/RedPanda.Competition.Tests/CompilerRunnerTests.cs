using System.Diagnostics;
using System.Text;
using RedPanda.Competition.Models;
using RedPanda.Competition.Services;

namespace RedPanda.Competition.Tests;

public sealed class CompilerRunnerTests
{
    private static readonly AppSettings Settings = new()
    {
        CppStandard = "c++17",
        Optimization = "-O2",
        TimeLimitMs = 2000,
        MemoryLimitMb = 256,
        OutputLimitKb = 1024
    };

    [Fact]
    public async Task CompileAndRun_ReadsStandardInputAndWritesOutput()
    {
        var directory = CreateTempDirectory();
        try
        {
            var executable = await CompileAsync(
                directory,
                """
                #include <bits/stdc++.h>
                using namespace std;
                int main() {
                    long long a, b;
                    cin >> a >> b;
                    cout << a + b << "\n";
                    return 0;
                }
                """);

            var result = await ProcessRunner.RunAsync(
                executable,
                directory,
                "2 3\n",
                Settings.TimeLimitMs,
                Settings.MemoryLimitMb,
                Settings.OutputLimitKb * 1024);

            Assert.Equal(RunOutcome.Normal, result.Outcome);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal("5", OutputComparer.Normalize(result.StandardOutput));
            Assert.True(result.ElapsedMs >= 0);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CompileAndRun_WorksWithChinesePath()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "RedPandaCPTests",
            "算法竞赛 " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var executable = await CompileAsync(
                directory,
                """
                #include <iostream>
                int main() {
                    std::cout << "中文输出 OK\n";
                    return 0;
                }
                """,
                "A+B 题.cpp");

            var result = await ProcessRunner.RunAsync(
                executable,
                directory,
                string.Empty,
                2000,
                256,
                1024 * 1024);

            Assert.Equal(RunOutcome.Normal, result.Outcome);
            Assert.Contains("中文输出 OK", result.StandardOutput);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Run_KillsProgramAfterTimeLimit()
    {
        var directory = CreateTempDirectory();
        try
        {
            var executable = await CompileAsync(
                directory,
                """
                int main() {
                    while (true) { }
                }
                """);

            var stopwatch = Stopwatch.StartNew();
            var result = await ProcessRunner.RunAsync(
                executable,
                directory,
                string.Empty,
                300,
                256,
                1024 * 1024);
            stopwatch.Stop();

            Assert.Equal(RunOutcome.Timeout, result.Outcome);
            Assert.True(stopwatch.ElapsedMilliseconds < 5000, "超时后进程没有被及时终止");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Run_StopsInfiniteOutputAtLimit()
    {
        var directory = CreateTempDirectory();
        try
        {
            var executable = await CompileAsync(
                directory,
                """
                #include <iostream>
                int main() {
                    for (;;) {
                        std::cout << "abcdefghij";
                    }
                }
                """);

            var outputLimit = 16 * 1024;
            var result = await ProcessRunner.RunAsync(
                executable,
                directory,
                string.Empty,
                5000,
                256,
                outputLimit);

            Assert.Equal(RunOutcome.OutputLimit, result.Outcome);
            Assert.True(result.StandardOutput.Length <= outputLimit + 64);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Compile_ReportsDiagnosticsForBrokenSource()
    {
        var directory = CreateTempDirectory();
        try
        {
            var sourcePath = Path.Combine(directory, "broken.cpp");
            await File.WriteAllTextAsync(sourcePath, "int main() { this is not c++ }", new UTF8Encoding(false));

            var result = await new CompilerService().CompileAsync(sourcePath, Settings);

            Assert.False(result.Success);
            Assert.False(string.IsNullOrWhiteSpace(result.Output));
            Assert.Contains("error", result.Output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<string> CompileAsync(
        string directory,
        string source,
        string fileName = "main.cpp")
    {
        var sourcePath = Path.Combine(directory, fileName);
        await File.WriteAllTextAsync(sourcePath, source, new UTF8Encoding(false));

        var result = await new CompilerService().CompileAsync(sourcePath, Settings);

        Assert.True(
            result.Success,
            "编译失败：\n" + result.Output + "\n" + result.ErrorMessage);
        Assert.NotNull(result.ExecutablePath);
        Assert.True(File.Exists(result.ExecutablePath));

        return result.ExecutablePath!;
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
