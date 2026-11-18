using System.Globalization;
using System.Text.Json;
using BlazorAccessibilityToolkit.Playwright.Internal;
using Microsoft.Playwright;

namespace BlazorAccessibilityToolkit.Playwright;

/// <summary>Verifications of composite widgets and form controls.</summary>
public static partial class AriaPatterns
{
    private const string RadioSelector = "[role=radio], input[type=radio]";
    private const string OptionSelector = "[role=option]";

    // Index of the "active" item among the container's items: the focused item, or the item referenced by
    // aria-activedescendant of the focused element. -1 when none.
    private const string ActiveIndexScript = """
        (c, sel) => {
          const items = Array.from(c.querySelectorAll(sel));
          const a = window.__blazorA11y.activeElementDeep();
          if (!a) return -1;
          const id = a.getAttribute('aria-activedescendant');
          const root = a.getRootNode();
          const target = id ? (root.getElementById ? root.getElementById(id) : document.getElementById(id)) : a;
          return target ? items.findIndex(i => i === target || i.contains(target)) : -1;
        }
        """;

    // Indexes of items that are selected or checked.
    private const string SelectedIndexesScript = """
        (c, sel) => Array.from(c.querySelectorAll(sel)).map((el, i) =>
          el.getAttribute('aria-selected') === 'true' || el.getAttribute('aria-checked') === 'true' || el.checked === true ? i : -1).filter(i => i >= 0)
        """;

    /// <summary>
    /// Radio group: a named group of at least two radio buttons with at most one checked, one tab stop for the group,
    /// arrow keys that move focus and check the radio (wrapping at both ends for custom widgets), and Tab that leaves the
    /// group. Works with native <c>input type="radio"</c> (Blazor's <c>InputRadioGroup</c>) and with <c>role="radio"</c>
    /// widgets; for native radio buttons the tab stop and wrapping are browser behavior and are not checked.
    /// </summary>
    public static async Task<PatternVerification> VerifyRadioGroupAsync(ILocator group, PatternOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(group);
        options ??= new PatternOptions();
        var page = group.Page;
        var checks = new List<PatternCheck>();
        var info = await group.GetAccessibilityInfoAsync().ConfigureAwait(false);
        checks.Add(new("Group has role radiogroup (or is a fieldset) and an accessible name", info.Role is "radiogroup" or "group" && info.Name.Length > 0, info.ToString()));

        var radios = group.Locator(RadioSelector);
        var count = await radios.CountAsync().ConfigureAwait(false);
        checks.Add(new("Group contains at least two radio buttons", count >= 2, count.ToString(CultureInfo.InvariantCulture)));
        if (count < 2)
        {
            return Result("Radio group", checks);
        }

        var selected = await SelectedAsync(group, RadioSelector).ConfigureAwait(false);
        checks.Add(new("At most one radio button is checked", selected.Length <= 1, selected.Length.ToString(CultureInfo.InvariantCulture)));

        var native = await group.EvaluateAsync<bool>("(c, s) => Array.from(c.querySelectorAll(s)).every(r => r.localName === 'input')", RadioSelector).ConfigureAwait(false);
        if (!native)
        {
            var tabStops = await group.EvaluateAsync<int>("(c, s) => Array.from(c.querySelectorAll(s)).filter(r => r.tabIndex >= 0 && !r.matches(':disabled')).length", RadioSelector).ConfigureAwait(false);
            checks.Add(new("Only one radio button is in the tab sequence (roving tabindex)", tabStops == 1, tabStops.ToString(CultureInfo.InvariantCulture)));
        }

        var start = selected.Length == 1 ? selected[0] : 0;
        await radios.Nth(start).FocusAsync().ConfigureAwait(false);
        var next = (start + 1) % count;
        var previous = start;
        checks.Add(new("ArrowDown moves focus to the next radio button and checks it", await MovesAndChecksAsync(group, radios, "ArrowDown", next, options).ConfigureAwait(false)));
        checks.Add(new("ArrowUp moves focus to the previous radio button and checks it", await MovesAndChecksAsync(group, radios, "ArrowUp", previous, options).ConfigureAwait(false)));

        // Native radio buttons get their arrow-key behavior from the browser (Safari does not wrap), so wrapping is only
        // required of custom radio widgets.
        if (!native)
        {
            await radios.Nth(0).FocusAsync().ConfigureAwait(false);
            await page.Keyboard.PressAsync(" ").ConfigureAwait(false);
            checks.Add(new("ArrowUp on the first radio button wraps to the last", await MovesAndChecksAsync(group, radios, "ArrowUp", count - 1, options).ConfigureAwait(false)));
            checks.Add(new("ArrowDown on the last radio button wraps to the first", await MovesAndChecksAsync(group, radios, "ArrowDown", 0, options).ConfigureAwait(false)));
        }

        await page.Keyboard.PressAsync("Tab").ConfigureAwait(false);
        checks.Add(new("Tab moves focus out of the group", await WaitAsync(async () => !await ContainsFocusAsync(group).ConfigureAwait(false), options).ConfigureAwait(false), (await page.GetFocusAsync().ConfigureAwait(false)).ToString()));
        return Result("Radio group", checks);
    }

