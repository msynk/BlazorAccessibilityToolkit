using System.Text.RegularExpressions;

namespace BlazorAccessibilityToolkit.Playwright.Tests;

public sealed class KeyboardAndFocusTests(BrowserFixture browser)
{
    // WebKit, like Safari's default settings, does not move focus to links with Tab.
    private bool LinksAreTabbable => browser.Browser.Name != "webkit";

    private const string Form = "<main><h1>Profile</h1><a href=\"#top\">Skip</a><label for=\"n\">Name</label><input id=\"n\"><button type=\"button\" disabled>Disabled</button><button type=\"button\">Save</button></main>";

    [Fact]
    public async Task Captures_focus_order_and_compares_with_expectation()
    {
        await using var session = await browser.PageAsync(Form);
        var order = await session.Page.CaptureFocusOrderAsync();

        Assert.Equal(FocusOrderTermination.ReachedEnd, order.Termination);
        var expected = LinksAreTabbable ? new[] { "link Skip", "textbox Name", "button Save" } : ["textbox Name", "button Save"];
        order.ShouldMatch(expected).ShouldNotTrapFocus();
        Assert.Empty(order.Findings);
        Assert.Empty(order.UnreachedCandidates);

        var ex = Assert.Throws<AccessibilityAssertionException>(() => order.ShouldMatch(expected.Reverse().ToArray()));
        Assert.Contains("stop 1", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Observed:", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reverse_capture_and_start_position()
    {
        await using var session = await browser.PageAsync(Form);
        var forward = await session.Page.CaptureFocusOrderAsync(new FocusOrderOptions { StartAfter = session.Page.Locator("#n") });
        forward.ShouldMatch("button Save");

        var reverse = await session.Page.CaptureFocusOrderAsync(new FocusOrderOptions { Reverse = true, StartAfter = session.Page.Locator("button:not([disabled])") });
        reverse.ShouldMatch(LinksAreTabbable ? ["textbox Name", "link Skip"] : ["textbox Name"]);
    }

    [Fact]
    public async Task Positive_tabindex_changes_the_observed_order_without_being_judged()
    {
        await using var session = await browser.PageAsync("<main><h1>x</h1><button>First in DOM</button><button tabindex=\"1\">Jumps ahead</button></main>");
        var order = await session.Page.CaptureFocusOrderAsync();
        order.ShouldMatch("button Jumps ahead", "button First in DOM");
        Assert.Empty(order.Findings);
    }

    [Fact]
    public async Task Detects_focus_trap_outside_modal_dialogs()
    {
        const string trap = """
            <main><h1>x</h1><button id="before">Before</button>
            <div id="widget"><button id="a">A</button><button id="b">B</button></div>
            <button id="after">After</button></main>
            <script>
              document.getElementById('widget').addEventListener('keydown', e => {
                if (e.key !== 'Tab') return;
                e.preventDefault();
                (document.activeElement.id === 'a' ? document.getElementById('b') : document.getElementById('a')).focus();
              });
            </script>
            """;
        await using var session = await browser.PageAsync(trap);
        var order = await session.Page.CaptureFocusOrderAsync(new FocusOrderOptions { MaxStops = 20 });
        Assert.Equal(FocusOrderTermination.Cycle, order.Termination);
        var finding = Assert.Single(order.Findings, f => f.RuleId == "blazor-focus-trap");
        Assert.Contains("2.1.2", finding.Wcag.Select(w => w.Id));
        Assert.Throws<AccessibilityAssertionException>(() => order.ShouldNotTrapFocus());
        Assert.Equal(AuditStatus.Completed, order.ToAudit("trap").Status);
    }

    [Fact]
    public async Task Focus_cycling_inside_a_modal_dialog_is_not_reported_as_a_trap()
    {
        const string modal = """
            <main inert><h1>x</h1><button>Behind</button></main>
            <div role="dialog" aria-modal="true" aria-label="Confirm" id="d"><button id="a">Cancel</button><button id="b">OK</button></div>
            <script>
              document.getElementById('d').addEventListener('keydown', e => {
                if (e.key !== 'Tab') return;
                e.preventDefault();
                (document.activeElement.id === 'a' ? document.getElementById('b') : document.getElementById('a')).focus();
              });
              document.getElementById('a').focus();
            </script>
            """;
        await using var session = await browser.PageAsync(modal);
        var order = await session.Page.CaptureFocusOrderAsync(new FocusOrderOptions { StartAfter = session.Page.Locator("#a"), MaxStops = 10 });
        Assert.Contains(order.Termination, new[] { FocusOrderTermination.Cycle, FocusOrderTermination.Wrapped });
        Assert.DoesNotContain(order.Findings, f => f.RuleId == "blazor-focus-trap");
        Assert.All(order.Stops, s => Assert.NotNull(s.ModalContainer));
    }

    [Fact]
    public async Task Detects_stuck_focus()
    {
        const string stuck = """
            <main><h1>x</h1><input id="i" aria-label="Code"><button>Next</button></main>
            <script>document.getElementById('i').addEventListener('keydown', e => { if (e.key === 'Tab') e.preventDefault(); });</script>
            """;
        await using var session = await browser.PageAsync(stuck);
        var order = await session.Page.CaptureFocusOrderAsync(new FocusOrderOptions { SettleTimeout = TimeSpan.FromMilliseconds(300) });
        Assert.Equal(FocusOrderTermination.Stuck, order.Termination);
        Assert.Single(order.Findings, f => f.RuleId == "blazor-focus-stuck");
    }

    [Fact]
    public async Task Reports_focus_on_hidden_elements()
    {
        await using var session = await browser.PageAsync("<main><h1>x</h1><button>Visible</button><div aria-hidden=\"true\"><button>Hidden from AT</button></div><button style=\"opacity:0\">Invisible</button></main>");
        var order = await session.Page.CaptureFocusOrderAsync();
        Assert.Equal(2, order.Findings.Count(f => f.RuleId == "blazor-focus-hidden-target"));
    }

    [Fact]
    public async Task Missing_focus_indicator_is_opt_in()
    {
        await using var session = await browser.PageAsync("<style>.bare:focus{outline:none}</style><main><h1>x</h1><button class=\"bare\">No ring</button><button>Default ring</button></main>");
        var plain = await session.Page.CaptureFocusOrderAsync();
        Assert.Empty(plain.Findings);
        var observed = await session.Page.CaptureFocusOrderAsync(new FocusOrderOptions { ReportMissingFocusIndicator = true });
        var finding = Assert.Single(observed.Findings);
        Assert.Equal("blazor-focus-indicator-not-detected", finding.RuleId);
        Assert.Equal("No ring", finding.Target.AccessibleName);
    }

    [Fact]
    public async Task Keyboard_scenario_with_expectations_and_focus_restoration()
    {
        const string disclosure = """
            <style>button, a { display: inline-block; min-width: 44px; min-height: 44px; margin: 8px; }</style>
            <main><h1>x</h1><button id="t" aria-expanded="false" aria-controls="p">Details</button>
            <div id="p" hidden><a href="#a">First link</a><button id="close">Close</button></div></main>
            <script>
              const t = document.getElementById('t'), p = document.getElementById('p');
              t.addEventListener('click', () => { setTimeout(() => { p.hidden = false; t.setAttribute('aria-expanded', 'true'); p.querySelector('a').focus(); }, 50); });
              document.getElementById('close').addEventListener('click', () => { p.hidden = true; t.setAttribute('aria-expanded', 'false'); t.focus(); });
              p.addEventListener('keydown', e => { if (e.key === 'Escape') document.getElementById('close').click(); });
            </script>
            """;
        await using var session = await browser.PageAsync(disclosure);
        var trigger = session.Page.Locator("#t");
        var scenario = KeyboardScenario.For(session.Page, new KeyboardScenarioOptions { Name = "disclosure" })
            .StartFromDocument()
            .PressTab()
            .ExpectFocused(role: "button", name: "Details")
            .RememberFocus("trigger")
            .PressEnter()
            .ExpectState(trigger, "expanded", "true")
            .ExpectFocused("link", new Regex("^first", RegexOptions.IgnoreCase))
            .ExpectFocusWithin(session.Page.Locator("#p"))
            .Audit("expanded")
            .PressEscape()
            .ExpectFocusRestored("trigger")
            .ExpectFocusNotWithin(session.Page.Locator("#p"))
            .ExpectState(trigger, "aria-expanded", "false");

        var steps = await scenario.RunAsync();
        Assert.All(steps, s => Assert.True(s.Passed));
        var audit = Assert.Single(scenario.Audits);
        Assert.Equal("expanded", audit.Context.State);
        Assert.Equal("disclosure", audit.Context.Scenario);
    }

    [Fact]
    public async Task Failed_expectation_reports_a_transcript()
    {
        await using var session = await browser.PageAsync(Form);
        var scenario = KeyboardScenario.For(session.Page, new KeyboardScenarioOptions { ExpectTimeout = TimeSpan.FromMilliseconds(300) })
            .StartFromDocument()
            .PressTab()
            .ExpectFocused("button", "Save");
        var ex = await Assert.ThrowsAsync<AccessibilityAssertionException>(async () => await scenario.RunAsync());
        Assert.Contains("failed at step 3", ex.Message, StringComparison.Ordinal);
        Assert.Contains(LinksAreTabbable ? "link \"Skip\"" : "textbox \"Name\"", ex.Message, StringComparison.Ordinal);
        Assert.Contains("press Tab", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Focus_containment_expectation_detects_escaping_focus()
    {
        await using var session = await browser.PageAsync("<main><h1>x</h1><div id=\"box\"><button id=\"a\">A</button></div><button>Outside</button></main>");
        var scenario = KeyboardScenario.For(session.Page)
            .FocusOn(session.Page.Locator("#a"))
            .ExpectFocusContainedWithin(session.Page.Locator("#box"));
        var ex = await Assert.ThrowsAsync<AccessibilityAssertionException>(async () => await scenario.RunAsync());
        Assert.Contains("escaped", ex.Message, StringComparison.Ordinal);
    }
}
