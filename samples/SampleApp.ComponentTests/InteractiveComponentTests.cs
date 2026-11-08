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
            .Do("click the backdrop", page => page.Mouse.ClickAsync(5, 5))
            .ExpectFocusWithin(dialog)
            .PressEscape()
            .ExpectFocusRestored("trigger")
            .ExpectState(trigger, "expanded", "false")
            .Audit("dialog closed")
            .RunAsync();

        Assert.Equal(2, component.Audits.Count);

        // The JS module and the stylesheet loaded, and nothing failed in the browser.
        Assert.Empty(component.ConsoleErrors);
    }

    [Fact]
    public async Task Modal_dialog_follows_the_aria_pattern()
    {
        await using var component = await env.Host.RenderAsync<ModalDialog>(env.Browser, p => p
            .Add(c => c.TriggerText, "Delete account")
            .Add(c => c.Title, "Delete account?")
            .AddChildContent("<p>This cannot be undone.</p>"));

        var result = await AriaPatterns.VerifyModalDialogAsync(component.Page.GetByRole(AriaRole.Button, new() { Name = "Delete account" }));

        result.ShouldPass();
    }

    [Fact]
    public async Task Tabs_support_arrow_keys_home_and_end()
    {
        await using var component = await env.Host.RenderAsync<Tabs>(env.Browser, p => p
            .Add(c => c.Label, "Sections")
            .Add(c => c.Items, SampleData.Tabs));
        var profile = component.Page.GetByRole(AriaRole.Tab, new() { Name = "Profile" });
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
            .PressHome()
            .ExpectFocused("tab", "Profile")
            .ExpectState(profile, "selected", "true")
            .PressArrowLeft()
            .ExpectFocused("tab", "Privacy")
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
    public async Task Tab_navigation_keys_do_not_scroll_the_page()
    {
        await using var component = await env.Host.RenderAsync<Tabs>(env.Browser, p => p
            .Add(c => c.Label, "Sections")
            .Add(c => c.Items, SampleData.Tabs));
        // A long page, and the furthest it scrolls. Focusing a tab scrolls it back into view, so the final position alone
        // would hide a jump to the bottom of the page and back.
        await component.Page.EvaluateAsync("""
            () => {
                document.body.style.minHeight = '5000px';
                window.__maxScrollY = 0;
                window.addEventListener('scroll', () => window.__maxScrollY = Math.max(window.__maxScrollY, window.scrollY));
            }
            """);

        await component.Keyboard()
            .FocusOn(component.Page.GetByRole(AriaRole.Tab, new() { Name = "Notifications" }))
            .PressEnd()
            .ExpectFocused("tab", "Privacy")
            .PressHome()
            .ExpectFocused("tab", "Profile")
            .RunAsync();

        // End and Home moved between tabs only; their default action (scrolling the page) was prevented.
        Assert.Equal(0, await component.Page.EvaluateAsync<double>("() => window.__maxScrollY"));
    }

    [Fact]
    public async Task Tabs_follow_the_aria_pattern()
    {
        await using var component = await env.Host.RenderAsync<Tabs>(env.Browser, p => p
            .Add(c => c.Label, "Sections")
            .Add(c => c.Items, SampleData.Tabs));

        var result = await AriaPatterns.VerifyTabsAsync(component.Page.GetByRole(AriaRole.Tablist));

        result.ShouldPass();
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
    public async Task Disclosure_card_follows_the_aria_pattern()
    {
        await using var component = await env.Host.RenderAsync<DisclosureCard>(env.Browser, p => p
            .Add(c => c.Title, "Shipping")
            .Add(c => c.Details, "Ships in 2 days."));

        var result = await AriaPatterns.VerifyDisclosureAsync(component.Page.GetByRole(AriaRole.Button, new() { Name = "Shipping" }));

        result.ShouldPass();
        (await component.AuditAsync("after keyboard toggling")).ShouldHaveNoViolations(AccessibilityExpectation.Strict);
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
    public async Task Save_failure_is_announced_and_saving_can_be_retried()
    {
        await using var component = await env.Host.RenderAsync<SaveButton>(env.Browser, p => p
            .Add(c => c.OnSave, () => Task.FromException(new InvalidOperationException("The server is unavailable."))));
        var button = component.Page.GetByRole(AriaRole.Button, new() { Name = "Save changes" });

        await component.Keyboard()
            .StartFromDocument()
            .PressTab()
            .ExpectFocused("button", "Save changes")
            .PressEnter()
            .ExpectLiveRegionUpdate("Changes could not be saved. Try again.", TextMatch.Exact)
            .ExpectState(button, "disabled", "false")
            .PressEnter()
            .ExpectLiveRegionUpdate("Saving")
            .ExpectLiveRegionUpdate("Changes could not be saved. Try again.", TextMatch.Exact)
            .Audit("save failed")
            .RunAsync();
    }

    [Fact]
    public async Task Signup_form_reports_errors_and_success()
    {
        await using var component = await env.Host.RenderAsync<SignupForm>(env.Browser);
        var name = component.Page.GetByLabel("Full name");
        var email = component.Page.GetByLabel("Email");
        var submit = component.Page.GetByRole(AriaRole.Button, new() { Name = "Create account" });

        await component.Keyboard()
            .StartFromDocument()
            .PressTab(3)
            .ExpectFocused("button", "Create account")
            .PressEnter()
            .ExpectState(name, "invalid", "true")
            .ExpectState(email, "invalid", "true")
            .ExpectFocused("textbox", "Full name")      // focus moves to the first invalid field
            .Audit("invalid submit")
            .RunAsync();

        Assert.Equal("Enter your full name.", (await name.GetAccessibilityInfoAsync()).Description);
        Assert.Equal("Enter your email address.", (await email.GetAccessibilityInfoAsync()).Description);

        await name.FillAsync("Ana Lima");
        await component.Keyboard()
            .FocusOn(submit)
            .PressEnter()
            .ExpectState(name, "invalid", null)
            .ExpectFocused("textbox", "Email")
            .RunAsync();

        await email.FillAsync("ana@example.com");
        await component.Keyboard()
            .FocusOn(submit)
            .PressEnter()
            .ExpectLiveRegionUpdate("Account created for ana@example.com.")
            .ExpectState(email, "invalid", null)
            .Audit("submitted")
            .RunAsync();
    }
}
