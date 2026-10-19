using System.Text.Json;
using RedPanda.Competition.Models;
using RedPanda.Competition.Services;

namespace RedPanda.Competition.Tests;

public sealed class AscJsonTests
{
    [Fact]
    public void Envelope_ParsesSuccessAndAuthFailure()
    {
        using var success = Parse("""{"code":200,"msg":"ok","data":{"records":[1]}}""");
        Assert.True(AscJson.IsSuccess(success.RootElement, out var data, out var code, out _));
        Assert.Equal(200, code);
        Assert.Equal(JsonValueKind.Object, data.ValueKind);

        using var failure = Parse("""{"code":401,"msg":"请先登录"}""");
        Assert.False(AscJson.IsSuccess(failure.RootElement, out _, out code, out var message));
        Assert.Equal(401, code);
        Assert.True(AscJson.IsAuthFailure(code, message));
    }

    [Fact]
    public void ParseTasks_HandlesLowercaseWorkAndTestIds()
    {
        using var work = Parse("""
            {"records":[{"workid":12,"workName":"作业一","sum":3,"isSubmit":1}]}
            """);
        var works = AscJson.ParseTasks(work.RootElement, AscProblemSource.Work);
        Assert.Single(works);
        Assert.Equal("12", works[0].TaskId);
        Assert.Equal("作业一", works[0].TaskName);
        Assert.Equal(3, works[0].ProblemCount);
        Assert.True(works[0].Submitted);

        using var test = Parse("""
            {"rows":[{"testid":"13","testName":"班测","questionCount":2}]}
            """);
        var tests = AscJson.ParseTasks(test.RootElement, AscProblemSource.Test);
        Assert.Single(tests);
        Assert.Equal("13", tests[0].TaskId);
        Assert.Equal(2, tests[0].ProblemCount);
    }

    [Fact]
    public void ParseTaskProblems_FiltersNonProgrammingAndReadsVerdicts()
    {
        using var doc = Parse("""
            {
              "records": [
                {"quesTypeId":5,"tkId":1,"displayId":"A1","title":"A+B","isDo":1,"subStatic":"答案正确"},
                {"quesTypeId":1,"tkId":2,"displayId":"Q2","title":"选择题"},
                {"quesTypeId":5,"displayId":"A3","title":"C","is_do":"1","subStatic":"超时"}
              ]
            }
            """);

        var problems = AscJson.ParseTaskProblems(doc.RootElement, AscProblemSource.Work);

        Assert.Equal(2, problems.Count);
        Assert.Equal("A1", problems[0].DisplayId);
        Assert.Equal("AC", problems[0].Verdict);
        Assert.True(problems[0].Done);
        Assert.Equal("TLE", problems[1].Verdict);
    }

    [Fact]
    public void ParseStatementOjDto_ReadsSamplesAndLimits()
    {
        using var doc = Parse("""
            {
              "_id":"A1131","title":"加法","description":"desc",
              "inputFormat":"in","outputFormat":"out","hint":"hint","titlePrompt":"prompt",
              "time_limit":1000,"memory_limit":256,
              "samples":[{"input":"2 3\n","output":"5\n"}]
            }
            """);

        var statement = AscJson.ParseStatementOjDto(doc.RootElement, "A1131", "fallback");

        Assert.Equal("A1131", statement.DisplayId);
        Assert.Equal("加法", statement.Title);
        Assert.Equal(1000, statement.TimeLimitMs);
        Assert.Equal(256, statement.MemoryLimitMb);
        Assert.Single(statement.Samples);
        Assert.Equal("2 3\n", statement.Samples[0].Input);
        Assert.Contains("题目描述", AscJson.StatementToMarkdown(statement));
    }

    [Fact]
    public void ParseStatementDetail_FallsBackToSingleSampleAndKeepsMarkdown()
    {
        using var doc = Parse("""
            {
              "displayId":"B7","title":"B","titleDescribe":"desc",
              "inputForm":"in","outputForm":"out",
              "all":"# B7 B\n\nraw",
              "input":"1\n","output":"2\n",
              "timeLimit":"2000","memoryLimit":"512"
            }
            """);

        var statement = AscJson.ParseStatementDetail(doc.RootElement, "B7", "B");

        Assert.Single(statement.Samples);
        Assert.Equal("1\n", statement.Samples[0].Input);
        Assert.Equal("# B7 B\n\nraw", statement.RawMarkdown);
        Assert.Equal(2000, statement.TimeLimitMs);
        Assert.Equal(512, statement.MemoryLimitMb);
    }

    [Fact]
    public void ParseSubmission_NormalizesVerdictAndJudgingFlag()
    {
        using var judging = Parse("""
            {"id":5,"subIsPass":7,"subStatic":"判题中","timeCost":10,"memoryCost":2048}
            """);
        var pending = AscJson.ParseSubmission(judging.RootElement);
        Assert.True(pending.Judging);
        Assert.False(pending.IsFinal);

        using var accepted = Parse("""
            {"id":6,"subIsPass":1,"subStatic":"答案正确","subScore":100,"timeCost":12,"memoryCost":4096}
            """);
        var done = AscJson.ParseSubmission(accepted.RootElement);
        Assert.False(done.Judging);
        Assert.Equal("AC", done.Verdict);
        Assert.True(done.IsFinal);
        Assert.True(done.Passed);
        Assert.Equal(100, done.Score);
    }