    /// <summary>
    /// Toolbar: a named toolbar with a single tab stop; ArrowRight and ArrowLeft (ArrowDown and ArrowUp for a vertical
    /// toolbar) move focus between its controls, Home and End go to the first and last control, and Tab leaves it.
    /// </summary>
    public static async Task<PatternVerification> VerifyToolbarAsync(ILocator toolbar, PatternOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(toolbar);
        options ??= new PatternOptions();
        var page = toolbar.Page;
        var checks = new List<PatternCheck>();
        await ToolkitScript.EnsureAsync(page).ConfigureAwait(false);
        var info = await toolbar.GetAccessibilityInfoAsync().ConfigureAwait(false);
        var toolbars = await page.Locator("[role=toolbar]").CountAsync().ConfigureAwait(false);
        checks.Add(new("Container has role toolbar", info.Role == "toolbar", info.ToString()));
        checks.Add(new("Toolbar has an accessible name (required when a page has more than one toolbar)", info.Name.Length > 0 || toolbars <= 1, info.Name.Length > 0 ? info.Name : $"{toolbars} toolbar(s) on the page"));

        var selectors = ToolkitScript.Deserialize<string[]>(await toolbar.EvaluateAsync<JsonElement>(
            "c => Array.from(c.querySelectorAll('button:not(:disabled), [role=button], a[href], input:not([type=hidden]):not(:disabled), select:not(:disabled), [role=checkbox], [role=radio], [role=switch], [role=combobox], [role=spinbutton], [tabindex]')).filter(e => window.__blazorA11y.isVisible(e) && !e.closest('[role=menu]')).map(e => window.__blazorA11y.selectorFor(e))").ConfigureAwait(false)) ?? [];
        var controls = selectors.Distinct(StringComparer.Ordinal).Select(s => page.Locator(s)).ToArray();
        checks.Add(new("Toolbar contains at least two controls", controls.Length >= 2, controls.Length.ToString(CultureInfo.InvariantCulture)));
        if (controls.Length < 2)
        {
            return Result("Toolbar", checks);
        }

        var tabStops = 0;
        foreach (var control in controls)
        {
            if ((await control.GetAccessibilityInfoAsync().ConfigureAwait(false)).TabIndex is >= 0)
            {
                tabStops++;
            }
        }

        checks.Add(new("Only one control is in the tab sequence (roving tabindex)", tabStops == 1, tabStops.ToString(CultureInfo.InvariantCulture)));

        var vertical = info.State("orientation") == "vertical";
        var (forward, back) = vertical ? ("ArrowDown", "ArrowUp") : ("ArrowRight", "ArrowLeft");
        await controls[0].FocusAsync().ConfigureAwait(false);
        checks.Add(new($"{forward} moves focus to the next control", await Press(page, forward).ConfigureAwait(false) && await WaitAsync(() => IsFocusedAsync(controls[1]), options).ConfigureAwait(false)));
        checks.Add(new($"{back} moves focus to the previous control", await Press(page, back).ConfigureAwait(false) && await WaitAsync(() => IsFocusedAsync(controls[0]), options).ConfigureAwait(false)));
        checks.Add(new("End moves focus to the last control", await Press(page, "End").ConfigureAwait(false) && await WaitAsync(() => IsFocusedAsync(controls[^1]), options).ConfigureAwait(false)));
        checks.Add(new("Home moves focus to the first control", await Press(page, "Home").ConfigureAwait(false) && await WaitAsync(() => IsFocusedAsync(controls[0]), options).ConfigureAwait(false)));

        await page.Keyboard.PressAsync("Tab").ConfigureAwait(false);
        var left = await WaitAsync(async () => !await ContainsFocusAsync(toolbar).ConfigureAwait(false), options).ConfigureAwait(false);
        var detail = (await page.GetFocusAsync().ConfigureAwait(false)).ToString();
        if (!left && await IsFocusedAsync(controls[0]).ConfigureAwait(false) && await LastTabStopAsync(toolbar).ConfigureAwait(false))
        {
            // Nothing follows the toolbar in the tab order: some browsers (headless Firefox) keep focus on the last
            // element instead of moving it to the browser chrome. Focus did not move inside the toolbar, which is the point.
            left = true;
            detail = "the toolbar is the last tab stop of the page; focus stayed on the same control";
        }

        checks.Add(new("Tab moves focus out of the toolbar", left, detail));
        return Result("Toolbar", checks);
    }

