using System.Xml;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;

namespace RedPanda.Competition.Services;

public static class SyntaxHighlightingService
{
    private static IHighlightingDefinition? _light;
    private static IHighlightingDefinition? _dark;

    public static IHighlightingDefinition GetCppDefinition(bool dark)
    {
        if (dark)
        {
            return _dark ??= Load("CppDark.xshd");
        }

        return _light ??= Load("CppLight.xshd");
    }

    private static IHighlightingDefinition Load(string fileName)
    {
        var assembly = typeof(SyntaxHighlightingService).Assembly;
        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
        {
            throw new InvalidOperationException($"缺少语法高亮资源：{fileName}");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"无法打开语法高亮资源：{fileName}");
        using var reader = XmlReader.Create(stream);

        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }
}
