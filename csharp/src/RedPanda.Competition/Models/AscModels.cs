using System.Text.Json.Serialization;

namespace RedPanda.Competition.Models;

public enum AscProblemSource
{
    Bank,
    Test,
    Work
}

public sealed class AscSession
{
    public string BaseUrl { get; set; } = string.Empty;

    public string Satoken { get; set; } = string.Empty;

    public string CsrfToken { get; set; } = string.Empty;

    public string SessionId { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public string Role { get; set; } = "student";

    public string Phone { get; set; } = string.Empty;

    [JsonIgnore]
    public bool IsValid => !string.IsNullOrWhiteSpace(Satoken);

    [JsonIgnore]
    public string MaskedPhone
    {
        get
        {
            if (Phone.Length < 5)
            {
                return Phone;
            }

            return Phone[..3] + "****" + Phone[^2..];
        }
    }
}

public sealed class AscSettings
{
    public const string DefaultBaseUrl = "https://www.aisichuang.net/tiku";

    public string BaseUrl { get; set; } = DefaultBaseUrl;

    public string TaskRootDir { get; set; } = string.Empty;

    public string SubmitLanguage { get; set; } = "C++";

    public bool EnableOnlineSubmit { get; set; }

    public string LastPhone { get; set; } = string.Empty;

    public string LastClassId { get; set; } = string.Empty;

    public string LastClassName { get; set; } = string.Empty;

    public int LastTaskKind { get; set; }

    public string LastTaskId { get; set; } = string.Empty;

    public string LastTaskName { get; set; } = string.Empty;

    public AscSession Session { get; set; } = new();
}

public sealed class AscProblemRef
{
    public string ProblemId { get; set; } = string.Empty;

    public string DisplayId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    [JsonIgnore]
    public string DisplayText => $"{DisplayId}  {Title}".Trim();
}

public sealed class AscSample
{
    public string Input { get; set; } = string.Empty;

    public string Output { get; set; } = string.Empty;
}

public sealed class AscStatement
{
    public string ProblemId { get; set; } = string.Empty;

    public string DisplayId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string InputDescription { get; set; } = string.Empty;

    public string OutputDescription { get; set; } = string.Empty;

    public string Hint { get; set; } = string.Empty;

    public string Prompt { get; set; } = string.Empty;

    public string RawMarkdown { get; set; } = string.Empty;

    public List<AscSample> Samples { get; set; } = new();

    public long TimeLimitMs { get; set; }

    public long MemoryLimitMb { get; set; }
}

public sealed class AscClassInfo
{
    public string ClassId { get; set; } = string.Empty;

    public string ClassName { get; set; } = string.Empty;

    public string ClassroomName { get; set; } = string.Empty;

    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ClassName))
            {
                return ClassName;
            }

            if (!string.IsNullOrWhiteSpace(ClassroomName))
            {
                return ClassroomName;
            }

            return $"班级 {ClassId}";
        }
    }

    public override string ToString() => DisplayName;
}

public sealed class AscTaskInfo
{
    public AscProblemSource Source { get; set; }

    public string TaskId { get; set; } = string.Empty;

    public string TaskName { get; set; } = string.Empty;

    public string CreateTime { get; set; } = string.Empty;

    public string EndTime { get; set; } = string.Empty;

    public int ProblemCount { get; set; }

    public bool Submitted { get; set; }

    public string DisplayName
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(TaskName) ? $"#{TaskId}" : TaskName;
            return ProblemCount > 0 ? $"{name} ({ProblemCount})" : name;
        }
    }

    [JsonIgnore]
    public bool IsWork => Source == AscProblemSource.Work;

    public override string ToString() => DisplayName;
}

public sealed class AscTaskProblem
{
    public string TkId { get; set; } = string.Empty;

    public string TestSubId { get; set; } = string.Empty;

    public string DisplayId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public bool Done { get; set; }

    public int Records { get; set; }

    public string Verdict { get; set; } = string.Empty;

    public string DisplayText => $"{DisplayId}  {Title}".Trim();

    [JsonIgnore]
    public string StatusText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Verdict))
            {
                return Verdict;
            }

            return Done ? "已做" : "未做";
        }
    }
}

public sealed class AscRankingProblem
{
    public string Name { get; set; } = string.Empty;

    public double Score { get; set; }

    public string BestSubmissionId { get; set; } = string.Empty;
}

public sealed class AscRankingRow
{
    public int Rank { get; set; }

    public string UserName { get; set; } = string.Empty;

    public double TotalScore { get; set; }

    public List<AscRankingProblem> Problems { get; set; } = new();

    [JsonIgnore]
    public string DisplayText => $"{Rank}.  {UserName}";

    [JsonIgnore]
    public string ScoreText => $"{TotalScore:0.##} 分";

    [JsonIgnore]
    public string ProblemSummary => string.Join(
        " · ",
        Problems.Select(problem => $"{problem.Name}: {problem.Score:0.##}"));
}

public sealed class AscSubmissionCase
{
    public int Index { get; set; }

    public string Verdict { get; set; } = string.Empty;

    public string Feedback { get; set; } = string.Empty;

