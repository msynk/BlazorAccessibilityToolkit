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

    /// <summary>Report <see cref="ToolkitRules.FocusIndicatorNotDetected"/> for stops without outline or box-shadow. Default <c>false</c>.</summary>
    public bool ReportMissingFocusIndicator { get; init; }

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

        // WebKit (like Safari's default settings) does not move focus to links with Tab.
        var excludeLinks = string.Equals(page.Context.Browser?.BrowserType.Name, "webkit", StringComparison.Ordinal);
        var candidates = ToolkitScript.Deserialize<TabbableDto[]>(await page.EvaluateAsync<JsonElement>("x => window.__blazorA11y.tabbables(null, x)", excludeLinks).ConfigureAwait(false)) ?? [];
        var tabOrder = candidates.Where(c => c.TabIndex > 0).OrderBy(c => c.TabIndex).Concat(candidates.Where(c => c.TabIndex == 0)).ToArray();
        var endKey = tabOrder.Length == 0 ? (int?)null : options.Reverse ? tabOrder[0].Key : tabOrder[^1].Key;
        var stops = new List<FocusStop>();
        var findings = new List<AccessibilityFinding>();
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

            var stop = new FocusStop { Index = stops.Count, Element = element, Indicator = snapshot.Indicator, ModalContainer = snapshot.ModalContainer };

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

            if (!element.Visible || element.HiddenFromAssistiveTechnologies)
            {
                findings.Add(ToolkitRules.FocusHiddenTarget.CreateFinding(
                    Target(element),
                    remediation: element.HiddenFromAssistiveTechnologies
                        ? "The focused element is hidden from assistive technologies (aria-hidden, inert or hidden). Remove it from the tab order or expose it."
                        : null,
                    data: new Dictionary<string, string> { ["visible"] = element.Visible.ToString(), ["hiddenForAT"] = element.HiddenFromAssistiveTechnologies.ToString() }));
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

    /// <summary>Wraps focus-order findings into an audit for reporting.</summary>
    public static AccessibilityAudit ToAudit(this FocusOrderResult result, string name, AccessibilityContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        var builder = new AccessibilityAuditBuilder(name, context ?? new AccessibilityContext { Kind = AuditTargetKind.Page, State = "focus order" });
        builder.AddEngine(ToolkitRules.EngineName, ToolkitScript.ToolkitVersion);
        builder.AddFindings(result.Findings);
        return PlaywrightAccessibility.Persist(builder.Build());
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
