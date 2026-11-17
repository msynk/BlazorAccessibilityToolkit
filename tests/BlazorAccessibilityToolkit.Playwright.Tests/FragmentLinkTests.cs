namespace BlazorAccessibilityToolkit.Playwright.Tests;

public sealed class FragmentLinkTests(BrowserFixture browser)
{
    [Fact]
    public async Task In_page_links_that_resolve_to_another_page_are_reported()
    {
        await using var session = await browser.Browser.NewSessionAsync();
        await session.Page.RouteAsync("**/app/orders", route => route.FulfillAsync(new() { ContentType = "text/html", Body = """
            <!DOCTYPE html><html lang="en"><head><title>Orders</title><base href="/app/"></head>
            <body><a href="#main">Skip to main content</a><a href="/app/orders#details">Details</a><main id="main"><h1>Orders</h1><p id="details">x</p></main></body></html>
            """ }));
        await session.Page.GotoAsync("http://a11y.test/app/orders");

        var audit = await session.Page.AuditAccessibilityAsync();

        var finding = Assert.Single(audit.Findings, f => f.RuleId == "blazor-fragment-link-leaves-page");
        Assert.Equal("#main", finding.Data["href"]);
        Assert.Equal("/app/#main", finding.Data["resolved"]);
        Assert.Equal(["2.4.1"], finding.Wcag.Select(w => w.Id));
    }
}
