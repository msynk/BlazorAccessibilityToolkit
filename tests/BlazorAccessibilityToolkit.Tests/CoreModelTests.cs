using BlazorAccessibilityToolkit.Internal;
using BlazorAccessibilityToolkit.Rules;

namespace BlazorAccessibilityToolkit.Tests;

public class WcagCatalogTests
{
    [Fact]
    public void Catalog_contains_all_wcag22_criteria()
    {
        Assert.Equal(87, WcagCatalog.All.Count);
        Assert.Equal("1.1.1", WcagCatalog.All[0].Id);
        Assert.Equal("4.1.3", WcagCatalog.All[^1].Id);
        Assert.True(WcagCatalog.Get("4.1.1").IsObsolete);
    }

    [Theory]
    [InlineData("wcag143", "1.4.3", WcagLevel.AA)]
    [InlineData("wcag1410", "1.4.10", WcagLevel.AA)]
    [InlineData("wcag2411", "2.4.11", WcagLevel.AA)]
    [InlineData("wcag258", "2.5.8", WcagLevel.AA)]
    [InlineData("wcag412", "4.1.2", WcagLevel.A)]
    public void Maps_axe_tags_to_criteria(string tag, string id, WcagLevel level)
    {
        var criterion = Assert.Single(WcagCatalog.FromTags([tag, "wcag2aa", "cat.color", "best-practice"]));
        Assert.Equal(id, criterion.Id);
        Assert.Equal(level, criterion.Level);
        Assert.StartsWith("https://www.w3.org/WAI/WCAG22/Understanding/", criterion.Url, StringComparison.Ordinal);
    }

    [Fact]
    public void Orders_criteria_numerically()
    {
        var result = WcagCatalog.FromTags(["wcag1410", "wcag143", "wcag111"]);
        Assert.Equal(["1.1.1", "1.4.3", "1.4.10"], result.Select(r => r.Id));
    }

    [Fact]
    public void Toolkit_rules_reference_existing_criteria_and_unique_ids()
    {
        Assert.Equal(ToolkitRules.All.Count, ToolkitRules.All.Select(r => r.Id).Distinct().Count());
        foreach (var rule in ToolkitRules.All)
        {
            Assert.All(rule.Wcag, id => Assert.NotNull(WcagCatalog.Find(id)));
            Assert.NotEmpty(rule.Environments);
            Assert.StartsWith("blazor-", rule.Id, StringComparison.Ordinal);
        }
    }
}

