namespace RedPanda.Competition.Models;

/// <summary>
/// One open document. Unsaved text and untitled test cases are kept in memory
/// while the tab is inactive, so switching tabs never silently discards work.
/// </summary>
public sealed class DocumentTab
{
    public string? Path { get; set; }

    public string UntitledName { get; set; } = "未命名.cpp";

    public bool IsDirty { get; set; }

    public string? PendingText { get; set; }

    public string PendingEncoding { get; set; } = "UTF-8";

    public List<TestCase>? PendingTests { get; set; }

    public int CaretOffset { get; set; }

    public string DisplayName
    {
        get
        {
            var name = Path is null
                ? UntitledName
                : System.IO.Path.GetFileName(Path);
            return IsDirty ? "*" + name : name;
        }
    }

    public override string ToString() => DisplayName;
}
