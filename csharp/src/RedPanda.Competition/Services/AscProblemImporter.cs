using System.Text.RegularExpressions;
using RedPanda.Competition.Models;

namespace RedPanda.Competition.Services;

public sealed record AscProblemPaths(
    string Folder,
    string Stem,
    string SourcePath,
    string StatementPath);

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

    public static AscProblemPaths ResolvePaths(
        string displayId,
        AscProblemSource source,
        AscTaskInfo? task,
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

        var stem = SanitizeName(displayId, "problem");
        return new AscProblemPaths(
            folder,
            stem,
            Path.Combine(folder, stem + ".cpp"),
            Path.Combine(folder, stem + ".md"));
    }

    /// <summary>
    /// Finds an already imported problem so weak network conditions do not
    /// force a re-download. The source file must exist; its link may be
    /// inferred from the file name (A1131.cpp) when the sidecar was lost.
    /// </summary>
    public static AscImportResult? TryOpenLocal(
        string displayId,
        string title,
        AscProblemSource source,
        AscTaskInfo? task,
        string? classId,
        string? className,
        AscSettings settings)
    {
        var paths = ResolvePaths(displayId, source, task, settings);
        if (!File.Exists(paths.SourcePath))
        {
            return null;
        }

        var link = AscLinkStore.Load(paths.SourcePath) ??
                   AscLinkStore.LoadOrInfer(paths.SourcePath);
        if (link is null)
        {
            return null;
        }

        link.DisplayId = string.IsNullOrWhiteSpace(link.DisplayId) ? displayId : link.DisplayId;
        link.Title = string.IsNullOrWhiteSpace(link.Title) ? title : link.Title;
        link.Source = source;
        if (task is not null)
        {
            link.TaskId = task.TaskId;
            link.TaskName = task.TaskName;
        }

        link.ClassId = classId ?? link.ClassId;
        link.ClassName = className ?? link.ClassName;

        var cases = TestCaseStore.Load(paths.SourcePath, out _);
        return new AscImportResult
        {
            SourcePath = paths.SourcePath,
            StatementPath = paths.StatementPath,
            Link = link,
            TestCases = cases,
            CreatedSource = false,
            CreatedTests = false
        };
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
        var displayId = !string.IsNullOrWhiteSpace(statement.DisplayId)
            ? statement.DisplayId
            : problem.DisplayId;
        var paths = ResolvePaths(displayId, source, task, settings);
        var folder = paths.Folder;
        var stem = paths.Stem;
        var sourcePath = paths.SourcePath;
        var statementPath = paths.StatementPath;

        Directory.CreateDirectory(folder);

        var createdSource = !File.Exists(sourcePath);

        if (createdSource)
        {
            FileEncodingService.Write(sourcePath, TemplateService.CreateCppTemplate());
        }

        var cases = BuildTestCases(statement);
        var sidecarPath = TestCaseStore.GetSidecarPath(sourcePath);
        var createdTests = !File.Exists(sidecarPath);
        var samplesFilled = false;

        if (createdTests)
        {
            TestCaseStore.Save(sourcePath, cases);
        }
        else
        {
            var loaded = TestCaseStore.Load(sourcePath, out var error);
            if (error is null && loaded.Count > 0)
            {
                if (statement.Samples.Count > 0 && IsPlaceholderSuite(loaded))
                {
                    // The offline import wrote one empty placeholder case. Once
                    // the network comes back, replace only that placeholder and
                    // leave any user-authored cases alone.
                    cases = BuildTestCases(statement);
                    TestCaseStore.Save(sourcePath, cases);
                    samplesFilled = true;
                }
                else
                {
                    cases = loaded;
                }
            }
            else
            {
                TestCaseStore.Save(sourcePath, cases);
                createdTests = true;
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
            CreatedTests = createdTests,
            SamplesFilled = samplesFilled
        };
    }

    private static bool IsPlaceholderSuite(IReadOnlyList<TestCase> cases)
    {
        return cases.All(testCase =>
            string.IsNullOrWhiteSpace(testCase.Input) &&
            string.IsNullOrWhiteSpace(testCase.ExpectedOutput) &&
            testCase.Name.StartsWith("样例", StringComparison.Ordinal));
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
