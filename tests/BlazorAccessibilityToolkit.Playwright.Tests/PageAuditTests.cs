using BlazorAccessibilityToolkit.Engines;

namespace BlazorAccessibilityToolkit.Playwright.Tests;

public sealed class PageAuditTests(BrowserFixture browser)
{
    [Fact]
    public async Task Detects_axe_violations_with_wcag_mapping_role_and_name()
    {
        await using var session = await browser.PageAsync("<main><h1>Hi</h1><button class=\"icon\"></button><img src=\"x.png\"></main>");
        var audit = await session.Page.AuditAccessibilityAsync();

        Assert.Equal(AuditStatus.Completed, audit.Status);
        var buttonName = audit.ShouldHaveFinding("button-name", FindingStatus.Violation);
        Assert.Equal("axe-core", buttonName.Engine);
        Assert.Equal("button", buttonName.Target.Role);
        Assert.Equal(string.Empty, buttonName.Target.AccessibleName);
        Assert.Contains(buttonName.Wcag, w => w.Id == "4.1.2");
        Assert.NotNull(buttonName.Remediation);
        Assert.NotNull(buttonName.Fingerprint);
        Assert.Contains(audit.Engines, e => e.Name == "axe-core" && e.Version!.StartsWith("4.14", StringComparison.Ordinal));
        Assert.Contains("document-title", audit.PassedRules);
        Assert.Throws<AccessibilityAssertionException>(() => audit.ShouldHaveNoViolations());
    }