    [Fact]
    public void ParseSubmission_ReadsDecimalAndAlternativeTimeFields()
    {
        using var doc = Parse("""
            {"id":9,"subIsPass":1,"subStatic":"答案正确","timeCost":12.0,"memory_cost":512.0}
            """);

        var result = AscJson.ParseSubmission(doc.RootElement);

        Assert.Equal(12, result.TimeMs);
        Assert.Equal(512, result.MemoryBytes);
        Assert.NotEqual("网站未返回时间/内存", result.PerformanceText);
    }

    [Fact]
    public void ParseSubmission_ReadsAlternativeNames()
    {
        using var doc = Parse("""
            {"id":10,"subIsPass":1,"subStatic":"通过","timeUsed":"15","memoryUsed":"1024"}
            """);

        var result = AscJson.ParseSubmission(doc.RootElement);

        Assert.Equal(15, result.TimeMs);
        Assert.Equal(1024, result.MemoryBytes);
    }

    [Fact]
    public void ParseSubmission_ReadsCompileInfoCasePerformance()
    {
        using var doc = Parse("""
            {
              "subIsPass":1,"subStatic":"答案正确",
              "infoVo":{"data":[
                {"test_case":1,"feedback":"AC","real_time":8,"memory":524288},
                {"test_case":2,"feedback":"AC","real_time":15.0,"memory":1048576}
              ]}
            }
            """);

        var result = AscJson.ParseSubmission(doc.RootElement);

        Assert.Equal(15, result.TimeMs);
        Assert.Equal(1048576, result.MemoryBytes);
        Assert.Equal("15 ms · 1.0 MB", result.PerformanceText);
        Assert.Equal(2, result.Cases.Count);
        Assert.Equal(1, result.Cases[0].Index);
        Assert.Equal("AC", result.Cases[0].Verdict);
        Assert.Equal(8, result.Cases[0].TimeMs);
        Assert.Equal("15 ms · 1.0 MB", result.Cases[1].PerformanceText);
    }

    [Fact]
    public void ParseSubmission_KeepsLargerOfFlatAndCasePerformance()
    {
        using var doc = Parse("""
            {
              "subIsPass":1,"subStatic":"答案正确",
              "timeCost":12,"memoryCost":2097152,
              "infoVo":{"data":[{"real_time":30,"memory":1048576}]}
            }
            """);

        var result = AscJson.ParseSubmission(doc.RootElement);

        Assert.Equal(30, result.TimeMs);
        Assert.Equal(2097152, result.MemoryBytes);
    }

    [Fact]
    public void ParseRanking_SortsByTotalAndReadsProblemRows()
    {
        using var doc = Parse("""
            {
              "list":[
                {
                  "testChartsUser":"张三","testChartsScores":80,
                  "list":[
                    {"name":"A1","score":100,"maxScore":2635935},
                    {"name":"A2","score":60,"maxScore":null}
                  ]
                },
                {
                  "testChartsUser":"李四","testChartsScores":150,
                  "list":[
                    {"name":"A1","score":100,"maxScore":"2636000"},
                    {"name":"A2","score":50,"maxScore":"2636001"}
                  ]
                }
              ]
            }
            """);

        var rows = AscJson.ParseRanking(doc.RootElement);

        Assert.Equal(2, rows.Count);
        Assert.Equal(1, rows[0].Rank);
        Assert.Equal("李四", rows[0].UserName);
        Assert.Equal(150, rows[0].TotalScore);
        Assert.Equal(2, rows[0].Problems.Count);
        Assert.Equal("2636000", rows[0].Problems[0].BestSubmissionId);
        Assert.Equal(2, rows[1].Rank);
        Assert.Equal("张三", rows[1].UserName);
        Assert.Equal("2635935", rows[1].Problems[0].BestSubmissionId);
        Assert.Empty(rows[1].Problems[1].BestSubmissionId);
    }

    [Fact]
    public void ParseSubmissionRecords_ReadsAllSubmissions()
    {
        using var doc = Parse("""
            {
              "records":[
                {"id":2635935,"nickname":"张三","time":"2026-10-19 12:00","subStatic":"答案错误","subScore":50,"timeCost":12,"memoryCost":1048576,"codeLanguage":"C++"},
                {"id":2636000,"nickname":"张三","time":"2026-10-19 12:05","subStatic":"答案正确","subScore":100,"timeCost":15,"memoryCost":2097152,"codeLanguage":"C++"}
              ]
            }
            """);

        var records = AscJson.ParseSubmissionRecords(doc.RootElement);

        Assert.Equal(2, records.Count);
        Assert.Equal("张三", records[0].Nickname);
        Assert.Equal("WA", records[0].Verdict);
        Assert.Equal(12, records[0].TimeMs);
        Assert.Equal("AC", records[1].Verdict);
        Assert.Equal(100, records[1].Score);
    }

    [Theory]
    [InlineData("答案正确", "AC")]
    [InlineData("答案错误", "WA")]
    [InlineData("时间超限", "TLE")]
    [InlineData("空间超限", "MLE")]
    [InlineData("运行错误", "RE")]
    [InlineData("编译错误", "CE")]
    [InlineData("TLE (2001ms)", "TLE")]
    [InlineData("Accepted", "AC")]
    public void NormalizeVerdict_HandlesChineseAndEnglish(string raw, string expected)
    {
        Assert.Equal(expected, AscJson.NormalizeVerdict(raw));
    }

    [Fact]
    public void SubmissionIdFrom_UsesDataOrMessage()
    {
        using var data = Parse("12345");
        Assert.Equal("12345", AscJson.SubmissionIdFrom(data.RootElement, "ignored"));
        using var none = Parse("null");
        Assert.Equal("777", AscJson.SubmissionIdFrom(none.RootElement, " 777 "));
    }

    private static JsonDocument Parse(string json) => JsonDocument.Parse(json);
}
