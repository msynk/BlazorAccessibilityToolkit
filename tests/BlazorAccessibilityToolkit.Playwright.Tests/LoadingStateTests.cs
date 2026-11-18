using BlazorAccessibilityToolkit.Playwright.Hosting;
using BlazorAccessibilityToolkit.Playwright.Tests.TestComponents.RenderModes;
using Microsoft.Playwright;

namespace BlazorAccessibilityToolkit.Playwright.Tests;

public sealed class LoadingStateTests(BrowserFixture browser, HostFixture host)
{
    [Fact]
    public async Task Streamed_content_with_aria_busy_and_status_messages_passes()
    {
        await using var component = await host.Host.RenderAsync<StreamingOrders>(browser.Browser, p => p.Add(c => c.Accessible, true), HostedRenderMode.Static);

        var result = await AriaPatterns.VerifyLoadingStateAsync(component.Page, component.Url.AbsoluteUri, "#orders", component.Page.GetByRole(AriaRole.List, new() { Name = "Orders" }));

        Assert.True(result.Passed, result.Describe());
    }

    [Fact]
    public async Task Bare_streaming_placeholder_fails()
    {
        await using var component = await host.Host.RenderAsync<StreamingOrders>(browser.Browser, renderMode: HostedRenderMode.Static);

        var result = await AriaPatterns.VerifyLoadingStateAsync(component.Page, component.Url.AbsoluteUri, "#orders", component.Page.GetByRole(AriaRole.List, new() { Name = "Orders" }));

        Assert.Contains(result.Checks, c => !c.Passed && c.Requirement.StartsWith("While loading", StringComparison.Ordinal));
        Assert.Contains(result.Checks, c => c.Passed && c.Requirement == "The content finishes loading");
        Assert.Contains(result.Checks, c => !c.Passed && c.Requirement == "A status message reports the result");
    }

    [Fact]
    public async Task A_persistent_unrelated_status_does_not_count_as_announcing_the_load()
    {
        await using var component = await host.Host.RenderAsync<StreamingOrders>(browser.Browser, p => p.Add(c => c.CartStatus, "Cart: 2 items"), HostedRenderMode.Static);

        var result = await AriaPatterns.VerifyLoadingStateAsync(component.Page, component.Url.AbsoluteUri, "#orders", component.Page.GetByRole(AriaRole.List, new() { Name = "Orders" }));

        Assert.Contains(result.Checks, c => !c.Passed && c.Requirement.StartsWith("While loading", StringComparison.Ordinal));
        Assert.Contains(result.Checks, c => !c.Passed && c.Requirement == "A status message reports the result");
    }
}
