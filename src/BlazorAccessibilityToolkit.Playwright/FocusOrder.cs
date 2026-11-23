using System.Globalization;
using System.Text;
using System.Text.Json;
using BlazorAccessibilityToolkit.Playwright.Internal;
using BlazorAccessibilityToolkit.Rules;
using Microsoft.Playwright;

namespace BlazorAccessibilityToolkit.Playwright;

/// <summary>Focus-order capture settings.</summary>
public sealed record FocusOrderOptions
{
    /// <summary>Maximum number of Tab presses. Default 100.</summary>
    public int MaxStops { get; init; } = 100;

    /// <summary>Focus this element first; the capture starts with the element after it. Default: start from the document (focus cleared).</summary>
    public ILocator? StartAfter { get; init; }

    /// <summary>Use Shift+Tab instead of Tab.</summary>
    public bool Reverse { get; init; }

    /// <summary>
    /// Report <see cref="ToolkitRules.FocusIndicatorNotDetected"/> for stops whose style does not change when they
    /// receive focus. Default <c>false</c>.
    /// </summary>
    public bool ReportMissingFocusIndicator { get; init; }

    /// <summary>
    /// After the capture, measure the focus indicator of every stop from screenshots
    /// (<see cref="FocusAppearance.MeasureFocusAppearanceAsync"/>), record it in <see cref="FocusStop.Appearance"/> and
    /// report <see cref="ToolkitRules.FocusNotVisible"/> (WCAG 2.4.7) for stops where nothing changes on screen.
    /// Default <c>false</c>: each stop takes two screenshots.
    /// </summary>
    public bool MeasureFocusAppearance { get; init; }

    /// <summary>
    /// With <see cref="MeasureFocusAppearance"/>, also report <see cref="ToolkitRules.FocusAppearance"/> (WCAG 2.4.13,
    /// AAA) for indicators that are smaller than a 2 CSS pixel perimeter or change contrast by less than 3:1.
    /// </summary>
    public bool ReportFocusAppearance { get; init; }

    /// <summary>Settings for <see cref="MeasureFocusAppearance"/>.</summary>
    public FocusAppearanceOptions? FocusAppearance { get; init; }

    /// <summary>
    /// Report <see cref="ToolkitRules.FocusObscured"/> for stops that are entirely covered by other content (sticky
    /// headers, banners, non-modal overlays). Default <c>true</c>.
    /// </summary>
    public bool ReportObscuredFocus { get; init; } = true;

    /// <summary>
    /// Report <see cref="ToolkitRules.FocusOrderVisualMismatch"/> for a stop that leaves the visual reading order and
    /// comes back: focus goes from A to C and then to B, where B is between A and C on screen. Layouts with columns,
    /// where focus moves up to the next column, are not reported. Default <c>false</c>.
    /// </summary>
    public bool ReportVisualOrderMismatch { get; init; }

    /// <summary>How long to wait for focus to move after a key press before concluding it is stuck. Default 1 s.</summary>
    public TimeSpan SettleTimeout { get; init; } = TimeSpan.FromSeconds(1);
}

/// <summary>Why a focus-order capture stopped.</summary>
public enum FocusOrderTermination
{
    /// <summary>
    /// The sequence reached its end: focus left the document after the last element, or (in browsers such as headless
    /// Firefox that keep focus on the last element) Tab did not move focus away from the last element of the tab order.
    /// </summary>
    ReachedEnd,

    /// <summary>Focus returned to the first captured element (the sequence wrapped).</summary>
    Wrapped,

    /// <summary>Focus cycled among a subset of elements (possible trap, or an intentional modal dialog).</summary>
    Cycle,

    /// <summary>A key press did not move focus.</summary>
    Stuck,

    /// <summary>The maximum number of stops was reached.</summary>
    MaxStopsReached,

    /// <summary>Nothing in the page received focus.</summary>
    NothingFocusable,
}

/// <summary>One stop in a focus sequence.</summary>
public sealed record FocusStop
{
    /// <summary>Zero-based position.</summary>
    public int Index { get; init; }

