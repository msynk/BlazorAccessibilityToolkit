using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace BlazorAccessibilityToolkit.Demo.EndToEndTests;

/// <summary>
/// The development overlay that App.razor renders in the Development environment. It stays off under browser
/// automation, so the other tests never see it; these tests turn it on as a developer would, with localStorage.
/// </summary>
public sealed partial class DevOverlayTests(AppFixture app)
{
    [Fact]
    public async Task Overlay_reports_the_legacy_page_issues_and_checks_again_after_navigation()
    {
        await using var session = await app.Browser.NewSessionAsync();
        var page = session.Page;
        await session.Context.AddInitScriptAsync("localStorage.setItem('blazor-a11y-devtools', 'on')");
        await page.GotoAsync(app.Url("/legacy"));
        await page.Locator("main[data-interactive=true]").WaitForAsync();

        // The unlabelled button of the legacy page (see PageTests.Legacy_page_known_issues_are_still_reported).
        var badge = page.GetByRole(AriaRole.Button, new() { NameRegex = IssueCount() });
        await badge.ClickAsync();
        var panel = page.GetByRole(AriaRole.Region, new() { Name = "Accessibility issues" });
        Assert.Contains("button-name", await panel.InnerTextAsync(), StringComparison.Ordinal);

        // Interactive routing replaces the page content; the overlay checks the new page.
        await page.GetByRole(AriaRole.Link, new() { Name = "Rule catalog", Exact = true }).First.ClickAsync();
        await page.WaitForFunctionAsync("() => window.blazorA11yDevTools.results?.url.endsWith('/rules')");
    }

    [GeneratedRegex(@"^\d+ a11y issues?$")]
    private static partial Regex IssueCount();
}
