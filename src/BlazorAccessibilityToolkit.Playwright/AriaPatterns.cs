using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Playwright;

namespace BlazorAccessibilityToolkit.Playwright;

/// <summary>One requirement of a pattern verification.</summary>
public sealed record PatternCheck(string Requirement, bool Passed, string? Detail = null)
{
    /// <inheritdoc />
    public override string ToString() => $"[{(Passed ? "pass" : "FAIL")}] {Requirement}{(Detail is null ? string.Empty : " — " + Detail)}";
}

/// <summary>Result of verifying a widget against a WAI-ARIA Authoring Practices keyboard and semantics pattern.</summary>
public sealed record PatternVerification
{
    /// <summary>Pattern name.</summary>
    public required string Pattern { get; init; }

    /// <summary>Checks in execution order.</summary>
    public IReadOnlyList<PatternCheck> Checks { get; init; } = [];

    /// <summary>True when every check passed.</summary>
    public bool Passed => Checks.All(c => c.Passed);

    /// <summary>Human readable checklist.</summary>
    public string Describe() => $"{Pattern}:{Environment.NewLine}" + string.Join(Environment.NewLine, Checks.Select(c => "  " + c));

    /// <summary>Throws <see cref="AccessibilityAssertionException"/> listing the failed requirements.</summary>
    public PatternVerification ShouldPass()
    {
        if (!Passed)
        {
            throw new AccessibilityAssertionException($"The widget does not follow the {Pattern} pattern.{Environment.NewLine}{Describe()}{Environment.NewLine}Reference: https://www.w3.org/WAI/ARIA/apg/patterns/");
        }

        return this;
    }
}

/// <summary>Settings for pattern verification.</summary>
public sealed record PatternOptions
{
    /// <summary>How long to wait for each expected state change. Default 3 s.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(3);
}