    /// <summary>The focused element.</summary>
    public required AccessibleElementInfo Element { get; init; }

    /// <summary>Focus indicator observation.</summary>
    public FocusIndicator Indicator { get; init; }

    /// <summary>Selector of the containing modal dialog, if any.</summary>
    public string? ModalContainer { get; init; }

    /// <summary>Whether the element was covered by other content when it had focus.</summary>
    public FocusObscuration Obscuration { get; init; }

    /// <summary>The element's box in page coordinates when it had focus.</summary>
    public FocusBounds? Bounds { get; init; }

    /// <summary>The measured focus indicator, with <see cref="FocusOrderOptions.MeasureFocusAppearance"/>.</summary>
    public FocusAppearanceMeasurement? Appearance { get; init; }

    /// <inheritdoc />
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Index + 1}. {Element}");
}

/// <summary>
/// The observed keyboard focus sequence. <see cref="Stops"/> reports what happened; <see cref="Findings"/> holds
/// heuristic judgments (potential issues), which need human context to confirm. Whether the order is logical is a
/// judgment the toolkit does not make: compare it with an authored expectation using <see cref="ShouldMatch(string[])"/>.
/// </summary>
public sealed record FocusOrderResult
{
    /// <summary>Focus stops in the order they were reached.</summary>
    public IReadOnlyList<FocusStop> Stops { get; init; } = [];

    /// <summary>Why the capture stopped.</summary>
    public FocusOrderTermination Termination { get; init; }

    /// <summary>Elements that took part in a cycle, when <see cref="Termination"/> is <see cref="FocusOrderTermination.Cycle"/>.</summary>
    public IReadOnlyList<FocusStop> CycleMembers { get; init; } = [];

    /// <summary>Number of elements the toolkit considers tabbable in the page (heuristic).</summary>
    public int TabbableCandidates { get; init; }

    /// <summary>Selectors of tabbable candidates that were never reached when the sequence completed (heuristic, informational).</summary>
    public IReadOnlyList<string> UnreachedCandidates { get; init; } = [];

    /// <summary>Potential issues (keyboard traps, stuck focus, hidden focus targets, repeated stops ...).</summary>
    public IReadOnlyList<AccessibilityFinding> Findings { get; init; } = [];

    /// <summary>Human readable sequence, one stop per line.</summary>
    public string Describe()
    {
        var sb = new StringBuilder();
        foreach (var stop in Stops)
        {
            sb.AppendLine(stop.ToString());
        }

        sb.Append(CultureInfo.InvariantCulture, $"({Termination})");
        return sb.ToString();
    }

    /// <summary>
    /// Asserts the sequence of stops. Each expectation is <c>"role name"</c>, <c>"role"</c> or <c>"*"</c> (any); the name
    /// is compared exactly after whitespace normalization, for example <c>"button Save"</c> or <c>"link Privacy policy"</c>.
    /// </summary>
    public FocusOrderResult ShouldMatch(params string[] expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var problems = new List<string>();
        for (var i = 0; i < Math.Max(expected.Length, Stops.Count); i++)
        {
            if (i >= expected.Length)
            {
                problems.Add($"stop {i + 1}: unexpected extra stop {Stops[i].Element}");
                continue;
            }

            if (i >= Stops.Count)
            {
                problems.Add($"stop {i + 1}: expected '{expected[i]}' but the sequence ended ({Termination})");
                continue;
            }

            if (!Matches(Stops[i].Element, expected[i]))
            {
                problems.Add($"stop {i + 1}: expected '{expected[i]}' but was {Stops[i].Element}");
            }
        }

        if (problems.Count > 0)
        {
            throw new AccessibilityAssertionException("Focus order does not match the expectation:" + Environment.NewLine + string.Join(Environment.NewLine, problems.Select(p => "  " + p)) + Environment.NewLine + "Observed:" + Environment.NewLine + Describe());
        }

        return this;
    }

