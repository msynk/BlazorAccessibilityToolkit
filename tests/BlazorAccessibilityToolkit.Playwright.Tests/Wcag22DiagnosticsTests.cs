namespace BlazorAccessibilityToolkit.Playwright.Tests;

public sealed class Wcag22DiagnosticsTests(BrowserFixture browser)
{
    [Fact]
    public async Task Paste_blocking_password_managers_redundant_entry_and_dragging_are_reported()
    {
        const string html = """
            <main><h1>Sign in</h1>
              <label for="u">User name</label><input id="u" autocomplete="username">
              <label for="p">Password</label><input id="p" type="password" autocomplete="current-password">
              <label for="o">Old password</label><input id="o" type="password" autocomplete="off">
              <label for="e">Email</label><input id="e" name="Email" autocomplete="email">
              <label for="ce">Confirm email</label><input id="ce" name="Input.ConfirmEmail" autocomplete="email">
              <ul aria-label="Priorities"><li draggable="true">One</li><li draggable="true">Two</li><li draggable="true">Three</li></ul>
            </main>
            <script>document.getElementById('p').addEventListener('paste', e => e.preventDefault());</script>
            """;
        await using var session = await browser.PageAsync(html);
        var audit = await session.Page.AuditAccessibilityAsync();

        var paste = audit.Findings.Where(f => f.RuleId == "blazor-paste-blocked").ToArray();
        Assert.Equal(2, paste.Length);
        Assert.Contains(paste, f => f.Target.Selector == "#p" && f.Data["reason"] == "paste");
        Assert.Contains(paste, f => f.Target.Selector == "#o" && f.Data["reason"] == "autocomplete-off");
        Assert.Equal("email", Assert.Single(audit.Findings, f => f.RuleId == "blazor-redundant-entry").Data["purpose"]);
        Assert.Equal("3", Assert.Single(audit.Findings, f => f.RuleId == "blazor-dragging-alternative").Data["draggable"]);
        audit.ShouldHaveNoViolations();

        // The probe does not change field values.
        Assert.Equal(string.Empty, await session.Page.InputValueAsync("#p"));
    }

    [Fact]
    public async Task Disabling_the_rule_skips_the_paste_probe()
    {
        const string html = """
            <main><h1>Sign in</h1>
              <label for="p">Password</label><input id="p" type="password" autocomplete="current-password">
            </main>
            <script>window.pastes = 0; document.getElementById('p').addEventListener('paste', e => { window.pastes++; e.preventDefault(); });</script>
            """;
        await using var session = await browser.PageAsync(html);
        var audit = await session.Page.AuditAccessibilityAsync(new AccessibilityAuditOptions { DisabledRules = ["blazor-paste-blocked"] });

        Assert.DoesNotContain(audit.Findings, f => f.RuleId == "blazor-paste-blocked");
        Assert.Equal(0, await session.Page.EvaluateAsync<int>("window.pastes"));
    }

    [Fact]
    public async Task Fields_that_accept_paste_pass()
    {
        const string html = """
            <main><h1>Sign in</h1>
              <label for="u">User name</label><input id="u" autocomplete="username">
              <label for="p">Password</label><input id="p" type="password" autocomplete="current-password">
            </main>
            """;
        await using var session = await browser.PageAsync(html);
        var audit = await session.Page.AuditAccessibilityAsync();
        Assert.DoesNotContain(audit.Findings, f => f.RuleId is "blazor-paste-blocked" or "blazor-redundant-entry" or "blazor-dragging-alternative");
        Assert.Contains("blazor-paste-blocked", audit.PassedRules);
        Assert.Contains("blazor-dragging-alternative", audit.PassedRules);
    }
}
