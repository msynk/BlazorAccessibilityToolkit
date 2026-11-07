using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BlazorAccessibilityToolkit.Playwright.Internal;
using BlazorAccessibilityToolkit.Rules;
using Microsoft.Playwright;

namespace BlazorAccessibilityToolkit.Playwright;

/// <summary>What happened to a live region.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LiveRegionChangeKind>))]
public enum LiveRegionChangeKind
{
    /// <summary>The live region element was added to the DOM (possibly together with its content).</summary>
    [JsonStringEnumMemberName("inserted")]
    Inserted,

    /// <summary>Text was added to or changed inside an existing live region.</summary>
    [JsonStringEnumMemberName("content-added")]
    ContentAdded,

    /// <summary>Content was removed from an existing live region.</summary>
    [JsonStringEnumMemberName("content-removed")]
    ContentRemoved,
}

/// <summary>
/// DOM evidence of a live-region update. This records what changed in the DOM and how the region is exposed; it does
/// not prove that a screen reader announced it (announcements depend on the assistive technology, browser, timing and
/// user settings).
/// </summary>
public sealed record LiveRegionChange
{
    /// <summary>Monotonic sequence number within the page.</summary>
    public int Sequence { get; init; }

    /// <summary>performance.now() timestamp in milliseconds.</summary>
    public double Timestamp { get; init; }

    /// <summary>Kind of change.</summary>
    public LiveRegionChangeKind Kind { get; init; }

    /// <summary>Selector of the live region.</summary>
    public string? Selector { get; init; }

    /// <summary>Role of the region (status, alert, log, marquee, timer).</summary>
    public string? Role { get; init; }

    /// <summary>Effective politeness: <c>polite</c>, <c>assertive</c> or <c>off</c>.</summary>
    public string? Politeness { get; init; }

    /// <summary>Explicit aria-live attribute value.</summary>
    public string? AriaLive { get; init; }

    /// <summary>Whether the region is atomic (explicitly or implicitly for status/alert).</summary>
    public bool Atomic { get; init; }

    /// <summary>aria-relevant value (default <c>additions text</c>).</summary>
    public string? Relevant { get; init; }

    /// <summary>aria-busy at the time of the change.</summary>
    public bool Busy { get; init; }

    /// <summary>True when the region was hidden (not rendered or aria-hidden) after the change.</summary>
    public bool Hidden { get; init; }

    /// <summary>Region text after the change.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Text added by this change.</summary>
    public string AddedText { get; init; } = string.Empty;

    /// <summary>Text removed by this change.</summary>
    public string RemovedText { get; init; } = string.Empty;

    /// <summary>True when the region existed before this change.</summary>
    public bool RegionWasPresent { get; init; }

    /// <inheritdoc />
    public override string ToString() => $"#{Sequence} {Kind} {Role ?? "live"} ({Politeness}) \"{(AddedText.Length > 0 ? AddedText : Text)}\"{(Hidden ? " [hidden]" : string.Empty)}";
}

/// <summary>A live region currently present in the page.</summary>
public sealed record LiveRegionInfo
{
    /// <summary>Selector of the region.</summary>
    public string? Selector { get; init; }

    /// <summary>Role (status, alert, log, marquee, timer), if any.</summary>
    public string? Role { get; init; }

    /// <summary>Effective politeness: <c>polite</c>, <c>assertive</c> or <c>off</c>.</summary>
    public string? Politeness { get; init; }

    /// <summary>Explicit aria-live attribute value.</summary>
    public string? AriaLive { get; init; }

    /// <summary>Whether the region is atomic.</summary>
    public bool Atomic { get; init; }

    /// <summary>aria-relevant value.</summary>
    public string? Relevant { get; init; }

    /// <summary>aria-busy.</summary>
    public bool Busy { get; init; }

    /// <summary>True when the region is not rendered or is aria-hidden.</summary>
    public bool Hidden { get; init; }

    /// <summary>Current text.</summary>
    public string Text { get; init; } = string.Empty;
}

