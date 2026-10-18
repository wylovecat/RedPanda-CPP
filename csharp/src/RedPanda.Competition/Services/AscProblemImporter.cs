using System.Text.RegularExpressions;
using RedPanda.Competition.Models;

namespace RedPanda.Competition.Services;

public static class AscProblemImporter
{
    private static readonly Regex ForbiddenNameChars = new("[\\\\/:*?\"<>|\\r\\n\\t]", RegexOptions.Compiled);

    public static string ResolveRoot(AscSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.TaskRootDir))
        {
            return settings.TaskRootDir;
        }

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(documents))
        {
            documents = AppContext.BaseDirectory;
        }

        return Path.Combine(documents, "RedPandaCP", "Aisichuang");
    }

    public static AscImportResult Import(
        AscStatement statement,
        AscProblemRef problem,
        AscProblemSource source,
        AscTaskInfo? task,
        string? classId,
        string? className,
        AscSettings settings)
    {
        var root = ResolveRoot(settings);
        var taskFolder = task is null
            ? string.Empty
            : SanitizeName(string.IsNullOrWhiteSpace(task.TaskName) ? task.TaskId : task.TaskName,
                "未命名任务");

        var folder = source switch
        {
            AscProblemSource.Test => Path.Combine(root, "班测", taskFolder),
            AscProblemSource.Work => Path.Combine(root, "作业", taskFolder),
            _ => Path.Combine(root, "题库")
        };

        Directory.CreateDirectory(folder);

        var displayId = !string.IsNullOrWhiteSpace(statement.DisplayId)
            ? statement.DisplayId
            : problem.DisplayId;
        var stem = SanitizeName(displayId, "problem");

        var sourcePath = Path.Combine(folder, stem + ".cpp");
        var statementPath = Path.Combine(folder, stem + ".md");
        var createdSource = !File.Exists(sourcePath);

        if (createdSource)
        {
            FileEncodingService.Write(sourcePath, TemplateService.CreateCppTemplate());
        }

        var cases = BuildTestCases(statement);
        var sidecarPath = TestCaseStore.GetSidecarPath(sourcePath);
        var createdTests = !File.Exists(sidecarPath);

        if (createdTests)
        {
            TestCaseStore.Save(sourcePath, cases);
        }
        else
        {
            var loaded = TestCaseStore.Load(sourcePath, out var error);
            if (error is null && loaded.Count > 0)
            {
                cases = loaded;
            }
        }

        WriteSamples(folder, stem, statement);

        var markdown = string.IsNullOrWhiteSpace(statement.RawMarkdown)
            ? AscJson.StatementToMarkdown(statement)
            : statement.RawMarkdown;
        FileEncodingService.Write(statementPath, markdown);

        var previous = AscLinkStore.Load(sourcePath);
        var link = new AscProblemLink
        {
            DisplayId = displayId,
            ProblemId = statement.ProblemId,
            Title = string.IsNullOrWhiteSpace(statement.Title) ? problem.Title : statement.Title,
            Source = source,
            TaskId = task?.TaskId ?? string.Empty,
            TaskName = task?.TaskName ?? string.Empty,
            ClassId = classId ?? string.Empty,
            ClassName = className ?? string.Empty,
            TimeLimitMs = statement.TimeLimitMs > 0 ? statement.TimeLimitMs : 0,
            MemoryLimitMb = statement.MemoryLimitMb > 0 ? statement.MemoryLimitMb : 0,
            LastVerdict = previous?.LastVerdict ?? string.Empty,
            LastSubmissionId = previous?.LastSubmissionId ?? string.Empty
        };

        AscLinkStore.Save(sourcePath, link);

        return new AscImportResult
        {
            SourcePath = sourcePath,
            StatementPath = statementPath,
            Link = link,
            TestCases = cases,
            CreatedSource = createdSource,
            CreatedTests = createdTests
        };
    }

    public static string SanitizeName(string? name, string fallback)
    {
        var result = ForbiddenNameChars.Replace(name ?? string.Empty, "_").Trim();
        while (result.StartsWith('.'))
        {
            result = result[1..];
        }

        if (result.Length == 0)
        {
            result = fallback;
        }

        return result.Length > 60 ? result[..60] : result;
    }

    private static List<TestCase> BuildTestCases(AscStatement statement)
    {
        var cases = new List<TestCase>();

        for (var i = 0; i < statement.Samples.Count; i++)
        {
            var sample = statement.Samples[i];
            var input = NormalizeInput(sample.Input);
            var expected = NormalizeExpected(sample.Output);

            cases.Add(new TestCase
            {
                Name = $"样例 {i + 1}",
                Input = input,
                ExpectedOutput = expected,
                IsChecked = true
            });
        }

        if (cases.Count == 0)
        {
            cases.Add(new TestCase { Name = "样例 1", IsChecked = true });
        }

        return cases;
    }

    private static void WriteSamples(string folder, string stem, AscStatement statement)
    {
        for (var i = 0; i < statement.Samples.Count; i++)
        {
            var inputPath = Path.Combine(folder, $"{stem}_{i + 1}.in");
            var outputPath = Path.Combine(folder, $"{stem}_{i + 1}.out");
            FileEncodingService.Write(inputPath, NormalizeInput(statement.Samples[i].Input));
            FileEncodingService.Write(outputPath, NormalizeExpected(statement.Samples[i].Output));
        }
    }

    private static string NormalizeInput(string? text)
    {
        var value = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        return value.EndsWith('\n') || value.Length == 0 ? value : value + "\n";
    }

    private static string NormalizeExpected(string? text)
    {
        var value = AscJson.NormalizeOutput(text);
        return value.Length == 0 ? string.Empty : value + "\n";
    }
}