    /// <summary>
    /// Single-select listbox: a named listbox with options, focusable as one tab stop (focus on the listbox with
    /// aria-activedescendant, or roving focus on the options); ArrowDown and ArrowUp move the active option, Home and End
    /// go to the first and last option, and selection follows the active option or Space selects it.
    /// </summary>
    public static async Task<PatternVerification> VerifyListboxAsync(ILocator listbox, PatternOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(listbox);
        options ??= new PatternOptions();
        var page = listbox.Page;
        var checks = new List<PatternCheck>();
        await ToolkitScript.EnsureAsync(page).ConfigureAwait(false);
        var info = await listbox.GetAccessibilityInfoAsync().ConfigureAwait(false);
        checks.Add(new("Container has role listbox and an accessible name", info.Role == "listbox" && info.Name.Length > 0, info.ToString()));
        var optionsLocator = listbox.Locator(OptionSelector);
        var count = await optionsLocator.CountAsync().ConfigureAwait(false);
        checks.Add(new("Listbox contains at least two options", count >= 2, count.ToString(CultureInfo.InvariantCulture)));
        if (count < 2)
        {
            return Result("Listbox", checks);
        }

        // Enter the listbox the way a keyboard user does: focus the element in the tab sequence.
        var entry = await listbox.EvaluateAsync<string?>("(c, s) => { const t = [c, ...c.querySelectorAll(s)].find(e => e.tabIndex >= 0); return t ? window.__blazorA11y.selectorFor(t) : null; }", OptionSelector).ConfigureAwait(false);
        checks.Add(new("The listbox or one option is in the tab sequence", entry is not null, entry));
        if (entry is null)
        {
            return Result("Listbox", checks);
        }

        await page.Locator(entry).FocusAsync().ConfigureAwait(false);
        var multi = info.State("multiselectable") == "true";
        var start = await ActiveIndexAsync(listbox, OptionSelector).ConfigureAwait(false);
        if (start < 0)
        {
            // An activedescendant listbox may only activate an option on the first arrow key press.
            await page.Keyboard.PressAsync("ArrowDown").ConfigureAwait(false);
            await WaitAsync(async () => await ActiveIndexAsync(listbox, OptionSelector).ConfigureAwait(false) >= 0, options).ConfigureAwait(false);
            start = await ActiveIndexAsync(listbox, OptionSelector).ConfigureAwait(false);
        }

        checks.Add(new("An option is active when the listbox has focus", start >= 0, start.ToString(CultureInfo.InvariantCulture)));
        if (start < 0)
        {
            return Result("Listbox", checks);
        }

        var target = start < count - 1 ? start + 1 : start - 1;
        var key = start < count - 1 ? "ArrowDown" : "ArrowUp";
        checks.Add(new($"{key} moves to the {(key == "ArrowDown" ? "next" : "previous")} option", await MovesActiveAsync(listbox, OptionSelector, key, target, options).ConfigureAwait(false)));
        if (!multi)
        {
            var follows = await WaitAsync(async () => (await SelectedAsync(listbox, OptionSelector).ConfigureAwait(false)).SequenceEqual([target]), options with { Timeout = TimeSpan.FromMilliseconds(Math.Min(options.Timeout.TotalMilliseconds, 750)) }).ConfigureAwait(false);
            if (!follows)
            {
                await page.Keyboard.PressAsync(" ").ConfigureAwait(false);
                follows = await WaitAsync(async () => (await SelectedAsync(listbox, OptionSelector).ConfigureAwait(false)).SequenceEqual([target]), options).ConfigureAwait(false);
                checks.Add(new("Selection follows the active option, or Space selects it (aria-selected)", follows, "Space"));
            }
            else
            {
                checks.Add(new("Selection follows the active option, or Space selects it (aria-selected)", true, "selection follows focus"));
            }
        }

        checks.Add(new("End moves to the last option", await MovesActiveAsync(listbox, OptionSelector, "End", count - 1, options).ConfigureAwait(false)));
        checks.Add(new("Home moves to the first option", await MovesActiveAsync(listbox, OptionSelector, "Home", 0, options).ConfigureAwait(false)));
        return Result("Listbox", checks);
    }

