using System.Globalization;
using Microsoft.Playwright;

namespace BlazorAccessibilityToolkit.Playwright;

public static partial class AriaPatterns
{
    /// <summary>
    /// Sortable table (WAI-ARIA Authoring Practices "Sortable Table", as rendered by QuickGrid with <c>Sortable</c>
    /// columns): sortable column headers contain a button; at most one header has <c>aria-sort</c> other than
    /// <c>none</c>; Enter on a header button sorts by that column, sets its <c>aria-sort</c> to ascending or descending
    /// and clears the others; pressing it again reverses the direction; focus stays on the button; and the rows are in
    /// the announced order.
    /// </summary>
    /// <param name="table">The table (or grid).</param>
    /// <param name="column">Zero-based index of the sortable header to verify. Default: the first sortable header.</param>
    /// <param name="options">Timeouts.</param>
    public static async Task<PatternVerification> VerifySortableTableAsync(ILocator table, int? column = null, PatternOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        options ??= new PatternOptions();
        var page = table.Page;
        var checks = new List<PatternCheck>();
        var headers = table.Locator("thead th, [role=columnheader]");
        var count = await headers.CountAsync().ConfigureAwait(false);
        var sortable = new List<int>();
        for (var i = 0; i < count; i++)
        {
            if (await headers.Nth(i).Locator("button, [role=button]").CountAsync().ConfigureAwait(false) > 0)
            {
                sortable.Add(i);
            }
        }

        checks.Add(new("Sortable column headers contain a button", sortable.Count > 0, $"{sortable.Count} of {count} headers"));
        if (sortable.Count == 0)
        {
            return Result("Sortable table", checks);
        }

        var initial = await SortStatesAsync(headers, count).ConfigureAwait(false);
        var sorted = initial.Count(s => s is "ascending" or "descending" or "other");
        checks.Add(new("At most one header has aria-sort other than none", sorted <= 1, string.Join(", ", initial.Select((s, i) => $"{i + 1}:{s ?? "(absent)"}"))));

        var index = column ?? sortable[0];
        var header = headers.Nth(index);
        var button = header.Locator("button, [role=button]").First;
        var name = (await button.GetAccessibilityInfoAsync().ConfigureAwait(false)).Name;
        checks.Add(new("The sort button has an accessible name", name.Length > 0, name));

        string? first = null;
        foreach (var round in new[] { "first", "second" })
        {
            var before = (await SortStatesAsync(headers, count).ConfigureAwait(false))[index];
            await button.FocusAsync().ConfigureAwait(false);
            await page.Keyboard.PressAsync("Enter").ConfigureAwait(false);
            string? after = null;
            var changed = await WaitAsync(async () =>
            {
                after = (await SortStatesAsync(headers, count).ConfigureAwait(false))[index];
                return after is "ascending" or "descending" && (round == "first" ? after != before || before is null or "none" : after != first);
            }, options).ConfigureAwait(false);

            if (round == "first")
            {
                checks.Add(new("Enter on the header button sets aria-sort to ascending or descending", changed, $"aria-sort={after ?? "(absent)"}"));
                first = after;
                var others = (await SortStatesAsync(headers, count).ConfigureAwait(false)).Where((s, i) => i != index && s is "ascending" or "descending" or "other").Count();
                checks.Add(new("Other headers no longer report a sort direction", others == 0));
            }
            else
            {
                checks.Add(new("Pressing it again reverses the direction", changed, $"{first} then {after ?? "(absent)"}"));
            }

            checks.Add(new($"Focus stays on the sort button ({round} press)", await WaitAsync(() => IsFocusedAsync(button), options).ConfigureAwait(false), (await page.GetFocusAsync().ConfigureAwait(false)).ToString()));
            if (changed && after is "ascending" or "descending")
            {
                var values = await table.Locator("tbody tr").EvaluateAllAsync<string[]>("(rows, i) => rows.map(r => (r.children[i] ? r.children[i].textContent : '').trim())", index).ConfigureAwait(false);
                checks.Add(new($"Rows are in {after} order ({round} press)", IsOrdered(values, after == "ascending"), values.Length < 2 ? "fewer than two rows to compare" : string.Join(", ", values.Take(6))));
            }
        }

        return Result("Sortable table", checks);
    }

