namespace BlazorAccessibilityToolkit.Playwright.Tests;

public sealed class WidgetPatternTests(BrowserFixture browser)
{
    private static readonly PatternOptions Fast = new() { Timeout = TimeSpan.FromMilliseconds(400) };

    private const string RadioGroup = """
        <main><h1>x</h1>
        <div id="g" role="radiogroup" aria-label="Size">
          <div role="radio" aria-checked="true" tabindex="0">Small</div>
          <div role="radio" aria-checked="false" tabindex="-1">Medium</div>
          <div role="radio" aria-checked="false" tabindex="-1">Large</div>
        </div><button>After</button></main>
        <script>
          const g = document.getElementById('g'), radios = () => Array.from(g.querySelectorAll('[role=radio]'));
          const select = r => { radios().forEach(x => { x.setAttribute('aria-checked', String(x === r)); x.tabIndex = x === r ? 0 : -1; }); r.focus(); };
          g.addEventListener('keydown', e => {
            const list = radios(), i = list.indexOf(document.activeElement);
            if (e.key === 'ArrowDown' || e.key === 'ArrowRight') { e.preventDefault(); select(list[(i + 1) % list.length]); }
            if (e.key === 'ArrowUp' || e.key === 'ArrowLeft') { e.preventDefault(); select(list[(i - 1 + list.length) % list.length]); }
            if (e.key === ' ') { e.preventDefault(); select(list[i]); }
          });
        </script>
        """;

    [Fact]
    public async Task Custom_radio_group_following_the_pattern_passes()
    {
        await using var session = await browser.PageAsync(RadioGroup);
        (await AriaPatterns.VerifyRadioGroupAsync(session.Page.Locator("#g"))).ShouldPass();
    }

    [Fact]
    public async Task Native_radio_buttons_in_a_fieldset_pass()
    {
        const string html = """
            <main><h1>x</h1><fieldset id="g"><legend>Delivery</legend>
              <label><input type="radio" name="d" value="a" checked> Standard</label>
              <label><input type="radio" name="d" value="b"> Express</label>
              <label><input type="radio" name="d" value="c"> Pickup</label>
            </fieldset><button>Next</button></main>
            """;
        await using var session = await browser.PageAsync(html);
        var result = await AriaPatterns.VerifyRadioGroupAsync(session.Page.Locator("#g"));
        Assert.True(result.Passed, result.Describe());
    }

    [Fact]
    public async Task Radio_group_without_arrow_keys_and_with_every_radio_tabbable_fails()
    {
        var broken = RadioGroup.Replace("tabindex=\"-1\"", "tabindex=\"0\"", StringComparison.Ordinal).Replace("g.addEventListener('keydown'", "g.addEventListener('unused'", StringComparison.Ordinal);
        await using var session = await browser.PageAsync(broken);
        var result = await AriaPatterns.VerifyRadioGroupAsync(session.Page.Locator("#g"), Fast);
        Assert.False(result.Passed);
        Assert.Contains(result.Checks, c => !c.Passed && c.Requirement.StartsWith("Only one radio button", StringComparison.Ordinal));
        Assert.Contains(result.Checks, c => !c.Passed && c.Requirement.StartsWith("ArrowDown moves focus", StringComparison.Ordinal));
    }

    private const string Toolbar = """
        <main><h1>x</h1>
        <div id="t" role="toolbar" aria-label="Formatting">
          <button tabindex="0">Bold</button><button tabindex="-1">Italic</button><button tabindex="-1">Underline</button>
        </div><button>After</button></main>
        <script>
          const t = document.getElementById('t'), items = () => Array.from(t.querySelectorAll('button'));
          const move = b => { items().forEach(x => x.tabIndex = x === b ? 0 : -1); b.focus(); };
          t.addEventListener('keydown', e => {
            const list = items(), i = list.indexOf(document.activeElement);
            if (e.key === 'ArrowRight') move(list[(i + 1) % list.length]);
            if (e.key === 'ArrowLeft') move(list[(i - 1 + list.length) % list.length]);
            if (e.key === 'Home') move(list[0]);
            if (e.key === 'End') move(list[list.length - 1]);
          });
        </script>
        """;

    [Fact]
    public async Task Toolbar_with_roving_tabindex_passes()
    {
        await using var session = await browser.PageAsync(Toolbar);
        (await AriaPatterns.VerifyToolbarAsync(session.Page.Locator("#t"))).ShouldPass();
    }

    [Fact]
    public async Task Toolbar_where_every_button_is_a_tab_stop_fails()
    {
        await using var session = await browser.PageAsync(Toolbar.Replace("tabindex=\"-1\"", string.Empty, StringComparison.Ordinal).Replace("t.addEventListener", "t.removeEventListener", StringComparison.Ordinal));
        var result = await AriaPatterns.VerifyToolbarAsync(session.Page.Locator("#t"), Fast);
        Assert.Contains(result.Checks, c => !c.Passed && c.Requirement.StartsWith("Only one control", StringComparison.Ordinal));
        Assert.Contains(result.Checks, c => !c.Passed && c.Requirement.StartsWith("ArrowRight", StringComparison.Ordinal));
    }