    /// <summary>
    /// Editable combobox with a listbox popup (autocomplete): a named combobox with aria-expanded and aria-controls;
    /// ArrowDown opens the popup and moves through options with aria-activedescendant while DOM focus stays in the
    /// combobox, Enter accepts the active option and closes the popup, and Escape closes it.
    /// </summary>
    public static async Task<PatternVerification> VerifyComboboxAsync(ILocator combobox, ILocator? popup = null, PatternOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(combobox);
        options ??= new PatternOptions();
        var page = combobox.Page;
        var checks = new List<PatternCheck>();
        await ToolkitScript.EnsureAsync(page).ConfigureAwait(false);
        var info = await combobox.GetAccessibilityInfoAsync().ConfigureAwait(false);
        checks.Add(new("Control has role combobox and an accessible name", info.Role == "combobox" && info.Name.Length > 0, info.ToString()));
        checks.Add(new("Combobox has aria-expanded=\"false\" while collapsed", info.State("expanded") == "false", info.State("expanded") ?? "(absent)"));
        var controls = await combobox.GetAttributeAsync("aria-controls").ConfigureAwait(false);
        checks.Add(new("aria-controls references the popup", !string.IsNullOrWhiteSpace(controls), controls));
        popup ??= string.IsNullOrWhiteSpace(controls) ? page.Locator("[role=listbox]") : page.Locator($"[id=\"{controls.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]}\"]");

        await combobox.FocusAsync().ConfigureAwait(false);
        await page.Keyboard.PressAsync("ArrowDown").ConfigureAwait(false);
        var opened = await WaitAsync(async () => (await combobox.GetAccessibilityInfoAsync().ConfigureAwait(false)).State("expanded") == "true"
            && await popup.CountAsync().ConfigureAwait(false) == 1 && await popup.IsVisibleAsync().ConfigureAwait(false), options).ConfigureAwait(false);
        checks.Add(new("ArrowDown opens the popup and sets aria-expanded=\"true\"", opened));
        if (!opened)
        {
            return Result("Combobox", checks);
        }

        var count = await popup.Locator(OptionSelector).CountAsync().ConfigureAwait(false);
        checks.Add(new("Popup contains options", count > 0, count.ToString(CultureInfo.InvariantCulture)));
        if (count == 0)
        {
            return Result("Combobox", checks);
        }

        if (await ActiveIndexAsync(popup, OptionSelector).ConfigureAwait(false) < 0)
        {
            await page.Keyboard.PressAsync("ArrowDown").ConfigureAwait(false);
        }

        var active = await WaitForActiveAsync(popup, options).ConfigureAwait(false);
        checks.Add(new("Arrow keys activate an option through aria-activedescendant", active >= 0, active.ToString(CultureInfo.InvariantCulture)));
        checks.Add(new("DOM focus stays on the combobox", await IsFocusedAsync(combobox).ConfigureAwait(false), (await page.GetFocusAsync().ConfigureAwait(false)).ToString()));
        if (count > 1 && active >= 0)
        {
            var target = active < count - 1 ? active + 1 : active - 1;
            var key = active < count - 1 ? "ArrowDown" : "ArrowUp";
            checks.Add(new($"{key} moves the active option", await MovesActiveAsync(popup, OptionSelector, key, target, options).ConfigureAwait(false)));
            active = target;
        }

        if (active >= 0)
        {
            var optionText = PlaywrightAccessibility.Normalize(await popup.Locator(OptionSelector).Nth(active).InnerTextAsync().ConfigureAwait(false));
            await page.Keyboard.PressAsync("Enter").ConfigureAwait(false);
            var accepted = await WaitAsync(async () => (await combobox.GetAccessibilityInfoAsync().ConfigureAwait(false)).State("expanded") != "true", options).ConfigureAwait(false);
            checks.Add(new("Enter accepts the active option and closes the popup", accepted));
            var value = await combobox.EvaluateAsync<string>("e => e.value ?? e.textContent ?? ''").ConfigureAwait(false);
            checks.Add(new("The combobox shows the accepted option", PlaywrightAccessibility.Normalize(value).Contains(optionText, StringComparison.OrdinalIgnoreCase), $"value \"{value}\", option \"{optionText}\""));
        }

        await page.Keyboard.PressAsync("ArrowDown").ConfigureAwait(false);
        await WaitAsync(async () => (await combobox.GetAccessibilityInfoAsync().ConfigureAwait(false)).State("expanded") == "true", options).ConfigureAwait(false);
        await page.Keyboard.PressAsync("Escape").ConfigureAwait(false);
        checks.Add(new("Escape closes the popup", await WaitAsync(async () => (await combobox.GetAccessibilityInfoAsync().ConfigureAwait(false)).State("expanded") != "true"
            && (await popup.CountAsync().ConfigureAwait(false) == 0 || !await popup.IsVisibleAsync().ConfigureAwait(false)), options).ConfigureAwait(false)));
        checks.Add(new("Focus stays on the combobox after Escape", await IsFocusedAsync(combobox).ConfigureAwait(false)));
        return Result("Combobox", checks);
    }

