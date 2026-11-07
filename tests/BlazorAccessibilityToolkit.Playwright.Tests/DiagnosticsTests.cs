namespace BlazorAccessibilityToolkit.Playwright.Tests;

public sealed class DiagnosticsTests(BrowserFixture browser)
{
    private async Task<AccessibilityAudit> AuditAsync(string body, AccessibilityAuditOptions? options = null)
    {
        await using var session = await browser.PageAsync("<main><h1>Test</h1>" + body + "</main>");
        var audit = await session.Page.AuditAccessibilityAsync(options);
        Assert.Equal(AuditStatus.Completed, audit.Status);
        return audit;
    }

    [Fact]
    public async Task Duplicate_ids_are_a_best_practice_finding()
    {
        var html = "<p id=\"x\">a</p><p id=\"x\">b</p>";
        (await AuditAsync(html)).ShouldNotHaveFinding("blazor-duplicate-id");
        var finding = (await AuditAsync(html, new AccessibilityAuditOptions { IncludeBestPractices = true })).ShouldHaveFinding("blazor-duplicate-id");
        Assert.True(finding.IsBestPractice);
        Assert.Equal(FindingStatus.PotentialIssue, finding.Status);
    }

    [Fact]
    public async Task Broken_id_references_are_reported()
    {
        var audit = await AuditAsync("<button aria-describedby=\"help missing\">Go</button><p id=\"help\">Help</p><label for=\"nope\">Name</label><input aria-label=\"Name\">");
        var findings = audit.Findings.Where(f => f.RuleId == "blazor-idref-missing-target").ToArray();
        Assert.Equal(2, findings.Length);
        Assert.Contains(findings, f => f.Data["missing"] == "missing" && f.Data["attribute"] == "aria-describedby");
        Assert.Contains(findings, f => f.Data["attribute"] == "for");
    }

    [Fact]
    public async Task Expanded_state_must_match_controlled_visibility()
    {
        var audit = await AuditAsync(
            "<button aria-expanded=\"true\" aria-controls=\"menu\">Menu</button><ul id=\"menu\" hidden><li>One</li></ul>" +
            "<button aria-expanded=\"false\" aria-controls=\"panel\">Details</button><div id=\"panel\">Visible panel</div>" +
            "<button aria-expanded=\"true\" aria-controls=\"ok\">Fine</button><div id=\"ok\">Shown</div>");
        var findings = audit.Findings.Where(f => f.RuleId == "blazor-expanded-state-mismatch").ToArray();
        Assert.Equal(2, findings.Length);
        Assert.All(findings, f => Assert.Equal(FindingStatus.PotentialIssue, f.Status));
    }

    [Fact]
    public async Task Blazor_validation_messages_must_be_associated()
    {
        // Markup produced by Blazor's InputText + ValidationMessage after a failed submit.
        var audit = await AuditAsync(
            "<label for=\"email\">Email</label><input id=\"email\" class=\"invalid\" aria-invalid=\"true\" value=\"\">" +
            "<div class=\"validation-message\">The Email field is required.</div>" +
            "<label for=\"name\">Name</label><input id=\"name\" aria-invalid=\"true\" aria-describedby=\"name-error\">" +
            "<div id=\"name-error\" class=\"validation-message\">Name is required.</div>");
        var message = audit.ShouldHaveFinding("blazor-validation-message-association");
        Assert.Contains("Email field", message.Data["text"], StringComparison.Ordinal);
        Assert.Single(audit.Findings, f => f.RuleId == "blazor-invalid-without-error-reference");
        Assert.Single(audit.Findings, f => f.RuleId == "blazor-validation-message-association");
    }

    [Fact]
    public async Task Radio_groups_and_fieldsets_need_group_labels()
    {
        var audit = await AuditAsync(
            "<input type=\"radio\" name=\"size\" id=\"s\" aria-label=\"Small\"><input type=\"radio\" name=\"size\" id=\"l\" aria-label=\"Large\">" +
            "<fieldset><legend>Color</legend><input type=\"radio\" name=\"c\" aria-label=\"Red\"><input type=\"radio\" name=\"c\" aria-label=\"Blue\"></fieldset>" +
            "<fieldset><input type=\"checkbox\" aria-label=\"Agree\"></fieldset>");
        Assert.Single(audit.Findings, f => f.RuleId == "blazor-radio-group-ungrouped");
        Assert.Single(audit.Findings, f => f.RuleId == "blazor-fieldset-missing-legend");
    }

    [Fact]
    public async Task Toolkit_diagnostics_can_be_disabled()
    {
        var audit = await AuditAsync("<label for=\"nope\">x</label><input aria-label=\"x\">", new AccessibilityAuditOptions { ToolkitDiagnostics = false });
        audit.ShouldNotHaveFinding("blazor-idref-missing-target");
        Assert.DoesNotContain(audit.Engines, e => e.Name == "blazor-a11y");
    }

    [Fact]
    public async Task Accessible_info_uses_the_full_name_computation()
    {
        await using var session = await browser.PageAsync(
            "<span id=\"l1\">Delete</span><span id=\"l2\">invoice 42</span>" +
            "<button id=\"b\" aria-labelledby=\"l1 l2\" aria-expanded=\"false\" aria-describedby=\"d\"><svg aria-hidden=\"true\"></svg></button><p id=\"d\">Permanently</p>" +
            "<label>Email <input id=\"e\" required></label><input type=\"checkbox\" id=\"c\" checked aria-label=\"Remember me\">");
        var button = await session.Page.Locator("#b").GetAccessibilityInfoAsync();
        Assert.Equal("button", button.Role);
        Assert.Equal("Delete invoice 42", button.Name);
        Assert.Equal("Permanently", button.Description);
        Assert.Equal("false", button.State("expanded"));

        await session.Page.Locator("#e").ShouldHaveAccessibleNameAsync("Email");
        await session.Page.Locator("#e").ShouldHaveStateAsync("required", "true");
        await session.Page.Locator("#c").ShouldHaveStateAsync("checked", "true");
        await session.Page.Locator("#c").ShouldHaveRoleAsync("checkbox");
        await Assert.ThrowsAsync<AccessibilityAssertionException>(() => session.Page.Locator("#b").ShouldHaveAccessibleNameAsync("Delete"));
    }
}