/// <summary>
/// Reusable verifications of common widgets against the WAI-ARIA Authoring Practices Guide (APG): modal dialog,
/// disclosure, tabs and menu button. They drive the widget with real key presses and record every requirement as a
/// <see cref="PatternCheck"/>, so one run shows all problems. They check the documented keyboard interaction and
/// ARIA states; they do not replace testing with assistive technologies.
/// </summary>
public static class AriaPatterns
{
    /// <summary>
    /// Modal dialog: the trigger opens it with Enter, focus moves inside, Tab and Shift+Tab stay inside, Escape closes
    /// it and focus returns to the trigger. The dialog must be named and modal.
    /// </summary>
    public static async Task<PatternVerification> VerifyModalDialogAsync(ILocator trigger, ILocator? dialog = null, PatternOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        options ??= new PatternOptions();
        var page = trigger.Page;
        dialog ??= page.Locator("[role=dialog], [role=alertdialog], dialog[open]");
        var checks = new List<PatternCheck>();

        var triggerInfo = await trigger.GetAccessibilityInfoAsync().ConfigureAwait(false);
        checks.Add(new("Trigger is a named button", triggerInfo.Role == "button" && triggerInfo.Name.Length > 0, triggerInfo.ToString()));

        await trigger.FocusAsync().ConfigureAwait(false);
        await page.Keyboard.PressAsync("Enter").ConfigureAwait(false);
        var opened = await WaitAsync(async () => await dialog.CountAsync().ConfigureAwait(false) == 1 && await dialog.IsVisibleAsync().ConfigureAwait(false), options).ConfigureAwait(false);
        checks.Add(new("Enter on the trigger opens the dialog", opened));
        if (!opened)
        {
            return Result("Modal dialog", checks);
        }

        var info = await dialog.GetAccessibilityInfoAsync().ConfigureAwait(false);
        checks.Add(new("Dialog has an accessible name", info.Name.Length > 0, info.ToString()));
        var modal = info.State("modal") == "true" || await dialog.EvaluateAsync<bool>("d => { try { return d.matches(':modal'); } catch { return false; } }").ConfigureAwait(false);
        checks.Add(new("Dialog is modal (aria-modal=\"true\" or showModal())", modal));

        var focusInside = await WaitAsync(() => ContainsFocusAsync(dialog), options).ConfigureAwait(false);
        checks.Add(new("Focus moves into the dialog when it opens", focusInside, (await page.GetFocusAsync().ConfigureAwait(false)).ToString()));

        var tabbable = await dialog.EvaluateAsync<int>("d => window.__blazorA11y.tabbables(window.__blazorA11y.selectorFor(d)).length").ConfigureAwait(false);
        var contained = true;
        string? escapedTo = null;
        foreach (var key in new[] { "Tab", "Shift+Tab" })
        {
            for (var i = 0; i < Math.Max(2, tabbable + 1) && contained; i++)
            {
                await page.Keyboard.PressAsync(key).ConfigureAwait(false);

                // A trap may move focus back asynchronously (for example from a Blazor Server event handler).
                if (!await WaitAsync(() => ContainsFocusAsync(dialog), options).ConfigureAwait(false))
                {
                    contained = false;
                    escapedTo = $"{key} moved focus to {await page.GetFocusAsync().ConfigureAwait(false)}";
                }
            }
        }

        checks.Add(new("Tab and Shift+Tab keep focus inside the dialog", contained, escapedTo));

        if (!await ContainsFocusAsync(dialog).ConfigureAwait(false))
        {
            await dialog.Locator(":is(button, [href], input, select, textarea, [tabindex]:not([tabindex='-1']))").First.FocusAsync().ConfigureAwait(false);
        }

        await page.Keyboard.PressAsync("Escape").ConfigureAwait(false);
        var closed = await WaitAsync(async () => await dialog.CountAsync().ConfigureAwait(false) == 0 || !await dialog.First.IsVisibleAsync().ConfigureAwait(false), options).ConfigureAwait(false);
        checks.Add(new("Escape closes the dialog", closed));
        if (closed)
        {
            var restored = await WaitAsync(() => IsFocusedAsync(trigger), options).ConfigureAwait(false);
            checks.Add(new("Focus returns to the trigger when the dialog closes", restored, (await page.GetFocusAsync().ConfigureAwait(false)).ToString()));
        }

        return Result("Modal dialog", checks);
    }

    /// <summary>
    /// Disclosure: a named button with aria-expanded; Enter and Space toggle aria-expanded and the visibility of the
    /// element referenced by aria-controls, and focus stays on the button.
    /// </summary>
    public static async Task<PatternVerification> VerifyDisclosureAsync(ILocator button, PatternOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(button);
        options ??= new PatternOptions();
        var checks = new List<PatternCheck>();
        var info = await button.GetAccessibilityInfoAsync().ConfigureAwait(false);
        checks.Add(new("Control is a named button", info.Role == "button" && info.Name.Length > 0, info.ToString()));
        var initial = info.State("expanded");
        checks.Add(new("Button has aria-expanded=\"true\" or \"false\"", initial is "true" or "false", $"aria-expanded={initial ?? "(absent)"}"));
        if (initial is not ("true" or "false"))
        {
            return Result("Disclosure", checks);
        }

        var controls = await button.GetAttributeAsync("aria-controls").ConfigureAwait(false);
        var panel = string.IsNullOrWhiteSpace(controls) ? null : button.Page.Locator($"[id=\"{controls.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]}\"]");
        checks.Add(new("aria-controls references the content", panel is not null && await panel.CountAsync().ConfigureAwait(false) == 1, controls));

        await button.FocusAsync().ConfigureAwait(false);
        foreach (var (key, label) in new[] { ("Enter", "Enter"), (" ", "Space") })
        {
            var before = (await button.GetAccessibilityInfoAsync().ConfigureAwait(false)).State("expanded");
            var expected = before == "true" ? "false" : "true";
            await button.Page.Keyboard.PressAsync(key).ConfigureAwait(false);
            var toggled = await WaitAsync(async () => (await button.GetAccessibilityInfoAsync().ConfigureAwait(false)).State("expanded") == expected, options).ConfigureAwait(false);
            checks.Add(new($"{label} toggles aria-expanded to \"{expected}\"", toggled));
            if (panel is not null && await panel.CountAsync().ConfigureAwait(false) == 1)
            {
                var visible = await WaitAsync(async () => await panel.IsVisibleAsync().ConfigureAwait(false) == (expected == "true"), options).ConfigureAwait(false);
                checks.Add(new($"Content is {(expected == "true" ? "shown" : "hidden")} after {label}", visible));
            }

            checks.Add(new($"Focus stays on the button after {label}", await IsFocusedAsync(button).ConfigureAwait(false), (await button.Page.GetFocusAsync().ConfigureAwait(false)).ToString()));
        }

        return Result("Disclosure", checks);
    }

