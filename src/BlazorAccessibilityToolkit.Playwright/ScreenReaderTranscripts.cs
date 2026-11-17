using System.Text.Json;
using BlazorAccessibilityToolkit.Playwright.Internal;
using Microsoft.Playwright;

namespace BlazorAccessibilityToolkit.Playwright;

/// <summary>
/// The accessibility tree of a page or region, computed in the browser with axe-core's role and name implementation,
/// and the virtual screen-reader transcript of it (see <see cref="ScreenReaderTranscript"/>). Unlike ARIA snapshots,
/// the transcript includes "false" states (<c>not pressed</c>, <c>collapsed</c>), <c>aria-current</c>, required fields
/// and descriptions, and it uses the same format as the bUnit transcript of ComponentTesting.
/// </summary>
/// <remarks>The transcript is not what a particular screen reader says; test with real assistive technologies for conformance.</remarks>
public static class ScreenReaderTranscripts
{
    /// <summary>Computes the accessibility tree of the element (included) and its content.</summary>
    public static async Task<IReadOnlyList<AccessibilityNode>> GetAccessibilityTreeAsync(this ILocator locator)
    {
        ArgumentNullException.ThrowIfNull(locator);
        await ToolkitScript.EnsureAsync(locator.Page).ConfigureAwait(false);
        var json = await locator.EvaluateAsync<JsonElement>("el => window.__blazorA11y.accessibilityTree({ element: el })").ConfigureAwait(false);
        return ToolkitScript.Deserialize<AccessibilityNode[]>(json) ?? [];
    }

    /// <summary>Computes the accessibility tree of the page's body.</summary>
    public static async Task<IReadOnlyList<AccessibilityNode>> GetAccessibilityTreeAsync(this IPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        await ToolkitScript.EnsureAsync(page).ConfigureAwait(false);
        var json = await page.EvaluateAsync<JsonElement>("() => window.__blazorA11y.accessibilityTree({})").ConfigureAwait(false);
        return ToolkitScript.Deserialize<AccessibilityNode[]>(json) ?? [];
    }

    /// <summary>The transcript of the element and its content (lines separated by <c>\n</c>).</summary>
    public static async Task<string> GetScreenReaderTranscriptAsync(this ILocator locator) =>
        ScreenReaderTranscript.Format(await locator.GetAccessibilityTreeAsync().ConfigureAwait(false));

    /// <summary>The transcript of the page (lines separated by <c>\n</c>).</summary>
    public static async Task<string> GetScreenReaderTranscriptAsync(this IPage page) =>
        ScreenReaderTranscript.Format(await page.GetAccessibilityTreeAsync().ConfigureAwait(false));

    /// <summary>
    /// Asserts that the transcript of the element equals <paramref name="expected"/> (lines trimmed, blank lines ignored).
    /// Retries until <paramref name="timeout"/> (default 5 s) so that asynchronous rendering can finish, then fails with a line diff.
    /// </summary>
    public static async Task ShouldMatchTranscriptAsync(this ILocator locator, string expected, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(expected);
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (true)
        {
            var actual = await locator.GetScreenReaderTranscriptAsync().ConfigureAwait(false);
            var difference = ScreenReaderTranscript.Compare(expected, actual);
            if (difference is null)
            {
                return;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new AccessibilityAssertionException(difference);
            }

            await Task.Delay(100).ConfigureAwait(false);
        }
    }
}