    /// <summary>
    /// Slider: a named slider (role slider or <c>input type="range"</c>) with a current, minimum and maximum value;
    /// ArrowRight/ArrowUp increase and ArrowLeft/ArrowDown decrease the value, Home sets the minimum and End the maximum.
    /// </summary>
    public static async Task<PatternVerification> VerifySliderAsync(ILocator slider, PatternOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(slider);
        options ??= new PatternOptions();
        var page = slider.Page;
        var checks = new List<PatternCheck>();
        var info = await slider.GetAccessibilityInfoAsync().ConfigureAwait(false);
        checks.Add(new("Control has role slider and an accessible name", info.Role == "slider" && info.Name.Length > 0, info.ToString()));
        var value = await SliderValuesAsync(slider).ConfigureAwait(false);
        checks.Add(new("Slider exposes its current, minimum and maximum value", value is { Now: not null, Min: not null, Max: not null }, value?.ToString()));
        if (value is not { Now: { } now, Min: { } min, Max: { } max })
        {
            return Result("Slider", checks);
        }

        await slider.FocusAsync().ConfigureAwait(false);
        if (now >= max)
        {
            await page.Keyboard.PressAsync("Home").ConfigureAwait(false);
            await WaitAsync(async () => (await SliderValuesAsync(slider).ConfigureAwait(false))?.Now < max, options).ConfigureAwait(false);
            now = (await SliderValuesAsync(slider).ConfigureAwait(false))?.Now ?? now;
        }

        checks.Add(new("ArrowRight increases the value", await ChangesValueAsync(slider, "ArrowRight", (before, after) => after > before, options).ConfigureAwait(false)));
        checks.Add(new("ArrowLeft decreases the value", await ChangesValueAsync(slider, "ArrowLeft", (before, after) => after < before, options).ConfigureAwait(false)));
        checks.Add(new("ArrowUp increases the value", await ChangesValueAsync(slider, "ArrowUp", (before, after) => after > before, options).ConfigureAwait(false)));
        checks.Add(new("ArrowDown decreases the value", await ChangesValueAsync(slider, "ArrowDown", (before, after) => after < before, options).ConfigureAwait(false)));
        checks.Add(new("End sets the maximum value", await ChangesValueAsync(slider, "End", (_, after) => Math.Abs(after - max) < 1e-9, options).ConfigureAwait(false)));
        checks.Add(new("Home sets the minimum value", await ChangesValueAsync(slider, "Home", (_, after) => Math.Abs(after - min) < 1e-9, options).ConfigureAwait(false)));
        checks.Add(new("Focus stays on the slider", await IsFocusedAsync(slider).ConfigureAwait(false)));
        return Result("Slider", checks);
    }

