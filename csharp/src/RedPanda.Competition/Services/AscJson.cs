using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using RedPanda.Competition.Models;

namespace RedPanda.Competition.Services;

/// <summary>
/// The aisichuang site mixes snake_case and camelCase fields and wraps almost
/// every response in {code,msg,data}. These helpers follow the same tolerant
/// rules as the C++ branch instead of relying on one fixed schema.
/// </summary>
public static class AscJson
{
    private static readonly Regex NonAlphanumeric = new("[^0-9A-Za-z]", RegexOptions.Compiled);
    private static readonly Regex ProblemIdPattern = new("^[A-Za-z]\\w*\\d+$", RegexOptions.Compiled);

    public static bool HasCode(JsonElement root)
    {
        return root.ValueKind == JsonValueKind.Object && TryGet(root, "code", out _);
    }

    public static bool IsSuccess(
        JsonElement root,
        out JsonElement data,
        out long code,
        out string message)
    {
        data = default;
        code = -1;
        message = string.Empty;

        if (root.ValueKind != JsonValueKind.Object)
        {
            message = "站点返回了无法识别的响应。";
            return false;
        }

        if (!TryGet(root, "code", out var codeElement))
        {
            message = "站点响应里没有状态码。";
            return false;
        }

        code = ToLong(codeElement, -1);
        message = Str(root, "msg", "message");

        if (TryGet(root, "data", out var dataElement) &&
            dataElement.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            data = dataElement;
        }

        return code == 200;
    }

    public static bool IsAuthFailure(long code, string message)
    {
        if (code == 401)
        {
            return true;
        }

        string[] needles =
        {
            "请登录", "请先登录", "需要登录", "未登录",
            "登录已过期", "会话已过期", "登录失效", "重新登录",
            "未能读取到有效Token", "无效Token", "Token无效"
        };

        return needles.Any(needle => message.Contains(needle, StringComparison.Ordinal));
    }

    public static bool TryGet(JsonElement element, string key, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (element.TryGetProperty(key, out value))
        {
            return true;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        return false;
    }

    public static string Str(JsonElement element, params string[] keys)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        foreach (var key in keys)
        {
            if (!TryGet(element, key, out var value))
            {
                continue;
            }

            var text = ValueText(value);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return string.Empty;
    }

    public static long Num(JsonElement element, long defaultValue, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (TryGet(element, key, out var value))
            {
                var number = ToLong(value, long.MinValue);
                if (number != long.MinValue)
                {
                    return number;
                }
            }
        }

        return defaultValue;
    }

    public static bool Bool(JsonElement element, bool defaultValue, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!TryGet(element, key, out var value))
            {
                continue;
            }

