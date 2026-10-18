using System.Diagnostics;
using System.Text;
using RedPanda.Competition.Models;

namespace RedPanda.Competition.Services;

public sealed class CompilerService
{
    private static readonly TimeSpan CompileTimeout = TimeSpan.FromSeconds(60);

    public async Task<CompileResult> CompileAsync(
        string sourcePath,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        if (!File.Exists(sourcePath))
        {
            return Failure(stopwatch, string.Empty, $"源文件不存在：{sourcePath}");
        }

        var gpp = ToolchainLocator.FindGpp(settings.CompilerPath);
        if (gpp is null)
        {
            return Failure(
                stopwatch,
                string.Empty,
                "未找到 g++.exe。请在“设置”中指定编译器路径，或把 MinGW 的 bin 目录加入 PATH。");
        }

        string executablePath;
        try
        {
            var buildDirectory = AppPaths.MakeBuildDirectory(sourcePath);
            executablePath = Path.Combine(buildDirectory, "program.exe");

            if (File.Exists(executablePath))
            {
                File.Delete(executablePath);
            }
        }
        catch (Exception ex)
        {
            return Failure(stopwatch, string.Empty, "无法准备编译目录：" + ex.Message);
        }

        var arguments = BuildArguments(settings, executablePath, sourcePath);
        var commandLine = CommandLineSplitter.Format(gpp, arguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = gpp,
            WorkingDirectory = Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var compilerDirectory = Path.GetDirectoryName(gpp);
        if (!string.IsNullOrEmpty(compilerDirectory))
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            startInfo.Environment["PATH"] = compilerDirectory + ";" + path;
        }

        // Keep diagnostics in English so the captured bytes are predictable
        // regardless of the Windows display language.
        startInfo.Environment["LC_ALL"] = "C";
        startInfo.Environment["LANG"] = "C";

        using var process = new Process { StartInfo = startInfo };

        try
        {
            if (!process.Start())
            {
                return Failure(stopwatch, commandLine, "无法启动 g++。");
            }
        }
        catch (Exception ex)
        {
            return Failure(stopwatch, commandLine, "无法启动 g++：" + ex.Message);
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(CompileTimeout);

        Task<string> stdoutTask;
        Task<string> stderrTask;

        try
        {
            stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
            stderrTask = process.StandardError.ReadToEndAsync(timeoutSource.Token);
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            var message = cancellationToken.IsCancellationRequested
                ? "编译已取消。"
                : $"编译超时（{CompileTimeout.TotalSeconds:F0} 秒）。";
            return Failure(stopwatch, commandLine, message);
        }

        var stdout = await SafeReadAsync(stdoutTask);
        var stderr = await SafeReadAsync(stderrTask);
        var output = (stderr + stdout).Trim();
        stopwatch.Stop();

        var success = process.ExitCode == 0 && File.Exists(executablePath);

        if (!success)
        {
            try
            {
                if (File.Exists(executablePath))
                {
                    File.Delete(executablePath);
                }
            }
            catch
            {
                // The stale binary is harmless: the next compile deletes it again.
            }

            if (output.Length == 0)
            {
                output = $"编译失败（g++ 退出码 {process.ExitCode}）。";
            }

            return new CompileResult(
                false,
                output,
                null,
                stopwatch.ElapsedMilliseconds,
                null,
                commandLine);
        }

        return new CompileResult(
            true,
            output,
            executablePath,
            stopwatch.ElapsedMilliseconds,
            null,
            commandLine);
    }

    private static List<string> BuildArguments(
        AppSettings settings,
        string executablePath,
        string sourcePath)
    {
        var arguments = new List<string>();

        if (!string.IsNullOrWhiteSpace(settings.Optimization))
        {
            arguments.Add(settings.Optimization.Trim());
        }

        arguments.Add("-std=" + settings.CppStandard);
        arguments.Add("-pipe");
        arguments.Add("-Wall");
        arguments.Add("-fdiagnostics-color=never");
        arguments.Add("-finput-charset=UTF-8");
        arguments.Add("-fexec-charset=UTF-8");

        // Algorithm problems often recurse deeply; the default 1 MiB Windows
        // stack is easy to overflow. Reservation is virtual, not committed.
        arguments.Add("-Wl,--stack=268435456");
        arguments.Add("-static-libgcc");
        arguments.Add("-static-libstdc++");

        arguments.AddRange(CommandLineSplitter.Split(settings.ExtraCompilerArgs));

        arguments.Add("-o");
        arguments.Add(executablePath);
        arguments.Add(sourcePath);

        return arguments;
    }

    private static async Task<string> SafeReadAsync(Task<string> task)
    {
        try
        {
            return await task;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort only.
        }
    }

    private static CompileResult Failure(Stopwatch stopwatch, string commandLine, string message)
    {
        stopwatch.Stop();
        return new CompileResult(false, message, null, stopwatch.ElapsedMilliseconds, message, commandLine);
    }
}