    /// <summary>
    /// Pagination (for example QuickGrid's <c>Paginator</c>): the "next page" control is a named button; Enter shows the
    /// next page of <paramref name="content"/>; focus is never lost, including on the last page, where the control
    /// usually becomes disabled (a focused button that becomes disabled drops focus to the document body); and the page
    /// change is conveyed to screen reader users through a live region or a focus move into the content (WCAG 2.4.3,
    /// 4.1.3).
    /// </summary>
    /// <param name="next">The control that shows the next page.</param>
    /// <param name="content">The paged content, for example the table.</param>
    /// <param name="options">Timeouts.</param>
    public static async Task<PatternVerification> VerifyPaginationAsync(ILocator next, ILocator content, PatternOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(content);
        options ??= new PatternOptions();
        var page = next.Page;
        var checks = new List<PatternCheck>();
        var info = await next.GetAccessibilityInfoAsync().ConfigureAwait(false);
        checks.Add(new("The next-page control is a named button", info.Role == "button" && info.Name.Length > 0, info.ToString()));

        var monitor = await LiveRegionMonitor.StartAsync(page).ConfigureAwait(false);
        var before = PlaywrightAccessibility.Normalize(await content.InnerTextAsync().ConfigureAwait(false));
        await next.FocusAsync().ConfigureAwait(false);
        await page.Keyboard.PressAsync("Enter").ConfigureAwait(false);
        var changed = await WaitAsync(async () => PlaywrightAccessibility.Normalize(await content.InnerTextAsync().ConfigureAwait(false)) != before, options).ConfigureAwait(false);
        checks.Add(new("Enter shows the next page", changed));
        if (!changed)
        {
            return Result("Pagination", checks);
        }

        var focus = await page.GetFocusAsync().ConfigureAwait(false);
        var focusInContent = await content.EvaluateAsync<bool>("el => { const a = window.__blazorA11y.activeElementDeep(); return !!a && el.contains(a); }").ConfigureAwait(false);
        var updates = (await monitor.GetChangesAsync().ConfigureAwait(false)).Where(c => !c.Hidden && (c.AddedText.Length > 0 || c.Text.Length > 0)).ToArray();
        checks.Add(new(
            "The page change is conveyed (a live region update, or focus moves into the content)",
            focusInContent || updates.Length > 0,
            updates.Length > 0 ? "live region: " + string.Join(" | ", updates.Select(u => u.AddedText.Length > 0 ? u.AddedText : u.Text)) : $"no live region update; focus on {focus}"));

        // Go to the last page: focus must survive the control becoming disabled.
        var lostAt = (string?)null;
        for (var i = 0; i < 50; i++)
        {
            var current = await page.GetFocusAsync().ConfigureAwait(false);
            if (current.Element is null)
            {
                lostAt = i == 0 ? "after the first page change" : $"after {i + 1} page changes";
                break;
            }

            var disabled = await next.EvaluateAsync<bool>("b => b.disabled || b.getAttribute('aria-disabled') === 'true'").ConfigureAwait(false);
            if (disabled || !await IsFocusedAsync(next).ConfigureAwait(false))
            {
                break;
            }

            var text = PlaywrightAccessibility.Normalize(await content.InnerTextAsync().ConfigureAwait(false));
            await page.Keyboard.PressAsync("Enter").ConfigureAwait(false);
            await WaitAsync(async () => PlaywrightAccessibility.Normalize(await content.InnerTextAsync().ConfigureAwait(false)) != text, options with { Timeout = TimeSpan.FromMilliseconds(Math.Min(options.Timeout.TotalMilliseconds, 1000)) }).ConfigureAwait(false);
        }

        if (lostAt is null && (await page.GetFocusAsync().ConfigureAwait(false)).Element is null)
        {
            lostAt = "on the last page, when the control became disabled";
        }

        checks.Add(new("Focus is never lost to the document body", lostAt is null, lostAt is null ? (await page.GetFocusAsync().ConfigureAwait(false)).ToString() : $"focus was lost {lostAt}"));
        return Result("Pagination", checks);
    }

    private static async Task<string?[]> SortStatesAsync(ILocator headers, int count)
    {
        var states = new string?[count];
        for (var i = 0; i < count; i++)
        {
            states[i] = (await headers.Nth(i).GetAttributeAsync("aria-sort").ConfigureAwait(false))?.Trim().ToLowerInvariant();
        }

        return states;
    }

    // Numbers compare as numbers; text is accepted in the culture's order or an ordinal order (with or without case),
    // since apps sort with different collations. Fewer than two rows prove nothing.
    private static bool IsOrdered(IReadOnlyList<string> values, bool ascending)
    {
        if (values.Count < 2)
        {
            return false;
        }

        if (values.All(v => double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out _)))
        {
            var numbers = values.Select(v => double.Parse(v, NumberStyles.Any, CultureInfo.InvariantCulture)).ToArray();
            return InOrder(numbers, Comparer<double>.Default, ascending);
        }

        return new IComparer<string>[] { StringComparer.CurrentCultureIgnoreCase, StringComparer.OrdinalIgnoreCase, StringComparer.Ordinal }
            .Any(comparer => InOrder(values, comparer, ascending));
    }

    private static bool InOrder<T>(IReadOnlyList<T> values, IComparer<T> comparer, bool ascending)
    {
        for (var i = 1; i < values.Count; i++)
        {
            var comparison = comparer.Compare(values[i - 1], values[i]);
            if (ascending ? comparison > 0 : comparison < 0)
            {
                return false;
            }
        }

        return true;
    }
}
