using System.Text;
using RedPanda.Competition.Models;

namespace RedPanda.Competition.Services;

public static class AscRankingCsv
{
    public static string Build(
        AscTaskInfo task,
        AscClassInfo? classInfo,
        IReadOnlyList<AscRankingRow> rows)
    {
        var builder = new StringBuilder();
        var problemNames = rows.FirstOrDefault()?.Problems
            .Select(problem => problem.Name)
            .ToList() ?? new List<string>();

        builder.Append("班级,任务类型,任务ID,任务名称,名次,用户,总分");
        foreach (var name in problemNames)
        {
            builder.Append(',').Append(Quote(name));
        }

        builder.AppendLine();

        var className = classInfo?.DisplayName ?? string.Empty;
        var kindText = task.Source == AscProblemSource.Work ? "作业" : "班测";

        foreach (var row in rows)
        {
            builder.Append(Quote(className)).Append(',')
                .Append(Quote(kindText)).Append(',')
                .Append(Quote(task.TaskId)).Append(',')
                .Append(Quote(task.TaskName)).Append(',')
                .Append(row.Rank).Append(',')
                .Append(Quote(row.UserName)).Append(',')
                .Append(row.TotalScore.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));

            for (var i = 0; i < problemNames.Count; i++)
            {
                var score = i < row.Problems.Count ? row.Problems[i].Score : 0;
                builder.Append(',')
                    .Append(score.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string Quote(string? value)
    {
        return "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
    }
}
