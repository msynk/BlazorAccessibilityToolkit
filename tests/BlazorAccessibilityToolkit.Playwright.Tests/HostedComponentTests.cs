using BlazorAccessibilityToolkit.Playwright.Hosting;
using BlazorAccessibilityToolkit.Playwright.Tests.TestComponents;
using Microsoft.AspNetCore.Components;

[assembly: AssemblyFixture(typeof(BlazorAccessibilityToolkit.Playwright.Tests.HostFixture))]

namespace BlazorAccessibilityToolkit.Playwright.Tests;

public sealed class HostFixture : IAsyncLifetime
{
    public BlazorTestHost Host { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Host = await BlazorTestHost.StartAsync();

    public async ValueTask DisposeAsync() => await Host.DisposeAsync();
}

public sealed class HostedComponentTests(BrowserFixture browser, HostFixture host)
{
    [Fact]
    public void Host_supports_interactive_rendering_from_a_web_sdk_test_project() =>
        Assert.True(host.Host.SupportsInteractiveRendering, host.Host.InteractiveUnavailableReason);

    [Fact]
    public async Task Renders_interactively_with_in_process_parameters_and_keyboard_input()
    {
        var saved = 0;
        await using var component = await host.Host.RenderAsync<SaveButton>(browser.Browser, p => p
            .Add(c => c.Title, "Notifications")
            .Add(c => c.OnSaved, EventCallback.Factory.Create(this, () => saved++))
            .AddCascadingValue("dark", "Theme")
            .AddChildContent("<p id=\"child\">Child content</p>"));

        await component.Root.Locator("h2", new() { HasText = "Notifications" }).WaitForAsync();
        Assert.Equal(1, await component.Root.Locator("[data-theme=dark]").CountAsync());
        Assert.Equal(1, await component.Root.Locator("#child").CountAsync());

        await component.Keyboard()
            .StartFromDocument()
            .PressTab()
            .ExpectFocused("button", "Save")
            .PressEnter()
            .ExpectLiveRegionUpdate("Changes saved")
            .Audit("after save")
            .RunAsync();

        Assert.Equal(1, saved);
        var audit = Assert.Single(component.Audits);
        Assert.Equal(AuditTargetKind.Component, audit.Context.Kind);
        Assert.Equal("interactive-server", audit.Context.RenderMode);
        Assert.Equal(typeof(SaveButton).FullName, audit.Context.ComponentType);
        Assert.Equal("after save", audit.Context.State);
    }

    [Fact]
    public async Task Invoke_runs_on_the_circuit_dispatcher()
    {
        await using var component = await host.Host.RenderAsync<SaveButton>(browser.Browser);
        var monitor = await component.MonitorLiveRegionsAsync();
        await component.InvokeAsync(c => c.Reset());
        await component.InvokeAsync(_ => Task.CompletedTask);
        await monitor.ExpectUpdateAsync("Reset");
    }

    [Fact]
    public async Task Findings_are_attributed_to_the_component()
    {
        await using var component = await host.Host.RenderAsync<IconOnlyButton>(browser.Browser);
        var audit = await component.AuditAsync();
        var finding = audit.ShouldHaveFinding("button-name");
        Assert.Equal(typeof(IconOnlyButton).FullName, finding.Component!.ComponentType);
        Assert.Equal("fixture-root", finding.Component.Method);
        Assert.DoesNotContain(audit.Findings, f => f.RuleId is "region" or "page-has-heading-one" or "landmark-one-main");
    }

    [Fact]
    public async Task Static_rendering_produces_auditable_markup_without_interactivity()
    {
        await using var component = await host.Host.RenderAsync<IconOnlyButton>(browser.Browser, renderMode: HostedRenderMode.Static);
        Assert.Equal(HostedRenderMode.Static, component.RenderMode);
        var audit = await component.AuditAsync("static");
        Assert.Equal("static-ssr", audit.Context.RenderMode);
        audit.ShouldHaveFinding("button-name");
        await Assert.ThrowsAsync<InvalidOperationException>(() => component.InvokeAsync(_ => { }));
    }

    [Fact]
    public async Task Modal_dialog_moves_contains_and_restores_focus()
    {
        await using var component = await host.Host.RenderAsync<ConfirmDialog>(browser.Browser);
        var dialog = component.Page.GetByRole(Microsoft.Playwright.AriaRole.Dialog);
        var trigger = component.Page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Delete file" });

        await component.Keyboard()
            .StartFromDocument()
            .PressTab()
            .ExpectFocused("button", "Delete file")
            .RememberFocus("trigger")
            .PressEnter()
            .ExpectFocused("button", "Cancel")
            .ExpectState(trigger, "expanded", "true")
            .ExpectFocusContainedWithin(dialog)
            .Audit("dialog open")
            .PressEscape()
            .ExpectFocusRestored("trigger")
            .ExpectState(trigger, "expanded", "false")
            .RunAsync();

        var order = await component.CaptureFocusOrderAsync();
        order.ShouldMatch("button Delete file").ShouldNotTrapFocus();
    }

    [Fact]
    public async Task Modal_dialog_follows_the_apg_pattern()
    {
        await using var component = await host.Host.RenderAsync<ConfirmDialog>(browser.Browser);
        var result = await AriaPatterns.VerifyModalDialogAsync(component.Page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Delete file" }));
        result.ShouldPass();
    }

    [Fact]
    public async Task Server_disconnection_fails_expectations_instead_of_hanging()
    {
        var isolatedHost = await BlazorTestHost.StartAsync();
        await using var component = await isolatedHost.RenderAsync<SaveButton>(browser.Browser);
        await isolatedHost.DisposeAsync();

        // The DOM is still auditable, but interactions can no longer reach the server.
        Assert.Equal(AuditStatus.Completed, (await component.AuditAsync("disconnected")).Status);
        var scenario = component.Keyboard(new KeyboardScenarioOptions { ExpectTimeout = TimeSpan.FromSeconds(1) })
            .StartFromDocument()
            .PressTab()
            .PressEnter()
            .ExpectLiveRegionUpdate("Changes saved");
        var ex = await Assert.ThrowsAsync<AccessibilityAssertionException>(() => scenario.RunAsync());
        Assert.Contains("no live-region updates were observed", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rendering_errors_surface_with_server_details()
    {
        var ex = await Assert.ThrowsAsync<HostedComponentException>(() => host.Host.RenderAsync<Exploding>(browser.Browser, name: "exploding"));
        Assert.Contains("Boom from Exploding", ex.Message, StringComparison.Ordinal);
        Assert.Contains("failed while rendering", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Parallel_fixtures_are_isolated()
    {
        var titles = Enumerable.Range(1, 4).Select(i => $"Panel {i}").ToArray();
        var components = await Task.WhenAll(titles.Select(t => host.Host.RenderAsync<SaveButton>(browser.Browser, p => p.Add(c => c.Title, t))));
        try
        {
            for (var i = 0; i < titles.Length; i++)
            {
                Assert.Equal(titles[i], await components[i].Root.Locator("h2").InnerTextAsync());
                Assert.Equal(AuditStatus.Completed, (await components[i].AuditAsync()).Status);
            }
        }
        finally
        {
            foreach (var component in components)
            {
                await component.DisposeAsync();
            }
        }
    }
}
