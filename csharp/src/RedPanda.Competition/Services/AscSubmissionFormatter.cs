using System.Text;
using RedPanda.Competition.Models;

namespace RedPanda.Competition.Services;

public static class AscSubmissionFormatter
{
    public static string Format(
        AscProblemLink link,
        AscSubmissionResult result,
        string submissionId)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"{link.DisplayId} {link.Title}".Trim());
        builder.AppendLine($"提交号：{submissionId}");
        builder.AppendLine($"结果：{result.VerdictText}");
        builder.AppendLine($"时间/内存：{result.PerformanceText}");
        builder.AppendLine($"得分：{result.Score}");
        if (long.TryParse(submissionId, out _))
        {
            builder.AppendLine(
                "网站：https://www.aisichuang.net/main/questionBank/compileInfo" +
                "?namefrom=SubmitRecord_Everyone&id=" + submissionId);
        }

        if (link.IsTask)
        {
            builder.AppendLine($"任务：{link.TaskName}");
        }

        if (!string.IsNullOrWhiteSpace(result.Feedback))
        {
            builder.AppendLine().AppendLine("反馈：").AppendLine(result.Feedback.Trim());
        }

        if (result.Cases.Count > 0)
        {
            builder.AppendLine().AppendLine($"测试点（{result.Cases.Count}）：");
            foreach (var item in result.Cases.Take(200))
            {
                var verdict = string.IsNullOrWhiteSpace(item.Verdict) ? "?" : item.Verdict;
                builder.Append('#').Append(item.Index).Append(' ')
                    .Append(verdict.PadRight(4))
                    .Append("  ").AppendLine(item.PerformanceText);
            }

            if (result.Cases.Count > 200)
            {
                builder.AppendLine($"……其余 {result.Cases.Count - 200} 个测试点未显示");
            }
        }

        return builder.ToString().TrimEnd();
    }
}
