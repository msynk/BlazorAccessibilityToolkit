using BlazorAccessibilityToolkit.ComponentTesting;
using BlazorAccessibilityToolkit.Demo.Components.Accessible;
using BlazorAccessibilityToolkit.Demo.Components.Defects;
using Bunit;

namespace BlazorAccessibilityToolkit.Demo.WebApp.Lab.Examples;

/// <summary>
/// The Razor-specific rules. These audits run without a browser. They use AccessibilityExpectation.Strict because most
/// of these rules report potential issues, which the default expectation only lists.
/// </summary>
public static class BlazorRuleExamples
{
    public static readonly LabExample ClickableCardAudit = new()
    {
        Id = "clickable-card-audit",
        ExpectFailure = true,
        Run = async env =>
        {
            #region snippet:clickable-card-audit
            await using var fixture = await AccessibilityFixture.For<ClickableCard>()
                .WithParameter(p => p.Title, "Shipping")
                .WithParameter(p => p.Details, "Ships in 2 days.")
                .BuildAsync();

            var audit = await fixture.AuditAsync();
            env.Show(audit);

            audit.ShouldHaveNoViolations(AccessibilityExpectation.Strict);
            #endregion
        },
    };

    public static readonly LabExample DisclosureCardAudit = new()
    {
        Id = "disclosure-card-audit",
        Run = async env =>
        {
            #region snippet:disclosure-card-audit
            await using var fixture = await AccessibilityFixture.For<DisclosureCard>()
                .WithParameter(p => p.Title, "Shipping")
                .WithParameter(p => p.Details, "Ships in 2 days.")
                .BuildAsync();

            var audit = await fixture.AuditAsync();
            env.Show(audit);

            audit.ShouldHaveNoViolations(AccessibilityExpectation.Strict);
            #endregion
        },
    };

    public static readonly LabExample LegacySignupAudit = new()
    {
        Id = "legacy-signup-audit",
        ExpectFailure = true,
        Run = async env =>
        {
            #region snippet:legacy-signup-audit
            await using var fixture = await AccessibilityFixture.For<LegacySignupForm>().BuildAsync();

            fixture.Component.Find("form").Submit();           // submit the empty form: validation fails
            var audit = await fixture.AuditAsync("after invalid submit");
            env.Show(audit);

            audit.ShouldHaveNoViolations(AccessibilityExpectation.Strict);
            #endregion
        },
    };

    public static readonly LabExample SignupAudit = new()
    {
        Id = "signup-audit",
        Run = async env =>
        {
            #region snippet:signup-audit
            await using var fixture = await AccessibilityFixture.For<SignupForm>().BuildAsync();

            fixture.Component.Find("form").Submit();           // submit the empty form: validation fails
            var audit = await fixture.AuditAsync("after invalid submit");
            env.Show(audit);

            audit.ShouldHaveNoViolations(AccessibilityExpectation.Strict);
            #endregion
        },
    };

    public static readonly LabExample LegacyCartAudit = new()
    {
        Id = "legacy-cart-audit",
        ExpectFailure = true,
        Run = async env =>
        {
            #region snippet:legacy-cart-audit
            await using var fixture = await AccessibilityFixture.For<LegacyCartLines>().BuildAsync();

            var audit = await fixture.AuditAsync();
            env.Show(audit);

            audit.ShouldHaveNoViolations(AccessibilityExpectation.Strict);
            #endregion
        },
    };

    public static readonly LabExample CartAudit = new()
    {
        Id = "cart-audit",
        Run = async env =>
        {
            #region snippet:cart-audit
            await using var fixture = await AccessibilityFixture.For<CartLines>().BuildAsync();

            var audit = await fixture.AuditAsync();
            env.Show(audit);

            audit.ShouldHaveNoViolations(AccessibilityExpectation.Strict);
            #endregion
        },
    };

    public static readonly LabExample UngroupedRadiosAudit = new()
    {
        Id = "ungrouped-radios-audit",
        ExpectFailure = true,
        Run = async env =>
        {
            #region snippet:ungrouped-radios-audit
            await using var fixture = await AccessibilityFixture.For<UngroupedShippingOptions>().BuildAsync();

            var audit = await fixture.AuditAsync();
            env.Show(audit);

            audit.ShouldHaveNoViolations(AccessibilityExpectation.Strict);
            #endregion
        },
    };

    public static readonly LabExample GroupedRadiosAudit = new()
    {
        Id = "grouped-radios-audit",
        Run = async env =>
        {
            #region snippet:grouped-radios-audit
            await using var fixture = await AccessibilityFixture.For<ShippingOptions>().BuildAsync();

            var audit = await fixture.AuditAsync();
            env.Show(audit);

            audit.ShouldHaveNoViolations(AccessibilityExpectation.Strict);
            #endregion
        },
    };
}