    /// <summary>Asserts that no potential keyboard traps or stuck focus were detected.</summary>
    public FocusOrderResult ShouldNotTrapFocus()
    {
        var traps = Findings.Where(f => f.RuleId == ToolkitRules.FocusTrap.Id || f.RuleId == ToolkitRules.FocusStuck.Id).ToArray();
        if (traps.Length > 0)
        {
            throw new AccessibilityAssertionException("Possible keyboard trap:" + Environment.NewLine + string.Join(Environment.NewLine, traps.Select(FindingFormatter.Format)) + Environment.NewLine + "Observed:" + Environment.NewLine + Describe()) { Findings = traps };
        }

        return this;
    }

    internal static bool Matches(AccessibleElementInfo element, string expectation)
    {
        var text = expectation.Trim();
        if (text == "*")
        {
            return true;
        }

        var space = text.IndexOf(' ', StringComparison.Ordinal);
        var role = space < 0 ? text : text[..space];
        var name = space < 0 ? null : PlaywrightAccessibility.Normalize(text[(space + 1)..]);
        return string.Equals(element.Role, role, StringComparison.Ordinal) && (name is null || string.Equals(element.Name, name, StringComparison.Ordinal));
    }
}

/// <summary>Captures the keyboard focus sequence of a page with real Tab key presses.</summary>
public static class FocusOrder
{
    /// <summary>Presses Tab (or Shift+Tab) repeatedly and records each focused element.</summary>
    public static async Task<FocusOrderResult> CaptureFocusOrderAsync(this IPage page, FocusOrderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        options ??= new FocusOrderOptions();
        await ToolkitScript.EnsureAsync(page).ConfigureAwait(false);

        var key = options.Reverse ? "Shift+Tab" : "Tab";
        int? previousKey = null;
        if (options.StartAfter is { } start)
        {
            await start.FocusAsync().ConfigureAwait(false);
            previousKey = (await page.GetFocusAsync().ConfigureAwait(false)).Element?.ElementKey;
        }
        else
        {
            await page.EvaluateAsync("r => window.__blazorA11y.blurActive(r)", options.Reverse).ConfigureAwait(false);
        }

        // The unfocused style of every candidate, so a focus indicator is detected as a style change on focus.
        await page.EvaluateAsync("() => window.__blazorA11y.recordFocusBaselines(null)").ConfigureAwait(false);

        // WebKit (like Safari's default settings) does not move focus to links with Tab.
        var excludeLinks = string.Equals(page.Context.Browser?.BrowserType.Name, "webkit", StringComparison.Ordinal);
        var candidates = ToolkitScript.Deserialize<TabbableDto[]>(await page.EvaluateAsync<JsonElement>("x => window.__blazorA11y.tabbables(null, x)", excludeLinks).ConfigureAwait(false)) ?? [];
        var tabOrder = candidates.Where(c => c.TabIndex > 0).OrderBy(c => c.TabIndex).Concat(candidates.Where(c => c.TabIndex == 0)).ToArray();
        var endKey = tabOrder.Length == 0 ? (int?)null : options.Reverse ? tabOrder[0].Key : tabOrder[^1].Key;
        var stops = new List<FocusStop>();
        var findings = new List<AccessibilityFinding>();
        var rightToLeft = false;
        var positions = new Dictionary<int, int>();
        var termination = FocusOrderTermination.MaxStopsReached;
        IReadOnlyList<FocusStop> cycle = [];
        (int Start, int Length)? pendingLoop = null;
        var repeatedReported = new HashSet<int>();

        for (var i = 0; i < options.MaxStops; i++)
        {
            await page.Keyboard.PressAsync(key).ConfigureAwait(false);
            var snapshot = await WaitForFocusChangeAsync(page, previousKey, previousKey == endKey ? TimeSpan.FromMilliseconds(150) : options.SettleTimeout).ConfigureAwait(false);
            var element = snapshot.Element;

            if (element is null || !snapshot.DocumentHasFocus)
            {
                termination = stops.Count == 0 ? FocusOrderTermination.NothingFocusable : FocusOrderTermination.ReachedEnd;
                break;
            }

            if (element.ElementKey == previousKey && element.ElementKey == endKey)
            {
                // Browsers that keep focus on the last element when tabbing past it.
                termination = FocusOrderTermination.ReachedEnd;
                break;
            }

            if (element.ElementKey == previousKey)
            {
                termination = FocusOrderTermination.Stuck;
                findings.Add(ToolkitRules.FocusStuck.CreateFinding(Target(element), data: new Dictionary<string, string> { ["key"] = key }));
                break;
            }

            if (snapshot.Obscuration == FocusObscuration.EntirelyObscured)
            {
                // Smooth scrolling may still be bringing the element into view; measure again once it has settled.
                await page.WaitForTimeoutAsync(250).ConfigureAwait(false);
                var settled = await page.GetFocusAsync().ConfigureAwait(false);
                if (settled.Element?.ElementKey == element.ElementKey)
                {
                    snapshot = settled;
                }
            }

            var stop = new FocusStop { Index = stops.Count, Element = element, Indicator = snapshot.Indicator, ModalContainer = snapshot.ModalContainer, Obscuration = snapshot.Obscuration, Bounds = snapshot.Bounds };
            rightToLeft |= snapshot.RightToLeft;

            if (positions.TryGetValue(element.ElementKey, out var firstIndex))
            {
                // A revisit: either the sequence wrapped, focus is cycling in a subset (possible trap), or one element
                // is reached twice. A cycle is confirmed by observing the same loop a second time.
                var continuesLoop = pendingLoop is { } loop
                    && stops[loop.Start + ((stops.Count - loop.Start) % loop.Length)].Element.ElementKey == element.ElementKey;
                if (continuesLoop)
                {
                    var (loopStart, loopLength) = pendingLoop!.Value;
                    if (stops.Count - loopStart >= loopLength * 2)
                    {
                        termination = FocusOrderTermination.Cycle;
                        cycle = stops.Skip(loopStart).Take(loopLength).ToArray();
                        break;
                    }
                }
                else if (firstIndex == 0)
                {
                    if (pendingLoop is { } repeatedLoop && repeatedReported.Add(stops[repeatedLoop.Start].Element.ElementKey))
                    {
                        findings.Add(ToolkitRules.FocusRepeated.CreateFinding(Target(stops[repeatedLoop.Start].Element)));
                    }

                    termination = FocusOrderTermination.Wrapped;
                    cycle = stops.ToArray();
                    break;
                }
                else
                {
                    pendingLoop = (firstIndex, stops.Count - firstIndex);
                }
            }
            else if (pendingLoop is { } loop)
            {
                // Focus escaped the suspected loop: the earlier element was visited twice, not trapped.
                var repeated = stops[loop.Start].Element;
                if (repeatedReported.Add(repeated.ElementKey))
                {
                    findings.Add(ToolkitRules.FocusRepeated.CreateFinding(Target(repeated)));
                }

                pendingLoop = null;
            }

            positions.TryAdd(element.ElementKey, stops.Count);
            stops.Add(stop);
            previousKey = element.ElementKey;

            if (!element.Visible || element.HiddenFromAssistiveTechnologies || snapshot.Obscuration == FocusObscuration.OutsideViewport)
            {
                findings.Add(ToolkitRules.FocusHiddenTarget.CreateFinding(
                    Target(element),
                    remediation: element.HiddenFromAssistiveTechnologies
                        ? "The focused element is hidden from assistive technologies (aria-hidden, inert or hidden). Remove it from the tab order or expose it."
                        : snapshot.Obscuration == FocusObscuration.OutsideViewport
                            ? "The focused element is outside the viewport, so keyboard users cannot see where focus is. Move it into view when it receives focus (for example a skip link that becomes visible on :focus)."
                            : null,
                    data: new Dictionary<string, string> { ["visible"] = element.Visible.ToString(), ["hiddenForAT"] = element.HiddenFromAssistiveTechnologies.ToString(), ["obscuration"] = snapshot.Obscuration.ToString() }));
            }

            if (options.ReportObscuredFocus && snapshot.Obscuration == FocusObscuration.EntirelyObscured)
            {
                findings.Add(ToolkitRules.FocusObscured.CreateFinding(
                    Target(element),
                    remediation: snapshot.ObscuredBy is { } cover
                        ? $"The focused element is covered by {cover}. {ToolkitRules.FocusObscured.Remediation}"
                        : null,
                    data: new Dictionary<string, string> { ["coveredBy"] = snapshot.ObscuredBy ?? string.Empty }));
            }

            if (options.ReportMissingFocusIndicator && snapshot.Indicator == FocusIndicator.NotDetected)
            {
                findings.Add(ToolkitRules.FocusIndicatorNotDetected.CreateFinding(Target(element)));
            }
        }

        if (termination is FocusOrderTermination.Cycle or FocusOrderTermination.Wrapped && cycle.Count > 0)
        {
            var modal = cycle.Select(s => s.ModalContainer).Distinct().ToArray();
            var coversPage = cycle.Count >= candidates.Length;
            if (!coversPage && !(modal.Length == 1 && modal[0] is not null))
            {
                findings.Add(ToolkitRules.FocusTrap.CreateFinding(
                    Target(cycle[0].Element),
                    remediation: "Focus cycled among: " + string.Join(", ", cycle.Select(c => c.Element.ToString())) + ". " + ToolkitRules.FocusTrap.Remediation,
                    data: new Dictionary<string, string> { ["members"] = string.Join(" | ", cycle.Select(c => c.Element.Selector)) }));
            }
        }

        if (options.ReportVisualOrderMismatch)
        {
            findings.AddRange(VisualOrderMismatches(options.Reverse ? stops.AsEnumerable().Reverse().ToList() : stops, rightToLeft));
        }

        if (options.MeasureFocusAppearance)
        {
            // Measured after the capture, so that blurring and refocusing elements cannot change the sequence.
            var measured = new Dictionary<int, FocusAppearanceMeasurement?>();
            for (var i = 0; i < stops.Count; i++)
            {
                var element = stops[i].Element;
                if (!measured.TryGetValue(element.ElementKey, out var measurement))
                {
                    var locator = element.Selector is { } selector ? page.Locator(selector) : null;
                    measurement = locator is not null && await locator.CountAsync().ConfigureAwait(false) == 1
                        ? await locator.MeasureFocusAppearanceAsync(options.FocusAppearance).ConfigureAwait(false)
                        : null;
                    measured[element.ElementKey] = measurement;
                    if (measurement is not null && element.Visible && stops[i].Obscuration != FocusObscuration.EntirelyObscured)
                    {
                        findings.AddRange(FocusAppearance.Findings(measurement, Target(element), options.ReportFocusAppearance));
                    }
                }

                stops[i] = stops[i] with { Appearance = measurement };
            }
        }

        var reached = stops.Select(s => s.Element.ElementKey).ToHashSet();
        var unreached = termination is FocusOrderTermination.ReachedEnd or FocusOrderTermination.Wrapped && options.StartAfter is null
            ? candidates.Where(c => !reached.Contains(c.Key)).Select(c => c.Selector ?? "?").ToArray()
            : [];

        return new FocusOrderResult
        {
            Stops = stops,
            Termination = termination,
            CycleMembers = cycle,
            TabbableCandidates = candidates.Length,
            UnreachedCandidates = unreached,
            Findings = findings,
        };
    }

