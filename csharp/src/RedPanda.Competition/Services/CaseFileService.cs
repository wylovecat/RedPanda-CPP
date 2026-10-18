using RedPanda.Competition.Models;

namespace RedPanda.Competition.Services;

public sealed record ImportedTestCase(
    string Name,
    string Input,
    string ExpectedOutput,
    string InputPath,
    string? AnswerPath);

public static class CaseFileService
{
    public static (List<ImportedTestCase> Cases, List<string> Warnings) LoadFromFiles(
        IEnumerable<string> selectedPaths)
    {
        var selected = selectedPaths
            .Where(File.Exists)
            .Select(Path.GetFullPath)
            .ToList();

        var inputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();

        foreach (var path in selected)
        {
            if (IsInput(path))
            {
                inputs.Add(path);
            }
            else if (IsAnswer(path))
            {
                var input = FindSiblingInput(path);
                if (input is null)
                {
                    warnings.Add($"未找到 {Path.GetFileName(path)} 对应的 .in 文件，已跳过。");
                }
                else
                {
                    inputs.Add(input);
                }
            }
        }

        return LoadInputs(
            inputs.OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase),
            warnings);
    }

    public static (List<ImportedTestCase> Cases, List<string> Warnings) LoadFromFolder(string folder)
    {
        var inputs = Directory
            .EnumerateFiles(folder, "*.in", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);

        return LoadInputs(inputs, new List<string>());
    }

    public static int Export(
        string folder,
        string baseName,
        IEnumerable<TestCase> testCases)
    {
        Directory.CreateDirectory(folder);

        var index = 0;
        foreach (var testCase in testCases)
        {
            if (string.IsNullOrEmpty(testCase.Input) && string.IsNullOrEmpty(testCase.ExpectedOutput))
            {
                continue;
            }

            index++;
            var stem = $"{baseName}_{index}";
            FileEncodingService.Write(Path.Combine(folder, stem + ".in"), testCase.Input);
            FileEncodingService.Write(Path.Combine(folder, stem + ".out"), testCase.ExpectedOutput);
        }

        return index;
    }

    private static (List<ImportedTestCase> Cases, List<string> Warnings) LoadInputs(
        IEnumerable<string> inputs,
        List<string> warnings)
    {
        var cases = new List<ImportedTestCase>();

        foreach (var inputPath in inputs)
        {
            var answerPath = FindAnswer(inputPath);

            try
            {
                var (input, _) = FileEncodingService.Read(inputPath);
                var expected = string.Empty;

                if (answerPath is not null)
                {
                    (expected, _) = FileEncodingService.Read(answerPath);
                }
                else
                {
                    warnings.Add($"未找到 {Path.GetFileName(inputPath)} 对应的 .out/.ans，期望输出留空。");
                }

                cases.Add(new ImportedTestCase(
                    Path.GetFileNameWithoutExtension(inputPath),
                    input,
                    expected,
                    inputPath,
                    answerPath));
            }
            catch (Exception ex)
            {
                warnings.Add($"读取 {Path.GetFileName(inputPath)} 失败：{ex.Message}");
            }
        }

        return (cases, warnings);
    }

    private static string? FindAnswer(string inputPath)
    {
        foreach (var extension in new[] { ".out", ".ans" })
        {
            var candidate = Path.ChangeExtension(inputPath, extension);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? FindSiblingInput(string answerPath)
    {
        var candidate = Path.ChangeExtension(answerPath, ".in");
        return File.Exists(candidate) ? candidate : null;
    }

    private static bool IsInput(string path)
    {
        return string.Equals(Path.GetExtension(path), ".in", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAnswer(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".out", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".ans", StringComparison.OrdinalIgnoreCase);
    }
}