    /// <summary>
    /// Checkbox or switch: a named control with role checkbox or switch (native or ARIA) and a checked state; Space
    /// toggles the state both ways and focus stays on the control.
    /// </summary>
    public static async Task<PatternVerification> VerifyCheckboxAsync(ILocator control, PatternOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(control);
        options ??= new PatternOptions();
        var page = control.Page;
        var checks = new List<PatternCheck>();
        var info = await control.GetAccessibilityInfoAsync().ConfigureAwait(false);
        var pattern = info.Role == "switch" ? "Switch" : "Checkbox";
        checks.Add(new("Control has role checkbox or switch and an accessible name", info.Role is "checkbox" or "switch" && info.Name.Length > 0, info.ToString()));
        var initial = info.State("checked");
        checks.Add(new("Control exposes its checked state", initial is "true" or "false" or "mixed", initial ?? "(absent)"));
        checks.Add(new("Control is in the tab sequence", info.TabIndex is >= 0, info.TabIndex?.ToString(CultureInfo.InvariantCulture) ?? "not focusable"));
        if (initial is not ("true" or "false" or "mixed"))
        {
            return Result(pattern, checks);
        }

        await control.FocusAsync().ConfigureAwait(false);
        foreach (var round in new[] { "first", "second" })
        {
            var before = (await control.GetAccessibilityInfoAsync().ConfigureAwait(false)).State("checked");
            var expected = before == "true" ? "false" : "true";
            await page.Keyboard.PressAsync(" ").ConfigureAwait(false);
            var toggled = await WaitAsync(async () => (await control.GetAccessibilityInfoAsync().ConfigureAwait(false)).State("checked") == expected, options).ConfigureAwait(false);
            checks.Add(new($"Space toggles the state to \"{expected}\" ({round} press)", toggled));
        }

        checks.Add(new("Focus stays on the control", await IsFocusedAsync(control).ConfigureAwait(false)));
        return Result(pattern, checks);
    }

