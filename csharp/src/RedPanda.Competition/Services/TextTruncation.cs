namespace RedPanda.Competition.Services;

public static class TextTruncation
{
    public static string ForDisplay(string? text, int limitInCharacters)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        if (limitInCharacters <= 0 || text.Length <= limitInCharacters)
        {
            return text;
        }

        return text[..limitInCharacters] +
               Environment.NewLine +
               $"…（界面显示已截断，完整长度 {text.Length:N0} 字符）";
    }
}
