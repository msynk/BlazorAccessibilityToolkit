using BlazorAccessibilityToolkit.ComponentTesting.Tests.Components;
using BlazorAccessibilityToolkit.Engines;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorAccessibilityToolkit.ComponentTesting.Tests;

public sealed class MarkupRuleTests
{
    [Fact]
    public async Task Findings_are_attributed_to_the_nested_component_that_rendered_them()
    {
        await using var fixture = await AccessibilityFixture.For<Toolbar>().BuildAsync();
        var audit = await fixture.AuditAsync();

        Assert.Equal(AuditStatus.Completed, audit.Status);
        var finding = audit.ShouldHaveFinding("blazor-markup-control-name", FindingStatus.Violation);
        Assert.Equal("render-tree", finding.Component!.Method);
        Assert.Equal(typeof(IconButton).FullName, finding.Component.ComponentType);
        Assert.Equal([typeof(Toolbar).FullName!, typeof(IconButton).FullName!], finding.Component.ComponentPath);
        Assert.Equal("button.icon-button:nth-child(2)", finding.Target.Selector);
        Assert.DoesNotContain("blazor:", finding.Target.Html, StringComparison.Ordinal);
        Assert.Empty(audit.Warnings);
        Assert.Throws<AccessibilityAssertionException>(() => audit.ShouldHaveNoViolations());
    }

    [Fact]
    public async Task Blazor_click_handlers_need_role_focus_and_keyboard_support()
    {
        await using var fixture = await AccessibilityFixture.For<ClickTargets>().BuildAsync();
        var audit = await fixture.AuditAsync();
        var findings = audit.Findings.Where(f => f.RuleId == "blazor-click-handler-semantics").ToArray();

        Assert.Equal(2, findings.Length);
        var card = Assert.Single(findings, f => f.Target.Selector!.Contains("card", StringComparison.Ordinal));
        Assert.Contains("no interactive role", card.Remediation, StringComparison.Ordinal);
        Assert.Contains("not focusable", card.Remediation, StringComparison.Ordinal);
        var noKeys = Assert.Single(findings, f => f.Target.Selector!.Contains("no-keys", StringComparison.Ordinal));
        Assert.Contains("Enter or Space", noKeys.Remediation, StringComparison.Ordinal);
        Assert.All(findings, f => Assert.Equal(FindingStatus.PotentialIssue, f.Status));
        Assert.Equal(["2.1.1", "4.1.2"], card.Wcag.Select(w => w.Id));
    }

    [Fact]
    public async Task Click_handlers_out_of_keyboard_reach_are_reported()
    {
        await using var fixture = await AccessibilityFixture.For<UnreachableClickTargets>().BuildAsync();
        var audit = await fixture.AuditAsync();
        var findings = audit.Findings.Where(f => f.RuleId == "blazor-click-handler-semantics").ToArray();

        var negative = Assert.Single(findings, f => f.Target.Selector!.Contains("negative", StringComparison.Ordinal));
        Assert.Contains("tabindex=\"-1\"", negative.Remediation, StringComparison.Ordinal);
        Assert.DoesNotContain("Enter or Space", negative.Remediation, StringComparison.Ordinal);
        var label = Assert.Single(findings, f => f.Target.Selector!.Contains("label-only", StringComparison.Ordinal));
        Assert.Contains("not focusable", label.Remediation, StringComparison.Ordinal);

        // A video with controls is focusable, but keyboard operation of the controls does not run the click handler.
        var video = Assert.Single(findings, f => f.Target.Selector!.Contains("player", StringComparison.Ordinal));
        Assert.DoesNotContain("not focusable", video.Remediation, StringComparison.Ordinal);
        Assert.Contains("Enter or Space", video.Remediation, StringComparison.Ordinal);

        // Space on the wrapped checkbox dispatches a click that bubbles to the label.
        Assert.DoesNotContain(findings, f => f.Target.Selector!.Contains("label-checkbox", StringComparison.Ordinal));
        Assert.Equal(3, findings.Length);
    }

    [Fact]
    public async Task Selectors_are_anchored_on_unique_ancestor_ids()
    {
        await using var fixture = await AccessibilityFixture.For<IdAnchors>().BuildAsync();
        var audit = await fixture.AuditAsync();
        var selectors = audit.Findings.Where(f => f.RuleId == "blazor-markup-control-name").Select(f => f.Target.Selector!).Order(StringComparer.Ordinal);
        Assert.Equal(["#billing > div > button.edit", "#shipping > div > button.edit"], selectors);
    }