    /// <summary>
    /// Tooltip and other content on hover or focus (WAI-ARIA tooltip pattern and WCAG 1.4.13): the tooltip appears when
    /// the trigger receives keyboard focus and on hover, is referenced by aria-describedby, can be dismissed with Escape
    /// without moving focus, stays visible while the pointer moves onto it (hoverable), and does not disappear on its
    /// own while the trigger is hovered or focused (persistent).
    /// </summary>
    public static async Task<PatternVerification> VerifyTooltipAsync(ILocator trigger, ILocator? tooltip = null, PatternOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        options ??= new PatternOptions();
        var page = trigger.Page;
        var checks = new List<PatternCheck>();
        var describedBy = await trigger.GetAttributeAsync("aria-describedby").ConfigureAwait(false);
        tooltip ??= string.IsNullOrWhiteSpace(describedBy)
            ? page.Locator("[role=tooltip]")
            : page.Locator($"[id=\"{describedBy.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]}\"]");

        async Task<bool> Shown() => await tooltip.CountAsync().ConfigureAwait(false) == 1 && await tooltip.IsVisibleAsync().ConfigureAwait(false);
        async Task<bool> Hidden() => !await Shown().ConfigureAwait(false);

        // Keyboard: focus shows, Escape dismisses without moving focus.
        await page.Mouse.MoveAsync(0, 0).ConfigureAwait(false);
        await trigger.FocusAsync().ConfigureAwait(false);
        var shownOnFocus = await WaitAsync(Shown, options).ConfigureAwait(false);
        checks.Add(new("Tooltip appears when the trigger receives focus", shownOnFocus));
        if (shownOnFocus)
        {
            var tooltipInfo = await tooltip.GetAccessibilityInfoAsync().ConfigureAwait(false);
            checks.Add(new("Tooltip has role tooltip", tooltipInfo.Role == "tooltip", tooltipInfo.ToString()));
            var tooltipId = await tooltip.GetAttributeAsync("id").ConfigureAwait(false);
            var referenced = !string.IsNullOrEmpty(tooltipId) && (describedBy ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(tooltipId, StringComparer.Ordinal);
            checks.Add(new("Trigger references the tooltip with aria-describedby", referenced, $"aria-describedby=\"{describedBy}\", tooltip id=\"{tooltipId}\""));
            await page.Keyboard.PressAsync("Escape").ConfigureAwait(false);
            checks.Add(new("Escape dismisses the tooltip (WCAG 1.4.13 dismissible)", await WaitAsync(Hidden, options).ConfigureAwait(false)));
            checks.Add(new("Focus stays on the trigger after Escape", await IsFocusedAsync(trigger).ConfigureAwait(false), (await page.GetFocusAsync().ConfigureAwait(false)).ToString()));
        }

        // Pointer: hover shows, the pointer can move onto the tooltip, and it persists.
        await trigger.BlurAsync().ConfigureAwait(false);
        await WaitAsync(Hidden, options with { Timeout = TimeSpan.FromMilliseconds(Math.Min(options.Timeout.TotalMilliseconds, 1000)) }).ConfigureAwait(false);
        await trigger.HoverAsync().ConfigureAwait(false);
        var shownOnHover = await WaitAsync(Shown, options).ConfigureAwait(false);
        checks.Add(new("Tooltip appears when the pointer hovers over the trigger", shownOnHover));
        if (shownOnHover)
        {
            await page.WaitForTimeoutAsync(1000).ConfigureAwait(false);
            checks.Add(new("Tooltip stays visible while the trigger is hovered (WCAG 1.4.13 persistent)", await Shown().ConfigureAwait(false)));
            var box = await tooltip.BoundingBoxAsync().ConfigureAwait(false);
            if (box is not null)
            {
                // One direct move: intermediate steps race the page's own hide timers and make the result depend on
                // how fast the browser dispatches mouse events.
                await page.Mouse.MoveAsync(box.X + (box.Width / 2), box.Y + (box.Height / 2), new MouseMoveOptions { Steps = 1 }).ConfigureAwait(false);
                await page.WaitForTimeoutAsync(300).ConfigureAwait(false);
                checks.Add(new("Tooltip stays visible when the pointer moves onto it (WCAG 1.4.13 hoverable)", await Shown().ConfigureAwait(false)));
            }

            await page.Mouse.MoveAsync(0, 0).ConfigureAwait(false);
        }

        return Result("Tooltip", checks);
    }

    /// <summary>
    /// Accordion: every header button is a named button inside a heading, has aria-expanded and aria-controls pointing at
    /// its panel, and Enter and Space toggle aria-expanded and the panel. <paramref name="accordion"/> is the element
    /// containing the headers.
    /// </summary>
    public static async Task<PatternVerification> VerifyAccordionAsync(ILocator accordion, PatternOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(accordion);
        options ??= new PatternOptions();
        var checks = new List<PatternCheck>();
        var buttons = accordion.Locator(":is(button, [role=button])[aria-expanded]");
        var count = await buttons.CountAsync().ConfigureAwait(false);
        checks.Add(new("Accordion contains header buttons with aria-expanded", count > 0, count.ToString(CultureInfo.InvariantCulture)));
        for (var i = 0; i < count; i++)
        {
            var button = buttons.Nth(i);
            var info = await button.GetAccessibilityInfoAsync().ConfigureAwait(false);
            var inHeading = await button.EvaluateAsync<bool>("b => !!b.closest('h1, h2, h3, h4, h5, h6, [role=heading]')").ConfigureAwait(false);
            checks.Add(new($"Header \"{info.Name}\" is a button inside a heading", inHeading, info.ToString()));
            var disclosure = await VerifyDisclosureAsync(button, options).ConfigureAwait(false);
            checks.AddRange(disclosure.Checks.Select(c => c with { Requirement = $"Header \"{info.Name}\": {c.Requirement}" }));
        }

        return Result("Accordion", checks);
    }

    private static async Task<int[]> SelectedAsync(ILocator container, string selector) =>
        ToolkitScript.Deserialize<int[]>(await container.EvaluateAsync<JsonElement>(SelectedIndexesScript, selector).ConfigureAwait(false)) ?? [];

    private static async Task<int> ActiveIndexAsync(ILocator container, string selector)
    {
        await ToolkitScript.EnsureAsync(container.Page).ConfigureAwait(false);
        return await container.EvaluateAsync<int>(ActiveIndexScript, selector).ConfigureAwait(false);
    }

    private static async Task<int> WaitForActiveAsync(ILocator container, PatternOptions options)
    {
        var index = -1;
        await WaitAsync(async () => (index = await ActiveIndexAsync(container, OptionSelector).ConfigureAwait(false)) >= 0, options).ConfigureAwait(false);
        return index;
    }

    private static async Task<bool> MovesActiveAsync(ILocator container, string selector, string key, int target, PatternOptions options)
    {
        await container.Page.Keyboard.PressAsync(key).ConfigureAwait(false);
        return await WaitAsync(async () => await ActiveIndexAsync(container, selector).ConfigureAwait(false) == target, options).ConfigureAwait(false);
    }

    private static async Task<bool> MovesAndChecksAsync(ILocator group, ILocator radios, string key, int target, PatternOptions options)
    {
        await group.Page.Keyboard.PressAsync(key).ConfigureAwait(false);
        return await WaitAsync(async () => await IsFocusedAsync(radios.Nth(target)).ConfigureAwait(false)
            && (await SelectedAsync(group, RadioSelector).ConfigureAwait(false)).SequenceEqual([target]), options).ConfigureAwait(false);
    }

    private sealed record SliderValue(double? Now, double? Min, double? Max)
    {
        public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"now={Now?.ToString(CultureInfo.InvariantCulture) ?? "?"}, min={Min?.ToString(CultureInfo.InvariantCulture) ?? "?"}, max={Max?.ToString(CultureInfo.InvariantCulture) ?? "?"}");
    }

