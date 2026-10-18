using RedPanda.Competition.Models;
using RedPanda.Competition.Services;

namespace RedPanda.Competition.Tests;

public sealed class DiffServiceTests
{
    [Fact]
    public void Build_IgnoresTrailingWhitespaceAndTrailingBlankLines()
    {
        var lines = DiffService.Build("1 2\n", "1 2   \r\n\r\n", 64 * 1024);

        Assert.Contains(lines, line => line.Kind == DiffLine.Equal && line.Text.Contains("输出一致"));
        Assert.DoesNotContain(lines, line => line.Kind == DiffLine.Deleted || line.Kind == DiffLine.Inserted);
    }

    [Fact]
    public void Build_ShowsDeletedAndInsertedLines()
    {
        var lines = DiffService.Build("1\n2\n3", "1\n9\n3", 64 * 1024);

        Assert.Contains(lines, line => line.Kind == DiffLine.Deleted && line.Text == "2");
        Assert.Contains(lines, line => line.Kind == DiffLine.Inserted && line.Text == "9");
    }

    [Fact]
    public void Build_NotesTruncationForLargeInput()
    {
        var expected = string.Join('\n', Enumerable.Range(0, 100).Select(i => "a" + i));
        var actual = string.Join('\n', Enumerable.Range(0, 100).Select(i => "b" + i));

        var lines = DiffService.Build(expected, actual, 32);

        Assert.Contains(lines, line => line.Text.Contains("截断"));
    }

    [Fact]
    public void TextTruncation_KeepsShortTextUnchanged()
    {
        Assert.Equal("abc", TextTruncation.ForDisplay("abc", 10));
        Assert.Equal(string.Empty, TextTruncation.ForDisplay(null, 10));
    }

    [Fact]
    public void TextTruncation_AddsNoteForLongText()
    {
        var result = TextTruncation.ForDisplay(new string('x', 20), 5);

        Assert.StartsWith("xxxxx", result);
        Assert.Contains("截断", result);
    }
}