/// <summary>How expected live-region text is matched.</summary>
public enum TextMatch
{
    /// <summary>Case-insensitive substring match (whitespace normalized).</summary>
    Contains,

    /// <summary>Exact match (whitespace normalized).</summary>
    Exact,
}

/// <summary>
/// Records live-region updates in a page with a MutationObserver, so tests can assert that dynamic messages are exposed
/// with appropriate semantics.
/// </summary>
/// <remarks>
/// Limitations: this observes the DOM, not speech output. It does not emulate a screen reader, cannot tell whether
/// an announcement was interrupted or de-duplicated, and does not observe cross-origin iframes or closed shadow roots.
/// Re-setting identical text without a DOM change produces no record (and is often not re-announced either).
/// </remarks>
public sealed class LiveRegionMonitor
{
    private int _cursor;

    private LiveRegionMonitor(IPage page) => Page = page;

    /// <summary>The monitored page.</summary>
    public IPage Page { get; }

    /// <summary>Default timeout for expectations.</summary>
    public TimeSpan DefaultTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Starts monitoring. Start before triggering the interaction: regions that appear after this call are tracked, and
    /// content present when monitoring starts is not reported as an update. Monitoring is re-installed after navigations.
    /// </summary>
    public static async Task<LiveRegionMonitor> StartAsync(IPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        await page.AddInitScriptAsync(ToolkitScript.InitScript + "\n;window.__blazorA11y.installLiveRegionObserver();").ConfigureAwait(false);
        await ToolkitScript.EnsureAsync(page).ConfigureAwait(false);
        await page.EvaluateAsync("() => window.__blazorA11y.installLiveRegionObserver()").ConfigureAwait(false);
        return new LiveRegionMonitor(page);
    }

    /// <summary>All changes recorded since monitoring started (or since the page last navigated).</summary>
    public async Task<IReadOnlyList<LiveRegionChange>> GetChangesAsync()
    {
        var json = await Page.EvaluateAsync<JsonElement>("() => window.__blazorA11y && window.__blazorA11y.liveRecords(0)").ConfigureAwait(false);
        return ToolkitScript.Deserialize<LiveRegionChange[]>(json) ?? [];
    }

    /// <summary>The live regions currently in the page and their politeness.</summary>
    public async Task<IReadOnlyList<LiveRegionInfo>> GetRegionsAsync()
    {
        await ToolkitScript.EnsureAsync(Page).ConfigureAwait(false);
        var json = await Page.EvaluateAsync<JsonElement>("() => window.__blazorA11y.liveRegions()").ConfigureAwait(false);
        return ToolkitScript.Deserialize<LiveRegionInfo[]>(json) ?? [];
    }

    /// <summary>Ignores changes recorded so far for subsequent expectations.</summary>
    public async Task ResetAsync()
    {
        var changes = await GetChangesAsync().ConfigureAwait(false);
        _cursor = changes.Count == 0 ? _cursor : changes.Max(c => c.Sequence);
    }

    /// <summary>
    /// Waits for a live-region update whose text matches, after the previously matched update (so repeated identical
    /// messages can be asserted one by one). Throws <see cref="AccessibilityAssertionException"/> listing the observed
    /// updates when none matches in time.
    /// </summary>
    public Task<LiveRegionChange> ExpectUpdateAsync(string text, TextMatch match = TextMatch.Contains, string? politeness = null, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        return ExpectCoreAsync(new { text, exact = match == TextMatch.Exact }, $"\"{text}\"", politeness, timeout);
    }

    /// <summary>Waits for a live-region update whose text matches <paramref name="pattern"/>.</summary>
    public Task<LiveRegionChange> ExpectUpdateAsync(Regex pattern, string? politeness = null, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        var flags = pattern.Options.HasFlag(RegexOptions.IgnoreCase) ? "i" : string.Empty;
        return ExpectCoreAsync(new { pattern = pattern.ToString(), flags }, $"/{pattern}/", politeness, timeout);
    }

