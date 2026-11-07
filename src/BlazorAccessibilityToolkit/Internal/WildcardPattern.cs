using System.Text.RegularExpressions;

namespace BlazorAccessibilityToolkit.Internal;

/// <summary>Minimal <c>*</c> wildcard matcher (case-sensitive, ordinal).</summary>
internal static class WildcardPattern
{
    public static bool IsMatch(string? value, string pattern)
    {
        if (value is null)
        {
            return false;
        }

        if (!pattern.Contains('*', StringComparison.Ordinal))
        {
            return string.Equals(value, pattern, StringComparison.Ordinal);
        }

        var regex = "^" + string.Join(".*", pattern.Split('*').Select(Regex.Escape)) + "$";
        return Regex.IsMatch(value, regex, RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromSeconds(1));
    }
}
