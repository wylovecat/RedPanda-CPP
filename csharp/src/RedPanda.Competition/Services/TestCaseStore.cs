using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using RedPanda.Competition.Models;

namespace RedPanda.Competition.Services;

public static class TestCaseStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string GetSidecarPath(string sourcePath)
    {
        return sourcePath + ".tests.json";
    }

    public static List<TestCase> Load(string sourcePath, out string? error)
    {
        error = null;
        var path = GetSidecarPath(sourcePath);

        if (!File.Exists(path))
        {
            return new List<TestCase>();
        }

        try
        {
            var json = File.ReadAllText(path);
            var file = JsonSerializer.Deserialize<TestSuiteFile>(json, JsonOptions);
            if (file?.Cases is null)
            {
                return new List<TestCase>();
            }

            var cases = new List<TestCase>(file.Cases.Count);
            for (var i = 0; i < file.Cases.Count; i++)
            {
                var item = file.Cases[i];
                cases.Add(new TestCase
                {
                    Name = string.IsNullOrWhiteSpace(item.Name) ? $"样例 {i + 1}" : item.Name,
                    Input = item.Input ?? string.Empty,
                    ExpectedOutput = item.ExpectedOutput ?? string.Empty,
                    IsChecked = item.Enabled
                });
            }

            return cases;
        }
        catch (Exception ex)
        {
            error = "测试点文件读取失败：" + ex.Message;
            return new List<TestCase>();
        }
    }

    public static void Save(string sourcePath, IEnumerable<TestCase> cases)
    {
        var path = GetSidecarPath(sourcePath);
        var file = new TestSuiteFile
        {
            Cases = cases.Select(c => new TestCaseFile
            {
                Name = c.Name,
                Input = c.Input,
                ExpectedOutput = c.ExpectedOutput,
                Enabled = c.IsChecked
            }).ToList()
        };

        var json = JsonSerializer.Serialize(file, JsonOptions);
        var temporaryPath = path + ".tmp";

        File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
        File.Move(temporaryPath, path, overwrite: true);
    }
}
