using BlazorAccessibilityToolkit.Components;
using BlazorAccessibilityToolkit.Playwright.Hosting;
using BlazorAccessibilityToolkit.Playwright.Tests.TestComponents.Helpers;
using Microsoft.Playwright;

namespace BlazorAccessibilityToolkit.Playwright.Tests;

/// <summary>A host with the runtime components' services and their scoped CSS bundle.</summary>
public sealed class ComponentsHostFixture : IAsyncLifetime
{
    public BlazorTestHost Host { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var options = new BlazorTestHostOptions { ConfigureServices = services => services.AddAccessibilityComponents() };
        options.Stylesheets.Add("_content/BlazorAccessibilityToolkit.Components/BlazorAccessibilityToolkit.Components.bundle.scp.css");
        Host = await BlazorTestHost.StartAsync(options);
    }

    public async ValueTask DisposeAsync() => await Host.DisposeAsync();
}

public sealed class RuntimeComponentTests(BrowserFixture browser, ComponentsHostFixture fixture) : IClassFixture<ComponentsHostFixture>
{
    [Fact]
    public async Task Live_announcer_announces_and_repeats_messages()
    {
        await using var component = await fixture.Host.RenderAsync<AnnouncerProbe>(browser.Browser);
        var monitor = await component.MonitorLiveRegionsAsync();
        var save = component.Page.GetByRole(AriaRole.Button, new() { Name = "Save" });

        await save.ClickAsync();
        Assert.Equal("polite", (await monitor.ExpectUpdateAsync("Saved", TextMatch.Exact)).Politeness);

        // The same message again is announced again (the region is emptied and refilled).
        await monitor.ResetAsync();
        await save.ClickAsync();
        await monitor.ExpectUpdateAsync("Saved", TextMatch.Exact);

        await component.Page.GetByRole(AriaRole.Button, new() { Name = "Fail" }).ClickAsync();
        Assert.Equal("assertive", (await monitor.ExpectUpdateAsync("Error: not saved")).Politeness);

        Assert.DoesNotContain(await monitor.AnalyzeAsync(), f => f.Status == FindingStatus.Violation);
        (await component.AuditAsync("after announcements")).ShouldHaveNoViolations();
    }

    [Fact]
    public async Task Skip_link_appears_on_focus_and_moves_focus_to_the_main_content()
    {
        await using var component = await fixture.Host.RenderAsync<SkipLinkProbe>(browser.Browser);
        var link = component.Page.GetByRole(AriaRole.Link, new() { Name = "Skip to main content" });

        Assert.True((await link.BoundingBoxAsync())!.Width <= 1, "The skip link is visually hidden until it receives focus.");

        var scenario = component.Keyboard();
        if (BrowserFixture.Current == "webkit")
        {
            // WebKit, like Safari's default settings, does not move focus to links with Tab.
            scenario.FocusOn(link);
        }
        else
        {
            scenario.StartFromDocument().PressTab();
        }

        await scenario
            .ExpectFocused("link", "Skip to main content")
            .ExpectFocusIndicator()
            .ExpectFocusNotObscured(allowPartial: false)
            .PressEnter()
            .ExpectFocused(component.Page.Locator("#main-content"))
            .PressTab()
            .ExpectFocused("button", "In main")
            .RunAsync();

        Assert.Equal(component.Url.AbsoluteUri, component.Page.Url.Split('#')[0]);

        (await component.AuditAsync()).ShouldHaveNoViolations();
    }

    [Fact]
    public async Task Focus_trap_keeps_focus_inside_and_restores_it()
    {
        await using var component = await fixture.Host.RenderAsync<TrapProbe>(browser.Browser);
        var dialog = component.Page.GetByRole(AriaRole.Dialog, new() { Name = "Filters" });

        await component.Keyboard()
            .StartFromDocument()
            .PressTab()
            .ExpectFocused("button", "Open filters")
            .RememberFocus("trigger")
            .PressEnter()
            .ExpectFocused("button", "Second")
            .ExpectFocusContainedWithin(dialog)
            .PressShiftTab(2)
            .ExpectFocused("button", "Close")
            .PressEnter()
            .ExpectFocusRestored("trigger")
            .RunAsync();

        // A click outside the open trap does not take focus out of it.
        await component.Page.GetByRole(AriaRole.Button, new() { Name = "Open filters" }).ClickAsync();
        await component.Page.GetByRole(AriaRole.Button, new() { Name = "After" }).FocusAsync();
        await component.Keyboard().ExpectFocusWithin(dialog).RunAsync();
    }

    [Fact]
    public async Task Route_announcer_announces_client_side_navigation()
    {
        await using var component = await fixture.Host.RenderAsync<RouteProbe>(browser.Browser);
        var monitor = await component.MonitorLiveRegionsAsync();

        await component.Page.GetByRole(AriaRole.Button, new() { Name = "Next page" }).ClickAsync();
        var change = await monitor.ExpectUpdateAsync("Now on ");
        Assert.Equal("polite", change.Politeness);
        Assert.Contains("page=2", component.Page.Url, StringComparison.Ordinal);

        // A second navigation to a page with the same title is announced again.
        await monitor.ResetAsync();
        await component.Page.GetByRole(AriaRole.Button, new() { Name = "Next page" }).ClickAsync();
        await monitor.ExpectUpdateAsync("Now on ");
    }

    [Fact]
    public async Task Announcer_without_a_rendered_host_reports_that_nothing_was_announced()
    {
        var service = new LiveAnnouncerService();
        Assert.False(service.IsAttached);
        Assert.False(await service.AnnounceAsync("Nobody hears this"));
        await service.ClearAsync();
    }
}
