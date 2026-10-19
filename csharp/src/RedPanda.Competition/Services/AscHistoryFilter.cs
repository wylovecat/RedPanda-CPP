using RedPanda.Competition.Models;

namespace RedPanda.Competition.Services;

public enum AscHistoryVerdictFilter
{
    All,
    Accepted,
    NotAccepted,
    Unjudged
}

public static class AscHistoryFilter
{
    public static List<AscSubmissionHistoryEntry> Filter(
        IEnumerable<AscSubmissionHistoryEntry> entries,
        string? query,
        AscHistoryVerdictFilter verdictFilter)
    {
        var text = query?.Trim() ?? string.Empty;

        return entries.Where(entry =>
            {
                if (verdictFilter == AscHistoryVerdictFilter.Accepted &&
                    !string.Equals(entry.Verdict, "AC", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (verdictFilter == AscHistoryVerdictFilter.NotAccepted &&
                    string.Equals(entry.Verdict, "AC", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (verdictFilter == AscHistoryVerdictFilter.Unjudged &&
                    !string.IsNullOrWhiteSpace(entry.Verdict) &&
                    entry.Verdict != "?")
                {
                    return false;
                }

                if (text.Length == 0)
                {
                    return true;
                }

                return entry.DisplayId.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                       entry.Title.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                       entry.TaskName.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                       entry.SubmissionId.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                       entry.Verdict.Contains(text, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();
    }

    public static string BuildCsv(IEnumerable<AscSubmissionHistoryEntry> entries)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("时间,提交号,题号,标题,类型,任务,判题,耗时(ms),内存(MB),得分,源码路径");

        foreach (var entry in entries)
        {
            builder.Append(Quote(entry.SubmittedAtUtc == DateTime.MinValue
                    ? string.Empty
                    : entry.SubmittedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")))
                .Append(',')
                .Append(Quote(entry.SubmissionId)).Append(',')
                .Append(Quote(entry.DisplayId)).Append(',')
                .Append(Quote(entry.Title)).Append(',')
                .Append(Quote(entry.Source == AscProblemSource.Bank
                    ? "题库"
                    : entry.Source == AscProblemSource.Work ? "作业" : "班测")).Append(',')
                .Append(Quote(entry.TaskName)).Append(',')
                .Append(Quote(entry.VerdictText)).Append(',')
                .Append(entry.TimeMs).Append(',')
                .Append((entry.MemoryBytes / 1024.0 / 1024.0).ToString("0.00",
                    System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                .Append(entry.Score.ToString("0.##",
                    System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                .Append(Quote(entry.SourcePath))
                .AppendLine();
        }

        return builder.ToString();
    }

    private static string Quote(string? value)
    {
        return "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
    }
}
