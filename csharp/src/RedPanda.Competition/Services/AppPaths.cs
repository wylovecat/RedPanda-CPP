namespace RedPanda.Competition.Services;

public static class AppPaths
{
    /// <summary>
    /// Portable builds are marked by a "portable.ini" next to the executable.
    /// In that mode settings, recovery data and build artifacts all stay in the
    /// application folder, which is what school labs usually want.
    /// </summary>
    public static bool IsPortable { get; } = File.Exists(
        Path.Combine(AppContext.BaseDirectory, "portable.ini"));

    public static string AppDataDirectory { get; } = IsPortable
        ? Path.Combine(AppContext.BaseDirectory, "data")
        : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RedPandaCP");

    public static string LocalDataDirectory { get; } = IsPortable
        ? Path.Combine(AppContext.BaseDirectory, "data")
        : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RedPandaCP");

    public static string SettingsFile => Path.Combine(AppDataDirectory, "settings.json");

    public static string BuildCacheDirectory => Path.Combine(LocalDataDirectory, "build");

    public static string RecoveryFile => Path.Combine(LocalDataDirectory, "recovery", "draft.json");

    public static string MakeBuildDirectory(string sourcePath)
    {
        var normalized = Path.GetFullPath(sourcePath).ToUpperInvariant();
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(normalized)))[..16];

        var dir = Path.Combine(BuildCacheDirectory, hash.ToLowerInvariant());
        Directory.CreateDirectory(dir);
        return dir;
    }
}
