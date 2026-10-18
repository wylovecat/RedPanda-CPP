using System.Diagnostics;
using System.Text;
using RedPanda.Competition.Models;

namespace RedPanda.Competition.Services;

public static class ProcessRunner
{
    public static async Task<RunResult> RunAsync(
        string executablePath,
        string workingDirectory,
        string standardInput,
        int timeLimitMs,
        int memoryLimitMb,
        int outputLimitBytes,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(executablePath))
        {
            return StartError("找不到可执行文件：" + executablePath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };

        var executableDirectory = Path.GetDirectoryName(executablePath);
        if (!string.IsNullOrEmpty(executableDirectory))
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            startInfo.Environment["PATH"] = executableDirectory + ";" + path;
        }

        using var process = new Process { StartInfo = startInfo };
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (!process.Start())
            {
                return StartError("无法启动程序。");
            }
        }
        catch (Exception ex)
        {
            return StartError("无法启动程序：" + ex.Message);
        }

        using var job = new JobObject(memoryLimitMb > 0 ? memoryLimitMb * 1024L * 1024L : 0);
        job.Assign(process);

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();
        var outputLimitHit = 0;
        var cancelledFlag = 0;

        void OnOutputLimit()
        {
            if (Interlocked.Exchange(ref outputLimitHit, 1) == 0)
            {
                Kill(process);
            }
        }

        var stdoutTask = ReadLimitedAsync(
            process.StandardOutput,
            stdoutBuilder,
            Math.Max(1024, outputLimitBytes),
            OnOutputLimit);

        var stderrTask = ReadLimitedAsync(
            process.StandardError,
            stderrBuilder,
            Math.Max(1024, outputLimitBytes),
            OnOutputLimit);

        _ = WriteInputAsync(process, standardInput);

        using var delaySource = new CancellationTokenSource();
        using var registration = cancellationToken.Register(() =>
        {
            Interlocked.Exchange(ref cancelledFlag, 1);
            Kill(process);
        });

        var exitTask = process.WaitForExitAsync();
        var delayTask = Task.Delay(Math.Max(1, timeLimitMs), delaySource.Token);
        var completed = await Task.WhenAny(exitTask, delayTask);
        delaySource.Cancel();

        var timedOut = false;
        if (completed != exitTask)
        {
            timedOut = cancellationToken.IsCancellationRequested is false;
            Kill(process);
            await Task.WhenAny(exitTask, Task.Delay(2000));
        }

        var bothReadTasks = Task.WhenAll(stdoutTask, stderrTask);
        await Task.WhenAny(bothReadTasks, Task.Delay(2000));
        if (!bothReadTasks.IsCompleted)
        {
            Kill(process);
            await Task.WhenAny(bothReadTasks, Task.Delay(1000));
        }

        stopwatch.Stop();

        var exitCode = -1;
        try
        {
            if (process.HasExited)
            {
                exitCode = process.ExitCode;
            }
        }
        catch
        {
            // Keep the sentinel value.
        }

        var peakBytes = job.PeakProcessMemoryBytes;
        if (peakBytes <= 0)
        {
            try
            {
                peakBytes = process.PeakWorkingSet64;
            }
            catch
            {
                // Peak memory is advisory when the job object is unavailable.
            }
        }

        var cancelled = Interlocked.CompareExchange(ref cancelledFlag, 0, 0) == 1;
        var truncated = Interlocked.CompareExchange(ref outputLimitHit, 0, 0) == 1;

        RunOutcome outcome;
        if (cancelled)
        {
            outcome = RunOutcome.Cancelled;
        }
        else if (truncated)
        {
            outcome = RunOutcome.OutputLimit;
        }
        else if (timedOut)
        {
            outcome = RunOutcome.Timeout;
        }
        else if (exitCode != 0)
        {
            outcome = RunOutcome.RuntimeError;
        }
        else
        {
            outcome = RunOutcome.Normal;
        }

        string stdout;
        string stderr;
        lock (stdoutBuilder)
        {
            stdout = stdoutBuilder.ToString();
        }

        lock (stderrBuilder)
        {
            stderr = stderrBuilder.ToString();
        }

        return new RunResult(
            outcome,
            exitCode,
            stdout,
            stderr,
            stopwatch.ElapsedMilliseconds,
            Math.Round(peakBytes / 1024.0 / 1024.0, 1));
    }

    private static async Task ReadLimitedAsync(
        StreamReader reader,
        StringBuilder builder,
        int limit,
        Action onLimit)
    {
        var buffer = new char[4096];
        var limitSignalled = false;

        try
        {
            while (true)
            {
                var read = await reader.ReadAsync(buffer.AsMemory());
                if (read == 0)
                {
                    break;
                }

                lock (builder)
                {
                    var remaining = limit - builder.Length;
                    if (remaining > 0)
                    {
                        builder.Append(buffer, 0, Math.Min(read, remaining));
                    }
                }

                if (!limitSignalled && builder.Length >= limit)
                {
                    limitSignalled = true;
                    onLimit();
                    break;
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // The process was killed and the pipe was closed.
        }
        catch (IOException)
        {
            // Same as above.
        }
    }

    private static async Task WriteInputAsync(Process process, string input)
    {
        try
        {
            if (input.Length > 0)
            {
                await process.StandardInput.WriteAsync(input);
                await process.StandardInput.FlushAsync();
            }

            process.StandardInput.Close();
        }
        catch
        {
            // Programs that exit without reading stdin are perfectly normal.
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

    private static RunResult StartError(string message)
    {
        return new RunResult(RunOutcome.StartError, -1, string.Empty, string.Empty, 0, 0, message);
    }
}
