using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace InfoPanel.HomeAssistant.Core.Mapping;

public static partial class SlugHelper
{
    [GeneratedRegex(@"[^a-z0-9-]", RegexOptions.IgnoreCase)]
    private static partial Regex NonSlugRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    public static string ToSlug(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return "unknown";
        }

        string normalized = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (char c in normalized)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category != UnicodeCategory.NonSpacingMark &&
                (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || c is '.' or '_' or '-'))
            {
                sb.Append(c);
            }
        }

        string cleaned = sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        string dashed = WhitespaceRegex().Replace(cleaned, "-").Trim('-');
        dashed = dashed.Replace('.', '-').Replace('_', '-');
        while (dashed.Contains("--", StringComparison.Ordinal))
        {
            dashed = dashed.Replace("--", "-", StringComparison.Ordinal);
        }

        string slug = NonSlugRegex().Replace(dashed, string.Empty).Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "unknown" : slug;
    }

    public static string ToContainerId(string integration, string deviceName) =>
        ToSlug($"{integration}.{deviceName}");
}
