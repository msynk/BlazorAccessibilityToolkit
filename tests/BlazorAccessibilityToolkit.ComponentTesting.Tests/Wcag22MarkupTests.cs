using BlazorAccessibilityToolkit.ComponentTesting.Tests.Components;

namespace BlazorAccessibilityToolkit.ComponentTesting.Tests;

public sealed class Wcag22MarkupTests
{
    [Fact]
    public async Task Paste_prevention_password_managers_and_dragging_are_reported()
    {
        await using var fixture = await AccessibilityFixture.For<AuthenticationFields>().BuildAsync();
        var audit = await fixture.AuditAsync();

        var paste = audit.Findings.Where(f => f.RuleId == "blazor-paste-blocked").ToArray();
        Assert.Equal(3, paste.Length);
        Assert.Contains(paste, f => f.Target.Selector == "#pw" && f.Data["reason"] == "paste");
        Assert.Contains(paste, f => f.Target.Selector == "#pw" && f.Data["reason"] == "autocomplete-off");
        var code = Assert.Single(paste, f => f.Target.Selector == "#code");
        Assert.Equal(AccessibilityImpact.Serious, code.Impact);
        Assert.Equal(["3.3.8"], code.Wcag.Select(w => w.Id));
        Assert.All(paste, f => Assert.Equal(FindingStatus.PotentialIssue, f.Status));

        // One finding for the list of two draggable items.
        var drag = Assert.Single(audit.Findings, f => f.RuleId == "blazor-dragging-alternative");
        Assert.Equal("2", drag.Data["draggable"]);
        Assert.DoesNotContain("blazor-paste-blocked", audit.PassedRules);
    }

    [Fact]
    public async Task Razor_rules_off_drops_the_paste_handler_finding_only()
    {
        var settings = ComponentAuditSettings.Default with { RazorRules = false };
        await using var fixture = await AccessibilityFixture.For<AuthenticationFields>().WithSettings(settings).BuildAsync();
        var audit = await fixture.AuditAsync();

        var paste = Assert.Single(audit.Findings, f => f.RuleId == "blazor-paste-blocked");
        Assert.Equal("autocomplete-off", paste.Data["reason"]);
    }

    [Fact]
    public async Task Confirmation_fields_are_reported_as_redundant_entry()
    {
        await using var context = new BunitContext();
        var cut = context.Render<RawMarkup>(p => p.Add(c => c.Html, """
            <label for="Model.Email">Email</label><input id="Model.Email" name="Model.Email" autocomplete="email">
            <label for="Model.ConfirmEmail">Confirm email</label><input id="Model.ConfirmEmail" name="Model.ConfirmEmail" autocomplete="email">
            <label for="pw2">Repeat password</label><input id="pw2" name="RepeatPassword" type="password" autocomplete="new-password">
            """));
        var audit = await cut.AuditAccessibilityAsync();

        var finding = Assert.Single(audit.Findings, f => f.RuleId == "blazor-redundant-entry");
        Assert.Equal("email", finding.Data["purpose"]);
        Assert.Equal(["3.3.7"], finding.Wcag.Select(w => w.Id));
    }

    [Fact]
    public async Task Options_of_a_focus_managing_listbox_are_not_click_handler_findings()
    {
        await using var fixture = await AccessibilityFixture.For<ManagedOptions>().BuildAsync();
        var audit = await fixture.AuditAsync();

        var finding = Assert.Single(audit.Findings, f => f.RuleId == "blazor-click-handler-semantics");
        Assert.Equal("#loose", finding.Target.Selector);
    }
}