public class EvidenceSanitizerTests
{
    [Fact]
    public void Redacts_input_values_but_keeps_button_labels()
    {
        var html = "<input type=\"password\" value=\"hunter2\"><input type=\"submit\" value=\"Save\"><input name=\"__RequestVerificationToken\" type=\"hidden\" value='CfDJ8abc'>";
        var result = EvidenceSanitizer.RedactFormValues(html);
        Assert.DoesNotContain("hunter2", result, StringComparison.Ordinal);
        Assert.DoesNotContain("CfDJ8abc", result, StringComparison.Ordinal);
        Assert.Contains("value=\"Save\"", result, StringComparison.Ordinal);
        Assert.Contains("[redacted]", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Redacts_textarea_content()
    {
        var result = EvidenceSanitizer.RedactFormValues("<textarea id=\"bio\">secret text</textarea>");
        Assert.Equal("<textarea id=\"bio\">[redacted]</textarea>", result);
        Assert.Equal("<textarea></textarea>", EvidenceSanitizer.RedactFormValues("<textarea></textarea>"));
    }

    [Fact]
    public void Truncates_long_evidence()
    {
        var result = EvidenceSanitizer.Sanitize(new string('a', 5000), redact: false, maxLength: 100);
        Assert.Equal(100, result!.Length);
        Assert.EndsWith("…", result, StringComparison.Ordinal);
    }
}

public class FingerprintTests
{
    [Fact]
    public void Fingerprint_ignores_html_message_and_generated_ids()
    {
        var context = TestData.ComponentContext();
        var a = TestData.Finding(selector: "#item-3f2504e0-4f89-11d3-9a0c-0305e82c3301 > button[b-a1b2c3d4e5]", html: "<button>1</button>");
        var b = TestData.Finding(selector: "#item-9a8b7c6d-1111-2222-3333-444455556666 > button[b-zz99yy88xx]", html: "<button>2</button>") with { Help = "different" };
        Assert.Equal(FindingFingerprint.Compute(a, context), FindingFingerprint.Compute(b, context));
    }

    [Fact]
    public void Fingerprint_distinguishes_rule_selector_state_and_component()
    {
        var context = TestData.ComponentContext();
        var baseFp = FindingFingerprint.Compute(TestData.Finding(), context);
        Assert.NotEqual(baseFp, FindingFingerprint.Compute(TestData.Finding(ruleId: "link-name"), context));
        Assert.NotEqual(baseFp, FindingFingerprint.Compute(TestData.Finding(selector: "button.other"), context));
        Assert.NotEqual(baseFp, FindingFingerprint.Compute(TestData.Finding(), context with { State = "open" }));
        Assert.NotEqual(baseFp, FindingFingerprint.Compute(TestData.Finding(), context with { ComponentType = "Other" }));
        Assert.NotEqual(baseFp, FindingFingerprint.Compute(TestData.Finding(status: FindingStatus.Inconclusive), context));
    }

    [Fact]
    public void Page_fingerprint_ignores_host_and_port()
    {
        var finding = TestData.Finding();
        var a = new AccessibilityContext { Kind = AuditTargetKind.Page, Url = "http://127.0.0.1:5123/counter?x=1" };
        var b = new AccessibilityContext { Kind = AuditTargetKind.Page, Url = "http://localhost:40001/counter" };
        Assert.Equal(FindingFingerprint.Compute(finding, a), FindingFingerprint.Compute(finding, b));
    }
}

public class AuditBuilderTests
{
    [Fact]
    public void Errors_make_the_audit_failed_even_with_findings()
    {
        var builder = new AccessibilityAuditBuilder("x", TestData.ComponentContext());
        builder.AddFinding(TestData.Finding());
        builder.AddError(new ExecutionError { Source = "axe-core", Message = "timeout" });
        Assert.Equal(AuditStatus.Failed, builder.Build().Status);
    }

    [Fact]
    public void Applies_suppressions_with_reason_and_ignores_expired_or_unjustified_ones()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 11, 4, 0, 0, 0, TimeSpan.Zero));
        var options = new AccessibilityAuditOptions
        {
            TimeProvider = clock,
            Suppressions =
            [
                new Suppression { RuleId = "button-name", Selector = "button.icon", Reason = "Tracked in #12", Owner = "team-a", Expires = new DateOnly(2026, 12, 31) },
                new Suppression { RuleId = "color-contrast", Reason = "Old", Expires = new DateOnly(2026, 1, 1) },
                new Suppression { RuleId = "link-name", Reason = " " },
            ],
        };
        var audit = TestData.Audit(
            options: options,
            findings: [TestData.Finding(), TestData.Finding(ruleId: "color-contrast"), TestData.Finding(ruleId: "link-name")]);

        Assert.True(audit.Findings.Single(f => f.RuleId == "button-name").IsSuppressed);
        Assert.Equal(2, audit.Violations.Count);
        Assert.Contains(audit.Warnings, w => w.Contains("expired", StringComparison.Ordinal));
        Assert.Contains(audit.Warnings, w => w.Contains("no reason", StringComparison.Ordinal));
    }

    [Fact]
    public void Suppression_filters_by_component_with_wildcards()
    {
        var options = new AccessibilityAuditOptions { Suppressions = [new Suppression { RuleId = "button-name", Component = "Icon*", Reason = "r" }] };
        var matching = TestData.Audit(options: options, findings: TestData.Finding());
        var other = TestData.Audit(context: TestData.ComponentContext("Sample.Toolbar"), options: options, findings: TestData.Finding());
        Assert.Empty(matching.Violations);
        Assert.Single(other.Violations);
    }

    [Fact]
    public void Removes_exact_duplicates_but_keeps_distinct_elements_with_equal_fingerprints()
    {
        var audit = TestData.Audit(findings: [TestData.Finding(selector: "#a-1234"), TestData.Finding(selector: "#a-1234"), TestData.Finding(selector: "#a-5678")]);
        Assert.Equal(2, audit.Findings.Count);
        Assert.Equal(audit.Findings[0].Fingerprint, audit.Findings[1].Fingerprint);
    }

    [Fact]
    public void Sorts_findings_deterministically_and_drops_passed_rules_with_findings()
    {
        var builder = new AccessibilityAuditBuilder("x", TestData.ComponentContext());
        builder.AddFinding(TestData.Finding(ruleId: "z-rule", impact: AccessibilityImpact.Minor));
        builder.AddFinding(TestData.Finding(ruleId: "b-rule", status: FindingStatus.Inconclusive));
        builder.AddFinding(TestData.Finding(ruleId: "a-rule", impact: AccessibilityImpact.Critical));
        builder.AddPassedRule("a-rule").AddPassedRule("ok-rule");
        var audit = builder.Build();
        Assert.Equal(["a-rule", "z-rule", "b-rule"], audit.Findings.Select(f => f.RuleId));
        Assert.Equal(["ok-rule"], audit.PassedRules);
    }

    [Fact]
    public void Redacts_evidence_by_default()
    {
        var audit = TestData.Audit(findings: TestData.Finding(html: "<input type=\"text\" value=\"4111111111111111\">"));
        Assert.DoesNotContain("4111", audit.Findings[0].Target.Html, StringComparison.Ordinal);
    }
}

