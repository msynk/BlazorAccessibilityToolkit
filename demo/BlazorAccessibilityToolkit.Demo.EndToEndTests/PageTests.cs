using BlazorAccessibilityToolkit.Demo.WebApp.Components.Layout;
using BlazorAccessibilityToolkit.Playwright;
using Microsoft.Playwright;

namespace BlazorAccessibilityToolkit.Demo.EndToEndTests;

/// <summary>End-to-end accessibility tests of complete pages of the running showcase application.</summary>
public sealed class PageTests(AppFixture app)
{
    /// <summary>
    /// The showcase pages contain intentionally broken components, inside elements marked <c>data-known-defect</c>.
    /// Page audits exclude them: everything else (layout, navigation, explanations, the accessible components and the
    /// live test results) must be clean. The defects themselves are covered by the component tests.
    /// </summary>
    private static readonly AccessibilityAuditOptions ShowcaseAudit = new() { ExcludeSelectors = ["[data-known-defect]"] };

    /// <summary>Every page in the navigation except the legacy page, whose known issues are tested separately.</summary>
    public static TheoryData<string> ShowcasePages => [.. NavMenu.Pages.Select(p => "/" + p.Href).Where(p => p != "/legacy")];

    [Theory]
    [MemberData(nameof(ShowcasePages))]
    public async Task Showcase_pages_have_no_detectable_violations(string path)
    {
        await using var session = await app.OpenAsync(path);

        var audit = await session.Page.AuditAccessibilityAsync(ShowcaseAudit, new AuditLabel { Scenario = "page", State = "loaded" });

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
    public async Task Layout_focus_order_follows_the_visual_order()
    {
        // The manual review page has a single control in its content, after the skip link, the brand and the navigation.
        await using var session = await app.OpenAsync("/manual-review");

        var order = await session.Page.CaptureFocusOrderAsync();

        var links = app.Browser.Name == "webkit"
            ? Array.Empty<string>() // WebKit skips links on Tab by default (Safari setting).
            : ["link Skip to main content", "link Blazor Accessibility Toolkit", .. NavMenu.Pages.Select(p => "link " + p.Text)];
        order.ShouldMatch([.. links, "checkbox Release policy: fail on outstanding manual checks (--fail-on-manual-review)"]).ShouldNotTrapFocus();
        Assert.DoesNotContain(order.Findings, f => f.RuleId == "blazor-focus-hidden-target");
    }

    [Fact]
    public async Task Navigation_moves_focus_to_the_new_page_heading()
    {
        await using var session = await app.OpenAsync("/");

        await KeyboardScenario.For(session.Page)
            .FocusOn(session.Page.GetByRole(AriaRole.Navigation, new() { Name = "Showcase" }).GetByRole(AriaRole.Link, new() { Name = "Component audits" }))
            .PressEnter()
            .ExpectFocused("heading", "Component audits")
            .RunAsync();
    }

    [Fact]
    public async Task Showcase_dialog_and_tabs_work_with_the_keyboard()
    {
        await using var session = await app.OpenAsync("/keyboard");
        var page = session.Page;
        var example = page.GetByRole(AriaRole.Article, new() { Name = "A dialog that manages focus" });
        var dialog = page.GetByRole(AriaRole.Dialog);

        await KeyboardScenario.For(page, new KeyboardScenarioOptions { Name = "keyboard page", AuditOptions = ShowcaseAudit })
            .FocusOn(example.GetByRole(AriaRole.Button, new() { Name = "Delete account" }))
            .RememberFocus("trigger")
            .PressEnter()
            .ExpectFocusWithin(dialog)
            .ExpectFocusContainedWithin(dialog)
            .Audit("delete dialog open")
            .PressTab()
            .ExpectFocused("button", "Delete")
            .PressEnter()
            .ExpectFocusRestored("trigger")
            .RunAsync();

        await page.GotoAsync(app.Url("/aria-patterns"));
        await page.Locator("main[data-interactive=true]").WaitForAsync();
        var tabs = page.GetByRole(AriaRole.Article, new() { Name = "Tabs with roving tabindex and arrow keys" });
        await KeyboardScenario.For(page)
            .FocusOn(tabs.GetByRole(AriaRole.Tab, new() { Name = "Profile" }))
            .PressArrowRight()
            .ExpectFocused("tab", "Notifications")
            .PressEnd()
            .ExpectFocused("tab", "Privacy")
            .RunAsync();
    }

    [Fact]
    public async Task Signup_errors_are_identified_and_described()
    {
        await using var session = await app.OpenAsync("/blazor-rules");
        var form = session.Page.GetByRole(AriaRole.Article, new() { Name = "Errors referenced by aria-describedby" });

        await form.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync();
        var email = form.GetByLabel("Email");
        await email.ShouldHaveStateAsync("invalid", "true");
        Assert.Equal("Enter your email address.", (await email.GetAccessibilityInfoAsync()).Description);

        var audit = await session.Page.AuditAccessibilityAsync(ShowcaseAudit, new AuditLabel { Scenario = "signup", State = "invalid submit" });
        audit.ShouldHaveNoViolations(AccessibilityExpectation.Strict);
    }

    [Fact]
    public async Task Live_test_runs_in_the_showcase_and_its_results_are_accessible()
    {
        // Runs a live example the way a visitor does: the button starts the bUnit test on the server, the outcome is
        // announced in the panel's status region, and the results, findings included, render accessibly.
        await using var session = await app.OpenAsync("/component-audits");
        var page = session.Page;
        var example = page.GetByRole(AriaRole.Article, new() { Name = "A regression test for a known defect" });

        await KeyboardScenario.For(page, new KeyboardScenarioOptions { ExpectTimeout = TimeSpan.FromSeconds(15) })
            .FocusOn(example.GetByRole(AriaRole.Button, new() { Name = "Run this test" }))
            .PressEnter()
            .ExpectLiveRegionUpdate("Test passed")
            .ExpectFocused("button", "Run it again")
            .RunAsync();

        await example.GetByText("blazor-markup-control-name").First.WaitForAsync();
        var audit = await page.AuditAccessibilityAsync(ShowcaseAudit, new AuditLabel { Scenario = "live test", State = "results shown" });
        audit.ShouldHaveNoViolations(AccessibilityExpectation.Strict);
    }

    [Fact]
    public async Task Legacy_page_known_issues_are_still_reported()
    {
        // The legacy page keeps two known defects and does not mark them. CI tracks them in the baseline
        // (demo/a11y-baseline.json), so the build does not fail on them, while any NEW finding still fails it. This test
        // ensures they are still detected. The page audit finds the unlabelled button. The clickable card looks like
        // static text in the rendered DOM: the @onclick handler is only visible in Blazor's render tree, so the bUnit
        // test of ClickableCard detects that one.
        await using var session = await app.OpenAsync("/legacy");

        var audit = await session.Page.AuditAccessibilityAsync(label: new AuditLabel { Scenario = "page", State = "loaded" });

        var finding = audit.ShouldHaveFinding("button-name", FindingStatus.Violation);
        Assert.Contains(finding.Wcag, w => w.Id == "4.1.2");
    }

    [Fact]
    public async Task Unknown_urls_show_an_accessible_not_found_page()
    {
        await using var session = await app.Browser.NewSessionAsync();

        var response = await session.Page.GotoAsync(app.Url("/no-such-page"));

        Assert.Equal(404, response!.Status);
        await session.Page.Locator("main[data-interactive=true]").WaitForAsync();
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Page not found", Level = 1 }).WaitForAsync();
        Assert.Equal("Page not found · Blazor Accessibility Toolkit", await session.Page.TitleAsync());
        var audit = await session.Page.AuditAccessibilityAsync(label: new AuditLabel { Scenario = "page", State = "not found" });
        audit.ShouldHaveNoViolations(AccessibilityExpectation.Strict);
    }

    [Fact]
    public async Task Error_page_is_rendered_statically_and_is_accessible()
    {
        // The exception handler re-executes /error outside Development. It must work without a circuit, so it is
        // excluded from interactive routing.
        await using var session = await app.Browser.NewSessionAsync();

        await session.Page.GotoAsync(app.Url("/error"));

        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Something went wrong", Level = 1 }).WaitForAsync();
        Assert.Equal("false", await session.Page.Locator("main").GetAttributeAsync("data-interactive"));
        var audit = await session.Page.AuditAccessibilityAsync(label: new AuditLabel { Scenario = "page", State = "error" });
        audit.ShouldHaveNoViolations(AccessibilityExpectation.Strict);
    }
}
