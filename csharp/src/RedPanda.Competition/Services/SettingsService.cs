using System.Text.Encodings.Web;
using System.Text.Json;
using RedPanda.Competition.Models;

namespace RedPanda.Competition.Services;

public static class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var json = File.ReadAllText(AppPaths.SettingsFile);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings is not null)
                {
                    return Sanitize(settings);
                }
            }
        }
        catch
        {
            // A broken settings file must never prevent the IDE from starting.
        }

        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.AppDataDirectory);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(AppPaths.SettingsFile, json);
        }
        catch
        {
            // Settings are best-effort; a read-only profile should not break a run.
        }
    }

    private static AppSettings Sanitize(AppSettings settings)
    {
        settings.TimeLimitMs = Math.Clamp(settings.TimeLimitMs, 100, 60_000);
        settings.MemoryLimitMb = Math.Clamp(settings.MemoryLimitMb, 16, 8192);
        settings.OutputLimitKb = Math.Clamp(settings.OutputLimitKb, 16, 16_384);
        settings.OutputDisplayLimitKb = Math.Clamp(settings.OutputDisplayLimitKb, 4, 1024);
        settings.EditorFontSize = Math.Clamp(settings.EditorFontSize, 9, 40);

        if (settings.Theme is not ("Light" or "Dark"))
        {
            settings.Theme = "Light";
        }

        if (string.IsNullOrWhiteSpace(settings.CppStandard))
        {
            settings.CppStandard = "c++17";
        }

        if (string.IsNullOrWhiteSpace(settings.Optimization))
        {
            settings.Optimization = "-O2";
        }

        settings.RecentFiles = (settings.RecentFiles ?? new List<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();

        return settings;
    }
}
