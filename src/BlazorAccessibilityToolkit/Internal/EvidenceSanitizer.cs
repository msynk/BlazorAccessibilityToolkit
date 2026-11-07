using System.Text.RegularExpressions;

namespace BlazorAccessibilityToolkit.Internal;

/// <summary>Redacts form values and bounds the size of diagnostic payloads.</summary>
internal static partial class EvidenceSanitizer
{
    internal const string Redacted = "[redacted]";

    /// <summary>Redacts form values (when enabled) and truncates.</summary>
    public static string? Sanitize(string? html, bool redact, int maxLength)
    {
        if (string.IsNullOrEmpty(html))
        {
            return html;
        }

        var result = html;
        if (redact)
        {
            result = RedactFormValues(result);
        }

        return Truncate(result, maxLength);
    }

    /// <summary>
    /// Replaces <c>value</c> attributes of input/option/select elements and the content of textareas.
    /// Button-like inputs keep their value because it is their visible label.
    /// </summary>
    public static string RedactFormValues(string html)
    {
        var withoutValues = InputTag().Replace(html, match =>
        {
            var tag = match.Value;
            var type = TypeAttribute().Match(tag);
            if (type.Success && IsLabelValueType(type.Groups["v"].Value.Trim('"', '\'')))
            {
                return tag;
            }

            return ValueAttribute().Replace(tag, m => $"{m.Groups["name"].Value}\"{Redacted}\"");
        });

        return TextareaContent().Replace(withoutValues, m => m.Groups["open"].Value + Redacted + m.Groups["close"].Value);
    }

    /// <summary>Truncates text to <paramref name="maxLength"/> characters, appending an ellipsis marker.</summary>
    public static string? Truncate(string? text, int maxLength)
    {
        if (text is null || maxLength <= 0 || text.Length <= maxLength)
        {
            return text;
        }

        return string.Concat(text.AsSpan(0, Math.Max(0, maxLength - 1)), "…");
    }

    private static bool IsLabelValueType(string type) =>
        type.Equals("submit", StringComparison.OrdinalIgnoreCase)
        || type.Equals("button", StringComparison.OrdinalIgnoreCase)
        || type.Equals("reset", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"<(input|option)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InputTag();

    [GeneratedRegex(@"\stype\s*=\s*(?<v>""[^""]*""|'[^']*'|[^\s>]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TypeAttribute();

    [GeneratedRegex(@"(?<name>\svalue\s*=\s*)(""[^""]*""|'[^']*'|[^\s>]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ValueAttribute();

    [GeneratedRegex(@"(?<open><textarea\b[^>]*>)(?s:[^<]+?)(?<close></textarea>|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TextareaContent();
}
