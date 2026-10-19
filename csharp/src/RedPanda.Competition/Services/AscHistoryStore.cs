using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using RedPanda.Competition.Models;

namespace RedPanda.Competition.Services;

/// <summary>
/// A small, global list of the latest online submissions. It is deliberately
/// separate from per-problem sidecars so the teacher can review recent work
/// without remembering which file was used.
/// </summary>
public static class AscHistoryStore
{
    public const int Limit = 50;

    private static readonly object Gate = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string DefaultPath =>
        Path.Combine(AppPaths.AppDataDirectory, "asc-history.json");

    public static List<AscSubmissionHistoryEntry> Load(string? path = null)
    {
        path ??= DefaultPath;

        try
        {
            if (!File.Exists(path))
            {
                return new List<AscSubmissionHistoryEntry>();
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<AscSubmissionHistoryEntry>>(json, JsonOptions)
                   ?? new List<AscSubmissionHistoryEntry>();
        }
        catch
        {
            return new List<AscSubmissionHistoryEntry>();
        }
    }

    public static void Add(AscSubmissionHistoryEntry entry, string? path = null)
    {
        if (string.IsNullOrWhiteSpace(entry.SubmissionId))
        {
            return;
        }

        path ??= DefaultPath;

        lock (Gate)
        {
            var items = Load(path);
            items.RemoveAll(item =>
                string.Equals(item.SubmissionId, entry.SubmissionId, StringComparison.Ordinal));
            items.Insert(0, entry);
            if (items.Count > Limit)
            {
                items.RemoveRange(Limit, items.Count - Limit);
            }

            Save(items, path);
        }
    }

    public static void Clear(string? path = null)
    {
        path ??= DefaultPath;

        lock (Gate)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best effort.
            }
        }
    }

    private static void Save(List<AscSubmissionHistoryEntry> items, string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(items, JsonOptions);
            var temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            // History is a convenience; never fail a submission because of it.
        }
    }
}