    /// <summary>Asserts that no live-region update happens within <paramref name="duration"/> (for example after a no-op action).</summary>
    public async Task ExpectNoUpdateAsync(TimeSpan duration)
    {
        var before = _cursor;
        try
        {
            await Page.WaitForFunctionAsync(
                "after => (window.__blazorA11y.liveRecords(after) || []).length > 0",
                before,
                new PageWaitForFunctionOptions { Timeout = (float)duration.TotalMilliseconds, PollingInterval = 50 }).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return;
        }

        var changes = (await GetChangesAsync().ConfigureAwait(false)).Where(c => c.Sequence > before).ToArray();
        throw new AccessibilityAssertionException("Expected no live-region update, but observed:" + Environment.NewLine + string.Join(Environment.NewLine, changes.Select(c => "  " + c)));
    }

    /// <summary>
    /// Analyzes recorded changes for patterns that commonly prevent announcements: regions inserted together with
    /// their content, updates while hidden, and contradictory politeness. Returns potential issues.
    /// </summary>
    public async Task<IReadOnlyList<AccessibilityFinding>> AnalyzeAsync()
    {
        var findings = new List<AccessibilityFinding>();
        var regions = await GetRegionsAsync().ConfigureAwait(false);
        foreach (var region in regions)
        {
            if ((region.Role == "alert" && region.AriaLive is "polite" or "off") || (region.Role == "status" && region.AriaLive is "assertive" or "off"))
            {
                findings.Add(ToolkitRules.LiveRegionPolitenessConflict.CreateFinding(
                    new FindingTarget { Selector = region.Selector, Role = region.Role },
                    data: new Dictionary<string, string> { ["role"] = region.Role ?? string.Empty, ["ariaLive"] = region.AriaLive ?? string.Empty }));
            }
        }

        foreach (var change in await GetChangesAsync().ConfigureAwait(false))
        {
            var target = new FindingTarget { Selector = change.Selector, Role = change.Role };
            var data = new Dictionary<string, string> { ["text"] = change.Text, ["sequence"] = change.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            if (change.Kind == LiveRegionChangeKind.Inserted && change.Text.Length > 0)
            {
                findings.Add(ToolkitRules.LiveRegionInsertedWithContent.CreateFinding(target, data: data));
            }

            if (change.Hidden && (change.AddedText.Length > 0 || change.Kind == LiveRegionChangeKind.Inserted))
            {
                findings.Add(ToolkitRules.LiveRegionHiddenUpdate.CreateFinding(target, data: data));
            }
        }

        return findings
            .GroupBy(f => (f.RuleId, f.Target.Selector))
            .Select(g => g.First())
            .ToArray();
    }

    private async Task<LiveRegionChange> ExpectCoreAsync(object match, string description, string? politeness, TimeSpan? timeout)
    {
        var wait = timeout ?? DefaultTimeout;
        try
        {
            var handle = await Page.WaitForFunctionAsync(
                "arg => window.__blazorA11y && window.__blazorA11y.findLiveRecord(arg)",
                new { after = _cursor, politeness, match },
                new PageWaitForFunctionOptions { Timeout = (float)wait.TotalMilliseconds, PollingInterval = 25 }).ConfigureAwait(false);
            var json = await handle.JsonValueAsync<JsonElement>().ConfigureAwait(false);
            var change = ToolkitScript.Deserialize<LiveRegionChange>(json)!;
            _cursor = change.Sequence;
            return change;
        }
        catch (TimeoutException)
        {
            var changes = (await GetChangesAsync().ConfigureAwait(false)).Where(c => c.Sequence > _cursor).ToArray();
            var observed = changes.Length == 0
                ? "  (no live-region updates were observed; check that the message is rendered inside an element with role=\"status\"/\"alert\" or aria-live that exists before the update)"
                : string.Join(Environment.NewLine, changes.Select(c => "  " + c));
            throw new AccessibilityAssertionException(
                $"Expected a live-region update matching {description}{(politeness is null ? string.Empty : $" with politeness '{politeness}'")} within {wait.TotalSeconds:0.#} s. Observed updates:{Environment.NewLine}{observed}{Environment.NewLine}" +
                "Note: this checks DOM evidence of the update, not screen-reader speech.");
        }
    }
}
