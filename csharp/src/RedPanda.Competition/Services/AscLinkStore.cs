using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using RedPanda.Competition.Models;

namespace RedPanda.Competition.Services;

/// <summary>
/// The local source file ↔ aisichuang problem/task link. It lives next to the
/// source file, so a problem folder can be copied to another machine without a
/// global path database.
/// </summary>
public static class AscLinkStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string PathFor(string sourcePath) => sourcePath + ".aisichuang.json";

    public static AscProblemLink? Load(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return null;
        }

        var path = PathFor(sourcePath);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            var link = JsonSerializer.Deserialize<AscProblemLink>(json, JsonOptions);
            return link is null || !link.IsValid ? null : link;
        }
        catch
        {
            return null;
        }
    }

    public static AscProblemLink? LoadOrInfer(string? sourcePath)
    {
        var link = Load(sourcePath);
        if (link is not null || string.IsNullOrWhiteSpace(sourcePath))
        {
            return link;
        }

        // "A1131.cpp" is a bank problem even when it was not imported by us.
        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        if (!AscJson.LooksLikeProblemId(stem))
        {
            return null;
        }

        return new AscProblemLink
        {
            DisplayId = stem,
            Source = AscProblemSource.Bank
        };
    }

    public static void Save(string sourcePath, AscProblemLink link)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !link.IsValid)
        {
            return;
        }

        var path = PathFor(sourcePath);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        link.UpdatedAtUtc = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(link, JsonOptions);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
        File.Move(temporaryPath, path, overwrite: true);
    }

    public static void Delete(string sourcePath)
    {
        try
        {
            var path = PathFor(sourcePath);
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