    [Fact]
    public async Task Findings_inside_iframes_are_reported_with_their_frame_path()
    {
        // Pages with frames are audited through Deque's cross-frame integration rather than the in-page fast path.
        await using var session = await browser.PageAsync("<main><h1>x</h1><iframe title=\"Embedded form\" srcdoc=\"<main><button class='inner'></button></main>\"></iframe></main>");
        await session.Page.FrameLocator("iframe").Locator("button").WaitForAsync(new() { State = Microsoft.Playwright.WaitForSelectorState.Attached });

        var audit = await session.Page.AuditAccessibilityAsync();

        var finding = audit.ShouldHaveFinding("button-name");
        Assert.Equal(2, finding.Target.SelectorPath.Count);
        Assert.Contains(">>>", finding.Target.Selector, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Repeated_audits_of_the_same_page_are_consistent()
    {
        await using var session = await browser.PageAsync("<main><h1>x</h1><button class=\"icon\"></button></main>");
        var first = await session.Page.AuditAccessibilityAsync();
        var second = await session.Page.AuditAccessibilityAsync();
        Assert.Equal(first.Findings.Select(f => f.Fingerprint), second.Findings.Select(f => f.Fingerprint));
        Assert.Equal(first.PassedRules, second.PassedRules);
    }

    [Fact]
    public async Task A_different_axe_version_already_in_the_page_is_replaced_by_the_pinned_one()
    {
        await using var session = await browser.PageAsync("<main><h1>x</h1><button></button></main><script>window.axe = { version: '1.0.0', commons: {} };</script>");
        var audit = await session.Page.AuditAccessibilityAsync();
        Assert.Equal(AuditStatus.Completed, audit.Status);
        Assert.StartsWith("4.14", audit.Engines.Single(e => e.Name == "axe-core").Version, StringComparison.Ordinal);
        audit.ShouldHaveFinding("button-name");
    }

    [Fact]
    public async Task Clean_page_passes()
    {
        await using var session = await browser.PageAsync("<main><h1>Settings</h1><button type=\"button\">Save</button><label for=\"n\">Name</label><input id=\"n\"></main>");
        var audit = await session.Page.AuditAccessibilityAsync();
        audit.ShouldHaveNoViolations();
        Assert.NotEmpty(audit.PassedRules);
    }

    [Fact]
    public async Task Region_audit_only_reports_inside_scope()
    {
        await using var session = await browser.PageAsync("<main><h1>x</h1><section id=\"a\"><button></button></section><section id=\"b\"><button>Ok</button><img src=\"y.png\"></section></main>");
        var audit = await session.Page.Locator("#b").AuditAccessibilityAsync();
        Assert.Equal(AuditTargetKind.Region, audit.Context.Kind);
        audit.ShouldHaveFinding("image-alt");
        audit.ShouldNotHaveFinding("button-name");
    }

    [Fact]
    public async Task Missing_or_ambiguous_scope_fails_instead_of_passing()
    {
        await using var session = await browser.PageAsync("<main><h1>x</h1><p>a</p><p>b</p></main>");
        var missing = await session.Page.Locator("#does-not-exist").AuditAccessibilityAsync();
        Assert.Equal(AuditStatus.Failed, missing.Status);
        Assert.Throws<AccessibilityAuditFailedException>(() => missing.ShouldHaveNoViolations());

        var ambiguous = await session.Page.Locator("p").AuditAccessibilityAsync();
        Assert.Equal(AuditStatus.Failed, ambiguous.Status);
    }

    [Fact]
    public async Task Engine_misconfiguration_fails_the_audit()
    {
        await using var session = await browser.PageAsync("<main><h1>x</h1></main>");
        var unknownTag = await session.Page.AuditAccessibilityAsync(new AccessibilityAuditOptions { Tags = ["no-such-tag"] });
        Assert.Equal(AuditStatus.Failed, unknownTag.Status);
        var unknownRule = await session.Page.AuditAccessibilityAsync(new AccessibilityAuditOptions { DisabledRules = ["no-such-rule"] });
        Assert.Equal(AuditStatus.Failed, unknownRule.Status);
        Assert.Contains("no-such-rule", unknownRule.Errors[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Timeout_fails_the_audit()
    {
        await using var session = await browser.PageAsync("<main><h1>x</h1></main>");
        var audit = await session.Page.AuditAccessibilityAsync(new AccessibilityAuditOptions { Timeout = TimeSpan.FromMilliseconds(1) });
        Assert.Equal(AuditStatus.Failed, audit.Status);
        Assert.Contains("did not finish", audit.Errors[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Closed_page_fails_the_audit()
    {
        var session = await browser.PageAsync("<main><h1>x</h1></main>");
        await session.DisposeAsync();
        var audit = await session.Page.AuditAccessibilityAsync();
        Assert.Equal(AuditStatus.Failed, audit.Status);
    }

    [Fact]
    public async Task Rule_selection_exclusions_and_suppressions()
    {
        await using var session = await browser.PageAsync("<main><h1>x</h1><div id=\"legacy\"><img src=\"a.png\"></div><img class=\"hero\" src=\"b.png\"><button></button></main>");
        var options = new AccessibilityAuditOptions
        {
            ExcludeSelectors = ["#legacy"],
            DisabledRules = ["button-name"],
            Suppressions = [new Suppression { RuleId = "image-alt", Selector = "*hero*", Reason = "Decorative image fix scheduled", Owner = "ui-team" }],
        };
        var audit = await session.Page.AuditAccessibilityAsync(options);
        Assert.Empty(audit.Violations);
        var suppressed = Assert.Single(audit.Suppressed);
        Assert.Equal("ui-team", suppressed.Suppression!.Owner);
        Assert.Contains("button-name", audit.Configuration.DisabledRules);
    }

    [Fact]
    public async Task Best_practices_are_separate_and_flagged()
    {
        await using var session = await browser.PageAsync("<div><h1>x</h1><p>outside landmarks</p></div>");
        var conformance = await session.Page.AuditAccessibilityAsync();
        conformance.ShouldNotHaveFinding("region");

        var withBestPractices = await session.Page.AuditAccessibilityAsync(new AccessibilityAuditOptions { IncludeBestPractices = true });
        var region = withBestPractices.ShouldHaveFinding("region");
        Assert.True(region.IsBestPractice);
        Assert.Empty(region.Wcag);
    }

    [Fact]
    public async Task Inconclusive_results_are_reported_and_never_count_as_passes()
    {
        // Text over a background image: axe-core cannot determine contrast and reports "incomplete".
        await using var session = await browser.PageAsync("<main><h1>x</h1><p style=\"background-image:url(data:image/png;base64,iVBORw0KGgo=);color:#777\">Overlaid text</p></main>");
        var audit = await session.Page.AuditAccessibilityAsync();
        var finding = audit.ShouldHaveFinding("color-contrast", FindingStatus.Inconclusive);
        Assert.StartsWith("Review manually", finding.Remediation, StringComparison.Ordinal);
        audit.ShouldHaveNoViolations();
        Assert.Throws<AccessibilityAssertionException>(() => audit.ShouldBeConclusive());
        Assert.DoesNotContain("color-contrast", audit.PassedRules);
    }

    [Fact]
    public async Task Evidence_redacts_form_values()
    {
        await using var session = await browser.PageAsync("<main><h1>x</h1><input type=\"text\" value=\"4111 1111 1111 1111\"></main>");
        await session.Page.FillAsync("input", "secret-value");
        var audit = await session.Page.AuditAccessibilityAsync();
        var label = audit.ShouldHaveFinding("label");
        Assert.DoesNotContain("4111", label.Target.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Markup_engine_reports_component_ordinals_and_strips_them_from_evidence()
    {
        var engine = new PlaywrightMarkupAuditEngine(browser.Browser);
        var result = await engine.AuditAsync(new MarkupAuditRequest
        {
            BodyHtml = "<div data-a11y-ord=\"0\"><button data-a11y-ord=\"1\" class=\"icon\"></button><img data-a11y-ord=\"2\" src=\"a.png\"></div>",
        });

        Assert.Empty(result.Errors);
        var button = Assert.Single(result.Findings, f => f.RuleId == "button-name");
        Assert.Equal(1, button.Target.ElementOrdinal);
        Assert.DoesNotContain("data-a11y-ord", button.Target.Html, StringComparison.Ordinal);
        Assert.Equal(2, result.Findings.Single(f => f.RuleId == "image-alt").Target.ElementOrdinal);
        Assert.Equal(browser.Browser.Name, result.Browser);
    }

    [Fact]
    public async Task Pooled_markup_pages_do_not_leak_markup_or_styles_between_audits()
    {
        await using var engine = new PlaywrightMarkupAuditEngine(browser.Browser);
        var styled = await engine.AuditAsync(new MarkupAuditRequest
        {
            BodyHtml = "<p data-a11y-ord=\"0\" class=\"faint\">Hard to read</p><img data-a11y-ord=\"1\" src=\"a.png\">",
            Document = new MarkupDocumentOptions { Styles = [".faint { color: #bbb; background: #fff; }"], Language = "de" },
        });
        Assert.Contains(styled.Findings, f => f.RuleId == "color-contrast");

        // Same engine, so the pooled page is reused: the previous document, styles and lang must be gone.
        var plain = await engine.AuditAsync(new MarkupAuditRequest { BodyHtml = "<p data-a11y-ord=\"0\" class=\"faint\">Hard to read</p>" });
        Assert.Empty(plain.Errors);
        Assert.DoesNotContain(plain.Findings, f => f.RuleId is "color-contrast" or "image-alt");
    }

    [Fact]
    public async Task Markup_engine_without_reuse_creates_fresh_contexts()
    {
        await using var engine = new PlaywrightMarkupAuditEngine(browser.Browser) { ReusePages = false };
        var result = await engine.AuditAsync(new MarkupAuditRequest { BodyHtml = "<button data-a11y-ord=\"0\"></button>" });
        Assert.Contains(result.Findings, f => f.RuleId == "button-name");
    }

    [Fact]
    public async Task Markup_engine_uses_supplied_styles_for_contrast()
    {
        var engine = new PlaywrightMarkupAuditEngine(browser.Browser);
        var request = new MarkupAuditRequest
        {
            BodyHtml = "<p data-a11y-ord=\"0\" class=\"faint\">Hard to read</p>",
            Document = new MarkupDocumentOptions { Styles = [".faint { color: #bbb; background: #fff; }"] },
        };
        var result = await engine.AuditAsync(request);
        Assert.Contains(result.Findings, f => f.RuleId == "color-contrast" && f.Status == FindingStatus.Violation);
    }
}