            switch (value.ValueKind)
            {
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                case JsonValueKind.Number:
                    return value.GetDouble() != 0;
                case JsonValueKind.String:
                    var text = value.GetString()?.Trim().ToLowerInvariant();
                    if (string.IsNullOrEmpty(text))
                    {
                        continue;
                    }

                    return text is not ("false" or "0" or "no");
            }
        }

        return defaultValue;
    }

    public static List<JsonElement> Elements(JsonElement data)
    {
        if (data.ValueKind == JsonValueKind.Array)
        {
            return data.EnumerateArray().ToList();
        }

        if (data.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "records", "list", "rows" })
            {
                if (TryGet(data, key, out var array) && array.ValueKind == JsonValueKind.Array)
                {
                    return array.EnumerateArray().ToList();
                }
            }
        }

        return new List<JsonElement>();
    }

    public static string NormalizeOutput(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n')
            .Select(line => line.TrimEnd(' ', '\t'))
            .ToList();

        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return string.Join('\n', lines);
    }

    public static string NormalizeVerdict(string? raw)
    {
        var text = raw?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return "?";
        }

        var compact = NonAlphanumeric.Replace(text, string.Empty).ToUpperInvariant();
        string[] exact = { "AC", "WA", "TLE", "MLE", "RE", "CE", "PE" };
        if (exact.Contains(compact))
        {
            return compact;
        }

        if (compact == "OK")
        {
            return "AC";
        }

        if (compact.StartsWith("ACCEPT", StringComparison.Ordinal)) return "AC";
        if (compact.StartsWith("WRONG", StringComparison.Ordinal)) return "WA";
        if (compact.StartsWith("TIME", StringComparison.Ordinal)) return "TLE";
        if (compact.StartsWith("MEMORY", StringComparison.Ordinal)) return "MLE";
        if (compact.StartsWith("RUNTIME", StringComparison.Ordinal)) return "RE";
        if (compact.StartsWith("COMPILE", StringComparison.Ordinal)) return "CE";
        if (compact.StartsWith("PRESENTATION", StringComparison.Ordinal)) return "PE";

        foreach (var code in exact)
        {
            if (compact.StartsWith(code, StringComparison.Ordinal))
            {
                return code;
            }
        }

        var tokens = new (string Needle, string Verdict)[]
        {
            ("编译", "CE"), ("超时", "TLE"), ("时间超限", "TLE"), ("超出时间", "TLE"),
            ("内存", "MLE"), ("空间超限", "MLE"), ("运行错误", "RE"),
            ("答案错误", "WA"), ("格式", "PE"), ("答案正确", "AC"), ("通过", "AC")
        };

        foreach (var (needle, verdict) in tokens)
        {
            if (text.Contains(needle, StringComparison.Ordinal))
            {
                return verdict;
            }
        }

        return "?";
    }

    public static string VerdictOfRecord(JsonElement record)
    {
        foreach (var key in new[] { "subStatic", "substatic", "status", "feedback", "result", "verdict" })
        {
            if (!TryGet(record, key, out var value) || value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var text = value.GetString()?.Trim() ?? string.Empty;
            if (text.Length > 0)
            {
                return NormalizeVerdict(text);
            }
        }

        return "?";
    }

    public static List<AscProblemRef> ParseProblemRefs(JsonElement data)
    {
        var result = new List<AscProblemRef>();
        foreach (var row in Elements(data))
        {
            var problem = new AscProblemRef
            {
                ProblemId = Str(row, "id", "questionId", "problemId"),
                DisplayId = Str(row, "displayId", "questionCode"),
                Title = Str(row, "title", "titleName", "name")
            };

            if (problem.DisplayId.Length == 0 && problem.ProblemId.Length == 0)
            {
                continue;
            }

            result.Add(problem);
        }

        return result;
    }

    public static List<AscClassInfo> ParseClasses(JsonElement data)
    {
        var result = new List<AscClassInfo>();
        foreach (var row in Elements(data))
        {
            var info = new AscClassInfo
            {
                ClassId = Str(row, "id", "classId"),
                ClassName = Str(row, "className", "name"),
                ClassroomName = Str(row, "classroomName")
            };

            if (info.ClassId.Length == 0)
            {
                continue;
            }

            result.Add(info);
        }

        return result;
    }

    public static List<AscTaskInfo> ParseTasks(JsonElement data, AscProblemSource source)
    {
        var isWork = source == AscProblemSource.Work;
        var idKeys = isWork ? new[] { "workid", "workId" } : new[] { "testid", "testId" };
        var nameKeys = isWork ? new[] { "workName", "name" } : new[] { "testName", "name" };

        var result = new List<AscTaskInfo>();
        foreach (var row in Elements(data))
        {
            var task = new AscTaskInfo
            {
                Source = source,
                TaskId = Str(row, idKeys),
                TaskName = Str(row, nameKeys),
                CreateTime = Str(row, "createTime"),
                EndTime = Str(row, "endTime"),
                ProblemCount = (int)Num(row, 0, "sum", "questionCount"),
                Submitted = Bool(row, false, "isSubmit")
            };

            if (task.TaskId.Length == 0)
            {
                continue;
            }

            result.Add(task);
        }

        return result;
    }

    public static List<AscTaskProblem> ParseTaskProblems(JsonElement data, AscProblemSource source)
    {
        var result = new List<AscTaskProblem>();

        foreach (var row in Elements(data))
        {
            // Only programming problems can be judged with code.
            if (Num(row, 0, "quesTypeId") != 5)
            {
                continue;
            }

            var doneKeys = source == AscProblemSource.Work
                ? new[] { "is_do", "isDo" }
                : new[] { "isDo", "is_do" };

            var problem = new AscTaskProblem
            {
                TkId = Str(row, "tkId", "id"),
                TestSubId = Str(row, "testSubId", "workSubId"),
                DisplayId = Str(row, "displayId", "questionCode"),
                Title = Str(row, "title", "titleName"),
                Done = Num(row, 0, doneKeys) != 0,
                Records = (int)Num(row, 0, "records")
            };

            var siteVerdict = VerdictOfRecord(row);
            problem.Verdict = siteVerdict == "?" ? string.Empty : siteVerdict;

            if (problem.DisplayId.Length == 0)
            {
                continue;
            }

            result.Add(problem);
        }

        return result;
    }

    public static List<AscSample> ParseSamples(JsonElement samples)
    {
        var result = new List<AscSample>();
        if (samples.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var value in samples.EnumerateArray())
        {
            var sample = new AscSample
            {
                Input = Str(value, "input", "sampleInput", "in", "inputSample"),
                Output = Str(value, "output", "sampleOutput", "out", "outputSample")
            };

            if (sample.Input.Length == 0 && sample.Output.Length == 0)
            {
                continue;
            }

            result.Add(sample);
        }

        return result;
    }

    public static AscStatement ParseStatementOjDto(JsonElement dto, string displayId, string title)
    {
        var statement = new AscStatement
        {
            DisplayId = Str(dto, "_id"),
            Title = Str(dto, "title"),
            Description = Str(dto, "description"),
            InputDescription = Str(dto, "input_description", "inputFormat", "input"),
            OutputDescription = Str(dto, "output_description", "outputFormat", "output"),
            Hint = Str(dto, "hint"),
            Prompt = Str(dto, "titlePrompt", "prompt"),
            TimeLimitMs = Num(dto, 0, "time_limit", "timeLimit"),
            MemoryLimitMb = Num(dto, 0, "memory_limit", "memoryLimit")
        };

        if (TryGet(dto, "samples", out var samples))
        {
            statement.Samples = ParseSamples(samples);
        }

        if (statement.DisplayId.Length == 0)
        {
            statement.DisplayId = displayId;
        }

        if (statement.Title.Length == 0)
        {
            statement.Title = title;
        }

        return statement;
    }

    public static AscStatement ParseStatementDetail(JsonElement dto, string displayId, string title)
    {
        var statement = new AscStatement
        {
            DisplayId = Str(dto, "displayId", "_id"),
            Title = Str(dto, "title", "titleName"),
            Description = Str(dto, "titleDescribe", "description"),
            InputDescription = Str(dto, "inputForm", "input_description"),
            OutputDescription = Str(dto, "outputForm", "output_description"),
            Hint = Str(dto, "titleBackground", "hint"),
            Prompt = Str(dto, "titlePrompt", "prompt"),
            TimeLimitMs = Num(dto, 0, "timeLimit", "time_limit"),
            MemoryLimitMb = Num(dto, 0, "memoryLimit", "memory_limit"),
            RawMarkdown = Str(dto, "all")
        };

        if (TryGet(dto, "samples", out var samples))
        {
            statement.Samples = ParseSamples(samples);
        }

        if (statement.Samples.Count == 0)
        {
            var sample = new AscSample
            {
                Input = Str(dto, "input"),
                Output = Str(dto, "output")
            };

            if (sample.Input.Length > 0 || sample.Output.Length > 0)
            {
                statement.Samples.Add(sample);
            }
        }

        if (statement.DisplayId.Length == 0)
        {
            statement.DisplayId = displayId;
        }

        if (statement.Title.Length == 0)
        {
            statement.Title = title;
        }

        return statement;
    }

    public static string StatementToMarkdown(AscStatement statement)
    {
        var builder = new StringBuilder();
        builder.Append("# ").Append(statement.DisplayId).Append(' ').Append(statement.Title)
            .AppendLine().AppendLine();

        var limits = new List<string>();
        if (statement.TimeLimitMs > 0) limits.Add($"时间限制 {statement.TimeLimitMs} ms");
        if (statement.MemoryLimitMb > 0) limits.Add($"内存限制 {statement.MemoryLimitMb} MB");
        if (limits.Count > 0)
        {
            builder.Append("> ").AppendLine(string.Join(" · ", limits)).AppendLine();
        }

        AppendSection(builder, "题目描述", statement.Description);
        AppendSection(builder, "输入格式", statement.InputDescription);
        AppendSection(builder, "输出格式", statement.OutputDescription);

        if (statement.Samples.Count > 0)
        {
            builder.AppendLine("## 样例").AppendLine();
            for (var i = 0; i < statement.Samples.Count; i++)
            {
                var sample = statement.Samples[i];
                builder.Append("### 样例 ").AppendLine((i + 1).ToString(CultureInfo.InvariantCulture));
                builder.AppendLine().AppendLine("**输入**").AppendLine();
                builder.AppendLine("```").AppendLine(sample.Input.Trim()).AppendLine("```").AppendLine();
                builder.AppendLine("**输出**").AppendLine();
                builder.AppendLine("```").AppendLine(sample.Output.Trim()).AppendLine("```").AppendLine();
            }
        }

        AppendSection(builder, "提示", statement.Prompt);
        AppendSection(builder, "说明", statement.Hint);
        return builder.ToString();
    }

    public static List<AscRankingRow> ParseRanking(JsonElement data)
    {
        var rows = new List<AscRankingRow>();

        foreach (var row in Elements(data))
        {
            var item = new AscRankingRow
            {
                UserName = Str(
                    row,
                    "testChartsUser", "workChartsUser",
                    "userName", "studentName", "nickname", "name"),
                TotalScore = Num(
                    row,
                    0,
                    "testChartsScores", "workChartsScores",
                    "totalScore", "total_score", "score")
            };

            if (TryGet(row, "list", out var problems) && problems.ValueKind == JsonValueKind.Array)
            {
                foreach (var problem in problems.EnumerateArray())
                {
                    item.Problems.Add(new AscRankingProblem
                    {
                        Name = Str(problem, "name", "title", "displayId"),
                        Score = Num(problem, 0, "score", "subScore", "workScore"),
                        BestSubmissionId = Str(
                            problem,
                            "maxScore", "max_score",
                            "submissionId", "subId", "tkSubId")
                    });
                }
            }

            rows.Add(item);
        }

        var ranked = rows.OrderByDescending(row => row.TotalScore).ToList();
        for (var i = 0; i < ranked.Count; i++)
        {
            ranked[i].Rank = i + 1;
        }

        return ranked;
    }

    public static AscSubmissionResult ParseSubmission(JsonElement data)
    {
        var result = new AscSubmissionResult();
        if (data.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        result.Id = Str(data, "id", "tkSubId");
        result.Time = Str(data, "time", "subTime");
        result.RawStatus = Str(data, "subStatic", "substatic", "status");
        result.Verdict = VerdictOfRecord(data);
        result.Feedback = Str(data, "compileFeedback", "errInfo");
        result.IsPass = Num(data, 0, "subIsPass", "isPass");
        result.Judging = result.IsPass == 7;
        result.Score = Num(data, 0, "subScore", "score");
        result.TimeMs = Num(
            data,
            0,
            "timeCost", "time_cost", "timeUsed", "time_used",
            "subTimeCost", "sub_time_cost", "costTime", "cost_time",
            "runTime", "run_time");
        result.MemoryBytes = Num(
            data,
            0,
            "memoryCost", "memory_cost", "memoryUsed", "memory_used",
            "subMemoryCost", "sub_memory_cost", "costMemory", "cost_memory",
            "runMemory", "run_memory");

        // The polling endpoint often returns only the verdict. The result page
        // (compileInfo) reads /studentsSubmitTk/getOneBySubId, where the runtime
        // lives per test case as real_time and memory in bytes. Use the slowest
        // case and the largest memory footprint as the summary when the flat
        // timeCost/memoryCost fields are absent.
        if (TryGet(data, "infoVo", out var infoVo) &&
            infoVo.ValueKind == JsonValueKind.Object &&
            TryGet(infoVo, "data", out var cases))
        {
            var index = 0;
            foreach (var row in Elements(cases))
            {
                index++;
                var item = new AscSubmissionCase
                {
                    Index = (int)Num(row, index, "test_case", "testCase", "index", "no"),
                    Feedback = Str(row, "feedback", "result", "status"),
                    Verdict = VerdictOfRecord(row),
                    TimeMs = Num(row, 0, "real_time", "realTime", "time", "timeCost", "time_cost"),
                    MemoryBytes = Num(row, 0, "memory", "memoryCost", "memory_cost", "mem")
                };

                if (item.Verdict == "?")
                {
                    item.Verdict = item.Feedback;
                }

                result.Cases.Add(item);
                result.TimeMs = Math.Max(result.TimeMs, item.TimeMs);
                result.MemoryBytes = Math.Max(result.MemoryBytes, item.MemoryBytes);
            }
        }

        return result;
    }

    public static string SubmissionIdFrom(JsonElement data, string message)
    {
        if (data.ValueKind == JsonValueKind.Number)
        {
            return data.TryGetInt64(out var number) ? number.ToString(CultureInfo.InvariantCulture) : string.Empty;
        }

        if (data.ValueKind == JsonValueKind.String)
        {
            var text = data.GetString()?.Trim() ?? string.Empty;
            if (text.Length > 0)
            {
                return text;
            }
        }

        return message.Trim();
    }

    public static bool LooksLikeProblemId(string? stem)
    {
        return !string.IsNullOrWhiteSpace(stem) && ProblemIdPattern.IsMatch(stem);
    }

    public static string MaskAccount(string? phone)
    {
        if (string.IsNullOrEmpty(phone) || phone.Length < 5)
        {
            return phone ?? string.Empty;
        }

        return phone[..3] + "****" + phone[^2..];
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 0)
        {
            return "?";
        }

        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024.0:F1} KB";
        }

        return $"{bytes / 1024.0 / 1024.0:F1} MB";
    }

    private static string ValueText(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.TryGetInt64(out var integer)
                ? integer.ToString(CultureInfo.InvariantCulture)
                : value.GetDouble().ToString(CultureInfo.InvariantCulture),
            JsonValueKind.True => "1",
            JsonValueKind.False => "0",
            _ => string.Empty
        };
    }

    private static long ToLong(JsonElement value, long defaultValue)
    {
        if (value.ValueKind == JsonValueKind.Number)
        {
            if (value.TryGetInt64(out var integer))
            {
                return integer;
            }

            // A field such as timeCost may arrive as 12.0 rather than 12.
            // The C++ client casts the double; do the same instead of losing it.
            try
            {
                return (long)Math.Round(value.GetDouble());
            }
            catch
            {
                return defaultValue;
            }
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                return number;
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var floating))
            {
                return (long)Math.Round(floating);
            }
        }

        return defaultValue;
    }

    private static void AppendSection(StringBuilder builder, string title, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return;
        }

        builder.Append("## ").AppendLine(title).AppendLine();
        builder.AppendLine(body.Trim()).AppendLine();
    }
}