    [Fact]
    public async Task Blazor_validation_messages_without_association_are_reported_after_submit()
    {
        await using var fixture = await AccessibilityFixture.For<SignupForm>().BuildAsync();
        (await fixture.AuditAsync("initial")).ShouldNotHaveFinding("blazor-validation-message-association");

        fixture.Component.Find("form").Submit();
        var audit = await fixture.AuditAsync("after invalid submit");
        var message = audit.ShouldHaveFinding("blazor-validation-message-association");
        Assert.Equal(typeof(Microsoft.AspNetCore.Components.Forms.ValidationMessage<string>).FullName!.Split('[')[0], message.Component!.ComponentType);
        audit.ShouldHaveFinding("blazor-invalid-without-error-reference");
        Assert.Equal(2, fixture.Audits.Count);
        Assert.Equal("after invalid submit", audit.Context.State);
    }

    [Fact]
    public async Task Associated_validation_messages_pass()
    {
        await using var fixture = await AccessibilityFixture.For<AccessibleSignupForm>().BuildAsync();
        fixture.Component.Find("form").Submit();
        var audit = await fixture.AuditAsync("after invalid submit");
        Assert.NotNull(fixture.Component.Find("#email-error"));
        audit.ShouldNotHaveFinding("blazor-validation-message-association");
        audit.ShouldNotHaveFinding("blazor-invalid-without-error-reference");
        audit.ShouldHaveNoViolations(AccessibilityExpectation.Strict);
    }

