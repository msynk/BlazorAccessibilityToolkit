using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlazorAccessibilityToolkit.Reporting;

namespace BlazorAccessibilityToolkit.ManualReview;

/// <summary>Status of a manual check.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ManualReviewStatus>))]
public enum ManualReviewStatus
{
    /// <summary>Nobody has tested this yet.</summary>
    NotTested,

    /// <summary>Flagged for review (for example after a change), or recorded incompletely.</summary>
    RequiresReview,

    /// <summary>A person verified the criterion. Requires tester and date.</summary>
    Passed,

    /// <summary>A person found a failure.</summary>
    Failed,

    /// <summary>The criterion does not apply. Requires a justification.</summary>
    NotApplicable,
}

/// <summary>One manual check in a checklist.</summary>
public sealed record ManualReviewItem
{
    /// <summary>Stable identifier (for example <c>focus-visible</c>).</summary>
    public required string Id { get; init; }

    /// <summary>What to verify.</summary>
    public required string Title { get; init; }

    /// <summary>How to verify it.</summary>
    public string? Guidance { get; init; }

    /// <summary>Related WCAG success criteria ids.</summary>
    public IReadOnlyList<string> Wcag { get; init; } = [];

    /// <summary>Recorded status.</summary>
    public ManualReviewStatus Status { get; init; } = ManualReviewStatus.NotTested;

    /// <summary>Who performed the check.</summary>
    public string? Tester { get; init; }

    /// <summary>When the check was performed.</summary>
    public DateOnly? Date { get; init; }

    /// <summary>Observations.</summary>
    public string? Notes { get; init; }

    /// <summary>Links or paths to evidence (recordings, screenshots, issue links).</summary>
    public IReadOnlyList<string> Evidence { get; init; } = [];

    /// <summary>Why the criterion does not apply (required for <see cref="ManualReviewStatus.NotApplicable"/>).</summary>
    public string? Justification { get; init; }

    /// <summary>Assistive technology / browser combinations used (for example <c>NVDA 2026.1 + Firefox</c>).</summary>
    public IReadOnlyList<string> Environments { get; init; } = [];
}

/// <summary>A manual-review checklist for a component, page or release.</summary>
public sealed record ManualReviewChecklist
{
    /// <summary>Schema version of the checklist file.</summary>
    public string SchemaVersion { get; init; } = "1.0";

    /// <summary>Checklist name.</summary>
    public required string Name { get; init; }

    /// <summary>What the checklist covers (component type, page URL, feature).</summary>
    public string? Scope { get; init; }

    /// <summary>The checks.</summary>
    public IReadOnlyList<ManualReviewItem> Items { get; init; } = [];
}

/// <summary>A problem with how a manual check was recorded.</summary>
public sealed record ManualReviewValidationIssue(string ItemId, string Message);

/// <summary>Counts of manual checks by effective status.</summary>
public sealed record ManualReviewCoverage
{
    /// <summary>Total checks.</summary>
    public int Total { get; init; }

    /// <summary>Checks not tested.</summary>
    public int NotTested { get; init; }

    /// <summary>Checks requiring review (including incompletely recorded ones).</summary>
    public int RequiresReview { get; init; }

    /// <summary>Checks verified as passing.</summary>
    public int Passed { get; init; }

    /// <summary>Checks that failed.</summary>
    public int Failed { get; init; }

    /// <summary>Checks justified as not applicable.</summary>
    public int NotApplicable { get; init; }

    /// <summary>Checks still outstanding (not tested or requiring review).</summary>
    [JsonIgnore]
    public int Outstanding => NotTested + RequiresReview;
}

/// <summary>Loading, validation and interpretation of manual-review checklists.</summary>
public static class ManualReviewEvaluator
{
    /// <summary>Engine name used for findings derived from manual reviews.</summary>
    public const string EngineName = "manual-review";

    /// <summary>
    /// The status the toolkit trusts. Incompletely recorded results are downgraded to <see cref="ManualReviewStatus.RequiresReview"/>:
    /// a pass without tester and date, or "not applicable" without justification, never counts.
    /// </summary>
    public static ManualReviewStatus EffectiveStatus(ManualReviewItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Status switch
        {
            ManualReviewStatus.Passed when string.IsNullOrWhiteSpace(item.Tester) || item.Date is null => ManualReviewStatus.RequiresReview,
            ManualReviewStatus.NotApplicable when string.IsNullOrWhiteSpace(item.Justification) => ManualReviewStatus.RequiresReview,
            _ => item.Status,
        };
    }