public class AssertionTests
{
    [Fact]
    public void Failed_audit_never_passes()
    {
        var ex = Assert.Throws<AccessibilityAuditFailedException>(() => TestData.FailedAudit().ShouldHaveNoViolations());
        Assert.Contains("axe is not defined", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Skipped_audit_never_passes()
    {
        var builder = new AccessibilityAuditBuilder("x", TestData.ComponentContext());
        builder.MarkSkipped("browser not installed");
        Assert.Throws<AccessibilityAuditFailedException>(() => builder.Build().ShouldHaveNoViolations());
    }

    [Fact]
    public void Empty_audit_list_never_passes() =>
        Assert.Throws<AccessibilityAuditFailedException>(() => Array.Empty<AccessibilityAudit>().ShouldHaveNoViolations());

    [Fact]
    public void Violation_message_lists_rule_element_wcag_and_fix()
    {
        var ex = Assert.Throws<AccessibilityAssertionException>(() => TestData.Audit(findings: TestData.Finding()).ShouldHaveNoViolations());
        Assert.Contains("button-name", ex.Message, StringComparison.Ordinal);
        Assert.Contains("button.icon", ex.Message, StringComparison.Ordinal);
        Assert.Contains("4.1.2", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Fix:", ex.Message, StringComparison.Ordinal);
        Assert.Single(ex.Findings);
    }

    [Fact]
    public void Potential_and_inconclusive_findings_only_fail_when_requested()
    {
        var audit = TestData.Audit(findings: [TestData.Finding(status: FindingStatus.PotentialIssue), TestData.Finding(ruleId: "color-contrast", status: FindingStatus.Inconclusive)]);
        audit.ShouldHaveNoViolations();
        Assert.Throws<AccessibilityAssertionException>(() => audit.ShouldHaveNoViolations(AccessibilityExpectation.Strict));
        Assert.Throws<AccessibilityAssertionException>(() => audit.ShouldBeConclusive());
    }

    [Fact]
    public void Minimum_impact_filters_violations_but_unknown_impact_fails()
    {
        var audit = TestData.Audit(findings: TestData.Finding(impact: AccessibilityImpact.Minor));
        audit.ShouldHaveNoViolations(new AccessibilityExpectation { MinimumImpact = AccessibilityImpact.Serious });
        var unknown = TestData.Audit(findings: TestData.Finding(impact: null));
        Assert.Throws<AccessibilityAssertionException>(() => unknown.ShouldHaveNoViolations(new AccessibilityExpectation { MinimumImpact = AccessibilityImpact.Critical }));
    }

    [Fact]
    public void Regression_assertions_find_expected_defects()
    {
        var audit = TestData.Audit(findings: TestData.Finding());
        Assert.Equal("button-name", audit.ShouldHaveFinding("button-name").RuleId);
        Assert.Throws<AccessibilityAssertionException>(() => audit.ShouldHaveFinding("image-alt"));
        Assert.Throws<AccessibilityAssertionException>(() => audit.ShouldNotHaveFinding("button-name"));
    }

    [Fact]
    public void Options_disable_page_level_rules_for_components_only()
    {
        var options = new AccessibilityAuditOptions { DisabledRules = ["color-contrast"], IncludeBestPractices = true };
        Assert.Contains("region", options.EffectiveDisabledRules(AuditTargetKind.Component));
        Assert.DoesNotContain("region", options.EffectiveDisabledRules(AuditTargetKind.Page));
        Assert.Contains("best-practice", options.EffectiveTags());
        var keep = options with { EnabledRules = ["region"] };
        Assert.DoesNotContain("region", keep.EffectiveDisabledRules(AuditTargetKind.Component));
    }
}