    /// <summary>
    /// Tabs: a named tablist with exactly one selected tab in the tab sequence (roving tabindex); arrow keys move between
    /// tabs (wrapping), Home and End go to the first and last tab, selection follows focus (automatic activation) or
    /// Enter/Space selects (manual activation), and only the selected tab's panel is shown.
    /// </summary>
    public static async Task<PatternVerification> VerifyTabsAsync(ILocator tablist, PatternOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(tablist);
        options ??= new PatternOptions();
        var checks = new List<PatternCheck>();
        var listInfo = await tablist.GetAccessibilityInfoAsync().ConfigureAwait(false);
        checks.Add(new("Container has role tablist and an accessible name", listInfo.Role == "tablist" && listInfo.Name.Length > 0, listInfo.ToString()));

        var tabs = tablist.Locator("[role=tab]");
        var count = await tabs.CountAsync().ConfigureAwait(false);
        checks.Add(new("Tablist contains at least two tabs", count >= 2, count.ToString(CultureInfo.InvariantCulture)));
        if (count < 2)
        {
            return Result("Tabs", checks);
        }

        var states = await TabStatesAsync(tabs, count).ConfigureAwait(false);
        var selected = states.Count(s => s.Selected);
        checks.Add(new("Exactly one tab is selected", selected == 1, selected.ToString(CultureInfo.InvariantCulture)));
        // A null tab index means the tab is not focusable at all, so it is not in the tab sequence.
        checks.Add(new(
            "Only the selected tab is in the tab sequence (roving tabindex)",
            states.All(s => s.Selected ? s.TabIndex >= 0 : s.TabIndex is null or < 0),
            string.Join(", ", states.Select(s => $"{s.Name}:{(s.TabIndex is { } index ? index.ToString(CultureInfo.InvariantCulture) : "not focusable")}"))));
        checks.Add(new("Every tab controls a tabpanel", states.All(s => s.PanelExists), null));
        checks.Add(new("Only the selected tab's panel is shown", states.All(s => !s.PanelExists || s.PanelVisible == s.Selected)));

        var start = Math.Max(0, Array.FindIndex(states, s => s.Selected));
        await tabs.Nth(start).FocusAsync().ConfigureAwait(false);

        async Task<bool> MovesTo(string key, int target) =>
            await Press(tablist.Page, key).ConfigureAwait(false) && await WaitAsync(() => IsFocusedAsync(tabs.Nth(target)), options).ConfigureAwait(false);

        var next = (start + 1) % count;
        checks.Add(new("ArrowRight moves focus to the next tab", await MovesTo("ArrowRight", next).ConfigureAwait(false)));
        var automatic = await WaitAsync(async () => await tabs.Nth(next).GetAttributeAsync("aria-selected").ConfigureAwait(false) == "true", options with { Timeout = TimeSpan.FromMilliseconds(Math.Min(options.Timeout.TotalMilliseconds, 750)) }).ConfigureAwait(false);
        if (!automatic)
        {
            await tablist.Page.Keyboard.PressAsync("Enter").ConfigureAwait(false);
            var manual = await WaitAsync(async () => await tabs.Nth(next).GetAttributeAsync("aria-selected").ConfigureAwait(false) == "true", options).ConfigureAwait(false);
            checks.Add(new("Selection follows focus, or Enter selects the focused tab", manual, "manual activation"));
        }
        else
        {
            checks.Add(new("Selection follows focus, or Enter selects the focused tab", true, "automatic activation"));
        }

        checks.Add(new("End moves focus to the last tab", await MovesTo("End", count - 1).ConfigureAwait(false)));
        checks.Add(new("ArrowRight on the last tab wraps to the first", await MovesTo("ArrowRight", 0).ConfigureAwait(false)));
        checks.Add(new("ArrowLeft on the first tab wraps to the last", await MovesTo("ArrowLeft", count - 1).ConfigureAwait(false)));
        checks.Add(new("Home moves focus to the first tab", await MovesTo("Home", 0).ConfigureAwait(false)));

        var after = await TabStatesAsync(tabs, count).ConfigureAwait(false);
        checks.Add(new("Panels still match the selection after keyboard interaction", after.All(s => !s.PanelExists || s.PanelVisible == s.Selected)));
        return Result("Tabs", checks);
    }

