using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using RedPanda.Competition.Views;

namespace RedPanda.Competition.Services;

/// <summary>
/// Writes unexpected failures to a daily log next to the portable data (or in
/// %LOCALAPPDATA%). A classroom machine often has no debugger, so the log is
/// the only way to find out what happened after the fact.
/// </summary>
public static class CrashLogger
{
    private static readonly object Gate = new();
    private static bool _uiErrorShown;

    public static string LogDirectory => Path.Combine(AppPaths.LocalDataDirectory, "logs");

    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write("AppDomain", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write("TaskScheduler", e.Exception);
            e.SetObserved();
        };

        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            var path = Write("UI", e.Exception);
            e.Handled = true;

            if (_uiErrorShown)
            {
                return;
            }

            _uiErrorShown = true;
            var owner = (Application.Current?.ApplicationLifetime
                as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

            if (owner is not null)
            {
                _ = Dialogs.ShowMessageAsync(
                    owner,
                    "程序遇到错误",
                    "界面线程出现异常，程序会尽量继续运行。\n\n" +
                    "错误已记录到：\n" + (path ?? LogDirectory));
            }
        };
    }

    public static string? Write(string context, Exception? exception, string? directory = null)
    {
        try
        {
            directory ??= LogDirectory;
            Directory.CreateDirectory(directory);

            var now = DateTime.Now;
            var path = Path.Combine(directory, $"crash-{now:yyyyMMdd}.log");
            var builder = new StringBuilder();
            builder.AppendLine(new string('-', 72));
            builder.Append("时间：").AppendLine(now.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.Append("版本：").AppendLine(AppInfo.Version);
            builder.Append("环境：").AppendLine(Environment.OSVersion.ToString());
            builder.Append("位置：").AppendLine(context);

            if (exception is null)
            {
                builder.AppendLine("异常：<none>");
            }
            else
            {
                builder.AppendLine(exception.ToString());
            }

            lock (Gate)
            {
                File.AppendAllText(path, builder.ToString(), new UTF8Encoding(false));
            }

            return path;
        }
        catch
        {
            return null;
        }
    }

    public static string? WriteMessage(string context, string message, string? directory = null)
    {
        return Write(context, new Exception(message), directory);
    }
}
