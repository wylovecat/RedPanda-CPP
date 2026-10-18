using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using RedPanda.Competition.Models;

namespace RedPanda.Competition.Services;

public static class DiffService
{
    private const int MaxDisplayLines = 2000;

    public static List<DiffLine> Build(string expected, string actual, int displayCharacterLimit)
    {
        var result = new List<DiffLine>
        {
            new("i", "比较规则：忽略行尾空白、末尾空行；\"-\" 为期望，\"+\" 为实际", DiffLine.Note)
        };

        var normalizedExpected = OutputComparer.Normalize(expected);
        var normalizedActual = OutputComparer.Normalize(actual);

        if (normalizedExpected == normalizedActual)
        {
            result.Add(new DiffLine("✓", "输出一致", DiffLine.Equal));
            return result;
        }

        var diff = InlineDiffBuilder.Diff(
            TextTruncation.ForDisplay(normalizedExpected, displayCharacterLimit),
            TextTruncation.ForDisplay(normalizedActual, displayCharacterLimit));

        foreach (var line in diff.Lines)
        {
            var (marker, kind) = line.Type switch
            {
                ChangeType.Deleted => ("-", DiffLine.Deleted),
                ChangeType.Inserted => ("+", DiffLine.Inserted),
                ChangeType.Modified => ("~", DiffLine.Inserted),
                ChangeType.Imaginary => ("?", DiffLine.Note),
                _ => (" ", DiffLine.Equal)
            };

            result.Add(new DiffLine(marker, line.Text ?? string.Empty, kind));
        }

        if (result.Count > MaxDisplayLines)
        {
            result.RemoveRange(MaxDisplayLines - 1, result.Count - MaxDisplayLines + 1);
            result.Add(new DiffLine(
                "…",
                $"差异过多，仅显示前 {MaxDisplayLines - 1} 行；请缩小输出或直接查看完整结果。",
                DiffLine.Note));
        }

        return result;
    }
}