    /// <summary>
    /// Menu button: a named button with aria-haspopup and aria-expanded; Enter opens the menu and focuses the first item,
    /// ArrowDown/ArrowUp move between items (wrapping), Escape closes the menu and returns focus to the button.
    /// </summary>
    public static async Task<PatternVerification> VerifyMenuButtonAsync(ILocator button, ILocator? menu = null, PatternOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(button);
        options ??= new PatternOptions();
        var page = button.Page;
        var checks = new List<PatternCheck>();
        var info = await button.GetAccessibilityInfoAsync().ConfigureAwait(false);
        checks.Add(new("Control is a named button", info.Role == "button" && info.Name.Length > 0, info.ToString()));
        checks.Add(new("Button has aria-haspopup", info.State("haspopup") is "true" or "menu", info.State("haspopup")));
        checks.Add(new("Button has aria-expanded=\"false\" while closed", info.State("expanded") == "false", info.State("expanded") ?? "(absent)"));

        if (menu is null)
        {
            var controls = await button.GetAttributeAsync("aria-controls").ConfigureAwait(false);
            menu = string.IsNullOrWhiteSpace(controls) ? page.Locator("[role=menu]") : page.Locator($"[id=\"{controls.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]}\"]");
        }

        await button.FocusAsync().ConfigureAwait(false);
        await page.Keyboard.PressAsync("Enter").ConfigureAwait(false);
        var opened = await WaitAsync(async () => await menu.CountAsync().ConfigureAwait(false) == 1 && await menu.IsVisibleAsync().ConfigureAwait(false), options).ConfigureAwait(false);
        checks.Add(new("Enter opens the menu", opened));
        if (!opened)
        {
            return Result("Menu button", checks);
        }

        checks.Add(new("aria-expanded is \"true\" while open", await WaitAsync(async () => (await button.GetAccessibilityInfoAsync().ConfigureAwait(false)).State("expanded") == "true", options).ConfigureAwait(false)));
        var items = menu.Locator("[role=menuitem], [role=menuitemcheckbox], [role=menuitemradio]");
        var count = await items.CountAsync().ConfigureAwait(false);
        checks.Add(new("Menu contains menu items", count > 0, count.ToString(CultureInfo.InvariantCulture)));
        if (count == 0)
        {
            return Result("Menu button", checks);
        }

        checks.Add(new("Focus moves to the first menu item", await WaitAsync(() => IsFocusedAsync(items.First), options).ConfigureAwait(false), (await page.GetFocusAsync().ConfigureAwait(false)).ToString()));
        if (count > 1)
        {
            checks.Add(new("ArrowDown moves to the next item", await Press(page, "ArrowDown").ConfigureAwait(false) && await WaitAsync(() => IsFocusedAsync(items.Nth(1)), options).ConfigureAwait(false)));
            checks.Add(new("ArrowUp moves to the previous item", await Press(page, "ArrowUp").ConfigureAwait(false) && await WaitAsync(() => IsFocusedAsync(items.First), options).ConfigureAwait(false)));
            checks.Add(new("ArrowUp on the first item wraps to the last", await Press(page, "ArrowUp").ConfigureAwait(false) && await WaitAsync(() => IsFocusedAsync(items.Nth(count - 1)), options).ConfigureAwait(false)));
        }

        await page.Keyboard.PressAsync("Escape").ConfigureAwait(false);
        var closed = await WaitAsync(async () => await menu.CountAsync().ConfigureAwait(false) == 0 || !await menu.IsVisibleAsync().ConfigureAwait(false), options).ConfigureAwait(false);
        checks.Add(new("Escape closes the menu", closed));
        checks.Add(new("Focus returns to the button", await WaitAsync(() => IsFocusedAsync(button), options).ConfigureAwait(false), (await page.GetFocusAsync().ConfigureAwait(false)).ToString()));
        checks.Add(new("aria-expanded is \"false\" after closing", await WaitAsync(async () => (await button.GetAccessibilityInfoAsync().ConfigureAwait(false)).State("expanded") == "false", options).ConfigureAwait(false)));
        return Result("Menu button", checks);
    }

