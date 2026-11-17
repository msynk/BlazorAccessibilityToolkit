namespace BlazorAccessibilityToolkit.Playwright.Tests;

public sealed class NameAndTargetHeuristicTests(BrowserFixture browser)
{
    private static readonly AccessibilityAuditOptions WithBestPractices = new() { IncludeBestPractices = true };

    [Fact]
    public async Task Same_link_names_with_different_destinations_and_role_words_are_reported()
    {
        const string html = """
            <main><h1>Orders</h1>
              <p>Order 41 <a href="/orders/41">Read more</a></p>
              <p>Order 42 <a href="/orders/42">Read more</a></p>
              <a href="/help">Help</a> <a href="/help#top">Help</a>
              <button type="button">Submit button</button>
              <a href="/history">Link to order history</a>
              <button type="button">Button</button>
            </main>
            """;
        await using var session = await browser.PageAsync(html);
        var audit = await session.Page.AuditAccessibilityAsync(WithBestPractices);

        var ambiguous = Assert.Single(audit.Findings, f => f.RuleId == "blazor-ambiguous-link-name");
        Assert.Equal(("Read more", "2", "2"), (ambiguous.Data["name"], ambiguous.Data["links"], ambiguous.Data["destinations"]));

        var roles = audit.Findings.Where(f => f.RuleId == "blazor-role-in-name").Select(f => f.Data["name"]).Order().ToArray();
        Assert.Equal(["Link to order history", "Submit button"], roles);

        // Without best practices, the role-in-name rule stays quiet.
        var defaults = await session.Page.AuditAccessibilityAsync();
        Assert.DoesNotContain(defaults.Findings, f => f.RuleId == "blazor-role-in-name");
    }

    [Fact]
    public async Task Reconnect_modal_without_name_and_live_region_is_reported()
    {
        const string template = """
            <main><h1>App</h1></main>
            <dialog id="components-reconnect-modal"><div><p>Rejoining the server...</p><button id="components-reconnect-button">Retry</button></div></dialog>
            """;
        await using var session = await browser.PageAsync(template);
        var finding = Assert.Single((await session.Page.AuditAccessibilityAsync()).Findings, f => f.RuleId == "blazor-reconnect-ui-not-accessible");
        Assert.Equal("2", finding.Data["problems"]);

        const string accessible = """
            <main><h1>App</h1></main>
            <dialog id="components-reconnect-modal" aria-labelledby="reconnect-status"><div role="status" id="reconnect-status"><p>Rejoining the server...</p></div><button>Retry</button></dialog>
            """;
        await using var fixedSession = await browser.PageAsync(accessible);
        var audit = await fixedSession.Page.AuditAccessibilityAsync();
        Assert.DoesNotContain(audit.Findings, f => f.RuleId == "blazor-reconnect-ui-not-accessible");
        Assert.Contains("blazor-reconnect-ui-not-accessible", audit.PassedRules);
    }

    [Fact]
    public async Task Targets_smaller_than_44_pixels_are_reported_except_inline_links()
    {
        const string html = """
            <style>.small { width: 24px; height: 24px; padding: 0; } .large { min-width: 48px; min-height: 48px; } label.row { display: block; padding: 14px 0; }</style>
            <main><h1>Targets</h1>
              <button class="small" aria-label="Close">×</button>
              <button class="large">Save</button>
              <p>Read the <a href="/terms">terms</a> before you continue.</p>
              <label class="row"><input type="checkbox"> Remember me</label>
            </main>
            """;
        await using var session = await browser.PageAsync(html);
        var audit = await session.Page.AuditTargetSizeAsync();

        var finding = Assert.Single(audit.Findings);
        Assert.Equal("blazor-target-size-enhanced", finding.RuleId);
        Assert.Equal("Close", finding.Target.AccessibleName);
        Assert.Equal("24", finding.Data["width"]);
        Assert.Equal(["2.5.5"], finding.Wcag.Select(w => w.Id));

        var material = await session.Page.AuditTargetSizeAsync(new TargetSizeOptions { Minimum = 60 });
        Assert.Contains(material.Findings, f => f.Target.AccessibleName == "Save");
    }
}