    // A stop that is out of place: its neighbors are in visual reading order, but it is not between them.
    private static IEnumerable<AccessibilityFinding> VisualOrderMismatches(IReadOnlyList<FocusStop> stops, bool rightToLeft)
    {
        for (var i = 1; i + 1 < stops.Count; i++)
        {
            if (stops[i - 1].Bounds is not { } previous || stops[i].Bounds is not { } current || stops[i + 1].Bounds is not { } next
                || stops[i - 1].ModalContainer != stops[i].ModalContainer || stops[i].ModalContainer != stops[i + 1].ModalContainer)
            {
                continue;
            }

            // When the neighbors share a row, focus is reading down a column and moving to the next one: not reported.
            if (VisuallyBefore(previous, next, rightToLeft) && !SameRow(previous, next)
                && ((VisuallyBefore(current, previous, rightToLeft) && VisuallyBefore(current, next, rightToLeft))
                    || (VisuallyBefore(previous, current, rightToLeft) && VisuallyBefore(next, current, rightToLeft))))
            {
                yield return ToolkitRules.FocusOrderVisualMismatch.CreateFinding(
                    Target(stops[i].Element),
                    remediation: $"Focus goes from {stops[i - 1].Element} to {stops[i].Element} and then to {stops[i + 1].Element}, but on screen {stops[i].Element} is not between them. {ToolkitRules.FocusOrderVisualMismatch.Remediation}",
                    data: new Dictionary<string, string> { ["index"] = (i + 1).ToString(CultureInfo.InvariantCulture) });
            }
        }
    }