    private static PatternVerification Result(string pattern, List<PatternCheck> checks) => new() { Pattern = pattern, Checks = checks };

    private static async Task<bool> Press(IPage page, string key)
    {
        await page.Keyboard.PressAsync(key).ConfigureAwait(false);
        return true;
    }

    private static async Task<(string Name, bool Selected, int? TabIndex, bool PanelExists, bool PanelVisible)[]> TabStatesAsync(ILocator tabs, int count)
    {
        var result = new (string, bool, int?, bool, bool)[count];
        for (var i = 0; i < count; i++)
        {
            var tab = tabs.Nth(i);
            var info = await tab.GetAccessibilityInfoAsync().ConfigureAwait(false);
            var controls = await tab.GetAttributeAsync("aria-controls").ConfigureAwait(false);
            var panel = string.IsNullOrWhiteSpace(controls) ? null : tab.Page.Locator($"[id=\"{controls.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]}\"][role=tabpanel]");
            var exists = panel is not null && await panel.CountAsync().ConfigureAwait(false) == 1;
            result[i] = (info.Name, info.State("selected") == "true", info.TabIndex, exists, exists && await panel!.IsVisibleAsync().ConfigureAwait(false));
        }

        return result;
    }

    private static async Task<bool> IsFocusedAsync(ILocator locator)
    {
        if (await locator.CountAsync().ConfigureAwait(false) != 1)
        {
            return false;
        }

        var focus = await locator.Page.GetFocusAsync().ConfigureAwait(false);
        var info = await locator.GetAccessibilityInfoAsync().ConfigureAwait(false);
        return focus.Element?.ElementKey == info.ElementKey;
    }

    private static async Task<bool> ContainsFocusAsync(ILocator container)
    {
        if (await container.CountAsync().ConfigureAwait(false) != 1)
        {
            return false;
        }

        return await container.EvaluateAsync<bool>("el => { const a = window.__blazorA11y.activeElementDeep(); return !!a && (el === a || el.contains(a)); }").ConfigureAwait(false);
    }

    private static async Task<bool> WaitAsync(Func<Task<bool>> condition, PatternOptions options)
    {
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            if (await condition().ConfigureAwait(false))
            {
                return true;
            }

            if (stopwatch.Elapsed >= options.Timeout)
            {
                return false;
            }

            await Task.Delay(25).ConfigureAwait(false);
        }
    }
}
