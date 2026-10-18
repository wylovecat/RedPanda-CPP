using System.Text;

namespace RedPanda.Competition.Services;

public static class FileEncodingService
{
    private static bool _providerRegistered;

    public static (string Text, string EncodingName) Read(string path)
    {
        var bytes = File.ReadAllBytes(path);

        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
        {
            return (new UTF32Encoding(false, true).GetString(bytes, 4, bytes.Length - 4), "UTF-32 LE");
        }

        if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
        {
            return (new UTF32Encoding(true, true).GetString(bytes, 4, bytes.Length - 4), "UTF-32 BE");
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return (Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), "UTF-8 BOM");
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return (Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), "UTF-16 LE");
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return (Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2), "UTF-16 BE");
        }

        try
        {
            var strictUtf8 = new UTF8Encoding(false, true);
            return (strictUtf8.GetString(bytes), "UTF-8");
        }
        catch (DecoderFallbackException)
        {
            RegisterCodePages();
            return (Encoding.GetEncoding(54936).GetString(bytes), "GB18030");
        }
    }

    public static void Write(string path, string text)
    {
        // The IDE always writes UTF-8 without BOM so g++ and online judges see
        // exactly the same bytes, regardless of the Windows system code page.
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    private static void RegisterCodePages()
    {
        if (_providerRegistered)
        {
            return;
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _providerRegistered = true;
    }
}