    [Fact]
    public async Task Browserless_fallback_rules_cover_common_markup_failures()
    {
        await using var fixture = await AccessibilityFixture.For<MarkupProblems>().WithOptions(new AccessibilityAuditOptions { IncludeBestPractices = true }).BuildAsync();
        var audit = await fixture.AuditAsync();
        var rules = audit.Findings.Select(f => f.RuleId).ToHashSet();

        Assert.Contains("blazor-markup-image-alt", rules);
        Assert.Contains("blazor-markup-aria-hidden-focusable", rules);
        Assert.Contains("blazor-markup-invalid-aria-attribute", rules);
        Assert.Contains("blazor-markup-invalid-role", rules);
        Assert.Contains("blazor-markup-positive-tabindex", rules);
        Assert.Contains("blazor-radio-group-ungrouped", rules);
        Assert.Contains("blazor-fieldset-missing-legend", rules);
        Assert.Contains("blazor-idref-missing-target", rules);

        var labels = audit.Findings.Where(f => f.RuleId == "blazor-markup-form-label").ToArray();
        Assert.Equal(2, labels.Length);
        Assert.Single(labels, l => l.Status == FindingStatus.Violation);
        Assert.Single(labels, l => l.Status == FindingStatus.PotentialIssue && l.Remediation!.Contains("external-label", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Duplicate_ids_from_reused_components_are_reported()
    {
        await using var fixture = await AccessibilityFixture.For<TwoFields>().BuildAsync();
        var audit = await fixture.AuditAsync();
        var finding = audit.ShouldHaveFinding("blazor-duplicate-id-referenced", FindingStatus.Violation);
        Assert.Equal(typeof(DuplicateField).FullName, finding.Component!.ComponentType);
        Assert.Equal("email", finding.Data["id"]);
    }

    [Fact]
    public async Task Options_disable_rules_exclude_selectors_and_apply_suppressions()
    {
        var options = new AccessibilityAuditOptions
        {
            DisabledRules = ["blazor-markup-image-alt"],
            ExcludeSelectors = ["fieldset"],
            Suppressions = [new Suppression { RuleId = "blazor-markup-invalid-role", Component = "MarkupProblems", Reason = "Legacy markup, ticket #42", Expires = new DateOnly(2099, 1, 1) }],
        };
        await using var fixture = await AccessibilityFixture.For<MarkupProblems>().WithOptions(options).BuildAsync();
        var audit = await fixture.AuditAsync();
        audit.ShouldNotHaveFinding("blazor-markup-image-alt");
        audit.ShouldNotHaveFinding("blazor-fieldset-missing-legend");
        Assert.Contains(audit.Suppressed, f => f.RuleId == "blazor-markup-invalid-role");
    }

    [Fact]
    public async Task Parameters_cascading_values_services_and_child_content()
    {
        await using var fixture = await AccessibilityFixture.For<ThemedPanel>()
            .WithParameter(p => p.Title, "Account")
            .WithCascadingValue("high-contrast")
            .WithChildContent("<p class=\"child\">Child</p>")
            .WithServices(s => s.AddSingleton(new GreetingService("Ana")))
            .WithName("themed panel")
            .BuildAsync();

        Assert.Equal("Account", fixture.Component.Find("h2").TextContent);
        Assert.Equal("high-contrast", fixture.Component.Find(".theme").TextContent);
        Assert.Equal("Hello Ana", fixture.Component.Find(".greeting").TextContent);
        Assert.NotNull(fixture.Component.Find(".child"));

        var audit = await fixture.AuditAsync();
        audit.ShouldHaveNoViolations(AccessibilityExpectation.Strict);
        Assert.Equal("themed panel", audit.Context.Scenario);
        Assert.Equal("bunit-markup", audit.Context.RenderMode);
        Assert.Contains("blazor-click-handler-semantics", audit.PassedRules);
    }

    [Fact]
    public async Task Async_rendering_is_audited_after_waiting_for_state()
    {
        await using var fixture = await AccessibilityFixture.For<AsyncList>().BuildAsync();
        fixture.Component.WaitForElement("ul");
        var audit = await fixture.AuditAsync("loaded");
        audit.ShouldHaveFinding("blazor-markup-control-name");
    }

    [Fact]
    public async Task Empty_render_fails_instead_of_passing()
    {
        await using var fixture = await AccessibilityFixture.For<Nothing>().BuildAsync();
        var audit = await fixture.AuditAsync();
        Assert.Equal(AuditStatus.Failed, audit.Status);
        Assert.Throws<AccessibilityAuditFailedException>(() => audit.ShouldHaveNoViolations());
    }

    [Fact]
    public async Task Attribution_falls_back_to_the_root_when_the_parser_changes_the_tree()
    {
        await using var fixture = await AccessibilityFixture.For<LooseTable>().BuildAsync();
        var audit = await fixture.AuditAsync();
        var finding = audit.ShouldHaveFinding("blazor-markup-control-name");
        Assert.Equal("fixture-root", finding.Component!.Method);
        Assert.Contains(audit.Warnings, w => w.Contains("tbody", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Engine_errors_fail_the_audit()
    {
        await using var fixture = await AccessibilityFixture.For<Toolbar>().WithEngine(new FailingEngine()).BuildAsync();
        var audit = await fixture.AuditAsync();
        Assert.Equal(AuditStatus.Failed, audit.Status);
        Assert.Equal("fake", audit.Errors.Single().Source);
    }

    [Fact]
    public async Task Existing_bunit_tests_can_audit_rendered_components()
    {
        await using var context = new BunitContext();
        var cut = context.Render<IconButton>(p => p.Add(c => c.Label, "Close"));
        var audit = await cut.AuditAccessibilityAsync("labelled");
        audit.ShouldHaveNoViolations(AccessibilityExpectation.Strict);
        Assert.Equal("IconButton [labelled]", audit.Name);
    }

    [Fact]
    public async Task Parallel_fixtures_do_not_share_state()
    {
        var tasks = Enumerable.Range(0, 16).Select(async i =>
        {
            await using var fixture = await AccessibilityFixture.For<IconButton>().WithParameter(p => p.Label, i % 2 == 0 ? $"Action {i}" : null).BuildAsync();
            var audit = await fixture.AuditAsync();
            return (i, HasFinding: audit.Findings.Any(f => f.RuleId == "blazor-markup-control-name"));
        });
        var results = await Task.WhenAll(tasks);
        Assert.All(results, r => Assert.Equal(r.i % 2 == 1, r.HasFinding));
    }

    private sealed class FailingEngine : IMarkupAuditEngine
    {
        public string Name => "fake";

        public Task<MarkupEngineResult> AuditAsync(MarkupAuditRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MarkupEngineResult { Errors = [new ExecutionError { Source = "fake", Message = "browser crashed" }] });
    }
}
