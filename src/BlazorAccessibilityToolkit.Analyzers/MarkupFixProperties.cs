using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis.Text;

namespace BlazorAccessibilityToolkit.Analyzers;

/// <summary>A text edit that fixes a finding in Razor markup.</summary>
internal sealed class MarkupFix
{
    public MarkupFix(string title, string equivalenceKey, TextSpan span, string original, string newText)
    {
        Title = title;
        EquivalenceKey = equivalenceKey;
        Span = span;
        Original = original;
        NewText = newText;
    }

    /// <summary>The code action title, for example <c>Rename to 'aria-labelledby'</c>.</summary>
    public string Title { get; }

    /// <summary>Groups fixes of the same kind for "fix all", for example <c>BA11Y004</c>.</summary>
    public string EquivalenceKey { get; }

    /// <summary>The span of the file text that is replaced.</summary>
    public TextSpan Span { get; }

    /// <summary>The text the span had when the finding was reported. A fix is not applied when it changed since.</summary>
    public string Original { get; }

    public string NewText { get; }
}

/// <summary>
/// Carries fixes from the analyzer to the code fix provider in <see cref="Microsoft.CodeAnalysis.Diagnostic.Properties"/>.
/// The analyzer knows the markup structure; the code fix provider (a separate assembly, because the compiler does not
/// load the Workspaces assemblies) only applies the edits.
/// </summary>
internal static class MarkupFixProperties
{
    private const string Count = "BlazorA11y.FixCount";
    private const string Prefix = "BlazorA11y.Fix";

    /// <summary>The rules that can carry fixes.</summary>
    public static readonly ImmutableArray<string> FixableIds = ImmutableArray.Create(
        "BA11Y003", "BA11Y004", "BA11Y023", "BA11Y028", "BA11Y029", "BA11Y033", "BA11Y035", "BA11Y038");

    public static ImmutableDictionary<string, string?> Encode(IReadOnlyList<MarkupFix> fixes)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
        if (fixes.Count == 0)
        {
            return builder.ToImmutable();
        }

        builder[Count] = fixes.Count.ToString(CultureInfo.InvariantCulture);
        for (var i = 0; i < fixes.Count; i++)
        {
            var fix = fixes[i];
            var prefix = Prefix + i.ToString(CultureInfo.InvariantCulture) + ".";
            builder[prefix + "Title"] = fix.Title;
            builder[prefix + "Key"] = fix.EquivalenceKey;
            builder[prefix + "Start"] = fix.Span.Start.ToString(CultureInfo.InvariantCulture);
            builder[prefix + "Length"] = fix.Span.Length.ToString(CultureInfo.InvariantCulture);
            builder[prefix + "Original"] = fix.Original;
            builder[prefix + "Text"] = fix.NewText;
        }

        return builder.ToImmutable();
    }

    public static IReadOnlyList<MarkupFix> Decode(ImmutableDictionary<string, string?> properties)
    {
        if (!properties.TryGetValue(Count, out var countText) || !int.TryParse(countText, NumberStyles.None, CultureInfo.InvariantCulture, out var count))
        {
            return [];
        }

        var fixes = new List<MarkupFix>(count);
        for (var i = 0; i < count; i++)
        {
            var prefix = Prefix + i.ToString(CultureInfo.InvariantCulture) + ".";
            if (properties.TryGetValue(prefix + "Title", out var title) && title is not null
                && properties.TryGetValue(prefix + "Key", out var key) && key is not null
                && properties.TryGetValue(prefix + "Start", out var startText) && int.TryParse(startText, NumberStyles.None, CultureInfo.InvariantCulture, out var start)
                && properties.TryGetValue(prefix + "Length", out var lengthText) && int.TryParse(lengthText, NumberStyles.None, CultureInfo.InvariantCulture, out var length)
                && properties.TryGetValue(prefix + "Original", out var original) && original is not null
                && properties.TryGetValue(prefix + "Text", out var text) && text is not null)
            {
                fixes.Add(new MarkupFix(title, key, new TextSpan(start, length), original, text));
            }
        }

        return fixes;
    }

    /// <summary>Applies one fix, or returns null when the text no longer matches what the analyzer saw.</summary>
    public static SourceText? Apply(SourceText text, MarkupFix fix)
    {
        if (fix.Span.End > text.Length || text.ToString(fix.Span) != fix.Original)
        {
            return null;
        }

        return text.WithChanges(new TextChange(fix.Span, fix.NewText));
    }

    /// <summary>Applies several non-overlapping fixes (the first of overlapping ones wins) that still match the text.</summary>
    public static SourceText ApplyAll(SourceText text, IEnumerable<MarkupFix> fixes, ICollection<MarkupFix>? applied = null)
    {
        var changes = new List<TextChange>();
        var end = -1;
        foreach (var fix in fixes.Where(f => f.Span.End <= text.Length && text.ToString(f.Span) == f.Original).OrderBy(f => f.Span.Start))
        {
            if (fix.Span.Start < end || (fix.Span.Length == 0 && fix.Span.Start == end))
            {
                continue;
            }

            changes.Add(new TextChange(fix.Span, fix.NewText));
            applied?.Add(fix);
            end = fix.Span.End;
        }

        return changes.Count == 0 ? text : text.WithChanges(changes);
    }
}
