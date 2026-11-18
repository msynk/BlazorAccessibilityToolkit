using System.Text.Json;
using BlazorAccessibilityToolkit.Playwright.Internal;
using Microsoft.Playwright;

namespace BlazorAccessibilityToolkit.Playwright;

/// <summary>
/// Checks what assistive technology users experience when a Blazor app changes pages. Blazor's router and enhanced
/// navigation replace page content without a document load, so browsers do not announce the new page: the app has to
/// update the title (<c>&lt;PageTitle&gt;</c>) and move focus to the new content (<c>&lt;FocusOnNavigate&gt;</c>) or
/// announce the change itself.
/// </summary>
public static class NavigationChecks
{
    private const string StateScript = """
        () => {
          const a = window.__blazorA11y.activeElementDeep();
          return {
            documentId: String(performance.timeOrigin),
            title: document.title || '',
            lang: document.documentElement.getAttribute('lang') || '',
            focus: a ? window.__blazorA11y.selectorFor(a) : null,
            focusInNewContent: !!a && !!a.closest('main, [role=main], h1, [role=heading][aria-level="1"]'),
            focusIsHeading: !!a && /^(h[1-6])$/.test(a.localName) || !!a && a.getAttribute('role') === 'heading',
          };
        }
        """;

    /// <summary>
    /// Activates <paramref name="link"/> with Enter and verifies the page change: the URL changes, the new page has a
    /// title that differs from the previous one (WCAG 2.4.2) and a language (3.1.1), and after a client-side navigation,
    /// focus moves into the new content (for example its heading) or the change is announced in a live region, instead
    /// of staying on the link or being lost (2.4.3, 4.1.3). A full document load passes the focus check, because
    /// browsers announce new documents themselves.
    /// </summary>
    public static async Task<PatternVerification> VerifyNavigationAsync(ILocator link, PatternOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(link);
        options ??= new PatternOptions();
        var page = link.Page;
        var checks = new List<PatternCheck>();
        await ToolkitScript.EnsureAsync(page).ConfigureAwait(false);
        var monitor = await LiveRegionMonitor.StartAsync(page).ConfigureAwait(false);
        var before = await StateAsync(page).ConfigureAwait(false);
        var urlBefore = page.Url;
        var linkSelector = await link.EvaluateAsync<string>("el => window.__blazorA11y.selectorFor(el)").ConfigureAwait(false);

        await link.FocusAsync().ConfigureAwait(false);
        await page.Keyboard.PressAsync("Enter").ConfigureAwait(false);
        var navigated = await Wait(() => Task.FromResult(page.Url != urlBefore), options).ConfigureAwait(false);
        checks.Add(new("Enter on the link changes the page URL", navigated, page.Url));
        if (!navigated)
        {
            return new PatternVerification { Pattern = "Page navigation", Checks = checks };
        }

        // Let the new page render: wait for the title to change, then for focus management to run.
        NavigationState? after = null;
        await Wait(async () => (after = await TryStateAsync(page).ConfigureAwait(false)) is { } s && s.Title != before.Title, options).ConfigureAwait(false);
        await page.WaitForTimeoutAsync(200).ConfigureAwait(false);
        after = await TryStateAsync(page).ConfigureAwait(false) ?? after ?? new NavigationState();

        checks.Add(new("The new page has a title", after.Title.Trim().Length > 0, $"\"{after.Title}\""));
        checks.Add(new("The title differs from the previous page's title", !string.Equals(after.Title, before.Title, StringComparison.Ordinal), $"before \"{before.Title}\", after \"{after.Title}\""));
        checks.Add(new("The page still declares its language", after.Lang.Trim().Length > 0, after.Lang));

        var fullLoad = after.DocumentId != before.DocumentId;
        if (fullLoad)
        {
            checks.Add(new("The new page is announced (full document load)", true, "the browser loaded a new document"));
        }
        else
        {
            var changes = await monitor.GetChangesAsync().ConfigureAwait(false);
            var announced = changes.Any(c => c.Kind != LiveRegionChangeKind.ContentRemoved && !c.Hidden && (c.AddedText ?? c.Text ?? string.Empty).Trim().Length > 0);
            var focusMoved = after.Focus is not null && after.Focus != linkSelector && (after.FocusInNewContent || after.FocusIsHeading);
            var detail = after.Focus is null
                ? "focus was lost (it is on the document body)"
                : after.Focus == linkSelector ? "focus stayed on the link" : $"focus is on {after.Focus}";
            checks.Add(new(
                "Focus moves to the new content (for example with <FocusOnNavigate Selector=\"h1\">) or the page change is announced",
                focusMoved || announced,
                detail + (announced ? "; a live region announced the change" : string.Empty)));
        }

        return new PatternVerification { Pattern = "Page navigation", Checks = checks };
    }

    private static async Task<NavigationState> StateAsync(IPage page)
    {
        await ToolkitScript.EnsureAsync(page).ConfigureAwait(false);
        return ToolkitScript.Deserialize<NavigationState>(await page.EvaluateAsync<JsonElement>(StateScript).ConfigureAwait(false)) ?? new NavigationState();
    }

