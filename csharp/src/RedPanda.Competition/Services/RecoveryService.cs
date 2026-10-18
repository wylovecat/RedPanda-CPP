using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using RedPanda.Competition.Models;

namespace RedPanda.Competition.Services;

public static class RecoveryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static bool Exists(string? path = null)
    {
        return File.Exists(path ?? AppPaths.RecoveryFile);
    }

    public static void Save(DraftState state, string? path = null)
    {
        path ??= AppPaths.RecoveryFile;
        state.SavedAtUtc = DateTime.UtcNow;

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(state, JsonOptions);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
        File.Move(temporaryPath, path, overwrite: true);
    }

    public static DraftState? Load(out string? error, string? path = null)
    {
        error = null;
        path ??= AppPaths.RecoveryFile;

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            var state = JsonSerializer.Deserialize<DraftState>(json, JsonOptions);
            if (state is null)
            {
                error = "恢复草稿内容为空。";
            }

            return state;
        }
        catch (Exception ex)
        {
            error = "恢复草稿读取失败：" + ex.Message;
            return null;
        }
    }

    public static void Delete(string? path = null)
    {
        try
        {
            path ??= AppPaths.RecoveryFile;
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            var temporaryPath = path + ".tmp";
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
        catch
        {
            // Recovery files are best-effort; never block closing the app.
        }
    }
}