    /// <summary>Finds incompletely recorded checks.</summary>
    public static IReadOnlyList<ManualReviewValidationIssue> Validate(ManualReviewChecklist checklist)
    {
        ArgumentNullException.ThrowIfNull(checklist);
        var issues = new List<ManualReviewValidationIssue>();
        foreach (var group in checklist.Items.GroupBy(i => i.Id).Where(g => g.Count() > 1))
        {
            issues.Add(new(group.Key, "Duplicate item id."));
        }

        foreach (var item in checklist.Items)
        {
            if (item.Status == ManualReviewStatus.Passed && string.IsNullOrWhiteSpace(item.Tester))
            {
                issues.Add(new(item.Id, "Marked Passed without a tester; treated as RequiresReview."));
            }

            if (item.Status == ManualReviewStatus.Passed && item.Date is null)
            {
                issues.Add(new(item.Id, "Marked Passed without a date; treated as RequiresReview."));
            }

            if (item.Status == ManualReviewStatus.NotApplicable && string.IsNullOrWhiteSpace(item.Justification))
            {
                issues.Add(new(item.Id, "Marked NotApplicable without a justification; treated as RequiresReview."));
            }

            if (item.Status == ManualReviewStatus.Failed && string.IsNullOrWhiteSpace(item.Notes))
            {
                issues.Add(new(item.Id, "Marked Failed without notes describing the failure."));
            }

            foreach (var id in item.Wcag.Where(id => WcagCatalog.Find(id) is null))
            {
                issues.Add(new(item.Id, $"Unknown WCAG success criterion '{id}'."));
            }
        }

        return issues;
    }

    /// <summary>Counts checks by effective status.</summary>
    public static ManualReviewCoverage Coverage(IEnumerable<ManualReviewChecklist> checklists)
    {
        ArgumentNullException.ThrowIfNull(checklists);
        var statuses = checklists.SelectMany(c => c.Items).Select(EffectiveStatus).ToArray();
        return new ManualReviewCoverage
        {
            Total = statuses.Length,
            NotTested = statuses.Count(s => s == ManualReviewStatus.NotTested),
            RequiresReview = statuses.Count(s => s == ManualReviewStatus.RequiresReview),
            Passed = statuses.Count(s => s == ManualReviewStatus.Passed),
            Failed = statuses.Count(s => s == ManualReviewStatus.Failed),
            NotApplicable = statuses.Count(s => s == ManualReviewStatus.NotApplicable),
        };
    }

    /// <summary>
    /// Converts outstanding and failed checks into findings: outstanding checks become
    /// <see cref="FindingStatus.ManualReviewRequired"/>, failed checks become <see cref="FindingStatus.Violation"/>.
    /// Passed and not-applicable checks produce no finding.
    /// </summary>
    public static IReadOnlyList<AccessibilityFinding> ToFindings(ManualReviewChecklist checklist)
    {
        ArgumentNullException.ThrowIfNull(checklist);
        var findings = new List<AccessibilityFinding>();
        foreach (var item in checklist.Items)
        {
            var status = EffectiveStatus(item);
            if (status is ManualReviewStatus.Passed or ManualReviewStatus.NotApplicable)
            {
                continue;
            }

            var wcag = item.Wcag.Select(WcagCatalog.Find).OfType<WcagReference>().ToArray();
            findings.Add(new AccessibilityFinding
            {
                RuleId = "manual:" + item.Id,
                Engine = EngineName,
                Status = status == ManualReviewStatus.Failed ? FindingStatus.Violation : FindingStatus.ManualReviewRequired,
                Description = item.Title,
                Help = status == ManualReviewStatus.Failed ? "Manual check failed: " + item.Title : "Manual check outstanding: " + item.Title,
                Remediation = status == ManualReviewStatus.Failed ? item.Notes : item.Guidance,
                HelpUrl = wcag.FirstOrDefault()?.Url,
                Wcag = wcag,
                Tags = ["manual"],
                Target = new FindingTarget { Selector = checklist.Scope },
                Data = new Dictionary<string, string>
                {
                    ["checklist"] = checklist.Name,
                    ["recordedStatus"] = item.Status.ToString(),
                    ["tester"] = item.Tester ?? string.Empty,
                    ["date"] = item.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
                    ["fingerprintKey"] = checklist.Name + "/" + item.Id,
                },
            });
        }

        return findings;
    }

    /// <summary>Reads a checklist JSON file.</summary>
    public static ManualReviewChecklist Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<ManualReviewChecklist>(stream, ReportJson.Options)
            ?? throw new InvalidDataException($"'{path}' does not contain a manual-review checklist.");
    }

    /// <summary>Writes a checklist JSON file.</summary>
    public static void Save(ManualReviewChecklist checklist, string path)
    {
        ArgumentNullException.ThrowIfNull(checklist);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(checklist, ReportJson.Options) + Environment.NewLine);
    }
}

