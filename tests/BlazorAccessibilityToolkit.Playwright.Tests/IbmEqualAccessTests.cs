namespace BlazorAccessibilityToolkit.Playwright.Tests;

/// <summary>The IBM Equal Access engine as a second rule engine next to axe-core.</summary>
public sealed class IbmEqualAccessTests(BrowserFixture browser)
{
    private const string Page = """
        <main>
          <h1>Orders</h1>
          <img id="logo" src="logo.png">
          <input id="search" type="text">
          <nav id="first"><a href="/a" style="display:inline-block;padding:12px">A</a></nav>
          <nav id="second"><a href="/b" style="display:inline-block;padding:12px">B</a></nav>
        </main>
        """;

    private static readonly AccessibilityAuditOptions Options = new() { IbmEqualAccess = true };

    [Fact]
    public async Task Adds_findings_axe_core_does_not_make_and_confirms_the_ones_it_does()
    {
        await using var session = await browser.PageAsync(Page);

        var audit = await session.Page.AuditAccessibilityAsync(Options);

        audit.ShouldHaveExecuted();
        Assert.Contains(audit.Engines, e => e.Name == "ibm-equal-access" && e.Version == "4.0.34");

        // Two navigation landmarks without labels that tell them apart: axe-core reports this only as a best practice.
        var navigation = audit.Findings.Where(f => f.RuleId == "aria_navigation_label_unique").ToArray();
        Assert.Equal(2, navigation.Length);
        Assert.All(navigation, f =>
        {
            Assert.Equal("ibm-equal-access", f.Engine);
            Assert.Equal(FindingStatus.Violation, f.Status);
            Assert.Contains(f.Wcag, w => w.Id == "1.3.1");
            Assert.StartsWith("https://able.ibm.com/rules/", f.HelpUrl, StringComparison.Ordinal);
            Assert.Equal("WCAG_2_2", f.Data["ruleset"]);
        });
        Assert.Equal(["#first", "#second"], navigation.Select(f => f.Target.Selector).Order(StringComparer.Ordinal));

        // The missing alt text and label are reported once, by axe-core, and confirmed by the second engine.
        Assert.Equal("ibm-equal-access:img_alt_valid", Assert.Single(audit.Findings, f => f.Target.Selector == "#logo").Data["confirmedBy"]);
        Assert.Equal("ibm-equal-access:input_label_exists", Assert.Single(audit.Findings, f => f.Target.Selector == "#search").Data["confirmedBy"]);
        Assert.Contains(audit.Warnings, w => w.Contains("confirmedBy", StringComparison.Ordinal));
        Assert.DoesNotContain("img_alt_valid", audit.PassedRules);
        Assert.Contains("html_lang_exists", audit.PassedRules);
    }

    [Fact]
    public async Task Is_off_unless_enabled_and_follows_rule_and_selector_settings()
    {
        await using var session = await browser.PageAsync(Page);

        var axeOnly = await session.Page.AuditAccessibilityAsync();
        Assert.DoesNotContain(axeOnly.Engines, e => e.Name == "ibm-equal-access");

        var disabled = await session.Page.AuditAccessibilityAsync(Options with { DisabledRules = ["aria_navigation_label_unique"] });
        Assert.DoesNotContain(disabled.Findings, f => f.RuleId == "aria_navigation_label_unique");

        var excluded = await session.Page.AuditAccessibilityAsync(Options with { ExcludeSelectors = ["#second"] });
        Assert.DoesNotContain(excluded.Findings, f => f.Engine == "ibm-equal-access" && f.Target.Selector == "#second");
    }

    [Fact]
    public async Task Audits_a_region_and_leaves_the_page_s_own_ace_variable_alone()
    {
        await using var session = await browser.PageAsync(Page);

        // Pages with the Ace code editor own window.ace.
        await session.Page.EvaluateAsync("() => { window.ace = { edit() { return 'editor'; } }; }");

        var region = await session.Page.Locator("#second").AuditAccessibilityAsync(Options);

        region.ShouldHaveExecuted();
        Assert.Contains(region.Engines, e => e.Name == "ibm-equal-access");
        Assert.DoesNotContain(region.Findings, f => f.Target.Selector == "#logo");
        Assert.Equal("editor", await session.Page.EvaluateAsync<string>("() => window.ace.edit()"));
    }

    [Fact]
    public void Embedded_engine_is_the_pinned_file()
    {
        using var stream = typeof(PlaywrightAccessibility).Assembly.GetManifestResourceStream("BlazorAccessibilityToolkit.ace.js")!;
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stream));
        var readme = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "BlazorAccessibilityToolkit.Playwright", "Scripts", "EqualAccess", "README.md"));
        Assert.Contains(hash, readme, StringComparison.Ordinal);
        Assert.Contains("version 4.0.34", readme, StringComparison.Ordinal);
    }

    private static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", ".."));

    [Fact]
    public async Task Best_practices_switch_to_the_ibm_accessibility_rule_set()
    {
        await using var session = await browser.PageAsync(Page);

        var audit = await session.Page.AuditAccessibilityAsync(Options with { IncludeBestPractices = true });

        Assert.All(audit.Findings.Where(f => f.Engine == "ibm-equal-access"), f => Assert.Equal("IBM_Accessibility", f.Data["ruleset"]));
        Assert.Contains(audit.Findings, f => f.RuleId == "aria_navigation_label_unique");
    }
}
