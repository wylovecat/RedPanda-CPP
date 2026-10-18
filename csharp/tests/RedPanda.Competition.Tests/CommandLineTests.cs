using RedPanda.Competition.Services;

namespace RedPanda.Competition.Tests;

public sealed class CommandLineTests
{
    [Fact]
    public void Split_HandlesQuotesAndSpaces()
    {
        var arguments = CommandLineSplitter.Split(
            "-DONLINE_JUDGE -Wl,--stack=67108864 \"C:\\my dir\\extra.cpp\" -O2");

        Assert.Equal(4, arguments.Count);
        Assert.Equal("C:\\my dir\\extra.cpp", arguments[2]);
    }

    [Fact]
    public void Format_QuotesArgumentsWhenNeeded()
    {
        var text = CommandLineSplitter.Format(
            @"C:\mingw64\bin\g++.exe",
            new[] { "-std=c++17", @"C:\题目\A B.cpp" });

        Assert.Contains("\"C:\\题目\\A B.cpp\"", text);
    }

    [Fact]
    public void ToolchainLocator_FindsCompilerOnThisMachine()
    {
        var gpp = ToolchainLocator.FindGpp(null);

        Assert.NotNull(gpp);
        Assert.True(File.Exists(gpp));
    }
}
