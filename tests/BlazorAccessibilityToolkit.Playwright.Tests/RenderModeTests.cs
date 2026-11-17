using BlazorAccessibilityToolkit.Playwright.Tests.TestComponents.RenderModes;

namespace BlazorAccessibilityToolkit.Playwright.Tests;

public sealed class RenderModeTests(BrowserFixture browser, HostFixture host)
{
    [Fact]
    public async Task Virtualized_items_without_position_and_set_size_are_reported()
    {
        await using var component = await host.Host.RenderAsync<VirtualList>(browser.Browser);
        await component.Root.GetByText("Message 1", new() { Exact = true }).WaitForAsync();

        var audit = await component.AuditAsync();

        var finding = Assert.Single(audit.Findings, f => f.RuleId == "blazor-virtualized-set-size");
        Assert.Equal(finding.Data["rendered"], finding.Data["withoutPosition"]);
        Assert.True(int.Parse(finding.Data["rendered"], System.Globalization.CultureInfo.InvariantCulture) < 1000);
    }

    [Fact]
    public async Task Virtualized_items_with_position_and_set_size_pass()
    {
        await using var component = await host.Host.RenderAsync<VirtualList>(browser.Browser, p => p.Add(c => c.Positioned, true));
        await component.Root.GetByText("Message 1", new() { Exact = true }).WaitForAsync();

        var audit = await component.AuditAsync();

        Assert.DoesNotContain(audit.Findings, f => f.RuleId == "blazor-virtualized-set-size");
        Assert.Contains("blazor-virtualized-set-size", audit.PassedRules);
        Assert.Equal("1000", await component.Root.Locator("li").First.GetAttributeAsync("aria-setsize"));
    }

    [Fact]
    public async Task Nested_interactive_elements_with_the_same_box_are_reported()
    {
        const string html = """
            <main><h1>x</h1>
              <div role="button" tabindex="0" style="display:inline-block"><a href="/edit" style="display:block">Edit</a></div>
              <button style="padding:8px"><span tabindex="-1">Save</span></button>
            </main>
            """;
        await using var session = await browser.PageAsync(html);
        var audit = await session.Page.AuditAccessibilityAsync(new AccessibilityAuditOptions { IncludeBestPractices = true });

        var finding = Assert.Single(audit.Findings, f => f.RuleId == "blazor-duplicate-clickable-bounds");
        Assert.Equal("Edit", finding.Target.AccessibleName);
    }
}