/// <summary>Built-in checklist templates covering criteria that automated tools cannot decide.</summary>
public static class ManualReviewTemplates
{
    /// <summary>Creates the standard checklist for a component, page or feature.</summary>
    public static ManualReviewChecklist Standard(string name, string? scope = null) => new()
    {
        Name = name,
        Scope = scope,
        Items =
        [
            Item("keyboard-operation", "Every function is operable with the keyboard alone, without timing requirements, and character-key shortcuts can be turned off or remapped.", "Unplug the mouse. Reach and operate every control with Tab, Shift+Tab, Enter, Space, arrow keys and Escape. Check custom widgets against the ARIA Authoring Practices keyboard patterns.", "2.1.1", "2.1.2", "2.1.4"),
            Item("focus-order", "Focus order is logical and preserves meaning; focus moves into and out of overlays as expected and returns to the trigger on close.", "Tab through the UI and compare the order with the visual reading order. Open and close dialogs, menus and popovers and confirm where focus goes.", "2.4.3"),
            Item("focus-visible", "A visible focus indicator is present on every focusable element and is not hidden behind sticky headers, footers or overlays.", "Tab through the UI at 100% and 200% zoom, in light and dark themes and with Windows high contrast / forced colors.", "2.4.7", "2.4.11"),
            Item("names-and-purpose", "Link and control names describe their purpose, headings and labels are descriptive, and visible label text is part of the accessible name.", "Review the accessible names (browser accessibility tree or a screen reader's elements list) against the visible labels.", "2.4.4", "2.4.6", "2.5.3"),
            Item("zoom-and-reflow", "Content works at 200% text zoom and reflows at 320 CSS pixels wide (400% zoom) without two-dimensional scrolling or loss of content.", "Zoom to 400% at 1280px wide, and separately increase text size to 200%.", "1.4.4", "1.4.10"),
            Item("text-spacing", "No content is clipped or overlaps when text spacing is increased (line height 1.5, paragraph spacing 2, letter spacing 0.12, word spacing 0.16).", "Apply a text-spacing bookmarklet or user stylesheet and review every state.", "1.4.12"),
            Item("contrast", "Text and meaningful graphics meet contrast in every state (hover, focus, selected, disabled, error), including text over images or gradients that tools cannot measure.", "Measure with a color picker where automated results were inconclusive; check focus indicators and icons against adjacent colors.", "1.4.3", "1.4.11"),
            Item("color-and-sensory", "Information is not conveyed by color, shape, size or position alone.", "Review status indicators, charts, required-field markers and error states in grayscale.", "1.4.1", "1.3.3"),
            Item("error-handling", "Errors are identified in text, associated with their fields, explained with suggestions, and important submissions can be reviewed or reversed.", "Submit forms with missing and invalid data; confirm messages are visible, associated with fields and announced.", "3.3.1", "3.3.3", "3.3.4"),
            Item("instructions-and-status", "Labels and instructions are provided, and status messages (saved, loading, results count) are announced without moving focus.", "Trigger each asynchronous operation with a screen reader running and confirm the announcement.", "3.3.2", "4.1.3"),
            Item("content-on-hover-focus", "Tooltips and popovers that appear on hover or focus are dismissible (Escape), hoverable and persistent.", "Hover and focus each trigger; press Escape; move the pointer onto the popup.", "1.4.13"),
            Item("motion-and-timing", "Nothing flashes more than three times per second, moving or auto-updating content can be paused, time limits can be extended, and animations respect reduced-motion preferences.", "Review carousels, spinners, toasts and session timeouts; enable 'reduce motion' in the OS.", "2.2.1", "2.2.2", "2.3.1"),
            Item("pointer-alternatives", "Path-based gestures and dragging have single-pointer alternatives, actions fire on pointer up, and targets are at least 24 by 24 CSS pixels or adequately spaced.", "Operate sliders, sortable lists and drag-and-drop with a single click/tap alternative; check target sizes.", "2.5.1", "2.5.2", "2.5.7", "2.5.8"),
            Item("screen-reader", "Representative screen reader testing confirms names, roles, states, reading order and announcements (for example NVDA + Firefox/Chrome, JAWS + Chrome, VoiceOver + Safari, TalkBack + Chrome).", "Record which assistive technology and browser versions were used in Environments.", "1.3.1", "4.1.2", "4.1.3"),
        ],
    };

    private static ManualReviewItem Item(string id, string title, string guidance, params string[] wcag) => new()
    {
        Id = id,
        Title = title,
        Guidance = guidance,
        Wcag = wcag,
    };
}
