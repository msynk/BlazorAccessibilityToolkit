using Microsoft.Playwright;

namespace BlazorAccessibilityToolkit.Playwright.Tests;

/// <summary>Focus indicators measured from screenshots (WCAG 2.4.7 and 2.4.13).</summary>
public sealed class FocusAppearanceTests(BrowserFixture browser)
{
    private const string Page = """
        <style>
          body { background: #fff; color: #000; font: 16px sans-serif; }
          button, input { width: 120px; height: 32px; margin: 24px; border: 1px solid #767676; background: #fff; color: #000; }
          #none:focus, #field:focus { outline: none; }
          #thin:focus-visible { outline: 1px solid #ccc; }
          #thick:focus-visible { outline: 3px solid #000; outline-offset: 2px; }
          #background { outline: none; }
          #background:focus-visible { outline: none; background: #00205b; color: #fff; }
        </style>
        <main>
          <h1>Focus</h1>
          <button id="default" style="outline: revert">Default</button>
          <button id="none">None</button>
          <button id="thin">Thin</button>
          <button id="thick">Thick</button>
          <button id="background">Background</button>
          <input id="field" aria-label="Field">
        </main>
        """;

    [Fact]
    public async Task Measures_what_changes_on_screen_when_an_element_receives_focus()
    {
        await using var session = await browser.PageAsync(Page);
        var page = session.Page;

        var none = await page.Locator("#none").MeasureFocusAppearanceAsync();
        var thin = await page.Locator("#thin").MeasureFocusAppearanceAsync();
        var thick = await page.Locator("#thick").MeasureFocusAppearanceAsync();
        var background = await page.Locator("#background").MeasureFocusAppearanceAsync();
        var field = await page.Locator("#field").MeasureFocusAppearanceAsync();

        Assert.NotNull(none);
        Assert.True(none.FocusVisibleMatched);
        Assert.False(none.IsVisible, none.ToString());
        Assert.Equal(4 * (120 + 32), none.RequiredArea, 0);

        Assert.True(thin!.IsVisible, thin.ToString());
        Assert.False(thin.MeetsFocusAppearance, thin.ToString());

        Assert.True(thick!.MeetsFocusAppearance, thick.ToString());
        Assert.True(background!.MeetsFocusAppearance, background.ToString());

        // The text caret is hidden in the screenshots, so a field without an indicator shows no change.
        Assert.False(field!.IsVisible, field.ToString());

        // Focus is removed afterwards.
        Assert.Equal("BODY", await page.EvaluateAsync<string>("() => document.activeElement.tagName"));
    }

    [Fact]
    public async Task Browser_default_focus_ring_is_visible()
    {
        await using var session = await browser.PageAsync("<main><h1>x</h1><button>Save</button><a href=\"#x\">Link</a><input aria-label=\"Name\"></main>");

        foreach (var selector in new[] { "button", "input" })
        {
            var measurement = await session.Page.Locator(selector).MeasureFocusAppearanceAsync();
            Assert.True(measurement!.IsVisible, $"{selector}: {measurement}");
        }
    }

    [Fact]
    public async Task Focus_order_reports_invisible_and_too_small_indicators()
    {
        await using var session = await browser.PageAsync(Page);

        var order = await session.Page.CaptureFocusOrderAsync(new FocusOrderOptions { MeasureFocusAppearance = true, ReportFocusAppearance = true });

        Assert.All(order.Stops, s => Assert.NotNull(s.Appearance));

        // The browser's own focus ring is exempt from 2.4.13.
        Assert.True(order.Stops.Single(s => s.Element.Selector == "#default").Appearance!.UserAgentIndicator);
        var notVisible = order.Findings.Where(f => f.RuleId == "blazor-focus-not-visible").Select(f => f.Target.Selector).Order(StringComparer.Ordinal);
        Assert.Equal(["#field", "#none"], notVisible);
        var tooSmall = Assert.Single(order.Findings, f => f.RuleId == "blazor-focus-appearance");
        Assert.Equal("#thin", tooSmall.Target.Selector);
        Assert.Equal(FindingStatus.Violation, tooSmall.Status);
        Assert.Contains(tooSmall.Wcag, w => w.Id == "2.4.13");
        Assert.Contains("608 px²", tooSmall.Remediation, StringComparison.Ordinal);

        // Without ReportFocusAppearance, only the AA rule is reported.
        var aa = await session.Page.CaptureFocusOrderAsync(new FocusOrderOptions { MeasureFocusAppearance = true });
        Assert.DoesNotContain(aa.Findings, f => f.RuleId == "blazor-focus-appearance");
        Assert.Equal(2, aa.Findings.Count(f => f.RuleId == "blazor-focus-not-visible"));
    }

    [Fact]
    public async Task Keyboard_scenario_expects_visible_focus()
    {
        await using var session = await browser.PageAsync(Page);
        await KeyboardScenario.For(session.Page)
            .FocusOn(session.Page.Locator("#thin")).PressTab().ExpectFocused("button", "Thick").ExpectVisibleFocus(focusAppearance: true)
            .PressTab().ExpectFocused("button", "Background").ExpectVisibleFocus()
            .RunAsync();

        var invisible = KeyboardScenario.For(session.Page).FocusOn(session.Page.Locator("#default")).PressTab().ExpectFocused("button", "None").ExpectVisibleFocus();
        var ex = await Assert.ThrowsAsync<AccessibilityAssertionException>(() => invisible.RunAsync());
        Assert.Contains("0 px² changed", ex.Message, StringComparison.Ordinal);

        var tooSmall = KeyboardScenario.For(session.Page).FocusOn(session.Page.Locator("#none")).PressTab().ExpectFocused("button", "Thin").ExpectVisibleFocus(focusAppearance: true);
        Assert.Contains("2.4.13", (await Assert.ThrowsAsync<AccessibilityAssertionException>(() => tooSmall.RunAsync())).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Skip_links_that_appear_on_focus_are_measured_where_they_appear()
    {
        const string html = """
            <style>
              body { background: #fff; }
              .skip { position: absolute; left: -9999px; top: 0; background: #000; color: #fff; padding: 8px; }
              .skip:focus { left: 8px; }
              .clipped { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; }
              .clipped:focus { width: auto; height: auto; clip: auto; top: 60px; left: 8px; background: #000; color: #fff; padding: 8px; }
              .never { position: absolute; left: -9999px; }
            </style>
            <a class="skip" href="#main">Skip to main content</a>
            <a class="clipped" href="#main">Skip to content</a>
            <a class="never" href="#main">Stays away</a>
            <main id="main"><h1>Content</h1></main>
            """;
        await using var session = await browser.PageAsync(html);

        foreach (var selector in new[] { ".skip", ".clipped" })
        {
            var measurement = await session.Page.Locator(selector).MeasureFocusAppearanceAsync();
            Assert.NotNull(measurement);
            Assert.True(measurement.HiddenUntilFocused, selector);
            Assert.True(measurement.IsVisible, $"{selector}: {measurement}");
            Assert.True(measurement.MeetsFocusAppearance, $"{selector}: {measurement}");
        }

        // A link that stays off screen when focused is reported by the focus order capture (blazor-focus-hidden-target).
        Assert.Null(await session.Page.Locator(".never").MeasureFocusAppearanceAsync());
    }

    [Fact]
    public async Task Elements_that_are_not_rendered_are_not_measured()
    {
        await using var session = await browser.PageAsync("<main><h1>x</h1><button style=\"display:none\">Hidden</button></main>");
        Assert.Null(await session.Page.Locator("button").MeasureFocusAppearanceAsync());
    }
}
