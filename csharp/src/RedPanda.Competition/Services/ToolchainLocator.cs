namespace RedPanda.Competition.Services;

public static class ToolchainLocator
{
    public static string? FindGpp(string? configuredPath)
    {
        var configured = NormalizeConfigured(configuredPath);
        if (configured is not null)
        {
            return configured;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var raw in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = raw.Trim().Trim('"');
            if (directory.Length == 0)
            {
                continue;
            }

            var candidate = Path.Combine(directory, "g++.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        foreach (var directory in EnumerateCandidateDirectories())
        {
            var candidate = Path.Combine(directory, "g++.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? NormalizeConfigured(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return null;
        }

        var value = configuredPath.Trim().Trim('"');

        try
        {
            if (File.Exists(value))
            {
                return Path.GetFullPath(value);
            }

            if (Directory.Exists(value))
            {
                var candidate = Path.Combine(value, "g++.exe");
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }
        catch
        {
            // A stale setting is treated as "not configured".
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCandidateDirectories()
    {
        yield return @"C:\mingw64\bin";
        yield return @"C:\msys64\mingw64\bin";
        yield return @"C:\ProgramData\mingw64\bin";

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrEmpty(programFiles))
        {
            yield return Path.Combine(programFiles, "RedPanda-CPP", "MinGW64", "bin");
            yield return Path.Combine(programFiles, "Red Panda Dev-C++", "MinGW64", "bin");
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrEmpty(programFilesX86))
        {
            yield return Path.Combine(programFilesX86, "RedPanda-CPP", "MinGW64", "bin");
            yield return Path.Combine(programFilesX86, "Red Panda Dev-C++", "MinGW64", "bin");
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(localAppData))
        {
            yield return Path.Combine(localAppData, "Programs", "RedPanda-CPP", "MinGW64", "bin");
            yield return Path.Combine(localAppData, "RedPanda-CPP", "MinGW64", "bin");
            yield return Path.Combine(localAppData, "Programs", "Red Panda Dev-C++", "MinGW64", "bin");
        }

        yield return Path.Combine(AppContext.BaseDirectory, "MinGW64", "bin");
        yield return Path.Combine(AppContext.BaseDirectory, "mingw64", "bin");

        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch
        {
            yield break;
        }

        foreach (var drive in drives)
        {
            if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
            {
                continue;
            }

            var root = drive.RootDirectory.FullName;
            yield return Path.Combine(root, "RedPanda-CPP", "MinGW64", "bin");
            yield return Path.Combine(root, "RedPandaCP", "MinGW64", "bin");
            yield return Path.Combine(root, "mingw64", "bin");
            yield return Path.Combine(root, "msys64", "mingw64", "bin");
        }
    }
}
