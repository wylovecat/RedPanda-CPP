using RedPanda.Competition.Services;

namespace RedPanda.Competition.Tests;

public sealed class OutputComparerTests
{
    [Theory]
    [InlineData("1 2\r\n", "1 2\n", true)]
    [InlineData("1 2   \n3\t\n\n", "1 2\n3", true)]
    [InlineData("", "\r\n\r\n", true)]
    [InlineData("1 2", "1 3", false)]
    public void AreEqual_NormalizesLineEndingsAndTrailingWhitespace(
        string expected,
        string actual,
        bool shouldMatch)
    {
        Assert.Equal(shouldMatch, OutputComparer.AreEqual(expected, actual));
    }

    [Fact]
    public void Normalize_KeepsMeaningfulBlankLines()
    {
        Assert.Equal("1\n\n2", OutputComparer.Normalize("1\r\n\r\n2\r\n\r\n"));
    }

    [Fact]
    public void DescribeDifference_ReportsFirstDifferentLine()
    {
        var message = OutputComparer.DescribeDifference("1\n2\n3", "1\n9\n3");

        Assert.Contains("第 2 行", message);
        Assert.Contains("\"2\"", message);
        Assert.Contains("\"9\"", message);
    }

    [Fact]
    public void DescribeDifference_HandlesExtraActualLine()
    {
        var message = OutputComparer.DescribeDifference("1", "1\n2");

        Assert.Contains("第 2 行", message);
        Assert.Contains("期望输出已结束", message);
    }
}
