using BlazorAccessibilityToolkit.ComponentTesting.Tests.Components;
using BlazorAccessibilityToolkit.Engines;
using BlazorAccessibilityToolkit.Playwright;

[assembly: AssemblyFixture(typeof(BlazorAccessibilityToolkit.ComponentTesting.Tests.BrowserEngineFixture))]

namespace BlazorAccessibilityToolkit.ComponentTesting.Tests;

public sealed class BrowserEngineFixture : IAsyncLifetime
{
    public AccessibilityBrowser Browser { get; private set; } = null!;

    public ComponentAuditSettings Settings { get; private set; } = null!;

    private PlaywrightMarkupAuditEngine engine = null!;

    public async ValueTask InitializeAsync()
    {
        Browser = await AccessibilityBrowser.LaunchAsync();
        engine = new PlaywrightMarkupAuditEngine(Browser);
        Settings = ComponentAuditSettings.Default.WithEngine(engine);
    }

    public async ValueTask DisposeAsync()
    {
        await engine.DisposeAsync();
        await Browser.DisposeAsync();
    }
}

public sealed class BrowserEngineTests(BrowserEngineFixture browser)
{
    [Fact]
    public async Task Axe_findings_on_bunit_markup_are_attributed_to_components()
    {
        await using var fixture = await AccessibilityFixture.For<Toolbar>().WithSettings(browser.Settings).BuildAsync();
        var audit = await fixture.AuditAsync();

        Assert.Equal(AuditStatus.Completed, audit.Status);
        var finding = audit.ShouldHaveFinding("button-name", FindingStatus.Violation);
        Assert.Equal("axe-core", finding.Engine);
        Assert.Equal(typeof(IconButton).FullName, finding.Component!.ComponentType);
        Assert.Equal("render-tree", finding.Component.Method);
        Assert.DoesNotContain("data-a11y-ord", finding.Target.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("blazor:", finding.Target.Html, StringComparison.Ordinal);
        Assert.Equal(browser.Browser.Name, audit.Context.Browser);
        Assert.Contains(audit.Engines, e => e.Name == "axe-core");

        // With a browser engine the browserless fallback rules do not run (axe-core covers them).
        audit.ShouldNotHaveFinding("blazor-markup-control-name");
    }

    [Fact]
    public async Task Razor_specific_rules_still_run_with_a_browser_engine()
    {
        await using var fixture = await AccessibilityFixture.For<ClickTargets>().WithSettings(browser.Settings).BuildAsync();
        var audit = await fixture.AuditAsync();
        Assert.Equal(2, audit.Findings.Count(f => f.RuleId == "blazor-click-handler-semantics"));
    }

    [Fact]
    public async Task Browser_diagnostics_run_on_bunit_markup()
    {
        await using var fixture = await AccessibilityFixture.For<SignupForm>().WithSettings(browser.Settings).BuildAsync();
        fixture.Component.Find("form").Submit();
        var audit = await fixture.AuditAsync("after invalid submit");
        var finding = audit.ShouldHaveFinding("blazor-validation-message-association");
        Assert.Equal("render-tree", finding.Component!.Method);
        Assert.Contains("ValidationMessage", finding.Component.ComponentType, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Page_level_rules_are_disabled_for_components()
    {
        await using var fixture = await AccessibilityFixture.For<IconButton>()
            .WithParameter(p => p.Label, "Close")
            .WithSettings(browser.Settings with { Options = new AccessibilityAuditOptions { IncludeBestPractices = true } })
            .BuildAsync();
        var audit = await fixture.AuditAsync();
        audit.ShouldHaveNoViolations();
        Assert.Contains("region", audit.Configuration.DisabledRules);
    }

    [Fact]
    public async Task Supplied_stylesheets_enable_contrast_checks()
    {
        var css = Path.Combine(Path.GetTempPath(), $"a11y-{Guid.NewGuid():N}.css");
        await File.WriteAllTextAsync(css, ".hint { color: #ccc; background: #fff; }");
        try
        {
            var settings = browser.Settings with { Document = new MarkupDocumentOptions { StylesheetFiles = [css] } };
            await using var fixture = await AccessibilityFixture.For<Toolbar>().WithSettings(settings).BuildAsync();
            var audit = await fixture.AuditAsync();
            var contrast = audit.ShouldHaveFinding("color-contrast", FindingStatus.Violation);
            Assert.Equal(typeof(Toolbar).FullName, contrast.Component!.ComponentType);
        }
        finally
        {
            File.Delete(css);
        }
    }
}