    public long TimeMs { get; set; }

    public long MemoryBytes { get; set; }

    [JsonIgnore]
    public string PerformanceText
    {
        get
        {
            var time = TimeMs > 0 ? $"{TimeMs} ms" : "? ms";
            var memory = MemoryBytes > 0
                ? $"{MemoryBytes / 1024.0 / 1024.0:F1} MB"
                : "? MB";
            return $"{time} · {memory}";
        }
    }
}

public sealed class AscSubmissionResult
{
    public string Id { get; set; } = string.Empty;

    public string Time { get; set; } = string.Empty;

    public string RawStatus { get; set; } = string.Empty;

    public string Verdict { get; set; } = string.Empty;

    public string Feedback { get; set; } = string.Empty;

    public bool Judging { get; set; }

    public long IsPass { get; set; }

    public double Score { get; set; }

    public long TimeMs { get; set; }

    public long MemoryBytes { get; set; }

    public List<AscSubmissionCase> Cases { get; set; } = new();

    [JsonIgnore]
    public bool Passed => Verdict == "AC" || IsPass == 1;

    [JsonIgnore]
    public string VerdictText => Judging ? "判题中" : (string.IsNullOrWhiteSpace(Verdict) ? "?" : Verdict);

    [JsonIgnore]
    public string PerformanceText
    {
        get
        {
            if (TimeMs <= 0 && MemoryBytes <= 0)
            {
                return "网站未返回时间/内存";
            }

            var time = TimeMs > 0 ? $"{TimeMs} ms" : "时间 ?";
            var memory = MemoryBytes > 0
                ? $"{MemoryBytes / 1024.0 / 1024.0:F1} MB"
                : "内存 ?";
            return $"{time} · {memory}";
        }
    }

    [JsonIgnore]
    public bool IsFinal
    {
        get
        {
            if (Judging)
            {
                return false;
            }

            return (long.TryParse(Id, out var id) && id > 0) ||
                   !string.IsNullOrWhiteSpace(RawStatus);
        }
    }
}

public sealed class AscSubmissionHistoryEntry
{
    public string SubmissionId { get; set; } = string.Empty;

    public string DisplayId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string TaskId { get; set; } = string.Empty;

    public string TaskName { get; set; } = string.Empty;

    public AscProblemSource Source { get; set; } = AscProblemSource.Bank;

    public string Verdict { get; set; } = string.Empty;

    public long TimeMs { get; set; }

    public long MemoryBytes { get; set; }

    public double Score { get; set; }

    public DateTime SubmittedAtUtc { get; set; }

    public string SourcePath { get; set; } = string.Empty;

    [JsonIgnore]
    public string TimeText => SubmittedAtUtc == DateTime.MinValue
        ? string.Empty
        : SubmittedAtUtc.ToLocalTime().ToString("MM-dd HH:mm:ss");

    [JsonIgnore]
    public string DisplayText => $"{DisplayId}  {Title}".Trim();

    [JsonIgnore]
    public string VerdictText => string.IsNullOrWhiteSpace(Verdict) ? "?" : Verdict;

    [JsonIgnore]
    public string PerformanceText
    {
        get
        {
            if (TimeMs <= 0 && MemoryBytes <= 0)
            {
                return "时间/内存 ?";
            }

            var time = TimeMs > 0 ? $"{TimeMs} ms" : "? ms";
            var memory = MemoryBytes > 0
                ? $"{MemoryBytes / 1024.0 / 1024.0:F1} MB"
                : "? MB";
            return $"{time} · {memory}";
        }
    }

    [JsonIgnore]
    public string TaskText => Source == AscProblemSource.Bank
        ? "题库"
        : (Source == AscProblemSource.Work ? "作业 " : "班测 ") + TaskName;

    [JsonIgnore]
    public string DetailText => $"{TaskText} · {PerformanceText}";
}

public sealed class AscProblemLink
{
    public string DisplayId { get; set; } = string.Empty;

    public string ProblemId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public AscProblemSource Source { get; set; } = AscProblemSource.Bank;

    public string TaskId { get; set; } = string.Empty;

    public string TaskName { get; set; } = string.Empty;

    public string ClassId { get; set; } = string.Empty;

    public string ClassName { get; set; } = string.Empty;

    public long TimeLimitMs { get; set; }

    public long MemoryLimitMb { get; set; }

    public string LastVerdict { get; set; } = string.Empty;

    public string LastSubmissionId { get; set; } = string.Empty;

    public string LastSubmittedCodeHash { get; set; } = string.Empty;

    public DateTime LastSubmittedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public bool IsTask => Source != AscProblemSource.Bank;

    [JsonIgnore]
    public bool IsValid => !string.IsNullOrWhiteSpace(DisplayId);
}

public sealed class AscImportResult
{
    public required string SourcePath { get; init; }

    public required string StatementPath { get; init; }

    public required AscProblemLink Link { get; init; }

    public required List<TestCase> TestCases { get; init; }

    public required bool CreatedSource { get; init; }

    public required bool CreatedTests { get; init; }

    public bool SamplesFilled { get; init; }
}
