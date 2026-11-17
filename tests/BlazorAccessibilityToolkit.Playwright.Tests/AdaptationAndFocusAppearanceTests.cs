using Microsoft.Playwright;

namespace BlazorAccessibilityToolkit.Playwright.Tests;

public sealed class AdaptationAndFocusAppearanceTests(BrowserFixture browser)
{
    [Fact]
    public async Task Reflow_reports_fixed_width_content_and_restores_the_viewport()
    {
        const string html = """
            <main><h1>Orders</h1>
              <div id="banner" style="width:600px">A banner with a fixed width that does not fit</div>
              <table><tr><td style="white-space:nowrap">A wide data table cell that may scroll on its own because it is tabular</td></tr></table>
            </main>
            """;
        await using var session = await browser.PageAsync(html);
        var audit = await session.Page.AuditReflowAsync();

        audit.ShouldHaveExecuted();
        var finding = audit.ShouldHaveFinding("blazor-reflow-horizontal-scroll");
        Assert.Equal("#banner", finding.Target.Selector);
        Assert.Contains(finding.Wcag, w => w.Id == "1.4.10");
        Assert.DoesNotContain(audit.Findings, f => f.Target.Selector?.Contains("td", StringComparison.Ordinal) == true);
        Assert.Contains(audit.Warnings, w => w.Contains("two-dimensional", StringComparison.Ordinal));
        Assert.Equal(1280, session.Page.ViewportSize!.Width);
    }

    [Fact]
    public async Task Responsive_page_passes_reflow()
    {
        await using var session = await browser.PageAsync("<main><h1>Orders</h1><p style=\"max-width:600px\">Text that wraps nicely at any width because it has a maximum width only.</p></main>");
        var audit = await session.Page.AuditReflowAsync(new ReflowOptions { AuditAtWidth = true });
        audit.ShouldHaveNoViolations();
        Assert.Contains("blazor-reflow-horizontal-scroll", audit.PassedRules);
        Assert.Contains(audit.Engines, e => e.Name == "axe-core");
    }

    [Fact]
    public async Task Text_spacing_reports_containers_that_start_clipping()
    {
        const string html = """
            <main><h1>Plans</h1>
              <div id="fixed" style="height:1.3em;overflow:hidden;width:20em">Basic plan with everything you need</div>
              <div id="already" style="height:1em;overflow:hidden;width:4em">This text was clipped before any spacing was applied at all</div>
              <div id="fluid" style="min-height:1.3em;width:20em">Pro plan with everything you need</div>
            </main>
            """;
        await using var session = await browser.PageAsync(html);
        var audit = await session.Page.AuditTextSpacingAsync();

        var findings = audit.Findings.Where(f => f.RuleId == "blazor-text-spacing-clipped").ToArray();
        Assert.Single(findings);
        Assert.Equal("#fixed", findings[0].Target.Selector);
        Assert.Equal(0, await session.Page.Locator("style[data-a11y-text-spacing]").CountAsync());
    }

    [Fact]
    public async Task Audits_can_run_under_display_conditions()
    {
        const string html = """
            <style>@media (prefers-color-scheme: dark) { p { color: #222; background: #000; } }</style>
            <main><h1>Theme</h1><p>Readable only in light mode</p></main>
            """;
        await using var session = await browser.PageAsync(html);
        (await session.Page.AuditAccessibilityAsync()).ShouldNotHaveFinding("color-contrast");

        var dark = await session.Page.AuditUnderAsync(DisplayConditions.Dark);
        Assert.Equal("dark", dark.Context.State);
        dark.ShouldHaveFinding("color-contrast");

        // The emulation is restored afterwards.
        Assert.False(await session.Page.EvaluateAsync<bool>("() => matchMedia('(prefers-color-scheme: dark)').matches"));
    }

    [Fact]
    public async Task Focus_hidden_under_a_sticky_header_is_reported()
    {
        const string html = """
            <header style="position:fixed;top:0;left:0;right:0;height:120px;background:#fff;z-index:10"><a href="#">Home</a></header>
            <main style="padding-top:130px"><h1>Long page</h1>
              <div style="height:2000px">Spacer</div>
              <button id="target">Hidden when scrolled</button>
              <div style="height:2000px">Spacer</div>
            </main>
            <script>
              document.getElementById('target').addEventListener('focus', () => requestAnimationFrame(() => window.scrollBy(0, document.getElementById('target').getBoundingClientRect().top - 40)));
            </script>
            """;
        await using var session = await browser.PageAsync(html);
        await session.Page.Locator("#target").FocusAsync();
        await session.Page.WaitForTimeoutAsync(100);
        var focus = await session.Page.GetFocusAsync();
        Assert.Equal(FocusObscuration.EntirelyObscured, focus.Obscuration);
        Assert.Equal("header", focus.ObscuredBy);

        var scenario = KeyboardScenario.For(session.Page, new KeyboardScenarioOptions { ExpectTimeout = TimeSpan.FromMilliseconds(300) }).ExpectFocusNotObscured();
        await Assert.ThrowsAsync<AccessibilityAssertionException>(() => scenario.RunAsync());
    }

