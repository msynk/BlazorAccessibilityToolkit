namespace BlazorAccessibilityToolkit.Playwright.Tests;

/// <summary>APCA contrast, as information next to WCAG 2 contrast.</summary>
public sealed class ApcaContrastTests(BrowserFixture browser)
{
    private const string Page = """
        <style>body { background: #fff; font-family: sans-serif; } p, h1 { margin: 8px; padding: 4px; }</style>
        <main>
          <h1 id="large" style="font-size: 40px; font-weight: 400; color: #888">Large heading</h1>
          <p id="grey" style="font-size: 16px; color: #777">Grey body text that WCAG 2 also fails at 4.48 to 1.</p>
          <p id="black" style="font-size: 16px; color: #111">Black body text.</p>
          <p id="dark" style="font-size: 18px; color: #999; background: #222">Light text on a dark background that passes WCAG 2.</p>
          <p id="gradient" style="font-size: 16px; color: #fff; background: linear-gradient(#000, #444)">Text on a gradient.</p>
        </main>
        """;

    [Fact]
    public async Task Reports_text_below_the_apca_guidance_for_its_size()
    {
        await using var session = await browser.PageAsync(Page);

        var audit = await session.Page.AuditApcaContrastAsync();

        audit.ShouldHaveExecuted();
        var findings = audit.Findings.Where(f => f.RuleId == "blazor-apca-contrast").ToDictionary(f => f.Target.Selector!);
        Assert.Equal(["#dark", "#grey"], findings.Keys.Order(StringComparer.Ordinal));

        var grey = findings["#grey"];
        Assert.Equal(Apca.Contrast("#777777", "#ffffff").ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), grey.Data["lc"]);
        Assert.Equal("90", grey.Data["requiredLc"]);
        Assert.True(grey.IsBestPractice);
        Assert.Empty(grey.Wcag);
        Assert.Equal(FindingStatus.PotentialIssue, grey.Status);

        // APCA is stricter than WCAG 2 for light text on dark backgrounds.
        var dark = findings["#dark"];
        Assert.StartsWith("-", dark.Data["lc"], StringComparison.Ordinal);
        Assert.Equal("75", dark.Data["requiredLc"]);
        Assert.True(double.Parse(dark.Data["wcag2Ratio"], System.Globalization.CultureInfo.InvariantCulture) >= 4.5);
        Assert.Contains("light text on a dark background", dark.Remediation, StringComparison.Ordinal);

        Assert.Contains(audit.Engines, e => e.Name == "APCA" && e.Version == "0.0.98G-4g");
        Assert.Contains(audit.Warnings, w => w.Contains("not measured", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_single_minimum_can_replace_the_size_based_guidance()
    {
        await using var session = await browser.PageAsync(Page);

        var audit = await session.Page.AuditApcaContrastAsync(new ApcaContrastOptions { MinimumLc = 40 });

        Assert.DoesNotContain(audit.Findings, f => f.RuleId == "blazor-apca-contrast");
        Assert.Contains("blazor-apca-contrast", audit.PassedRules);
    }

    [Fact]
    public async Task Findings_do_not_fail_the_wcag_checks_of_the_quality_gate()
    {
        await using var session = await browser.PageAsync("<main><h1>Title</h1><p style=\"color:#595959\">Text at 7 to 1 that APCA wants darker at 16 px.</p></main>");

        var audit = await session.Page.AuditApcaContrastAsync();

        Assert.NotEmpty(audit.Findings);
        Assert.All(audit.Findings, f => Assert.Equal(FindingStatus.PotentialIssue, f.Status));
        audit.ShouldHaveNoViolations();
    }
}
