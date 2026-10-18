namespace RedPanda.Competition.Models;

public sealed record DiffLine(string Marker, string Text, string Kind)
{
    public const string Equal = "equal";
    public const string Inserted = "inserted";
    public const string Deleted = "deleted";
    public const string Note = "note";
}
