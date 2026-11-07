using BlazorAccessibilityToolkit.Playwright;
using Microsoft.Playwright;
using SampleComponents;

namespace SampleApp.ComponentTests;

/// <summary>
/// Real-browser tests of isolated components hosted with Interactive Server: keyboard input, focus management and
/// live regions. Each test renders its component in its own browser context.
/// </summary>
public sealed class InteractiveComponentTests(TestEnvironment env)
{
    [Fact]
    public async Task Modal_dialog_manages_focus_and_closes_with_escape()
    {
        await using var component = await env.Host.RenderAsync<ModalDialog>(env.Browser, p => p
            .Add(c => c.TriggerText, "Delete account")
            .Add(c => c.Title, "Delete account?")
            .Add(c => c.ConfirmText, "Delete")
            .AddChildContent("<p>This cannot be undone.</p>"));

        var trigger = component.Page.GetByRole(AriaRole.Button, new() { Name = "Delete account" });
        var dialog = component.Page.GetByRole(AriaRole.Dialog);

        await component.Keyboard()
            .StartFromDocument()
            .PressTab()
            .ExpectFocused("button", "Delete account")
            .RememberFocus("trigger")
            .PressEnter()
            .ExpectFocusWithin(dialog)
            .ExpectFocused("button", "Cancel")
            .ExpectState(trigger, "expanded", "true")
            .ExpectFocusContainedWithin(dialog)
            .Audit("dialog open")
            .PressEscape()
            .ExpectFocusRestored("trigger")
            .ExpectState(trigger, "expanded", "false")
            .Audit("dialog closed")
            .RunAsync();

        Assert.Equal(2, component.Audits.Count);
    }

    [Fact]
    public async Task Tabs_support_arrow_keys_home_and_end()
    {
        await using var component = await env.Host.RenderAsync<Tabs>(env.Browser, p => p
            .Add(c => c.Label, "Sections")
            .Add(c => c.Items, SampleData.Tabs));
        var privacy = component.Page.GetByRole(AriaRole.Tab, new() { Name = "Privacy" });

        await component.Keyboard()
            .StartFromDocument()
            .PressTab()
            .ExpectFocused("tab", "Profile")
            .PressArrowRight()
            .ExpectFocused("tab", "Notifications")
            .PressEnd()
            .ExpectFocused("tab", "Privacy")
            .ExpectState(privacy, "selected", "true")
            .PressArrowRight()
            .ExpectFocused("tab", "Profile")
            .PressArrowLeft()
            .ExpectFocused("tab", "Privacy")
            .PressTab()
            .ExpectFocused("tabpanel", "Privacy")
            .Audit("privacy selected")
            .RunAsync();

        // Only the selected tab is in the tab order (roving tabindex).
        var order = await component.CaptureFocusOrderAsync();
        order.ShouldMatch("tab Privacy", "tabpanel Privacy").ShouldNotTrapFocus();
    }

    [Fact]
    public async Task Tabs_accessibility_tree_matches_the_snapshot()
    {
        await using var component = await env.Host.RenderAsync<Tabs>(env.Browser, p => p
            .Add(c => c.Label, "Sections")
            .Add(c => c.Items, SampleData.Tabs));

        await component.ShouldMatchStoredAriaSnapshotAsync("tabs-initial");
    }

    [Fact]
    public async Task Save_status_is_exposed_in_a_live_region()
    {
        await using var component = await env.Host.RenderAsync<SaveButton>(env.Browser, p => p
            .Add(c => c.OnSave, () => Task.Delay(100)));

        var scenario = component.Keyboard()
            .StartFromDocument()
            .PressTab()
            .ExpectFocused("button", "Save changes")
            .PressEnter()
            .ExpectLiveRegionUpdate("Saving")
            .ExpectLiveRegionUpdate("Changes saved");
        await scenario.RunAsync();

        // The region existed before the update and stayed visible: no known announcement blockers.
        Assert.Empty(await scenario.LiveRegions!.AnalyzeAsync());
    }

    [Fact]
    public async Task Signup_form_reports_errors_and_success()
    {
        await using var component = await env.Host.RenderAsync<SignupForm>(env.Browser);
        var name = component.Page.GetByLabel("Full name");
        var email = component.Page.GetByLabel("Email");

        await component.Keyboard()
            .StartFromDocument()
            .PressTab(3)
            .ExpectFocused("button", "Create account")
            .PressEnter()
            .ExpectState(name, "invalid", "true")
            .ExpectState(email, "invalid", "true")
            .Audit("invalid submit")
            .RunAsync();

        Assert.Equal("Enter your email address.", (await email.GetAccessibilityInfoAsync()).Description);

        await name.FillAsync("Ana Lima");
        await email.FillAsync("ana@example.com");
        await component.Keyboard()
            .FocusOn(component.Page.GetByRole(AriaRole.Button, new() { Name = "Create account" }))
            .PressEnter()
            .ExpectLiveRegionUpdate("Account created for ana@example.com.")
            .Audit("submitted")
            .RunAsync();
    }
}
