using BlazorAccessibilityToolkit.ComponentTesting;
using Bunit;
using BlazorAccessibilityToolkit.Demo.Components;

namespace BlazorAccessibilityToolkit.Demo.ComponentTests;

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

        var button = fixedCard.Component.Find("button");
        Assert.Equal("false", button.GetAttribute("aria-expanded"));
        Assert.True(fixedCard.Component.Find("#" + button.GetAttribute("aria-controls")).HasAttribute("hidden"));

        button.Click();

        (await fixedCard.AuditAsync("expanded")).ShouldHaveNoViolations(AccessibilityExpectation.Strict);
        button = fixedCard.Component.Find("button");
        Assert.Equal("true", button.GetAttribute("aria-expanded"));
        Assert.False(fixedCard.Component.Find("#" + button.GetAttribute("aria-controls")).HasAttribute("hidden"));
    }

    [Fact]
    public async Task Signup_form_errors_are_associated_with_their_fields()
    {
        await using var fixture = await AccessibilityFixture.For<SignupForm>().WithSettings(env.BrowserMarkup).BuildAsync();
        (await fixture.AuditAsync("empty")).ShouldHaveNoViolations(AccessibilityExpectation.Strict);
        Assert.All(fixture.Component.FindAll("input"), input => Assert.Equal("true", input.GetAttribute("aria-required")));

        fixture.Component.Find("form").Submit();

        var audit = await fixture.AuditAsync("after invalid submit");
        audit.ShouldHaveNoViolations(AccessibilityExpectation.Strict);
        foreach (var (selector, message) in new[] { ("input[autocomplete=name]", "Enter your full name."), ("input[type=email]", "Enter your email address.") })
        {
            var input = fixture.Component.Find(selector);
            Assert.Equal("true", input.GetAttribute("aria-invalid"));
            Assert.Equal(message, fixture.Component.Find("#" + input.GetAttribute("aria-describedby")).TextContent);
        }
    }

    [Fact]
    public async Task Tabs_markup_exposes_roles_and_states()
    {
        await using var fixture = await AccessibilityFixture.For<Tabs>()
            .WithParameter(p => p.Label, "Sections")
            .WithParameter(p => p.Items, DemoData.Tabs)
            .WithSettings(env.BrowserMarkup)
            .BuildAsync();

        (await fixture.AuditAsync()).ShouldHaveNoViolations(AccessibilityExpectation.Strict);

        Assert.Equal("Sections", fixture.Component.Find("[role=tablist]").GetAttribute("aria-label"));
        var tabs = fixture.Component.FindAll("[role=tab]");
        var panels = fixture.Component.FindAll("[role=tabpanel]");
        Assert.Equal(3, tabs.Count);
        Assert.Equal(3, panels.Count);
        for (var i = 0; i < tabs.Count; i++)
        {
            var selected = i == 0;
            Assert.Equal(selected ? "true" : "false", tabs[i].GetAttribute("aria-selected"));
            Assert.Equal(selected ? "0" : "-1", tabs[i].GetAttribute("tabindex"));    // roving tabindex
            Assert.Equal(panels[i].Id, tabs[i].GetAttribute("aria-controls"));
            Assert.Equal(tabs[i].Id, panels[i].GetAttribute("aria-labelledby"));
            Assert.Equal(!selected, panels[i].HasAttribute("hidden"));
        }
    }

    [Fact]
    public async Task Modal_dialog_is_named_described_and_modal_when_open()
    {
        await using var fixture = await AccessibilityFixture.For<ModalDialog>()
            .WithParameter(p => p.TriggerText, "Delete account")
            .WithParameter(p => p.Title, "Delete account?")
            .WithChildContent("<p>This cannot be undone.</p>")
            .WithSettings(env.BrowserMarkup)
            .BuildAsync();
        (await fixture.AuditAsync("closed")).ShouldHaveNoViolations(AccessibilityExpectation.Strict);
        Assert.Empty(fixture.Component.FindAll("[role=dialog]"));

        fixture.Component.Find("button").Click();

        (await fixture.AuditAsync("open")).ShouldHaveNoViolations(AccessibilityExpectation.Strict);
        var dialog = fixture.Component.Find("[role=dialog]");
        Assert.Equal("true", dialog.GetAttribute("aria-modal"));
        Assert.Equal("Delete account?", fixture.Component.Find("#" + dialog.GetAttribute("aria-labelledby")).TextContent);
        Assert.Equal("This cannot be undone.", fixture.Component.Find("#" + dialog.GetAttribute("aria-describedby")).TextContent);
        Assert.Equal("true", fixture.Component.Find("button[aria-haspopup=dialog]").GetAttribute("aria-expanded"));
    }

    [Fact]
    public async Task Save_status_region_is_rendered_before_any_update()
    {
        // Screen readers announce changes to live regions that already exist; a region inserted with its text often stays silent.
        await using var fixture = await AccessibilityFixture.For<SaveButton>().WithSettings(env.BrowserMarkup).BuildAsync();

        (await fixture.AuditAsync()).ShouldHaveNoViolations(AccessibilityExpectation.Strict);
        Assert.Equal(string.Empty, fixture.Component.Find("[role=status]").TextContent);
    }
}

internal static class DemoData
{
    public static IReadOnlyList<Tabs.TabItem> Tabs { get; } =
    [
        new("Profile", b => b.AddMarkupContent(0, "<p>Name and photo.</p>")),
        new("Notifications", b => b.AddMarkupContent(0, "<p>Email preferences.</p>")),
        new("Privacy", b => b.AddMarkupContent(0, "<p>Visibility.</p>")),
    ];
}
