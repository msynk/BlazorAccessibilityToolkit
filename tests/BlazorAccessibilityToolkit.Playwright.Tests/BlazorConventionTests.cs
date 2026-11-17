namespace BlazorAccessibilityToolkit.Playwright.Tests;

/// <summary>Diagnostics for Blazor conventions (NavLink, forms, the error notification) and client-side navigation.</summary>
public sealed class BlazorConventionTests(BrowserFixture browser)
{
    [Fact]
    public async Task Active_navlink_without_aria_current_is_reported()
    {
        const string html = """
            <nav aria-label="Main"><a href="/" class="active">Home</a><a href="/orders" class="active" aria-current="page">Orders</a><a href="/help">Help</a></nav>
            <main><h1>x</h1><a href="/x" class="active">Not in a navigation</a></main>
            """;
        await using var session = await browser.PageAsync(html);
        var audit = await session.Page.AuditAccessibilityAsync();
        var findings = audit.Findings.Where(f => f.RuleId == "blazor-navlink-missing-aria-current").ToArray();
        Assert.Single(findings);
        Assert.Equal("Home", findings[0].Target.AccessibleName);
    }

    [Fact]
    public async Task Personal_data_fields_without_autocomplete_are_reported()
    {
        const string html = """
            <main><h1>x</h1><form>
              <label for="e">Email</label><input id="e" name="Model.Email">
              <label for="p">Phone</label><input id="p" type="tel" autocomplete="tel">
              <label for="n">Product name</label><input id="n" name="Model.Name">
              <label for="pw">Password</label><input id="pw" type="password">
              <label for="q">Search</label><input id="q" type="search" name="q">
            </form></main>
            """;
        await using var session = await browser.PageAsync(html);
        var audit = await session.Page.AuditAccessibilityAsync();
        var findings = audit.Findings.Where(f => f.RuleId == "blazor-input-purpose-missing").ToArray();
        Assert.Equal(["#e", "#pw"], findings.Select(f => f.Target.Selector).Order(StringComparer.Ordinal));
        Assert.Equal("email", findings.Single(f => f.Target.Selector == "#e").Data["suggested"]);
        Assert.Contains(findings[0].Wcag, w => w.Id == "1.3.5");
    }

    [Fact]
    public async Task Fields_marked_required_only_in_the_label_are_reported()
    {
        const string html = """
            <main><h1>x</h1><form>
              <label for="a">Name *</label><input id="a" autocomplete="name">
              <label for="b">City (required)</label><input id="b">
              <label for="c">Email <span aria-hidden="true">*</span></label><input id="c" required autocomplete="email">
              <label for="d">Notes</label><textarea id="d"></textarea>
            </form></main>
            """;
        await using var session = await browser.PageAsync(html);
        var audit = await session.Page.AuditAccessibilityAsync();
        var selectors = audit.Findings.Where(f => f.RuleId == "blazor-required-not-exposed").Select(f => f.Target.Selector).Order(StringComparer.Ordinal);
        Assert.Equal(["#a", "#b"], selectors);
    }

    [Fact]
    public async Task Template_blazor_error_ui_is_reported_and_the_accessible_one_passes()
    {
        const string template = """
            <main><h1>x</h1></main>
            <div id="blazor-error-ui" style="display:none">An unhandled error has occurred. <a href="" class="reload">Reload</a> <span class="dismiss">🗙</span></div>
            """;
        await using (var session = await browser.PageAsync(template))
        {
            var finding = (await session.Page.AuditAccessibilityAsync()).ShouldHaveFinding("blazor-error-ui-not-accessible");
            Assert.Contains("live region", finding.Remediation, StringComparison.Ordinal);
            Assert.Contains("keyboard", finding.Remediation, StringComparison.Ordinal);
        }

        const string fixedUi = """
            <main><h1>x</h1></main>
            <div id="blazor-error-ui" role="alert" style="display:none">An unhandled error has occurred. <a href="" class="reload">Reload</a> <button type="button" class="dismiss" aria-label="Dismiss">🗙</button></div>
            """;
        await using var ok = await browser.PageAsync(fixedUi);
        var audit = await ok.Page.AuditAccessibilityAsync();
        audit.ShouldNotHaveFinding("blazor-error-ui-not-accessible");
        Assert.Contains("blazor-error-ui-not-accessible", audit.PassedRules);
    }

    private const string Spa = """
        <nav aria-label="Main"><a id="go" href="/orders">Orders</a></nav>
        <main id="content"><h1 tabindex="-1">Home</h1></main>
        <script>
          document.getElementById('go').addEventListener('click', e => {
            e.preventDefault();
            history.pushState({}, '', '/orders');
            document.getElementById('content').innerHTML = '<h1 tabindex="-1">Orders</h1>';
            /*TITLE*/document.title = 'Orders';
            /*FOCUS*/document.querySelector('h1').focus();
          });
        </script>
        """;

    [Fact]
    public async Task Client_side_navigation_that_updates_title_and_focus_passes()
    {
        await using var session = await browser.Browser.NewSessionAsync();
        await session.Page.RouteAsync("http://a11y.test/**", r => r.FulfillAsync(new() { ContentType = "text/html", Body = $"<!DOCTYPE html><html lang=\"en\"><head><title>Home</title></head><body>{Spa}</body></html>" }));
        await session.Page.GotoAsync("http://a11y.test/");
        var result = await NavigationChecks.VerifyNavigationAsync(session.Page.Locator("#go"));
        Assert.True(result.Passed, result.Describe());
    }

    [Fact]
    public async Task Client_side_navigation_without_title_or_focus_management_fails()
    {
        await using var session = await browser.Browser.NewSessionAsync();
        var broken = Spa.Replace("/*TITLE*/document.title = 'Orders';", string.Empty, StringComparison.Ordinal).Replace("/*FOCUS*/document.querySelector('h1').focus();", string.Empty, StringComparison.Ordinal);
        await session.Page.RouteAsync("http://a11y.test/**", r => r.FulfillAsync(new() { ContentType = "text/html", Body = $"<!DOCTYPE html><html lang=\"en\"><head><title>Home</title></head><body>{broken}</body></html>" }));
        await session.Page.GotoAsync("http://a11y.test/");
        var result = await NavigationChecks.VerifyNavigationAsync(session.Page.Locator("#go"), new PatternOptions { Timeout = TimeSpan.FromMilliseconds(500) });

        Assert.False(result.Passed);
        Assert.Contains(result.Checks, c => !c.Passed && c.Requirement.StartsWith("The title differs", StringComparison.Ordinal));
        var focus = Assert.Single(result.Checks, c => c.Requirement.StartsWith("Focus moves to the new content", StringComparison.Ordinal));
        Assert.False(focus.Passed);
        Assert.Contains("stayed on the link", focus.Detail, StringComparison.Ordinal);
    }
}
