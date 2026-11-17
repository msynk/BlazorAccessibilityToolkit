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
            Item("authentication-and-entry", "Logging in does not require a cognitive function test (remembering, transcribing or solving) without an alternative such as paste and password managers, and information already entered in a process is filled in or selectable rather than requested again.", "Paste into username, password and one-time-code fields; use a password manager; walk through multi-step forms and check for repeated questions.", "3.3.7", "3.3.8"),
            Item("consistent-help", "Help mechanisms (contact details, chat, help links) appear in the same relative order on every page that offers them, and navigation and identification are consistent across pages.", "Compare the header, footer and help placement across the pages in scope.", "3.2.3", "3.2.4", "3.2.6"),
            Item("text-alternatives-and-media", "Images, icons and charts have meaningful alternatives (or are marked decorative), and audio or video has captions, transcripts and audio description where needed.", "Review every image's alternative text in context; play media with captions on.", "1.1.1", "1.2.1", "1.2.2", "1.2.3", "1.2.5"),
            Item("screen-reader", "Representative screen reader testing confirms names, roles, states, reading order and announcements (for example NVDA + Firefox/Chrome, JAWS + Chrome, VoiceOver + Safari, TalkBack + Chrome).", "Record which assistive technology and browser versions were used in Environments.", "1.3.1", "4.1.2", "4.1.3"),
        ],
    };

    /// <summary>
    /// Creates a checklist with one item per WCAG 2.2 level A and AA success criterion (the obsolete 4.1.1 excluded), for
    /// a full conformance review or an accessibility conformance report. Automated results never complete an item; each
    /// one needs a person's verdict.
    /// </summary>
    public static ManualReviewChecklist Wcag22AA(string name, string? scope = null) => new()
    {
        Name = name,
        Scope = scope,
        Items = WcagCatalog.All
            .Where(c => c.Level <= WcagLevel.AA && !c.IsObsolete)
            .Select(c => new ManualReviewItem
            {
                Id = "wcag-" + c.Id,
                Title = $"{c.Id} {c.Title} (Level {c.Level})",
                Guidance = Wcag22Guidance.GetValueOrDefault(c.Id, "Review the criterion against its Understanding document.") + " " + c.Url,
                Wcag = [c.Id],
            })
            .ToArray(),
    };

    private static readonly Dictionary<string, string> Wcag22Guidance = new(StringComparer.Ordinal)
    {
        ["1.1.1"] = "Check that every image, icon and control has an alternative that serves the same purpose, and decorative images are hidden.",
        ["1.2.1"] = "Check that prerecorded audio-only content has a transcript and video-only content has a transcript or audio track.",
        ["1.2.2"] = "Check that prerecorded video with audio has accurate captions.",
        ["1.2.3"] = "Check that prerecorded video has audio description or a full text alternative.",
        ["1.2.4"] = "Check that live video with audio has captions.",
        ["1.2.5"] = "Check that prerecorded video has audio description of important visual information.",
        ["1.3.1"] = "Check that headings, lists, tables, form labels, groups and required states are marked up, not only styled.",
        ["1.3.2"] = "Read the page with CSS disabled or with a screen reader and confirm the reading order preserves meaning.",
        ["1.3.3"] = "Check that instructions do not rely only on shape, size, position, orientation or sound.",
        ["1.3.4"] = "Rotate the device or viewport and confirm content works in portrait and landscape.",
        ["1.3.5"] = "Check that fields collecting information about the user have the matching autocomplete token.",
        ["1.4.1"] = "Check that color is not the only way information, links, errors or states are conveyed.",
        ["1.4.2"] = "Check that audio that plays automatically for more than three seconds can be paused or muted.",
        ["1.4.3"] = "Measure text contrast (4.5:1, or 3:1 for large text) in every state, including over images and gradients.",
        ["1.4.4"] = "Zoom text to 200% and confirm nothing is lost or overlaps.",
        ["1.4.5"] = "Check that text is not presented as an image unless essential (logos).",
        ["1.4.10"] = "View the page at 320 CSS pixels wide (400% zoom) and confirm there is no horizontal scrolling or lost content.",
        ["1.4.11"] = "Measure contrast (3:1) of control boundaries, focus indicators, icons and chart elements.",
        ["1.4.12"] = "Apply the WCAG text-spacing values and confirm no text is clipped or overlaps.",
        ["1.4.13"] = "Check that content shown on hover or focus can be dismissed with Escape, hovered and stays until dismissed.",
        ["2.1.1"] = "Operate every function with the keyboard alone.",
        ["2.1.2"] = "Confirm focus can always leave every component with standard keys.",
        ["2.1.4"] = "Check that single-character shortcuts can be turned off, remapped or only work on focus.",
        ["2.2.1"] = "Check that time limits can be turned off, adjusted or extended.",
        ["2.2.2"] = "Check that moving, blinking, scrolling or auto-updating content can be paused, stopped or hidden.",
        ["2.3.1"] = "Check that nothing flashes more than three times per second.",
        ["2.4.1"] = "Check for a skip link or landmarks that let users bypass repeated blocks.",
        ["2.4.2"] = "Check that every page (and every client-side route) has a descriptive, unique title.",
        ["2.4.3"] = "Tab through the page and confirm focus order preserves meaning; check where focus goes after dialogs and route changes.",
        ["2.4.4"] = "Check that the purpose of each link is clear from its text or context.",
        ["2.4.5"] = "Check that pages can be found in more than one way (navigation, search, site map).",
        ["2.4.6"] = "Check that headings and labels describe their topic or purpose.",
        ["2.4.7"] = "Tab through the page and confirm a visible focus indicator on every element.",
        ["2.4.11"] = "Tab through the page and confirm the focused element is never entirely hidden by sticky headers, footers or banners.",
        ["2.5.1"] = "Check that multipoint and path-based gestures have single-pointer alternatives.",
        ["2.5.2"] = "Check that actions fire on pointer up and can be cancelled by moving away.",
        ["2.5.3"] = "Check that the accessible name of each control contains its visible label text.",
        ["2.5.4"] = "Check that functions triggered by device motion have a UI alternative and can be disabled.",
        ["2.5.7"] = "Check that every dragging operation has a single-pointer alternative.",
        ["2.5.8"] = "Check that targets are at least 24 by 24 CSS pixels or have enough spacing.",
        ["3.1.1"] = "Check that the page language is declared correctly.",
        ["3.1.2"] = "Check that passages in another language are marked with lang.",
        ["3.2.1"] = "Check that focusing a control does not change context.",
        ["3.2.2"] = "Check that changing a setting does not change context unless the user was told beforehand.",
        ["3.2.3"] = "Check that repeated navigation appears in the same relative order on every page.",
        ["3.2.4"] = "Check that components with the same function are identified consistently.",
        ["3.2.6"] = "Check that help mechanisms appear in the same relative order on every page that offers them.",
        ["3.3.1"] = "Submit forms with errors and confirm each error is identified in text and associated with its field.",
        ["3.3.2"] = "Check that fields have labels or instructions, including required and format hints.",
        ["3.3.3"] = "Check that error messages suggest how to fix the input when known.",
        ["3.3.4"] = "Check that legal, financial or data-changing submissions can be reversed, checked or confirmed.",
        ["3.3.7"] = "Walk through multi-step processes and confirm information already given is not requested again.",
        ["3.3.8"] = "Log in with a password manager and paste; confirm no cognitive function test without an alternative.",
        ["4.1.2"] = "Check names, roles, states and values of custom controls with a screen reader or the accessibility tree.",
        ["4.1.3"] = "Trigger status messages (saved, results count, errors) and confirm they are announced without moving focus.",
    };

    private static ManualReviewItem Item(string id, string title, string guidance, params string[] wcag) => new()
    {
        Id = id,
        Title = title,
        Guidance = guidance,
        Wcag = wcag,
    };
}
