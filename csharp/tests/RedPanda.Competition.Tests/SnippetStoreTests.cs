using RedPanda.Competition.Models;
using RedPanda.Competition.Services;

namespace RedPanda.Competition.Tests;

public sealed class SnippetStoreTests
{
    [Fact]
    public void Load_ReturnsDefaultsWhenFileIsMissing()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "snippets.json");

        try
        {
            var snippets = SnippetStore.Load(path);

            Assert.NotEmpty(snippets);
            Assert.Contains(snippets, snippet => snippet.Name == "并查集");
            Assert.All(snippets, snippet => Assert.False(string.IsNullOrWhiteSpace(snippet.Body)));
            Assert.All(snippets, snippet => Assert.False(string.IsNullOrWhiteSpace(snippet.Category)));
            Assert.Contains(snippets, snippet => snippet.Category == "图论");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SaveAndLoad_RoundTripsCustomSnippets()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "snippets.json");

        try
        {
            var custom = new List<CodeSnippet>
            {
                new()
                {
                    Id = "custom",
                    Name = "我的模板",
                    Prefix = "mine",
                    Body = "int main() { return 0; }\n"
                }
            };

            SnippetStore.Save(custom, path);
            var loaded = SnippetStore.Load(path);

            Assert.Single(loaded);
            Assert.Equal("custom", loaded[0].Id);
            Assert.Equal("我的模板", loaded[0].Name);
            Assert.Contains("int main", loaded[0].Body);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "RedPandaCPTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);
        return directory;
    }
}