    private static bool SameRow(FocusBounds a, FocusBounds b)
    {
        var tolerance = Math.Min(a.Height, b.Height) / 2;
        return a.Y + a.Height > b.Y + tolerance && b.Y + b.Height > a.Y + tolerance;
    }

    // Reading order: a row above comes first; within a row (vertical overlap), the start side comes first.
    private static bool VisuallyBefore(FocusBounds a, FocusBounds b, bool rightToLeft)
    {
        var tolerance = Math.Min(a.Height, b.Height) / 2;
        if (a.Y + a.Height <= b.Y + tolerance)
        {
            return true;
        }

        if (b.Y + b.Height <= a.Y + tolerance)
        {
            return false;
        }

        return rightToLeft ? a.X + a.Width > b.X + b.Width + 1 : a.X + 1 < b.X;
    }

    private const string OverlayScript = """
        stops => {
          const ns = 'http://www.w3.org/2000/svg';
          const doc = document.documentElement;
          const width = Math.max(doc.scrollWidth, document.body ? document.body.scrollWidth : 0);
          const height = Math.max(doc.scrollHeight, document.body ? document.body.scrollHeight : 0);
          const svg = document.createElementNS(ns, 'svg');
          svg.setAttribute('data-ba11y-tab-stops', '');
          svg.setAttribute('aria-hidden', 'true');
          svg.setAttribute('width', width);
          svg.setAttribute('height', height);
          svg.style.cssText = 'position:absolute;left:0;top:0;pointer-events:none;z-index:2147483647;overflow:visible';
          const add = (name, attrs, parent) => { const el = document.createElementNS(ns, name); for (const k in attrs) el.setAttribute(k, attrs[k]); (parent || svg).appendChild(el); return el; };
          const centers = stops.map(s => [s.x + s.width / 2, s.y + s.height / 2]);
          if (centers.length > 1) add('polyline', { points: centers.map(c => c.join(',')).join(' '), fill: 'none', stroke: '#5f6368', 'stroke-width': 2, 'stroke-dasharray': '6 4' });
          stops.forEach((s, i) => {
            add('rect', { x: s.x - 2, y: s.y - 2, width: s.width + 4, height: s.height + 4, fill: 'none', stroke: s.problem ? '#c5221f' : '#0b57d0', 'stroke-width': 3, rx: 3 });
            const cx = Math.max(12, s.x), cy = Math.max(12, s.y);
            add('circle', { cx, cy, r: 12, fill: s.problem ? '#c5221f' : '#0b57d0', stroke: '#fff', 'stroke-width': 2 });
            const t = add('text', { x: cx, y: cy + 4, 'text-anchor': 'middle', 'font-family': 'Arial, sans-serif', 'font-size': 12, 'font-weight': 'bold', fill: '#fff' });
            t.textContent = String(i + 1);
          });
          (document.body || doc).appendChild(svg);
        }
        """;

