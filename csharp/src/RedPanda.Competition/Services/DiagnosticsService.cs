using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using RedPanda.Competition.Models;

namespace RedPanda.Competition.Services;

public static class DiagnosticsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string BuildReport(AppSettings settings, string? compilerPath)
    {
        var gpp = compilerPath ?? ToolchainLocator.FindGpp(settings.CompilerPath);
        var builder = new StringBuilder();
        builder.AppendLine("RedPanda CP 诊断报告");
        builder.AppendLine("版本：" + AppInfo.Version);
        builder.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        builder.AppendLine("系统：" + Environment.OSVersion);
        builder.AppendLine("64 位进程：" + Environment.Is64BitProcess);
        builder.AppendLine("便携模式：" + (AppPaths.IsPortable ? "是" : "否"));
        builder.AppendLine("程序目录：" + AppContext.BaseDirectory);
        builder.AppendLine("数据目录：" + AppPaths.AppDataDirectory);
        builder.AppendLine("日志目录：" + CrashLogger.LogDirectory);
        builder.AppendLine();
        builder.AppendLine("编译器：" + (gpp ?? "未找到"));
        if (gpp is not null)
        {
            foreach (var line in ReadCompilerVersion(gpp))
            {
                builder.AppendLine("  " + line);
            }
        }

        builder.AppendLine("C++ 标准：" + settings.CppStandard);
        builder.AppendLine("优化：" + settings.Optimization);
        builder.AppendLine("额外参数：" + settings.ExtraCompilerArgs);
        builder.AppendLine($"本地限制：{settings.TimeLimitMs} ms / {settings.MemoryLimitMb} MB");
        builder.AppendLine($"输出上限：{settings.OutputLimitKb} KB，显示 {settings.OutputDisplayLimitKb} KB");
        builder.AppendLine("主题：" + settings.Theme);
        return builder.ToString().TrimEnd();
    }

    public static void Export(
        string targetPath,
        AppSettings settings,
        string? logDirectory = null)
    {
        logDirectory ??= CrashLogger.LogDirectory;

        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }

        using var archive = ZipFile.Open(targetPath, ZipArchiveMode.Create);
        WriteText(
            archive,
            "report.txt",
            BuildReport(settings, ToolchainLocator.FindGpp(settings.CompilerPath)));
        WriteText(archive, "settings.json", JsonSerializer.Serialize(settings, JsonOptions));

        if (Directory.Exists(logDirectory))
        {
            foreach (var log in Directory.EnumerateFiles(logDirectory, "*.log"))
            {
                var entry = archive.CreateEntry(
                    "logs/" + Path.GetFileName(log),
                    CompressionLevel.Optimal);
                using var input = File.OpenRead(log);
                using var output = entry.Open();
                input.CopyTo(output);
            }
        }
    }

    private static void WriteText(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(
            entry.Open(),
            new UTF8Encoding(true));
        writer.Write(content);
    }

    private static IEnumerable<string> ReadCompilerVersion(string gpp)
    {
        try
        {
            var startInfo = new ProcessStartInfo(gpp, "--version")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return Array.Empty<string>();
            }

            var lines = new List<string>();
            var first = process.StandardOutput.ReadLine();
            if (!string.IsNullOrWhiteSpace(first))
            {
                lines.Add(first.Trim());
            }

            process.WaitForExit(2000);
            return lines;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