    private const string Listbox = """
        <main><h1>x</h1>
        <span id="lbl">Fruit</span>
        <ul id="l" role="listbox" tabindex="0" aria-labelledby="lbl" aria-activedescendant="o1">
          <li id="o1" role="option" aria-selected="true">Apple</li><li id="o2" role="option" aria-selected="false">Banana</li><li id="o3" role="option" aria-selected="false">Cherry</li>
        </ul></main>
        <script>
          const l = document.getElementById('l'), opts = () => Array.from(l.querySelectorAll('[role=option]'));
          const activate = o => { l.setAttribute('aria-activedescendant', o.id); opts().forEach(x => x.setAttribute('aria-selected', String(x === o))); };
          l.addEventListener('keydown', e => {
            const list = opts(), i = list.findIndex(o => o.id === l.getAttribute('aria-activedescendant'));
            if (e.key === 'ArrowDown') { e.preventDefault(); activate(list[Math.min(i + 1, list.length - 1)]); }
            if (e.key === 'ArrowUp') { e.preventDefault(); activate(list[Math.max(i - 1, 0)]); }
            if (e.key === 'Home') { e.preventDefault(); activate(list[0]); }
            if (e.key === 'End') { e.preventDefault(); activate(list[list.length - 1]); }
          });
        </script>
        """;

    [Fact]
    public async Task Listbox_with_active_descendant_passes()
    {
        await using var session = await browser.PageAsync(Listbox);
        (await AriaPatterns.VerifyListboxAsync(session.Page.Locator("#l"))).ShouldPass();
    }

    [Fact]
    public async Task Listbox_without_keyboard_support_fails()
    {
        await using var session = await browser.PageAsync(Listbox.Replace("l.addEventListener", "l.removeEventListener", StringComparison.Ordinal));
        var result = await AriaPatterns.VerifyListboxAsync(session.Page.Locator("#l"), Fast);
        Assert.Contains(result.Checks, c => !c.Passed && c.Requirement.StartsWith("ArrowDown moves", StringComparison.Ordinal));
    }

    private const string Combobox = """
        <main><h1>x</h1>
        <label for="c">City</label>
        <input id="c" role="combobox" aria-expanded="false" aria-controls="lb" aria-autocomplete="list">
        <ul id="lb" role="listbox" aria-label="Cities" hidden>
          <li id="c1" role="option">Berlin</li><li id="c2" role="option">Paris</li><li id="c3" role="option">Rome</li>
        </ul></main>
        <script>
          const c = document.getElementById('c'), lb = document.getElementById('lb'), opts = () => Array.from(lb.querySelectorAll('[role=option]'));
          let i = -1;
          const open = () => { lb.hidden = false; c.setAttribute('aria-expanded', 'true'); };
          const close = () => { lb.hidden = true; c.setAttribute('aria-expanded', 'false'); c.removeAttribute('aria-activedescendant'); i = -1; };
          const set = n => { i = n; c.setAttribute('aria-activedescendant', opts()[i].id); };
          c.addEventListener('keydown', e => {
            if (e.key === 'ArrowDown') { e.preventDefault(); if (lb.hidden) { open(); } else { set(Math.min(i + 1, opts().length - 1)); } }
            if (e.key === 'ArrowUp') { e.preventDefault(); if (!lb.hidden) set(Math.max(i - 1, 0)); }
            if (e.key === 'Enter' && i >= 0) { c.value = opts()[i].textContent; close(); }
            if (e.key === 'Escape') close();
          });
        </script>
        """;

    [Fact]
    public async Task Combobox_following_the_pattern_passes()
    {
        await using var session = await browser.PageAsync(Combobox);
        (await AriaPatterns.VerifyComboboxAsync(session.Page.Locator("#c"))).ShouldPass();
    }