    private static async Task<SliderValue?> SliderValuesAsync(ILocator slider)
    {
        var raw = ToolkitScript.Deserialize<string?[]>(await slider.EvaluateAsync<JsonElement>(
            "e => [e.getAttribute('aria-valuenow') ?? (e.localName === 'input' ? e.value : null), e.getAttribute('aria-valuemin') ?? (e.localName === 'input' ? (e.min || '0') : null), e.getAttribute('aria-valuemax') ?? (e.localName === 'input' ? (e.max || '100') : null)]").ConfigureAwait(false));
        if (raw is not { Length: 3 })
        {
            return null;
        }

        static double? Parse(string? s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
        return new SliderValue(Parse(raw[0]), Parse(raw[1]), Parse(raw[2]));
    }

    // Presses key and waits until expected(valueBefore, valueAfter) holds.
    private static async Task<bool> ChangesValueAsync(ILocator slider, string key, Func<double, double, bool> expected, PatternOptions options)
    {
        if ((await SliderValuesAsync(slider).ConfigureAwait(false))?.Now is not { } before)
        {
            return false;
        }

        await slider.Page.Keyboard.PressAsync(key).ConfigureAwait(false);
        return await WaitAsync(async () => (await SliderValuesAsync(slider).ConfigureAwait(false))?.Now is { } after && expected(before, after), options).ConfigureAwait(false);
    }
}