    /// <summary>
    /// Saves a full-page screenshot of <paramref name="page"/> with the captured tab stops drawn on top: a numbered
    /// marker and an outline per stop, connected in focus order, as Accessibility Insights' tab-stop visualization shows.
    /// Stops with findings (hidden, obscured, out of visual order, without an indicator) are drawn in red. The overlay
    /// is removed afterwards. Use the image for reviews of the focus order and attach it to reports.
    /// </summary>
    /// <returns>The path of the PNG file.</returns>
    public static async Task<string> SaveTabStopOverlayAsync(this FocusOrderResult result, IPage page, string path)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var flagged = result.Findings.Select(f => f.Target.Selector).Where(s => s is not null).ToHashSet(StringComparer.Ordinal);
        var stops = result.Stops.Where(s => s.Bounds is not null).Select(s => new
        {
            x = s.Bounds!.X,
            y = s.Bounds.Y,
            width = s.Bounds.Width,
            height = s.Bounds.Height,
            problem = s.Element.Selector is { } selector && flagged.Contains(selector),
        }).ToArray();

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        await page.EvaluateAsync(OverlayScript, stops).ConfigureAwait(false);
        try
        {
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = true, Animations = ScreenshotAnimations.Disabled }).ConfigureAwait(false);
        }
        finally
        {
            await page.EvaluateAsync("() => document.querySelectorAll('[data-ba11y-tab-stops]').forEach(e => e.remove())").ConfigureAwait(false);
        }

        return Path.GetFullPath(path);
    }

    /// <summary>Wraps focus-order findings into an audit for reporting.</summary>
    public static AccessibilityAudit ToAudit(this FocusOrderResult result, string name, AccessibilityContext? context = null) =>
        PlaywrightAccessibility.Persist(BuildAudit(result, name, context));

    /// <summary>
    /// Wraps focus-order findings into an audit, with the tab-stop overlay screenshot of <paramref name="page"/> saved
    /// to <paramref name="overlayPath"/> and attached, so that the HTML report shows it next to the findings.
    /// </summary>
    public static async Task<AccessibilityAudit> ToAuditWithTabStopsAsync(this FocusOrderResult result, string name, IPage page, string overlayPath, AccessibilityContext? context = null)
    {
        var audit = BuildAudit(result, name, context);
        var path = await result.SaveTabStopOverlayAsync(page, overlayPath).ConfigureAwait(false);
        var description = string.Create(CultureInfo.InvariantCulture, $"Tab stops of {name}: {result.Stops.Count} stops in focus order{(result.Findings.Count > 0 ? $", {result.Findings.Count} with findings (red)" : string.Empty)}.");
        return PlaywrightAccessibility.Persist(audit.WithAttachment("tab stops", path, "image/png", description));
    }

    private static AccessibilityAudit BuildAudit(FocusOrderResult result, string name, AccessibilityContext? context)
    {
        ArgumentNullException.ThrowIfNull(result);
        var builder = new AccessibilityAuditBuilder(name, context ?? new AccessibilityContext { Kind = AuditTargetKind.Page, State = "focus order" });
        builder.AddEngine(ToolkitRules.EngineName, ToolkitScript.ToolkitVersion);
        builder.AddFindings(result.Findings);
        return builder.Build();
    }

    private static FindingTarget Target(AccessibleElementInfo element) => new()
    {
        Selector = element.Selector,
        Role = element.Role,
        AccessibleName = element.Name,
        Html = element.Html,
        ElementOrdinal = element.ElementOrdinal,
    };

    private static async Task<FocusSnapshot> WaitForFocusChangeAsync(IPage page, int? previousKey, TimeSpan timeout)
    {
        var snapshot = await page.GetFocusAsync().ConfigureAwait(false);
        if (previousKey is null || snapshot.Element?.ElementKey != previousKey)
        {
            return snapshot;
        }

        // Focus may move asynchronously (for example after a Blazor Server round trip). Wait for a change, bounded.
        try
        {
            await page.WaitForFunctionAsync(
                "k => { const a = window.__blazorA11y.activeElementDeep(); return !a || window.__blazorA11y.focusInfo().element.key !== k; }",
                previousKey.Value,
                new PageWaitForFunctionOptions { Timeout = (float)timeout.TotalMilliseconds, PollingInterval = 25 }).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Still on the same element.
        }

        return await page.GetFocusAsync().ConfigureAwait(false);
    }
}
