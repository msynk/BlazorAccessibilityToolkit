using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace BlazorAccessibilityToolkit;

/// <summary>WCAG conformance level.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WcagLevel>))]
public enum WcagLevel
{
    /// <summary>Level A.</summary>
    A = 1,

    /// <summary>Level AA.</summary>
    AA = 2,

    /// <summary>Level AAA.</summary>
    AAA = 3,
}

/// <summary>A WCAG 2.x success criterion.</summary>
public sealed record WcagReference
{
    /// <summary>Success criterion number, for example <c>1.4.3</c>.</summary>
    public required string Id { get; init; }

    /// <summary>Success criterion title, for example <c>Contrast (Minimum)</c>.</summary>
    public required string Title { get; init; }

    /// <summary>Conformance level.</summary>
    public required WcagLevel Level { get; init; }

    /// <summary>WCAG version that introduced the criterion (2.0, 2.1 or 2.2).</summary>
    public required string IntroducedIn { get; init; }

    /// <summary>Link to the W3C "Understanding" document.</summary>
    public required string Url { get; init; }

    /// <summary>True for criteria removed from WCAG 2.2 (4.1.1 Parsing).</summary>
    public bool IsObsolete { get; init; }

    /// <inheritdoc />
    public override string ToString() => $"{Id} {Title} ({Level})";
}

/// <summary>Catalog of WCAG 2.2 success criteria and helpers to map engine tags to criteria.</summary>
public static partial class WcagCatalog
{
    private static readonly FrozenDictionary<string, WcagReference> Criteria = Build();

    /// <summary>All success criteria of WCAG 2.2 (including the obsolete 4.1.1), ordered by number.</summary>
    public static IReadOnlyList<WcagReference> All { get; } = Criteria.Values.OrderBy(c => c, CriterionComparer.Instance).ToArray();

    /// <summary>Looks up a success criterion by number (for example <c>2.4.7</c>).</summary>
    public static WcagReference? Find(string id) => Criteria.GetValueOrDefault(id);

    /// <summary>Gets a success criterion by number, throwing when it does not exist.</summary>
    public static WcagReference Get(string id) =>
        Find(id) ?? throw new ArgumentException($"Unknown WCAG success criterion '{id}'.", nameof(id));

    /// <summary>
    /// Maps axe-core style tags (for example <c>wcag143</c>, <c>wcag1410</c>) to success criteria.
    /// Level tags such as <c>wcag2aa</c> are ignored.
    /// </summary>
    public static IReadOnlyList<WcagReference> FromTags(IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        var result = new List<WcagReference>();
        foreach (var tag in tags)
        {
            var match = CriterionTag().Match(tag);
            if (!match.Success)
            {
                continue;
            }

            var id = string.Create(CultureInfo.InvariantCulture, $"{match.Groups[1].Value}.{match.Groups[2].Value}.{int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture)}");
            if (Criteria.TryGetValue(id, out var criterion) && !result.Contains(criterion))
            {
                result.Add(criterion);
            }
        }

        result.Sort(CriterionComparer.Instance);
        return result;
    }

    [GeneratedRegex(@"^wcag(\d)(\d)(\d{1,2})$", RegexOptions.CultureInvariant)]
    private static partial Regex CriterionTag();

