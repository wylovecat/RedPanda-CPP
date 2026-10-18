using System.Text;

namespace RedPanda.Competition.Services;

public static class OutputComparer
{
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            lines[i] = lines[i].TrimEnd(' ', '\t');
        }

        var end = lines.Length;
        while (end > 0 && lines[end - 1].Length == 0)
        {
            end--;
        }

        return string.Join('\n', lines, 0, end);
    }

    public static bool AreEqual(string? expected, string? actual)
    {
        return string.Equals(Normalize(expected), Normalize(actual), StringComparison.Ordinal);
    }

    public static string DescribeDifference(string? expected, string? actual)
    {
        var normalizedExpected = Normalize(expected);
        var normalizedActual = Normalize(actual);

        if (normalizedExpected == normalizedActual)
        {
            return "输出一致";
        }

        var expectedLines = normalizedExpected.Length == 0 ? Array.Empty<string>() : normalizedExpected.Split('\n');
        var actualLines = normalizedActual.Length == 0 ? Array.Empty<string>() : normalizedActual.Split('\n');
        var count = Math.Max(expectedLines.Length, actualLines.Length);

        for (var i = 0; i < count; i++)
        {
            var e = i < expectedLines.Length ? expectedLines[i] : null;
            var a = i < actualLines.Length ? actualLines[i] : null;
            if (e != a)
            {
                var builder = new StringBuilder();
                builder.Append("第 ").Append(i + 1).Append(" 行不同");
                if (e is null)
                {
                    builder.Append("：期望输出已结束");
                }
                else
                {
                    builder.Append("：期望 \"").Append(Escape(e)).Append('"');
                }

                if (a is null)
                {
                    builder.Append("，实际输出已结束");
                }
                else
                {
                    builder.Append("，实际 \"").Append(Escape(a)).Append('"');
                }

                return builder.ToString();
            }
        }

        return "输出不一致";
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\t", "\\t");
    }
}
