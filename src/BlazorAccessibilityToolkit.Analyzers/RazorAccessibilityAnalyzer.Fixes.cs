using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace BlazorAccessibilityToolkit.Analyzers;

public sealed partial class RazorAccessibilityAnalyzer
{
    private static readonly Regex PageRoute = new("^[ \\t]*@page[ \\t]+\"([^\"]*)\"", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>
    /// The fixes for a finding, when there is one safe edit (or a short list of equally safe choices, such as
    /// <c>current-password</c> or <c>new-password</c>). Most findings have none: adding alt text or a label needs a person.
    /// </summary>
    internal static IReadOnlyList<MarkupFix> FixesFor(DiagnosticDescriptor descriptor, RazorElement? element, TextSpan span, object[] args, string markup)
    {
        // The zoom finding points at the <meta> tag; the fix edits its content attribute.
        if (descriptor.Id == "BA11Y029" && element?.Attribute("content") is { IsDynamic: false, Value: { } content } contentAttribute)
        {
            var allowed = string.Join(", ", content.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).Where(p => p.Length > 0 && ViewportProblem(p) is null));
            return One(ReplaceValue(markup, contentAttribute, allowed.Length == 0 ? "width=device-width" : allowed, "Allow zooming", descriptor.Id));
        }

        var attribute = element?.Attributes.FirstOrDefault(a => a.Span == span);
        if (element is null || attribute is null)
        {
            return [];
        }

        var id = descriptor.Id;
        switch (id)
        {
            case "BA11Y003":
                return One(ReplaceValue(markup, attribute, "0", "Change to tabindex=\"0\"", id));

            case "BA11Y004" when Suggest(attribute.Name) is { } suggestion && !element.Has(suggestion):
                var written = markup.Substring(attribute.Span.Start, attribute.Span.Length);
                return [new MarkupFix($"Rename to '{suggestion}'", id, attribute.Span, written, suggestion + written.Substring(attribute.Name.Length))];

            case "BA11Y023" when attribute.Value is { } role && !role.Trim().Any(char.IsWhiteSpace):
                return [RemoveAttribute(markup, attribute, $"Remove role=\"{attribute.Value}\"", id)];

            case "BA11Y028" when attribute.Value is { } lang:
                var tag = lang.Trim().Replace('_', '-');
                return IsValidLanguageTag(tag) ? One(ReplaceValue(markup, attribute, tag, $"Change to lang=\"{tag}\"", id)) : [];

            case "BA11Y033" when attribute.Name == "autocomplete":
                return new[] { "current-password", "new-password" }
                    .Select(token => ReplaceValue(markup, attribute, token, $"Change to autocomplete=\"{token}\"", id + ":" + token))
                    .OfType<MarkupFix>()
                    .ToArray();

            case "BA11Y033" when attribute.Name == "@onpaste:preventDefault"
                || (attribute.Name == "onpaste" && attribute.Value?.Trim().TrimEnd(';').Trim() == "return false"):
                return [RemoveAttribute(markup, attribute, "Allow pasting", id)];

            case "BA11Y035" when args.Length > 2 && args[2] is string expression:
                return One(ReplaceValue(markup, attribute, $"@({expression} ? \"true\" : \"false\")", "Render \"true\" or \"false\"", id));

            case "BA11Y038" when attribute.Value is { } fragment:
                var routes = PageRoute.Matches(markup).Cast<Match>().Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToArray();
                if (routes.Length != 1 || !routes[0].StartsWith("/", StringComparison.Ordinal) || routes[0].IndexOf('{') >= 0 || routes[0].IndexOf('#') >= 0)
                {
                    return [];
                }

                var href = routes[0] + fragment.Trim();
                return One(ReplaceValue(markup, attribute, href, $"Change to href=\"{href}\"", id));

            default:
                return [];
        }
    }

    /// <summary>Includes the fixes in the diagnostic properties. Exposed for tests.</summary>
    internal static Diagnostic CreateDiagnostic(DiagnosticDescriptor descriptor, Location location, IReadOnlyList<MarkupFix> fixes, object[] args) =>
        Diagnostic.Create(descriptor, location, MarkupFixProperties.Encode(fixes), args);

    private static IReadOnlyList<MarkupFix> One(MarkupFix? fix) => fix is null ? [] : [fix];

    // Replaces the attribute value, keeping the quotes as written (an unquoted value gets double quotes). The edit covers
    // the whole attribute, so that a fix is not applied to a different attribute after the text moved.
    private static MarkupFix? ReplaceValue(string markup, RazorAttribute attribute, string value, string title, string key)
    {
        var i = attribute.Span.Start + attribute.Name.Length;
        while (i < attribute.Span.End && char.IsWhiteSpace(markup[i]))
        {
            i++;
        }

        if (i >= attribute.Span.End || markup[i] != '=')
        {
            return null;
        }

        i++;
        while (i < attribute.Span.End && char.IsWhiteSpace(markup[i]))
        {
            i++;
        }

        string replacement;
        if (i < attribute.Span.End && markup[i] is '"' or '\'' && attribute.Span.Length >= 2 && markup[attribute.Span.End - 1] == markup[i] && attribute.Span.End - 1 > i)
        {
            var quote = markup[i];
            if (value.IndexOf(quote) >= 0 && !value.StartsWith("@(", StringComparison.Ordinal))
            {
                return null;
            }

            replacement = markup.Substring(attribute.Span.Start, i + 1 - attribute.Span.Start) + value + quote;
        }
        else
        {
            replacement = markup.Substring(attribute.Span.Start, i - attribute.Span.Start) + "\"" + value + "\"";
        }

        return new MarkupFix(title, key, attribute.Span, markup.Substring(attribute.Span.Start, attribute.Span.Length), replacement);
    }

    // Removes the attribute with the white space before it, so that no double spaces or blank lines are left.
    private static MarkupFix RemoveAttribute(string markup, RazorAttribute attribute, string title, string key)
    {
        var start = attribute.Span.Start;
        while (start > 0 && char.IsWhiteSpace(markup[start - 1]))
        {
            start--;
        }

        var span = TextSpan.FromBounds(start, attribute.Span.End);
        return new MarkupFix(title, key, span, markup.Substring(span.Start, span.Length), string.Empty);
    }
}