    private static async Task<NavigationState?> TryStateAsync(IPage page)
    {
        try
        {
            return await StateAsync(page).ConfigureAwait(false);
        }
        catch (PlaywrightException)
        {
            // The page is between documents.
            return null;
        }
    }

    private static async Task<bool> Wait(Func<Task<bool>> condition, PatternOptions options)
    {
        var deadline = DateTime.UtcNow + options.Timeout;
        while (true)
        {
            if (await condition().ConfigureAwait(false))
            {
                return true;
            }

            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(25).ConfigureAwait(false);
        }
    }

    private sealed class NavigationState
    {
        public string DocumentId { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string Lang { get; set; } = string.Empty;

        public string? Focus { get; set; }

        public bool FocusInNewContent { get; set; }

        public bool FocusIsHeading { get; set; }
    }

    /// <summary>
    /// Verifies that focus a user places while a prerendered page is loading survives the moment the page becomes
    /// interactive (WCAG 2.4.3, 3.2.1). Blazor's <c>&lt;FocusOnNavigate&gt;</c> runs on the router's first interactive
    /// render and moves focus to its selector, so a keyboard user who already tabbed into the page is sent back to the
    /// heading; re-rendered elements can also lose focus to the document body.
    /// </summary>
    /// <param name="page">The page to load <paramref name="url"/> in.</param>
    /// <param name="url">The prerendered page.</param>
    /// <param name="target">The element to focus as soon as it is rendered, for example a navigation link.</param>
    /// <param name="isInteractive">Reports whether the page is interactive, for example by checking a marker your layout sets in <c>OnAfterRender</c>.</param>
    /// <param name="timeout">How long to wait for interactivity. Default 10 s.</param>
    public static async Task<PatternVerification> VerifyFocusSurvivesInteractivityAsync(IPage page, string url, Func<IPage, ILocator> target, Func<IPage, Task<bool>> isInteractive, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(isInteractive);
        var checks = new List<PatternCheck>();
        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.Commit }).ConfigureAwait(false);
        var element = target(page);
        await element.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached }).ConfigureAwait(false);

        if (await isInteractive(page).ConfigureAwait(false))
        {
            checks.Add(new("The target receives focus before the page is interactive", false, "the page was already interactive; slow the connection down (for example with page.RouteAsync and a delay for _framework/blazor.web.js) to verify the prerendered phase"));
            return new PatternVerification { Pattern = "Focus during prerendering", Checks = checks };
        }

        await element.FocusAsync().ConfigureAwait(false);
        await ToolkitScript.EnsureAsync(page).ConfigureAwait(false);
        var focused = await page.GetFocusAsync().ConfigureAwait(false);
        var before = focused.Element is { } was ? (was.Name + "|" + was.TagName + "|" + await FocusOrdinalAsync(page).ConfigureAwait(false)) : null;
        checks.Add(new("The target receives focus before the page is interactive", before is not null, before));
        if (before is null)
        {
            return new PatternVerification { Pattern = "Focus during prerendering", Checks = checks };
        }

        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        var interactive = false;
        while (DateTime.UtcNow < deadline && !(interactive = await isInteractive(page).ConfigureAwait(false)))
        {
            await Task.Delay(25).ConfigureAwait(false);
        }

        checks.Add(new("The page becomes interactive", interactive));
        if (!interactive)
        {
            return new PatternVerification { Pattern = "Focus during prerendering", Checks = checks };
        }

        // Let first-render work (OnAfterRender, FocusOnNavigate) finish.
        await page.WaitForTimeoutAsync(300).ConfigureAwait(false);
        await ToolkitScript.EnsureAsync(page).ConfigureAwait(false);
        var focus = await page.GetFocusAsync().ConfigureAwait(false);
        var after = focus.Element is { } now ? (now.Name + "|" + now.TagName + "|" + await FocusOrdinalAsync(page).ConfigureAwait(false)) : null;
        var kept = after is not null && string.Equals(Normalize(after), Normalize(before), StringComparison.Ordinal);
        checks.Add(new("Focus stays on the target when the page becomes interactive", kept, $"focus moved to {focus}"));
        return new PatternVerification { Pattern = "Focus during prerendering", Checks = checks };
    }

    // The position of the focused element among the elements with the same tag and text, so that focus moving to a
    // sibling with the same name (a second "Next" button) is not mistaken for the restored element.
    private static Task<int> FocusOrdinalAsync(IPage page) => page.EvaluateAsync<int>("""
        () => {
          const a = window.__blazorA11y.activeElementDeep();
          if (!a) return -1;
          const text = e => (e.textContent || '').replace(/\s+/g, ' ').trim();
          return Array.from(document.getElementsByTagName(a.localName)).filter(e => text(e) === text(a)).indexOf(a);
        }
        """);

    private static string Normalize(string value) => string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
}
