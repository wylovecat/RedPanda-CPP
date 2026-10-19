using System.IO.Compression;
using RedPanda.Competition.Models;
using RedPanda.Competition.Services;

namespace RedPanda.Competition.Tests;

public sealed class DiagnosticsServiceTests
{
    [Fact]
    public void BuildReport_ContainsVersionCompilerAndLimits()
    {
        var settings = new AppSettings
        {
            CppStandard = "c++20",
            TimeLimitMs = 1500,
            MemoryLimitMb = 512
        };

        var report = DiagnosticsService.BuildReport(settings, @"C:\mingw64\bin\g++.exe");

        Assert.Contains(AppInfo.Version, report);
        Assert.Contains(@"C:\mingw64\bin\g++.exe", report);
        Assert.Contains("c++20", report);
        Assert.Contains("1500 ms / 512 MB", report);
    }

    [Fact]
    public void Export_WritesReportSettingsAndLogsWithoutTokens()
    {
        var directory = CreateTempDirectory();
        var logs = Path.Combine(directory, "logs");
        Directory.CreateDirectory(logs);
        File.WriteAllText(Path.Combine(logs, "crash-20260101.log"), "boom");
        var target = Path.Combine(directory, "diagnostics.zip");

        try
        {
            DiagnosticsService.Export(target, new AppSettings(), logs);
            Assert.True(File.Exists(target));

            using var archive = ZipFile.OpenRead(target);
            Assert.NotNull(archive.GetEntry("report.txt"));
            Assert.NotNull(archive.GetEntry("settings.json"));
            Assert.NotNull(archive.GetEntry("logs/crash-20260101.log"));
            Assert.Null(archive.GetEntry("aisichuang.json"));

            using var reader = new StreamReader(archive.GetEntry("report.txt")!.Open());
            var report = reader.ReadToEnd();
            Assert.Contains("RedPanda CP 诊断报告", report);
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
