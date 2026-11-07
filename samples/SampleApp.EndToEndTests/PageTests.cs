using BlazorAccessibilityToolkit.Playwright;
using Microsoft.Playwright;

namespace SampleApp.EndToEndTests;

/// <summary>End-to-end accessibility tests of complete pages of the running application.</summary>
public sealed class PageTests(AppFixture app)
{
    [Theory]
    [InlineData("/")]
    [InlineData("/settings")]
    [InlineData("/signup")]
    public async Task Pages_have_no_detectable_violations(string path)
    {
        await using var session = await app.OpenAsync(path);

        var audit = await session.Page.AuditAccessibilityAsync(label: new AuditLabel { Scenario = "page", State = "loaded" });

        audit.ShouldHaveNoViolations();
    }

    [Fact]
    public async Task Skip_link_is_the_first_stop_and_moves_focus_to_main()
    {
        await using var session = await app.OpenAsync("/");
        var scenario = KeyboardScenario.For(session.Page);

        if (app.Browser.Name == "webkit")
        {
            // WebKit (like Safari's default settings) does not move focus to links with Tab.
            scenario.FocusOn(session.Page.GetByRole(AriaRole.Link, new() { Name = "Skip to main content" }));
        }
        else
        {
            scenario.StartFromDocument().PressTab();
        }

        await scenario
            .ExpectFocused("link", "Skip to main content")
            .PressEnter()
            .ExpectFocused("main")
            .RunAsync();
    }

    [Fact]
    public async Task Home_focus_order_follows_the_visual_order()
    {
        await using var session = await app.OpenAsync("/");

        var order = await session.Page.CaptureFocusOrderAsync();

        var navigation = app.Browser.Name == "webkit"
            ? Array.Empty<string>() // WebKit skips links on Tab by default (Safari setting).
            : ["link Skip to main content", "link Home", "link Settings", "link Sign up", "link Legacy"];
        order.ShouldMatch([.. navigation, "button What does the toolkit check?", "button Save changes"]).ShouldNotTrapFocus();
        Assert.DoesNotContain(order.Findings, f => f.RuleId == "blazor-focus-hidden-target");
    }

    [Fact]
    public async Task Settings_dialog_and_tabs_work_with_the_keyboard()
    {
        await using var session = await app.OpenAsync("/settings");
        var page = session.Page;
        var dialog = page.GetByRole(AriaRole.Dialog);

        await KeyboardScenario.For(page, new KeyboardScenarioOptions { Name = "settings" })
            .FocusOn(page.GetByRole(AriaRole.Tab, new() { Name = "Profile" }))
            .PressArrowRight()
            .ExpectFocused("tab", "Notifications")
            .FocusOn(page.GetByRole(AriaRole.Button, new() { Name = "Delete account" }))
            .RememberFocus("trigger")
            .PressEnter()
            .ExpectFocusWithin(dialog)
            .ExpectFocusContainedWithin(dialog)
            .Audit("delete dialog open")
            .PressTab()
            .ExpectFocused("button", "Delete")
            .PressEnter()
            .ExpectFocusRestored("trigger")
            .ExpectLiveRegionUpdate("Account deleted.")
            .RunAsync();
    }

    [Fact]
    public async Task Navigation_moves_focus_to_the_new_page_heading()
    {
        await using var session = await app.OpenAsync("/");

        await KeyboardScenario.For(session.Page)
            .FocusOn(session.Page.GetByRole(AriaRole.Link, new() { Name = "Sign up" }))
            .PressEnter()
            .ExpectFocused("heading", "Sign up")
            .RunAsync();
    }

    [Fact]
    public async Task Signup_errors_are_identified_and_described()
    {
        await using var session = await app.OpenAsync("/signup");
        var page = session.Page;

        await page.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync();
        var email = page.GetByLabel("Email");
        await email.ShouldHaveStateAsync("invalid", "true");
        Assert.Equal("Enter your email address.", (await email.GetAccessibilityInfoAsync()).Description);

        var audit = await page.AuditAccessibilityAsync(label: new AuditLabel { Scenario = "signup", State = "invalid submit" });
        audit.ShouldHaveNoViolations(AccessibilityExpectation.Strict);
    }

    [Fact]
    public async Task Legacy_page_known_issues_are_still_reported()
    {
        // The legacy page keeps two known defects. CI tracks them in the baseline (samples/a11y-baseline.json), so the
        // build does not fail on them, while any NEW finding still fails it. This test ensures they are still detected.
        await using var session = await app.OpenAsync("/legacy");

        var audit = await session.Page.AuditAccessibilityAsync(label: new AuditLabel { Scenario = "page", State = "loaded" });

        audit.ShouldHaveFinding("button-name", FindingStatus.Violation);
    }
}
