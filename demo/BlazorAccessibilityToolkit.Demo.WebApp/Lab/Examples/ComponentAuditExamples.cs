using BlazorAccessibilityToolkit.ComponentTesting;
using BlazorAccessibilityToolkit.Demo.Components.Accessible;
using BlazorAccessibilityToolkit.Demo.Components.Defects;
using Bunit;

namespace BlazorAccessibilityToolkit.Demo.WebApp.Lab.Examples;

/// <summary>bUnit markup audits: browserless, with axe-core, component attribution, states and regression tests.</summary>
public static class ComponentAuditExamples
{
    public static readonly LabExample UnlabelledButtonAudit = new()
    {
        Id = "unlabelled-button-audit",
        ExpectFailure = true,
        Run = async env =>
        {
            #region snippet:unlabelled-button-audit
            await using var fixture = await AccessibilityFixture.For<UnlabelledIconButton>().BuildAsync();

            var audit = await fixture.AuditAsync();
            env.Show(audit);

            audit.ShouldHaveNoViolations();
            #endregion
        },
    };

    public static readonly LabExample LabelledButtonAudit = new()
    {
        Id = "labelled-button-audit",
        Run = async env =>
        {
            #region snippet:labelled-button-audit
            await using var fixture = await AccessibilityFixture.For<IconButton>()
                .WithParameter(p => p.Label, "Close")
                .BuildAsync();

            var audit = await fixture.AuditAsync();
            env.Show(audit);

            audit.ShouldHaveNoViolations();
            #endregion
        },
    };

    public static readonly LabExample LegacyToolbarAudit = new()
    {
        Id = "legacy-toolbar-audit",
        ExpectFailure = true,
        Run = async env =>
        {
            #region snippet:legacy-toolbar-audit
            await using var fixture = await AccessibilityFixture.For<LegacyFormattingToolbar>().BuildAsync();

            var audit = await fixture.AuditAsync();
            env.Show(audit);

            audit.ShouldHaveNoViolations();
            #endregion
        },
    };

    public static readonly LabExample ToolbarAudit = new()
    {
        Id = "toolbar-audit",
        Run = async env =>
        {
            #region snippet:toolbar-audit
            await using var fixture = await AccessibilityFixture.For<FormattingToolbar>().BuildAsync();

            var audit = await fixture.AuditAsync();
            env.Show(audit);

            audit.ShouldHaveNoViolations();
            #endregion
        },
    };

    public static readonly LabExample LegacyToolbarAxeAudit = new()
    {
        Id = "legacy-toolbar-axe-audit",
        ExpectFailure = true,
        NeedsBrowser = true,
        Run = async env =>
        {
            #region snippet:legacy-toolbar-axe-audit
            // env.BrowserMarkup = ComponentAuditSettings.Default.WithEngine(new PlaywrightMarkupAuditEngine(browser)),
            // plus the component stylesheet so that color contrast is measured with the real colors.
            await using var fixture = await AccessibilityFixture.For<LegacyFormattingToolbar>()
                .WithSettings(env.BrowserMarkup)
                .BuildAsync();

            var audit = await fixture.AuditAsync();
            env.Show(audit);

            audit.ShouldHaveNoViolations();
            #endregion
        },
    };

    public static readonly LabExample DisclosureStatesAudit = new()
    {
        Id = "disclosure-states-audit",
        Run = async env =>
        {
            #region snippet:disclosure-states-audit
            await using var fixture = await AccessibilityFixture.For<DisclosureCard>()
                .WithParameter(p => p.Title, "Shipping")
                .WithParameter(p => p.Details, "Ships in 2 days.")
                .BuildAsync();

            var collapsed = await fixture.AuditAsync("collapsed");
            fixture.Component.Find("button").Click();          // change the state with bUnit, then audit again
            var expanded = await fixture.AuditAsync("expanded");
            env.Show(collapsed);
            env.Show(expanded);

            collapsed.ShouldHaveNoViolations(AccessibilityExpectation.Strict);
            expanded.ShouldHaveNoViolations(AccessibilityExpectation.Strict);
            #endregion
        },
    };

    public static readonly LabExample KnownDefectRegression = new()
    {
        Id = "known-defect-regression",
        Run = async env =>
        {
            #region snippet:known-defect-regression
            // A known defect that is not fixed yet. Assert that it is still detected: if someone "fixes" the test setup
            // instead of the component, or a rule stops seeing the problem, this test fails.
            await using var fixture = await AccessibilityFixture.For<UnlabelledIconButton>().BuildAsync();

            var audit = await fixture.AuditAsync();
            env.Show(audit);

            var finding = audit.ShouldHaveFinding("blazor-markup-control-name", FindingStatus.Violation);
            Assert.Contains("4.1.2", finding.Wcag.Select(w => w.Id));
            #endregion
        },
    };
}