    private static FrozenDictionary<string, WcagReference> Build()
    {
        // id, slug, title, level, introduced in
        (string Id, string Slug, string Title, WcagLevel Level, string Version)[] rows =
        [
            ("1.1.1", "non-text-content", "Non-text Content", WcagLevel.A, "2.0"),
            ("1.2.1", "audio-only-and-video-only-prerecorded", "Audio-only and Video-only (Prerecorded)", WcagLevel.A, "2.0"),
            ("1.2.2", "captions-prerecorded", "Captions (Prerecorded)", WcagLevel.A, "2.0"),
            ("1.2.3", "audio-description-or-media-alternative-prerecorded", "Audio Description or Media Alternative (Prerecorded)", WcagLevel.A, "2.0"),
            ("1.2.4", "captions-live", "Captions (Live)", WcagLevel.AA, "2.0"),
            ("1.2.5", "audio-description-prerecorded", "Audio Description (Prerecorded)", WcagLevel.AA, "2.0"),
            ("1.2.6", "sign-language-prerecorded", "Sign Language (Prerecorded)", WcagLevel.AAA, "2.0"),
            ("1.2.7", "extended-audio-description-prerecorded", "Extended Audio Description (Prerecorded)", WcagLevel.AAA, "2.0"),
            ("1.2.8", "media-alternative-prerecorded", "Media Alternative (Prerecorded)", WcagLevel.AAA, "2.0"),
            ("1.2.9", "audio-only-live", "Audio-only (Live)", WcagLevel.AAA, "2.0"),
            ("1.3.1", "info-and-relationships", "Info and Relationships", WcagLevel.A, "2.0"),
            ("1.3.2", "meaningful-sequence", "Meaningful Sequence", WcagLevel.A, "2.0"),
            ("1.3.3", "sensory-characteristics", "Sensory Characteristics", WcagLevel.A, "2.0"),
            ("1.3.4", "orientation", "Orientation", WcagLevel.AA, "2.1"),
            ("1.3.5", "identify-input-purpose", "Identify Input Purpose", WcagLevel.AA, "2.1"),
            ("1.3.6", "identify-purpose", "Identify Purpose", WcagLevel.AAA, "2.1"),
            ("1.4.1", "use-of-color", "Use of Color", WcagLevel.A, "2.0"),
            ("1.4.2", "audio-control", "Audio Control", WcagLevel.A, "2.0"),
            ("1.4.3", "contrast-minimum", "Contrast (Minimum)", WcagLevel.AA, "2.0"),
            ("1.4.4", "resize-text", "Resize Text", WcagLevel.AA, "2.0"),
            ("1.4.5", "images-of-text", "Images of Text", WcagLevel.AA, "2.0"),
            ("1.4.6", "contrast-enhanced", "Contrast (Enhanced)", WcagLevel.AAA, "2.0"),
            ("1.4.7", "low-or-no-background-audio", "Low or No Background Audio", WcagLevel.AAA, "2.0"),
            ("1.4.8", "visual-presentation", "Visual Presentation", WcagLevel.AAA, "2.0"),
            ("1.4.9", "images-of-text-no-exception", "Images of Text (No Exception)", WcagLevel.AAA, "2.0"),
            ("1.4.10", "reflow", "Reflow", WcagLevel.AA, "2.1"),
            ("1.4.11", "non-text-contrast", "Non-text Contrast", WcagLevel.AA, "2.1"),
            ("1.4.12", "text-spacing", "Text Spacing", WcagLevel.AA, "2.1"),
            ("1.4.13", "content-on-hover-or-focus", "Content on Hover or Focus", WcagLevel.AA, "2.1"),
            ("2.1.1", "keyboard", "Keyboard", WcagLevel.A, "2.0"),
            ("2.1.2", "no-keyboard-trap", "No Keyboard Trap", WcagLevel.A, "2.0"),
            ("2.1.3", "keyboard-no-exception", "Keyboard (No Exception)", WcagLevel.AAA, "2.0"),
            ("2.1.4", "character-key-shortcuts", "Character Key Shortcuts", WcagLevel.A, "2.1"),
            ("2.2.1", "timing-adjustable", "Timing Adjustable", WcagLevel.A, "2.0"),
            ("2.2.2", "pause-stop-hide", "Pause, Stop, Hide", WcagLevel.A, "2.0"),
            ("2.2.3", "no-timing", "No Timing", WcagLevel.AAA, "2.0"),
            ("2.2.4", "interruptions", "Interruptions", WcagLevel.AAA, "2.0"),
            ("2.2.5", "re-authenticating", "Re-authenticating", WcagLevel.AAA, "2.0"),
            ("2.2.6", "timeouts", "Timeouts", WcagLevel.AAA, "2.1"),
            ("2.3.1", "three-flashes-or-below-threshold", "Three Flashes or Below Threshold", WcagLevel.A, "2.0"),
            ("2.3.2", "three-flashes", "Three Flashes", WcagLevel.AAA, "2.0"),
            ("2.3.3", "animation-from-interactions", "Animation from Interactions", WcagLevel.AAA, "2.1"),
            ("2.4.1", "bypass-blocks", "Bypass Blocks", WcagLevel.A, "2.0"),
            ("2.4.2", "page-titled", "Page Titled", WcagLevel.A, "2.0"),
            ("2.4.3", "focus-order", "Focus Order", WcagLevel.A, "2.0"),
            ("2.4.4", "link-purpose-in-context", "Link Purpose (In Context)", WcagLevel.A, "2.0"),
            ("2.4.5", "multiple-ways", "Multiple Ways", WcagLevel.AA, "2.0"),
            ("2.4.6", "headings-and-labels", "Headings and Labels", WcagLevel.AA, "2.0"),
            ("2.4.7", "focus-visible", "Focus Visible", WcagLevel.AA, "2.0"),
            ("2.4.8", "location", "Location", WcagLevel.AAA, "2.0"),
            ("2.4.9", "link-purpose-link-only", "Link Purpose (Link Only)", WcagLevel.AAA, "2.0"),
            ("2.4.10", "section-headings", "Section Headings", WcagLevel.AAA, "2.0"),
            ("2.4.11", "focus-not-obscured-minimum", "Focus Not Obscured (Minimum)", WcagLevel.AA, "2.2"),
            ("2.4.12", "focus-not-obscured-enhanced", "Focus Not Obscured (Enhanced)", WcagLevel.AAA, "2.2"),
            ("2.4.13", "focus-appearance", "Focus Appearance", WcagLevel.AAA, "2.2"),
            ("2.5.1", "pointer-gestures", "Pointer Gestures", WcagLevel.A, "2.1"),
            ("2.5.2", "pointer-cancellation", "Pointer Cancellation", WcagLevel.A, "2.1"),
            ("2.5.3", "label-in-name", "Label in Name", WcagLevel.A, "2.1"),
            ("2.5.4", "motion-actuation", "Motion Actuation", WcagLevel.A, "2.1"),
            ("2.5.5", "target-size-enhanced", "Target Size (Enhanced)", WcagLevel.AAA, "2.1"),
            ("2.5.6", "concurrent-input-mechanisms", "Concurrent Input Mechanisms", WcagLevel.AAA, "2.1"),
            ("2.5.7", "dragging-movements", "Dragging Movements", WcagLevel.AA, "2.2"),
            ("2.5.8", "target-size-minimum", "Target Size (Minimum)", WcagLevel.AA, "2.2"),
            ("3.1.1", "language-of-page", "Language of Page", WcagLevel.A, "2.0"),
            ("3.1.2", "language-of-parts", "Language of Parts", WcagLevel.AA, "2.0"),
            ("3.1.3", "unusual-words", "Unusual Words", WcagLevel.AAA, "2.0"),
            ("3.1.4", "abbreviations", "Abbreviations", WcagLevel.AAA, "2.0"),
            ("3.1.5", "reading-level", "Reading Level", WcagLevel.AAA, "2.0"),
            ("3.1.6", "pronunciation", "Pronunciation", WcagLevel.AAA, "2.0"),
            ("3.2.1", "on-focus", "On Focus", WcagLevel.A, "2.0"),
            ("3.2.2", "on-input", "On Input", WcagLevel.A, "2.0"),
            ("3.2.3", "consistent-navigation", "Consistent Navigation", WcagLevel.AA, "2.0"),
            ("3.2.4", "consistent-identification", "Consistent Identification", WcagLevel.AA, "2.0"),
            ("3.2.5", "change-on-request", "Change on Request", WcagLevel.AAA, "2.0"),
            ("3.2.6", "consistent-help", "Consistent Help", WcagLevel.A, "2.2"),
            ("3.3.1", "error-identification", "Error Identification", WcagLevel.A, "2.0"),
            ("3.3.2", "labels-or-instructions", "Labels or Instructions", WcagLevel.A, "2.0"),
            ("3.3.3", "error-suggestion", "Error Suggestion", WcagLevel.AA, "2.0"),
            ("3.3.4", "error-prevention-legal-financial-data", "Error Prevention (Legal, Financial, Data)", WcagLevel.AA, "2.0"),
            ("3.3.5", "help", "Help", WcagLevel.AAA, "2.0"),
            ("3.3.6", "error-prevention-all", "Error Prevention (All)", WcagLevel.AAA, "2.0"),
            ("3.3.7", "redundant-entry", "Redundant Entry", WcagLevel.A, "2.2"),
            ("3.3.8", "accessible-authentication-minimum", "Accessible Authentication (Minimum)", WcagLevel.AA, "2.2"),
            ("3.3.9", "accessible-authentication-enhanced", "Accessible Authentication (Enhanced)", WcagLevel.AAA, "2.2"),
            ("4.1.1", "parsing", "Parsing (obsolete in WCAG 2.2)", WcagLevel.A, "2.0"),
            ("4.1.2", "name-role-value", "Name, Role, Value", WcagLevel.A, "2.0"),
            ("4.1.3", "status-messages", "Status Messages", WcagLevel.AA, "2.1"),
        ];

        return rows.ToFrozenDictionary(
            r => r.Id,
            r => new WcagReference
            {
                Id = r.Id,
                Title = r.Title,
                Level = r.Level,
                IntroducedIn = r.Version,
                Url = $"https://www.w3.org/WAI/WCAG22/Understanding/{r.Slug}.html",
                IsObsolete = r.Id == "4.1.1",
            },
            StringComparer.Ordinal);
    }

    /// <summary>Orders criteria numerically (1.4.3 before 1.4.10).</summary>
    internal sealed class CriterionComparer : IComparer<WcagReference>
    {
        public static readonly CriterionComparer Instance = new();

        public int Compare(WcagReference? x, WcagReference? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            var a = x.Id.Split('.');
            var b = y.Id.Split('.');
            for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
            {
                var c = int.Parse(a[i], CultureInfo.InvariantCulture).CompareTo(int.Parse(b[i], CultureInfo.InvariantCulture));
                if (c != 0)
                {
                    return c;
                }
            }

            return a.Length.CompareTo(b.Length);
        }
    }
}
