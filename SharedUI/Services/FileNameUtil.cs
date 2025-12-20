using System.Text;
using System.Text.RegularExpressions;

namespace SharedUI.Services;

public static class FileNameUtil
{
    public static string GetSafeBaseName(string? fileNameOrUrl, string fallbackBaseName = "image")
    {
        if (string.IsNullOrWhiteSpace(fileNameOrUrl))
            return fallbackBaseName;

        // Handle both file names and URL-ish strings.
        var name = fileNameOrUrl.Replace('\\', '/');
        var lastSlash = name.LastIndexOf('/');
        if (lastSlash >= 0)
            name = name[(lastSlash + 1)..];

        var queryOrHash = name.IndexOfAny(['?', '#']);
        if (queryOrHash >= 0)
            name = name[..queryOrHash];

        var lastDot = name.LastIndexOf('.');
        if (lastDot > 0)
            name = name[..lastDot];

        return SanitizeFileName(name, fallbackBaseName);
    }

    public static string GetSafeFileName(string? suggestedFileName, string defaultFileName, string requiredExtension)
    {
        var defaultBase = GetSafeBaseName(defaultFileName, "image");
        var baseName = GetSafeBaseName(suggestedFileName, defaultBase);
        return baseName + requiredExtension;
    }

    public static string SanitizeFileName(string? name, string fallback = "image")
    {
        if (string.IsNullOrWhiteSpace(name))
            return fallback;

        // Be defensive across platforms: always treat both separators as invalid.
        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars()) { '/', '\\' };

        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            sb.Append(invalid.Contains(ch) ? '_' : ch);
        }

        var cleaned = sb.ToString().Trim();
        cleaned = cleaned.Trim('.');
        cleaned = Regex.Replace(cleaned, "_{2,}", "_");

        return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
    }
}
