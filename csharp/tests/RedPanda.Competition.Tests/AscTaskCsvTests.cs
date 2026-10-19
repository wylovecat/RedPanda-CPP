using RedPanda.Competition.Models;
using RedPanda.Competition.Services;

namespace RedPanda.Competition.Tests;

public sealed class AscTaskCsvTests
{
    [Fact]
    public void Build_WritesHeaderAndRowsWithEscaping()
    {
        var task = new AscTaskInfo
        {
            Source = AscProblemSource.Test,
            TaskId = "55",
            TaskName = "班测,第一场"
        };
        var classInfo = new AscClassInfo { ClassId = "9", ClassName = "算法\"A\"班" };
        var problems = new List<AscTaskProblem>
        {
            new()
            {
                DisplayId = "A1",
                Title = "A+B",
                Done = true,
                Verdict = "AC",
                Records = 2
            },
            new()
            {
                DisplayId = "A2",
                Title = "未做, 题",
                Done = false,
                Records = 0
            }
        };

        var csv = AscTaskCsv.Build(task, classInfo, problems);
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith("班级,任务类型", lines[0]);
        Assert.Contains("\"算法\"\"A\"\"班\"", lines[1]);
        Assert.Contains("\"班测,第一场\"", lines[1]);
        Assert.Contains("\"A1\"", lines[1]);
        Assert.Contains("\"AC\"", lines[1]);
        Assert.EndsWith(",2", lines[1]);
        Assert.Contains("\"未完成\"", lines[2]);
        Assert.Contains("\"未判题\"", lines[2]);
    }
}
