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

    /// <summary>
    /// The same pages in the dark theme. The theme follows the operating system by default, so emulating a dark color
    /// scheme is enough; every color has a dark value that must meet the same contrast requirements.
    /// </summary>
    [Theory]
    [MemberData(nameof(ShowcasePages))]
    public async Task Showcase_pages_have_no_detectable_violations_in_the_dark_theme(string path)
    {
        await using var session = await app.OpenAsync(path, new BrowserNewContextOptions { ColorScheme = ColorScheme.Dark });
        Assert.Equal(DarkPageBackground, await session.Page.EvaluateAsync<string>("getComputedStyle(document.body).backgroundColor"));

        var audit = await session.Page.AuditAccessibilityAsync(ShowcaseAudit, new AuditLabel { Scenario = "page", State = "dark theme" });

        audit.ShouldHaveNoViolations();
    }

    /// <summary>The page background of the dark theme (--page-bg in app.css), to check that the theme is really applied.</summary>
    private const string DarkPageBackground = "rgb(15, 18, 24)";

    [Fact]
    public async Task Theme_switcher_works_with_the_keyboard_and_is_remembered()
    {
        await using var session = await app.OpenAsync("/");
        var page = session.Page;
        var themes = page.GetByRole(AriaRole.Group, new() { Name = "Theme" });
        var dark = themes.GetByRole(AriaRole.Radio, new() { Name = "Dark" });

        // One Tab stop on the selected option; arrow keys choose (Light, Dark, System).
        await KeyboardScenario.For(page)
            .FocusOn(themes.GetByRole(AriaRole.Radio, new() { Name = "System" }))
            .PressArrowLeft()
            .ExpectFocused("radio", "Dark")
            .ExpectState(dark, "checked", "true")
            .RunAsync();
        await page.Locator("html[data-theme=dark]").WaitForAsync();
        Assert.Equal(DarkPageBackground, await page.EvaluateAsync<string>("getComputedStyle(document.body).backgroundColor"));

        // The choice is stored in a cookie: the server renders the next page in the dark theme, with Dark selected.
        await page.ReloadAsync();
        await page.Locator("main[data-interactive=true]").WaitForAsync();
        Assert.Equal("dark", await page.Locator("html").GetAttributeAsync("data-theme"));
        await dark.ShouldHaveStateAsync("checked", "true");

        var audit = await page.AuditAccessibilityAsync(ShowcaseAudit, new AuditLabel { Scenario = "theme switcher", State = "dark chosen" });
        audit.ShouldHaveNoViolations(AccessibilityExpectation.Strict);
    }

    [Fact]
    public async Task Navigation_collapses_behind_a_menu_button_on_narrow_screens()
    {
        await using var session = await app.OpenAsync("/", new BrowserNewContextOptions { ViewportSize = new() { Width = 390, Height = 844 } });
        var page = session.Page;
        var menu = page.GetByRole(AriaRole.Button, new() { Name = "Menu" });
        var navigation = page.GetByRole(AriaRole.Navigation, new() { Name = "Showcase" });
        Assert.False(await navigation.IsVisibleAsync());

        await KeyboardScenario.For(page, new KeyboardScenarioOptions { Name = "narrow screen", AuditOptions = ShowcaseAudit })
            .FocusOn(menu)
            .PressEnter()
            .ExpectState(menu, "expanded", "true")
            .Audit("navigation open")
            .RunAsync();
        Assert.True(await navigation.IsVisibleAsync());

        // Choosing a page closes the navigation again, so the page is what the user sees. The rule catalog has the
        // widest table: at this width it must wrap instead of scrolling the page sideways.
        await navigation.GetByRole(AriaRole.Link, new() { Name = "Rule catalog" }).ClickAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "Rule catalog", Level = 1 }).WaitForAsync();
        await menu.ShouldHaveStateAsync("expanded", "false");
        Assert.False(await navigation.IsVisibleAsync());
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"), "The page scrolls sideways.");

        var audit = await page.AuditAccessibilityAsync(ShowcaseAudit, new AuditLabel { Scenario = "narrow screen", State = "loaded" });
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
        // The manual review page has a single control in its content: after the skip link, the masthead (brand, source
        // link, theme switcher) and the navigation, and before the links to the previous and next page of the tour.
        await using var session = await app.OpenAsync("/manual-review");

        var order = await session.Page.CaptureFocusOrderAsync();

        // WebKit skips links on Tab by default (a Safari setting), but not form controls.
        var webkit = app.Browser.Name == "webkit";
        string[] header = webkit
            ? ["radio System"]
            : ["link Skip to main content", "link Blazor Accessibility Toolkit", "link Source code on GitHub", "radio System"];
        string[] navigation = webkit ? [] : [.. NavMenu.Pages.Select(p => "link " + p.Text)];
        string[] tour = webkit ? [] : ["link Previous: CI gate and reports", "link Next: Rule catalog"];
        order.ShouldMatch([.. header, .. navigation, "checkbox Release policy: fail on outstanding manual checks (--fail-on-manual-review)", .. tour]).ShouldNotTrapFocus();
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
    public async Task All_live_tests_of_a_page_run_with_one_button()
    {
        // The home page has two bUnit examples: one catches a defect, one passes. "Run all" runs both, announces the
        // outcome in its status region, and lists each result with a link to its example.
        await using var session = await app.OpenAsync("/");
        var page = session.Page;

        await KeyboardScenario.For(page, new KeyboardScenarioOptions { ExpectTimeout = TimeSpan.FromSeconds(20) })
            .FocusOn(page.GetByRole(AriaRole.Button, new() { Name = "Run all 2 tests" }))
            .PressEnter()
            .ExpectLiveRegionUpdate("All 2 tests ended as expected: 1 defect caught, 1 accessible component passed.")
            .RunAsync();

        var results = page.GetByRole(AriaRole.List, new() { Name = "Results" });
        await results.GetByRole(AriaRole.Link, new() { Name = "Icon button without a name" }).WaitForAsync();
        await results.GetByRole(AriaRole.Link, new() { Name = "Icon button with a name" }).WaitForAsync();
        var audit = await page.AuditAccessibilityAsync(ShowcaseAudit, new AuditLabel { Scenario = "run all", State = "results shown" });
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
