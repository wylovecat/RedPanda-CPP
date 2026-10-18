using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using RedPanda.Competition.Models;

namespace RedPanda.Competition.Services;

/// <summary>
/// Persists the aisichuang session and preferences.
///
/// Only the token bundle is persisted, never the password. The site exchanges
/// a plaintext phone + password for a Sa-Token, so keeping the password would
/// add risk without any benefit.
/// </summary>
public static class AscSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string DefaultPath => Path.Combine(AppPaths.AppDataDirectory, "aisichuang.json");

    public static AscSettings Load(string? path = null)
    {
        path ??= DefaultPath;

        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<AscSettings>(json, JsonOptions);
                if (settings is not null)
                {
                    return Sanitize(settings);
                }
            }
        }
        catch
        {
            // A broken settings file must not prevent the IDE from starting.
        }

        return Sanitize(new AscSettings());
    }

    public static void Save(AscSettings settings, string? path = null)
    {
        path ??= DefaultPath;
        settings = Sanitize(settings);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
        File.Move(temporaryPath, path, overwrite: true);
    }

    public static AscSettings Sanitize(AscSettings settings)
    {
        settings.BaseUrl = (settings.BaseUrl ?? string.Empty).Trim();
        while (settings.BaseUrl.EndsWith('/'))
        {
            settings.BaseUrl = settings.BaseUrl[..^1];
        }

        if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            settings.BaseUrl = AscSettings.DefaultBaseUrl;
        }

        settings.TaskRootDir = (settings.TaskRootDir ?? string.Empty).Trim();
        settings.SubmitLanguage = string.IsNullOrWhiteSpace(settings.SubmitLanguage)
            ? "C++"
            : settings.SubmitLanguage.Trim();
        settings.LastPhone = settings.LastPhone?.Trim() ?? string.Empty;
        settings.LastClassId = settings.LastClassId ?? string.Empty;
        settings.LastClassName = settings.LastClassName ?? string.Empty;
        settings.LastTaskId = settings.LastTaskId ?? string.Empty;
        settings.LastTaskName = settings.LastTaskName ?? string.Empty;
        settings.LastTaskKind = settings.LastTaskKind == 1 ? 1 : 0;

        settings.Session ??= new AscSession();
        settings.Session.Satoken = settings.Session.Satoken ?? string.Empty;
        settings.Session.CsrfToken = settings.Session.CsrfToken ?? string.Empty;
        settings.Session.SessionId = settings.Session.SessionId ?? string.Empty;
        settings.Session.UserId = settings.Session.UserId ?? string.Empty;
        settings.Session.Role = string.IsNullOrWhiteSpace(settings.Session.Role)
            ? "student"
            : settings.Session.Role;
        settings.Session.BaseUrl = string.IsNullOrWhiteSpace(settings.Session.BaseUrl)
            ? settings.BaseUrl
            : settings.Session.BaseUrl;
        settings.Session.Phone = string.IsNullOrWhiteSpace(settings.Session.Phone)
            ? settings.LastPhone
            : settings.Session.Phone;

        return settings;
    }
}
