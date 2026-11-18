using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Playwright;

namespace BlazorAccessibilityToolkit.Playwright;

public static partial class AriaPatterns
{
    private const string LoadingStateScript = """
        sel => {
          const region = document.querySelector(sel);
          if (!region) return null;
          const busy = !!region.closest('[aria-busy=true]') || region.getAttribute('aria-busy') === 'true';
          const live = Array.from(document.querySelectorAll('[role=status], [role=alert], [role=progressbar], [aria-live]:not([aria-live=off])'))
            .filter(r => r.getClientRects().length > 0 || r.textContent.trim().length > 0)
            .map(r => r.getAttribute('role') === 'progressbar' ? 'progressbar' : (r.textContent || '').replace(/\s+/g, ' ').trim())
            .filter(Boolean);
          return { busy, live, text: (region.textContent || '').replace(/\s+/g, ' ').trim().slice(0, 300) };
        }
        """;

    /// <summary>
    /// Loading states, as produced by <c>[StreamRendering]</c> placeholders or content loaded in <c>OnInitializedAsync</c>
    /// (WCAG 4.1.3 status messages): while the region (<paramref name="regionSelector"/>) is loading, it is marked <c>aria-busy="true"</c>
    /// or a status message (live region or progress bar) says so; when <paramref name="loaded"/> appears,
    /// <c>aria-busy</c> is cleared and a status message reports the result. Loads <paramref name="url"/> in
    /// <paramref name="page"/> itself, to observe the loading phase from the start.
    /// </summary>
    /// <remarks>
    /// The states are recorded inside the page by an init script, so that streamed updates are seen in every browser.
    /// The script stays registered for later navigations of <paramref name="page"/>; use a fresh page for other tests.
    /// </remarks>
    /// <param name="page">The page.</param>
    /// <param name="url">The page that loads content.</param>
    /// <param name="regionSelector">CSS selector of the region that is filled in.</param>
    /// <param name="loaded">An element that exists once loading is complete.</param>
    /// <param name="timeout">How long loading may take. Default 15 s.</param>
    public static async Task<PatternVerification> VerifyLoadingStateAsync(IPage page, string url, string regionSelector, ILocator loaded, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(regionSelector);
        ArgumentNullException.ThrowIfNull(loaded);
        var checks = new List<PatternCheck>();

        // Record every state of the region inside the page from the start of the response, so that streamed updates
        // are seen in every browser (polling from .NET can miss them, for example in Firefox).
        var key = "__ba11yLoading" + Guid.NewGuid().ToString("N");
        await page.AddInitScriptAsync($$"""
            (() => {
              const sel = {{JsonSerializer.Serialize(regionSelector)}};
              const read = {{LoadingStateScript}};
              const history = window['{{key}}'] = [];
              const record = () => {
                const state = read(sel);
                if (!state) return;
                const last = history[history.length - 1];
                if (!last || last.busy !== state.busy || last.text !== state.text || last.live.join('|') !== state.live.join('|')) history.push(state);
              };
              // Observe the document node: it exists before the parser creates any element, so streamed content is seen
              // as it arrives (readystatechange would only fire once the streamed response has ended).
              new MutationObserver(record).observe(document, { subtree: true, childList: true, characterData: true, attributes: true });
            })();
            """).ConfigureAwait(false);
        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.Commit }).ConfigureAwait(false);

        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (DateTime.UtcNow < deadline && await loaded.CountAsync().ConfigureAwait(false) == 0)
        {
            await Task.Delay(50).ConfigureAwait(false);
        }

        var history = Internal.ToolkitScript.Deserialize<LoadingSnapshot[]>(await page.EvaluateAsync<JsonElement>($"() => window['{key}'] || []").ConfigureAwait(false)) ?? [];

        // Every recorded state before the last one belongs to the loading phase.
        var loadingStates = history.Length > 1 ? history[..^1] : [];
        var sawLoading = loadingStates.Length > 0;

        // Status text that is there before, during and after the load (a cart count, a greeting) is not about the load:
        // only text that differs from the final state counts while loading, and only new text counts afterwards.
        var finalLive = history.Length > 0 ? history[^1].Live : [];
        var relevant = loadingStates
            .Select(h => new LoadingSnapshot { Busy = h.Busy, Live = h.Live.Except(finalLive, StringComparer.Ordinal).ToList() })
            .ToArray();
        var whileLoading = relevant.FirstOrDefault(h => h.Busy || h.Live.Count > 0) ?? relevant.FirstOrDefault();
        var done = await loaded.CountAsync().ConfigureAwait(false) > 0;
        if (!sawLoading)
        {
            checks.Add(new("The loading phase can be observed", false, done ? "the content was already loaded when the region appeared; slow the data source down to verify the loading state" : $"no element matches {regionSelector}"));
            return Result("Loading state", checks);
        }

        checks.Add(new(
            "While loading, the region is aria-busy or a status message reports it",
            whileLoading is { Busy: true } || whileLoading is { Live.Count: > 0 },
            whileLoading is null ? null : $"aria-busy: {(whileLoading.Busy ? "true" : "not set")}; status: {(whileLoading.Live.Count == 0 ? "none" : string.Join(" | ", whileLoading.Live))}"));
        checks.Add(new("The content finishes loading", done));
        if (!done)
        {
            return Result("Loading state", checks);
        }

        var final = Internal.ToolkitScript.Deserialize<LoadingSnapshot>(await page.EvaluateAsync<JsonElement>(LoadingStateScript, regionSelector).ConfigureAwait(false)) ?? new LoadingSnapshot();
        checks.Add(new("aria-busy is cleared when loading is complete", !final.Busy));
        var before = loadingStates.SelectMany(h => h.Live).ToHashSet(StringComparer.Ordinal);
        var reported = final.Live.Where(text => !before.Contains(text)).ToArray();
        checks.Add(new("A status message reports the result", reported.Length > 0, reported.Length > 0 ? string.Join(" | ", reported) : "no new status text"));
        return Result("Loading state", checks);
    }

    private sealed class LoadingSnapshot
    {
        [JsonPropertyName("busy")]
        public bool Busy { get; set; }

        [JsonPropertyName("live")]
        public List<string> Live { get; set; } = [];
    }
}