    [Fact]
    public async Task Combobox_that_never_updates_aria_expanded_fails()
    {
        await using var session = await browser.PageAsync(Combobox.Replace("c.setAttribute('aria-expanded', 'true');", string.Empty, StringComparison.Ordinal));
        var result = await AriaPatterns.VerifyComboboxAsync(session.Page.Locator("#c"), options: Fast);
        Assert.Contains(result.Checks, c => !c.Passed && c.Requirement.StartsWith("ArrowDown opens the popup", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Native_range_input_passes_the_slider_pattern()
    {
        await using var session = await browser.PageAsync("<main><h1>x</h1><label for=\"s\">Volume</label><input id=\"s\" type=\"range\" min=\"0\" max=\"10\" value=\"5\"></main>");
        (await AriaPatterns.VerifySliderAsync(session.Page.Locator("#s"))).ShouldPass();
    }

    [Fact]
    public async Task Custom_slider_without_keyboard_support_fails()
    {
        const string html = "<main><h1>x</h1><div id=\"s\" role=\"slider\" tabindex=\"0\" aria-label=\"Volume\" aria-valuenow=\"5\" aria-valuemin=\"0\" aria-valuemax=\"10\"></div></main>";
        await using var session = await browser.PageAsync(html);
        var result = await AriaPatterns.VerifySliderAsync(session.Page.Locator("#s"), Fast);
        Assert.True(result.Checks[0].Passed);
        Assert.Contains(result.Checks, c => !c.Passed && c.Requirement == "ArrowRight increases the value");
        Assert.Contains(result.Checks, c => !c.Passed && c.Requirement == "End sets the maximum value");
    }

    [Fact]
    public async Task Native_checkbox_passes_and_a_clickable_switch_without_keyboard_fails()
    {
        await using (var session = await browser.PageAsync("<main><h1>x</h1><label><input id=\"c\" type=\"checkbox\"> Subscribe</label></main>"))
        {
            (await AriaPatterns.VerifyCheckboxAsync(session.Page.Locator("#c"))).ShouldPass();
        }

        const string html = """
            <main><h1>x</h1><span id="s" role="switch" tabindex="0" aria-checked="false">Dark mode</span></main>
            <script>document.getElementById('s').addEventListener('click', e => e.target.setAttribute('aria-checked', String(e.target.getAttribute('aria-checked') !== 'true')));</script>
            """;
        await using var broken = await browser.PageAsync(html);
        var result = await AriaPatterns.VerifyCheckboxAsync(broken.Page.Locator("#s"), Fast);
        Assert.Equal("Switch", result.Pattern);
        Assert.Contains(result.Checks, c => !c.Passed && c.Requirement.StartsWith("Space toggles", StringComparison.Ordinal));
    }

    private const string Tooltip = """
        <main><h1>x</h1><div style="padding:40px">
          <button id="b" aria-describedby="tip">Info</button>
          <div id="tip" role="tooltip" hidden style="position:absolute;top:10px;left:120px;padding:8px;border:1px solid">More details</div>
        </div></main>
        <script>
          const b = document.getElementById('b'), tip = document.getElementById('tip');
          let overTip = false, overButton = false;
          const show = () => tip.hidden = false;
          const hideSoon = () => setTimeout(() => { if (!overTip && !overButton && document.activeElement !== b) tip.hidden = true; }, 100);
          b.addEventListener('focus', show); b.addEventListener('blur', hideSoon);
          b.addEventListener('mouseenter', () => { overButton = true; show(); }); b.addEventListener('mouseleave', () => { overButton = false; hideSoon(); });
          tip.addEventListener('mouseenter', () => overTip = true); tip.addEventListener('mouseleave', () => { overTip = false; hideSoon(); });
          document.addEventListener('keydown', e => { if (e.key === 'Escape') tip.hidden = true; });
        </script>
        """;

    [Fact]
    public async Task Tooltip_that_is_dismissible_hoverable_and_persistent_passes()
    {
        await using var session = await browser.PageAsync(Tooltip);
        var result = await AriaPatterns.VerifyTooltipAsync(session.Page.Locator("#b"));
        Assert.True(result.Passed, result.Describe());
    }

    [Fact]
    public async Task Tooltip_without_escape_and_not_hoverable_fails()
    {
        var broken = Tooltip.Replace("document.addEventListener('keydown'", "document.addEventListener('unused'", StringComparison.Ordinal)
            .Replace("if (!overTip && !overButton && document.activeElement !== b) tip.hidden = true;", "if (!overButton && document.activeElement !== b) tip.hidden = true;", StringComparison.Ordinal);
        await using var session = await browser.PageAsync(broken);
        var result = await AriaPatterns.VerifyTooltipAsync(session.Page.Locator("#b"), options: Fast);
        Assert.Contains(result.Checks, c => !c.Passed && c.Requirement.StartsWith("Escape dismisses", StringComparison.Ordinal));
        Assert.Contains(result.Checks, c => !c.Passed && c.Requirement.Contains("hoverable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Accordion_headers_must_be_buttons_inside_headings()
    {
        const string html = """
            <main><h1>x</h1><div id="a">
              <h2><button aria-expanded="false" aria-controls="p1">Shipping</button></h2><div id="p1" hidden>Ships in 2 days</div>
              <button aria-expanded="false" aria-controls="p2">Returns</button><div id="p2" hidden>30 days</div>
            </div></main>
            <script>for (const b of document.querySelectorAll('[aria-expanded]')) b.addEventListener('click', () => { const p = document.getElementById(b.getAttribute('aria-controls')); p.hidden = !p.hidden; b.setAttribute('aria-expanded', String(!p.hidden)); });</script>
            """;
        await using var session = await browser.PageAsync(html);
        var result = await AriaPatterns.VerifyAccordionAsync(session.Page.Locator("#a"));
        Assert.Contains(result.Checks, c => c.Passed && c.Requirement == "Header \"Shipping\" is a button inside a heading");
        Assert.Contains(result.Checks, c => !c.Passed && c.Requirement == "Header \"Returns\" is a button inside a heading");
        Assert.Contains(result.Checks, c => c.Passed && c.Requirement.StartsWith("Header \"Returns\": Enter toggles", StringComparison.Ordinal));
    }
}
