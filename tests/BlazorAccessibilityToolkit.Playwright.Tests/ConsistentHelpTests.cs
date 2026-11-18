namespace BlazorAccessibilityToolkit.Playwright.Tests;

public sealed class ConsistentHelpTests(BrowserFixture browser)
{
    private static string Page(string title, string footer, string header = "") => $"""
        <!DOCTYPE html><html lang="en"><head><title>{title}</title></head><body>
        <header>{header}<a href="/">Home</a></header><main><h1>{title}</h1></main>
        <footer>{footer}</footer></body></html>
        """;

    private const string Standard = """<a href="/help">Help centre</a> <a href="mailto:support@example.com">Email us</a>""";

    private async Task<BrowserSession> SiteAsync(Dictionary<string, string> pages)
    {
        var session = await browser.Browser.NewSessionAsync();
        await session.Page.RouteAsync("http://site.test/**", route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            return pages.TryGetValue(path, out var html)
                ? route.FulfillAsync(new() { ContentType = "text/html", Body = html })
                : route.FulfillAsync(new() { Status = 404, Body = "not found" });
        });
        return session;
    }

    [Fact]
    public async Task Help_in_the_same_order_and_region_passes()
    {
        await using var session = await SiteAsync(new()
        {
            ["/a"] = Page("Orders", Standard),
            ["/b"] = Page("Account", Standard),
            ["/c"] = Page("Search", string.Empty),     // a page without help is allowed
        });

        var audit = await session.Page.AuditConsistentHelpAsync(new Uri("http://site.test/"), ["/a", "/b", "/c"]);

        Assert.Equal(AuditStatus.Completed, audit.Status);
        Assert.Empty(audit.Findings);
        Assert.Contains("blazor-help-inconsistent", audit.PassedRules);
    }

    [Fact]
    public async Task Reordered_or_moved_help_is_reported()
    {
        await using var session = await SiteAsync(new()
        {
            ["/a"] = Page("Orders", Standard),
            ["/b"] = Page("Account", """<a href="mailto:support@example.com">Email us</a> <a href="/help">Help centre</a>"""),
            ["/c"] = Page("Search", """<a href="mailto:support@example.com">Email us</a>""", header: """<a href="/help">Help centre</a>"""),
        });

        var audit = await session.Page.AuditConsistentHelpAsync(new Uri("http://site.test/"), ["/a", "/b", "/c"]);

        Assert.Equal(2, audit.Findings.Count);
        var reordered = Assert.Single(audit.Findings, f => f.Data["page"] == "/b");
        Assert.Contains("the order is \"Email us\", \"Help centre\"", reordered.Remediation, StringComparison.Ordinal);
        var moved = Assert.Single(audit.Findings, f => f.Data["page"] == "/c");
        Assert.Contains("\"Help centre\" is in the banner landmark, but on /a in the contentinfo landmark", moved.Remediation, StringComparison.Ordinal);
        Assert.All(audit.Findings, f => Assert.Equal(["3.2.6"], f.Wcag.Select(w => w.Id)));
    }

    [Fact]
    public async Task Help_repeated_in_header_and_footer_in_the_same_order_passes()
    {
        const string help = """<a href="/help">Help centre</a>""";
        await using var session = await SiteAsync(new()
        {
            ["/a"] = Page("Orders", help + " " + Standard, header: help),
            ["/b"] = Page("Account", help + " " + Standard, header: help),
        });

        var audit = await session.Page.AuditConsistentHelpAsync(new Uri("http://site.test/"), ["/a", "/b"]);

        Assert.Empty(audit.Findings);
    }
}
