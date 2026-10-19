using System.Text.Json.Serialization;

namespace RedPanda.Competition.Models;

public sealed class CodeSnippet
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public string Prefix { get; set; } = string.Empty;

    public string Category { get; set; } = "常用";

    public string Body { get; set; } = string.Empty;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public string DisplayText => string.IsNullOrWhiteSpace(Prefix)
        ? $"{Name}  ·  {Category}"
        : $"{Name}  ·  {Prefix}  ·  {Category}";
}
