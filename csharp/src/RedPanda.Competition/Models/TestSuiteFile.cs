namespace RedPanda.Competition.Models;

public sealed class TestSuiteFile
{
    public int Version { get; set; } = 1;

    public List<TestCaseFile> Cases { get; set; } = new();
}

public sealed class TestCaseFile
{
    public string Name { get; set; } = string.Empty;

    public string Input { get; set; } = string.Empty;

    public string ExpectedOutput { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;
}
