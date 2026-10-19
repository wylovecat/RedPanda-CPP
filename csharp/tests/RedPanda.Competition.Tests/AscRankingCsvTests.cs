using RedPanda.Competition.Models;
using RedPanda.Competition.Services;

namespace RedPanda.Competition.Tests;

public sealed class AscRankingCsvTests
{
    [Fact]
    public void Build_WritesDynamicProblemColumns()
    {
        var task = new AscTaskInfo
        {
            Source = AscProblemSource.Work,
            TaskId = "55",
            TaskName = "作业,一"
        };
        var classInfo = new AscClassInfo { ClassId = "9", ClassName = "算法班" };
        var rows = new List<AscRankingRow>
        {
            new()
            {
                Rank = 1,
                UserName = "张三",
                TotalScore = 150,
                Problems =
                {
                    new AscRankingProblem { Name = "A1", Score = 100 },
                    new AscRankingProblem { Name = "A2", Score = 50 }
                }
            },
            new()
            {
                Rank = 2,
                UserName = "李\"四",
                TotalScore = 80,
                Problems =
                {
                    new AscRankingProblem { Name = "A1", Score = 80 }
                }
            }
        };

        var csv = AscRankingCsv.Build(task, classInfo, rows);
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains("名次,用户,总分", lines[0]);
        Assert.Contains("\"A1\"", lines[0]);
        Assert.Contains("\"A2\"", lines[0]);
        Assert.Contains("\"李\"\"四\"", lines[2]);
        Assert.Contains("150", lines[1]);
        Assert.Contains(",80,0", lines[2]);
    }
}