    [Fact]
    public async Task Focus_order_reports_entirely_obscured_stops()
    {
        const string html = """
            <main><h1>x</h1><button id="a">First</button><button id="b" style="position:relative">Covered</button>
            <div style="position:fixed;top:0;left:0;width:100vw;height:100vh;background:rgba(255,255,255,.95)">Cookie banner</div></main>
            """;
        await using var session = await browser.PageAsync(html);
        var order = await session.Page.CaptureFocusOrderAsync();
        Assert.Contains(order.Stops, s => s.Obscuration == FocusObscuration.EntirelyObscured);
        Assert.Contains(order.Findings, f => f.RuleId == "blazor-focus-obscured" && f.Target.Selector == "#b");
    }

    [Fact]
    public async Task Focus_indicators_made_from_borders_and_backgrounds_are_detected()
    {
        const string html = """
            <style>
              button { outline: none; border: 2px solid transparent; background: #fff; }
              #border:focus { border-color: #000; }
              #background:focus { background: #ffd; }
              #outline-offset-only:focus { outline-offset: 4px; }
            </style>
            <main><h1>x</h1><button id="border">Border</button><button id="background">Background</button><button id="outline-offset-only">Nothing</button></main>
            """;
        await using var session = await browser.PageAsync(html);
        var order = await session.Page.CaptureFocusOrderAsync(new FocusOrderOptions { ReportMissingFocusIndicator = true });

        Assert.Equal(FocusIndicator.Detected, order.Stops.Single(s => s.Element.Name == "Border").Indicator);
        Assert.Equal(FocusIndicator.Detected, order.Stops.Single(s => s.Element.Name == "Background").Indicator);
        Assert.Equal(FocusIndicator.NotDetected, order.Stops.Single(s => s.Element.Name == "Nothing").Indicator);
        Assert.Single(order.Findings, f => f.RuleId == "blazor-focus-indicator-not-detected");
    }

    [Fact]
    public async Task Keyboard_scenario_expects_a_focus_indicator_and_a_title()
    {
        const string html = """
            <style>#bare:focus { outline: none; }</style>
            <main><h1>x</h1><button id="ok">Visible</button><button id="bare">Invisible</button></main>
            """;
        await using var session = await browser.PageAsync(html);
        await KeyboardScenario.For(session.Page).StartFromDocument().PressTab().ExpectFocusIndicator().ExpectTitle("Test").RunAsync();

        var failing = KeyboardScenario.For(session.Page, new KeyboardScenarioOptions { ExpectTimeout = TimeSpan.FromMilliseconds(1500) })
            .StartFromDocument().PressTab(2).ExpectFocused("button", "Invisible").ExpectFocusIndicator();
        var ex = await Assert.ThrowsAsync<AccessibilityAssertionException>(() => failing.RunAsync());
        Assert.Contains("focus indicator", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Display_conditions_can_be_set_for_every_context()
    {
        await using var dark = await AccessibilityBrowser.LaunchAsync(new AccessibilityBrowserOptions { DisplayConditions = DisplayConditions.Dark });
        await using var session = await dark.NewSessionAsync();
        Assert.True(await session.Page.EvaluateAsync<bool>("() => matchMedia('(prefers-color-scheme: dark)').matches"));
        Assert.True(await session.Page.EvaluateAsync<bool>("() => matchMedia('(prefers-reduced-motion: reduce)').matches"));

        // An audit under other conditions restores the context's own conditions afterwards.
        await session.Page.SetContentAsync("<!DOCTYPE html><html lang=\"en\"><head><title>T</title></head><body><main><h1>x</h1></main></body></html>");
        var light = await session.Page.AuditUnderAsync(DisplayConditions.Light);
        Assert.Equal("light", light.Context.State);
        Assert.True(await session.Page.EvaluateAsync<bool>("() => matchMedia('(prefers-color-scheme: dark)').matches"));
        Assert.Equal("dark, forced colors", (DisplayConditions.Dark with { ForcedColors = ForcedColors.Active }).Describe());
    }
}
