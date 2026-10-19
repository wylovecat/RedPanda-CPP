using System.Text;
using RedPanda.Competition.Models;

namespace RedPanda.Competition.Services;

public static class AscTaskCsv
{
    public static string Build(
        AscTaskInfo task,
        AscClassInfo? classInfo,
        IReadOnlyList<AscTaskProblem> problems)
    {
        var builder = new StringBuilder();
        builder.AppendLine("班级,任务类型,任务ID,任务名称,题号,标题,完成,判题结果,提交次数");

        var className = classInfo?.DisplayName ?? string.Empty;
        var kindText = task.Source == AscProblemSource.Work ? "作业" : "班测";

        foreach (var problem in problems)
        {
            builder.Append(Quote(className)).Append(',')
                .Append(Quote(kindText)).Append(',')
                .Append(Quote(task.TaskId)).Append(',')
                .Append(Quote(task.TaskName)).Append(',')
                .Append(Quote(problem.DisplayId)).Append(',')
                .Append(Quote(problem.Title)).Append(',')
                .Append(Quote(problem.Done || !string.IsNullOrWhiteSpace(problem.Verdict)
                    ? "已完成"
                    : "未完成")).Append(',')
                .Append(Quote(string.IsNullOrWhiteSpace(problem.Verdict) ? "未判题" : problem.Verdict)).Append(',')
                .Append(problem.Records)
                .AppendLine();
        }

        return builder.ToString();
    }

    private static string Quote(string? value)
    {
        return "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
    }
}
