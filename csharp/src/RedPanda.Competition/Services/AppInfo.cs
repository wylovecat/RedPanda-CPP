using System.Reflection;

namespace RedPanda.Competition.Services;

public static class AppInfo
{
    public static string Version { get; } = ResolveVersion();

    public static string ProductName => "RedPanda CP · 算法竞赛版";

    private static string ResolveVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString(3) ?? "dev";
    }
}
