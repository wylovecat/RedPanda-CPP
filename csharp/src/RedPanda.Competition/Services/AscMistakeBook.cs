using RedPanda.Competition.Models;

namespace RedPanda.Competition.Services;

public static class AscMistakeBook
{
    public static List<MistakeEntry> Build(IEnumerable<AscSubmissionHistoryEntry> entries)
    {
        var groups = entries.GroupBy(entry => string.IsNullOrWhiteSpace(entry.SourcePath)
            ? entry.DisplayId + "|" + entry.TaskId
            : entry.SourcePath, StringComparer.OrdinalIgnoreCase);

        var result = new List<MistakeEntry>();
        foreach (var group in groups)
        {
            var ordered = group.OrderBy(entry => entry.SubmittedAtUtc).ToList();
            var latest = ordered[^1];
            if (string.Equals(latest.Verdict, "AC", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var failures = ordered
                .Where(entry => !string.Equals(entry.Verdict, "AC", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var lastFailure = failures[^1];

            result.Add(new MistakeEntry
            {
                Key = group.Key,
                DisplayId = lastFailure.DisplayId,
                Title = lastFailure.Title,
                TaskName = lastFailure.TaskName,
                Source = lastFailure.Source,
                Verdict = lastFailure.VerdictText,
                FailCount = failures.Count,
                LastSubmittedAtUtc = lastFailure.SubmittedAtUtc,
                LastSubmissionId = lastFailure.SubmissionId,
                SourcePath = lastFailure.SourcePath
            });
        }

        result.Sort((left, right) =>
            right.LastSubmittedAtUtc.CompareTo(left.LastSubmittedAtUtc));
        return result;
    }
}
