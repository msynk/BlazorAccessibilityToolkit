using BlazorAccessibilityToolkit.ComponentTesting;
using Bunit;
using SampleComponents;

namespace SampleApp.ComponentTests;

/// <summary>
/// Component-level tests on bUnit-rendered markup. These check structure, names, roles, ARIA and Blazor event handlers;
/// keyboard and focus behavior is covered by <see cref="InteractiveComponentTests"/>.
/// </summary>
public sealed class ComponentMarkupTests(TestEnvironment env)
{
    [Fact]
    public async Task Icon_button_has_an_accessible_name()
    {
        await using var fixture = await AccessibilityFixture.For<IconButton>()
            .WithParameter(p => p.Label, "Close")
            .WithSettings(env.BrowserMarkup)
            .BuildAsync();

        var audit = await fixture.AuditAsync();

        audit.ShouldHaveNoViolations();
    }

    [Fact]
    public async Task Browserless_audit_works_without_a_browser()
    {
        // No engine configured: Razor rules plus the browserless fallback rules only.
        await using var fixture = await AccessibilityFixture.For<IconButton>().WithParameter(p => p.Label, "Close").BuildAsync();

        (await fixture.AuditAsync()).ShouldHaveNoViolations(AccessibilityExpectation.Strict);
    }

    [Fact]
    public async Task Regression_unlabelled_icon_button_is_detected()
    {
        // Regression test for a known defect: if someone "fixes" the test instead of the component, this fails.
        await using var fixture = await AccessibilityFixture.For<UnlabelledIconButton>().WithSettings(env.BrowserMarkup).BuildAsync();

        var audit = await fixture.AuditAsync();

        var finding = audit.ShouldHaveFinding("button-name", FindingStatus.Violation);
        Assert.Equal(typeof(UnlabelledIconButton).FullName, finding.Component!.ComponentType);
        Assert.Contains(finding.Wcag, w => w.Id == "4.1.2");
    }

    [Fact]
    public async Task Regression_clickable_div_is_detected_and_the_disclosure_fix_passes()
    {
        await using var broken = await AccessibilityFixture.For<ClickableCard>().WithSettings(env.BrowserMarkup).BuildAsync();
        (await broken.AuditAsync()).ShouldHaveFinding("blazor-click-handler-semantics");

        await using var fixedCard = await AccessibilityFixture.For<DisclosureCard>()
            .WithParameter(p => p.Title, "Shipping")
            .WithParameter(p => p.Details, "Ships in 2 days.")
            .WithSettings(env.BrowserMarkup)
            .BuildAsync();
        (await fixedCard.AuditAsync("collapsed")).ShouldHaveNoViolations(AccessibilityExpectation.Strict);

        fixedCard.Component.Find("button").Click();
        (await fixedCard.AuditAsync("expanded")).ShouldHaveNoViolations(AccessibilityExpectation.Strict);
    }

    [Fact]
    public async Task Signup_form_errors_are_associated_with_their_fields()
    {
        await using var fixture = await AccessibilityFixture.For<SignupForm>().WithSettings(env.BrowserMarkup).BuildAsync();
        (await fixture.AuditAsync("empty")).ShouldHaveNoViolations(AccessibilityExpectation.Strict);

        fixture.Component.Find("form").Submit();

        var audit = await fixture.AuditAsync("after invalid submit");
        audit.ShouldHaveNoViolations(AccessibilityExpectation.Strict);
        var email = fixture.Component.Find("input[type=email]");
        Assert.Equal("true", email.GetAttribute("aria-invalid"));
        Assert.NotNull(fixture.Component.Find("#" + email.GetAttribute("aria-describedby")));
    }

    [Fact]
    public async Task Tabs_markup_exposes_roles_and_states()
    {
        await using var fixture = await AccessibilityFixture.For<Tabs>()
            .WithParameter(p => p.Label, "Sections")
            .WithParameter(p => p.Items, SampleData.Tabs)
            .WithSettings(env.BrowserMarkup)
            .BuildAsync();

        (await fixture.AuditAsync()).ShouldHaveNoViolations(AccessibilityExpectation.Strict);
    }
}

internal static class SampleData
{
    public static IReadOnlyList<Tabs.TabItem> Tabs { get; } =
    [
        new("Profile", b => b.AddMarkupContent(0, "<p>Name and photo.</p>")),
        new("Notifications", b => b.AddMarkupContent(0, "<p>Email preferences.</p>")),
        new("Privacy", b => b.AddMarkupContent(0, "<p>Visibility.</p>")),
    ];
}
