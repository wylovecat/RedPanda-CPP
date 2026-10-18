using RedPanda.Competition.Models;
using RedPanda.Competition.Services;

namespace RedPanda.Competition.Tests;

public sealed class AscSettingsStoreTests
{
    [Fact]
    public void SaveLoad_RoundTripsSessionPreferencesButNeverPasswords()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "aisichuang.json");

        try
        {
            var settings = new AscSettings
            {
                BaseUrl = "https://example.test/tiku/",
                TaskRootDir = @"D:\Aisichuang",
                SubmitLanguage = "C++",
                EnableOnlineSubmit = true,
                LastPhone = "13800000000",
                LastClassId = "9",
                LastClassName = "算法班",
                LastTaskKind = 1,
                LastTaskId = "55",
                LastTaskName = "作业一",
                Session = new AscSession
                {
                    BaseUrl = "https://example.test/tiku",
                    Satoken = "token",
                    CsrfToken = "csrf",
                    SessionId = "sid",
                    UserId = "42",
                    Role = "teacher",
                    Phone = "13800000000"
                }
            };

            AscSettingsStore.Save(settings, path);
            var loaded = AscSettingsStore.Load(path);

            Assert.Equal("https://example.test/tiku", loaded.BaseUrl);
            Assert.Equal(@"D:\Aisichuang", loaded.TaskRootDir);
            Assert.True(loaded.EnableOnlineSubmit);
            Assert.Equal("token", loaded.Session.Satoken);
            Assert.Equal("teacher", loaded.Session.Role);
            Assert.Equal("55", loaded.LastTaskId);
            Assert.Equal(1, loaded.LastTaskKind);

            Assert.DoesNotContain(
                typeof(AscSettings).GetProperties(),
                property => property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Load_SanitizesInvalidBaseUrl()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "broken.json");

        try
        {
            File.WriteAllText(path, """{"BaseUrl":"not a url","Session":{}}""");

            var loaded = AscSettingsStore.Load(path);

            Assert.Equal(AscSettings.DefaultBaseUrl, loaded.BaseUrl);
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
