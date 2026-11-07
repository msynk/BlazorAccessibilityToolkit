using System.Text.Json;
using System.Xml.Linq;
using BlazorAccessibilityToolkit.Baselines;
using BlazorAccessibilityToolkit.ManualReview;
using BlazorAccessibilityToolkit.Reporting;

namespace BlazorAccessibilityToolkit.Tests;

public sealed class BaselineAndGateTests
{
    [Fact]
    public void Baseline_marks_known_findings_existing_and_new_ones_new()
    {
        var before = TestData.Audit(findings: TestData.Finding());
        var baseline = AccessibilityBaseline.FromAudits([before]);

        var after = TestData.Audit(findings: [TestData.Finding(), TestData.Finding(ruleId: "image-alt", selector: "img")]);
        var comparison = BaselineComparison.Compare(baseline, [after]);

        Assert.Single(comparison.Existing);
        var added = Assert.Single(comparison.New);
        Assert.Equal("image-alt", added.RuleId);
        Assert.Empty(comparison.Resolved);
    }

    [Fact]
    public void Baseline_is_count_aware_so_extra_occurrences_are_new()
    {
        var baseline = AccessibilityBaseline.FromAudits([TestData.Audit(findings: TestData.Finding(selector: "#x-1111"))]);
        Assert.Equal(1, baseline.Entries.Single().Count);

        // Same fingerprint (numbers normalized), but twice as many occurrences.
        var after = TestData.Audit(findings: [TestData.Finding(selector: "#x-1111"), TestData.Finding(selector: "#x-2222")]);
        var comparison = BaselineComparison.Compare(baseline, [after]);
        Assert.Single(comparison.New);
        Assert.Single(comparison.Existing);
    }

    [Fact]
    public void Resolved_entries_are_reported_and_marked_unreliable_when_audits_failed()
    {
        var baseline = AccessibilityBaseline.FromAudits([TestData.Audit(findings: TestData.Finding())]);
        var clean = BaselineComparison.Compare(baseline, [TestData.Audit()]);
        Assert.Single(clean.Resolved);
        Assert.True(clean.ResolvedIsReliable);

        var broken = BaselineComparison.Compare(baseline, [TestData.Audit(), TestData.FailedAudit()]);
        Assert.False(broken.ResolvedIsReliable);
    }

    [Fact]
    public void Baseline_round_trips_deterministically()
    {
        var path = Path.Combine(Path.GetTempPath(), $"baseline-{Guid.NewGuid():N}.json");
        try
        {
            var audits = new[] { TestData.Audit(findings: [TestData.Finding(ruleId: "b"), TestData.Finding(ruleId: "a")]) };
            AccessibilityBaseline.FromAudits(audits).Save(path);
            var first = File.ReadAllText(path);
            AccessibilityBaseline.Load(path).Save(path);
            Assert.Equal(first, File.ReadAllText(path));
            Assert.DoesNotContain("startedAt", first, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Gate_fails_on_violations_and_passes_clean_reports()
    {
        Assert.Equal(QualityGateResult.FindingsFailed, QualityGate.Evaluate(AccessibilityReport.Create([TestData.Audit(findings: TestData.Finding())])).ExitCode);
        Assert.Equal(QualityGateResult.Success, QualityGate.Evaluate(AccessibilityReport.Create([TestData.Audit()])).ExitCode);
    }

    [Fact]
    public void Gate_reports_execution_errors_and_empty_runs_as_infrastructure_failures()
    {
        var failed = QualityGate.Evaluate(AccessibilityReport.Create([TestData.Audit(), TestData.FailedAudit()]));
        Assert.Equal(QualityGateResult.ExecutionFailed, failed.ExitCode);

        var empty = QualityGate.Evaluate(AccessibilityReport.Create([]));
        Assert.Equal(QualityGateResult.ExecutionFailed, empty.ExitCode);
        Assert.Equal(QualityGateResult.Success, QualityGate.Evaluate(AccessibilityReport.Create([]), new QualityGateOptions { AllowEmpty = true }).ExitCode);

        var loadErrors = QualityGate.Evaluate(AccessibilityReport.Create([TestData.Audit()]), loadErrors: ["bad file"]);
        Assert.Equal(QualityGateResult.ExecutionFailed, loadErrors.ExitCode);
    }

    [Fact]
    public void Gate_with_baseline_only_fails_on_new_findings_unless_strict()
    {
        var baseline = AccessibilityBaseline.FromAudits([TestData.Audit(findings: TestData.Finding())]);
        var sameReport = AccessibilityReport.Create([TestData.Audit(findings: TestData.Finding())], baseline: baseline);
        Assert.True(QualityGate.Evaluate(sameReport).Passed);
        Assert.False(QualityGate.Evaluate(sameReport, new QualityGateOptions { Strict = true }).Passed);

        var newReport = AccessibilityReport.Create([TestData.Audit(findings: [TestData.Finding(), TestData.Finding(ruleId: "label", selector: "input")])], baseline: baseline);
        var result = QualityGate.Evaluate(newReport);
        Assert.False(result.Passed);
        Assert.Equal("label", Assert.Single(result.FailingFindings).RuleId);
    }

    [Fact]
    public void Strict_gate_fails_on_stale_baseline_entries()
    {
        var baseline = AccessibilityBaseline.FromAudits([TestData.Audit(findings: TestData.Finding())]);
        var fixedReport = AccessibilityReport.Create([TestData.Audit()], baseline: baseline);
        Assert.True(QualityGate.Evaluate(fixedReport).Passed);
        Assert.False(QualityGate.Evaluate(fixedReport, new QualityGateOptions { Strict = true }).Passed);
    }

    [Fact]
    public void Gate_thresholds_and_review_statuses()
    {
        var report = AccessibilityReport.Create([TestData.Audit(findings: [TestData.Finding(impact: AccessibilityImpact.Moderate), TestData.Finding(ruleId: "c", status: FindingStatus.Inconclusive)])]);
        Assert.True(QualityGate.Evaluate(report, new QualityGateOptions { FailOnImpact = AccessibilityImpact.Serious }).Passed);
        Assert.False(QualityGate.Evaluate(report, new QualityGateOptions { FailOnImpact = AccessibilityImpact.Serious, FailOnInconclusive = true }).Passed);
    }

    [Fact]
    public void Gate_fails_on_failed_manual_checks_and_optionally_on_outstanding_ones()
    {
        var checklist = ManualReviewTemplates.Standard("Dialog") with
        {
            Items = [new ManualReviewItem { Id = "a", Title = "A", Status = ManualReviewStatus.Failed, Notes = "Focus lost" }, new ManualReviewItem { Id = "b", Title = "B" }],
        };
        var report = AccessibilityReport.Create([TestData.Audit()], [checklist]);
        Assert.False(QualityGate.Evaluate(report).Passed);

        var outstanding = AccessibilityReport.Create([TestData.Audit()], [checklist with { Items = [checklist.Items[1]] }]);
        Assert.True(QualityGate.Evaluate(outstanding).Passed);
        Assert.False(QualityGate.Evaluate(outstanding, new QualityGateOptions { FailOnOutstandingManualReview = true }).Passed);
    }
}

public sealed class FormatterTests
{
    private static AccessibilityReport SampleReport()
    {
        var options = new AccessibilityAuditOptions { Suppressions = [new Suppression { RuleId = "color-contrast", Reason = "Brand colors <pending>" }] };
        return AccessibilityReport.Create(
            [
                TestData.Audit(options: options, findings: [TestData.Finding(html: "<button class=\"icon\"><script>alert(1)</script></button>"), TestData.Finding(ruleId: "color-contrast", selector: "p"), TestData.Finding(ruleId: "aria-hidden", status: FindingStatus.Inconclusive)]),
                TestData.FailedAudit(),
            ],
            [ManualReviewTemplates.Standard("Icon button", "Sample.IconButton")]);
    }

    [Fact]
    public void Json_report_round_trips_with_schema_version()
    {
        var json = new JsonReportFormatter().Format(SampleReport());
        using var document = JsonDocument.Parse(json);
        Assert.Equal("1.0", document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("Violation", document.RootElement.GetProperty("audits")[1].GetProperty("findings")[0].GetProperty("status").GetString());
        var path = Path.Combine(Path.GetTempPath(), $"report-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, json);
            var loaded = AccessibilityReport.Load(path);
            Assert.Equal(2, loaded.Audits.Count);
            Assert.Equal(SampleReport().Summary.Violations, loaded.Summary.Violations);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void JUnit_report_is_valid_xml_with_failures_errors_and_skips()
    {
        var xml = XDocument.Parse(new JUnitReportFormatter().Format(SampleReport()));
        var root = xml.Root!;
        Assert.Equal("testsuites", root.Name.LocalName);
        Assert.Equal("1", root.Attribute("failures")!.Value);
        Assert.Equal("1", root.Attribute("errors")!.Value);
        Assert.Contains(root.Descendants("skipped"), s => s.Attribute("message")!.Value.StartsWith("Suppressed", StringComparison.Ordinal));
        Assert.Contains(root.Descendants("skipped"), s => s.Attribute("message")!.Value.Contains("not a pass", StringComparison.Ordinal));
        Assert.Contains(root.Descendants("testcase"), t => t.Attribute("name")!.Value == "image-alt");
    }

    [Fact]
    public void Sarif_report_has_rules_results_suppressions_and_execution_notifications()
    {
        var formatter = new SarifReportFormatter { SourceResolver = type => type == "Sample.IconButton" ? "samples/IconButton.razor" : null };
        using var document = JsonDocument.Parse(formatter.Format(SampleReport()));
        var run = document.RootElement.GetProperty("runs")[0];
        Assert.Equal("2.1.0", document.RootElement.GetProperty("version").GetString());
        Assert.Equal(3, run.GetProperty("tool").GetProperty("driver").GetProperty("rules").GetArrayLength());
        var results = run.GetProperty("results").EnumerateArray().ToArray();
        Assert.Equal(3, results.Length);
        Assert.Contains(results, r => r.TryGetProperty("suppressions", out _));
        Assert.Contains(results, r => r.GetProperty("level").GetString() == "error");
        Assert.All(results, r => Assert.True(r.GetProperty("partialFingerprints").EnumerateObject().Any()));
        var physical = results[0].GetProperty("locations")[0].GetProperty("physicalLocation");
        Assert.Equal("samples/IconButton.razor", physical.GetProperty("artifactLocation").GetProperty("uri").GetString());
        Assert.Equal(1, physical.GetProperty("region").GetProperty("startLine").GetInt32());
        Assert.Equal("file", results[0].GetProperty("properties").GetProperty("locationPrecision").GetString());
        Assert.False(run.GetProperty("invocations")[0].GetProperty("executionSuccessful").GetBoolean());
    }

    [Fact]
    public void Sarif_fallback_uri_gives_unmapped_findings_a_file_location()
    {
        var page = TestData.Audit(context: new AccessibilityContext { Kind = AuditTargetKind.Page, Url = "http://localhost:5000/checkout" }, findings: TestData.Finding());
        var plain = JsonDocument.Parse(new SarifReportFormatter().Format(AccessibilityReport.Create([page])));
        var plainResult = plain.RootElement.GetProperty("runs")[0].GetProperty("results")[0];
        Assert.False(plainResult.GetProperty("locations")[0].TryGetProperty("physicalLocation", out _));
        Assert.Equal("logical", plainResult.GetProperty("properties").GetProperty("locationPrecision").GetString());

        var withFallback = JsonDocument.Parse(new SarifReportFormatter { FallbackUri = "tests/E2E/CheckoutTests.cs" }.Format(AccessibilityReport.Create([page])));
        var location = withFallback.RootElement.GetProperty("runs")[0].GetProperty("results")[0].GetProperty("locations")[0];
        Assert.Equal("tests/E2E/CheckoutTests.cs", location.GetProperty("physicalLocation").GetProperty("artifactLocation").GetProperty("uri").GetString());
        Assert.Equal("/checkout", location.GetProperty("logicalLocations")[0].GetProperty("fullyQualifiedName").GetString());
    }

    [Fact]
    public void Html_report_encodes_content_and_states_limitations()
    {
        var html = new HtmlReportFormatter().Format(SampleReport());
        Assert.DoesNotContain("<script>alert(1)</script>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("cannot prove WCAG conformance", html, StringComparison.Ordinal);
        Assert.Contains("<html lang=\"en\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Console_report_summarizes_counts()
    {
        var text = new ConsoleReportFormatter().Format(SampleReport());
        Assert.Contains("Violations: 1", text, StringComparison.Ordinal);
        Assert.Contains("1 failed", text, StringComparison.Ordinal);
        Assert.Contains("Manual review:", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Formatters_are_deterministic_except_for_generation_time()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var audits = SampleReport().Audits;
        var a = new SarifReportFormatter().Format(AccessibilityReport.Create(audits, timeProvider: clock));
        var b = new SarifReportFormatter().Format(AccessibilityReport.Create(audits.Reverse(), timeProvider: clock));
        Assert.Equal(a, b);
    }
}

public sealed class ResultStoreTests
{
    [Fact]
    public void Store_writes_one_file_per_audit_and_loader_reads_them_back()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"a11y-results-{Guid.NewGuid():N}");
        try
        {
            var store = new AccessibilityResultStore(directory);
            store.Save(TestData.Audit(name: "A / weird:name", findings: TestData.Finding()));
            store.Save(TestData.FailedAudit());
            File.WriteAllText(Path.Combine(directory, "corrupt" + AccessibilityResultStore.FileSuffix), "{ not json");

            var (audits, errors) = ReportLoader.LoadAudits([directory]);
            Assert.Equal(2, audits.Count);
            Assert.Single(errors);
            Assert.Contains(audits, a => a.Status == AuditStatus.Failed);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Loader_reports_missing_inputs() =>
        Assert.Single(ReportLoader.LoadAudits(["does-not-exist-" + Guid.NewGuid()]).Errors);
}

public sealed class ManualReviewTests
{
    [Fact]
    public void Incomplete_passes_and_unjustified_not_applicable_are_not_trusted()
    {
        Assert.Equal(ManualReviewStatus.RequiresReview, ManualReviewEvaluator.EffectiveStatus(new ManualReviewItem { Id = "a", Title = "A", Status = ManualReviewStatus.Passed }));
        Assert.Equal(ManualReviewStatus.Passed, ManualReviewEvaluator.EffectiveStatus(new ManualReviewItem { Id = "a", Title = "A", Status = ManualReviewStatus.Passed, Tester = "sam", Date = new DateOnly(2026, 11, 1) }));
        Assert.Equal(ManualReviewStatus.RequiresReview, ManualReviewEvaluator.EffectiveStatus(new ManualReviewItem { Id = "a", Title = "A", Status = ManualReviewStatus.NotApplicable }));
    }

    [Fact]
    public void Standard_template_is_valid_and_entirely_outstanding()
    {
        var checklist = ManualReviewTemplates.Standard("Settings dialog");
        Assert.Empty(ManualReviewEvaluator.Validate(checklist));
        var coverage = ManualReviewEvaluator.Coverage([checklist]);
        Assert.Equal(coverage.Total, coverage.NotTested);
        Assert.All(ManualReviewEvaluator.ToFindings(checklist), f => Assert.Equal(FindingStatus.ManualReviewRequired, f.Status));
    }

    [Fact]
    public void Validation_reports_incomplete_records()
    {
        var checklist = new ManualReviewChecklist
        {
            Name = "x",
            Items =
            [
                new ManualReviewItem { Id = "a", Title = "A", Status = ManualReviewStatus.Passed },
                new ManualReviewItem { Id = "a", Title = "dup", Status = ManualReviewStatus.NotApplicable, Wcag = ["9.9.9"] },
            ],
        };
        var issues = ManualReviewEvaluator.Validate(checklist);
        Assert.Contains(issues, i => i.Message.Contains("Duplicate", StringComparison.Ordinal));
        Assert.Contains(issues, i => i.Message.Contains("tester", StringComparison.Ordinal));
        Assert.Contains(issues, i => i.Message.Contains("justification", StringComparison.Ordinal));
        Assert.Contains(issues, i => i.Message.Contains("9.9.9", StringComparison.Ordinal));
    }

    [Fact]
    public void Failed_checks_become_violations_and_passed_checks_disappear()
    {
        var checklist = new ManualReviewChecklist
        {
            Name = "x",
            Items =
            [
                new ManualReviewItem { Id = "a", Title = "A", Status = ManualReviewStatus.Failed, Notes = "broken", Wcag = ["2.4.7"] },
                new ManualReviewItem { Id = "b", Title = "B", Status = ManualReviewStatus.Passed, Tester = "t", Date = new DateOnly(2026, 1, 1) },
            ],
        };
        var finding = Assert.Single(ManualReviewEvaluator.ToFindings(checklist));
        Assert.Equal(FindingStatus.Violation, finding.Status);
        Assert.Equal("2.4.7", finding.Wcag.Single().Id);
    }

    [Fact]
    public void Checklist_round_trips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"checklist-{Guid.NewGuid():N}.json");
        try
        {
            var checklist = ManualReviewTemplates.Standard("x") with { Items = [new ManualReviewItem { Id = "a", Title = "A", Status = ManualReviewStatus.Passed, Tester = "t", Date = new DateOnly(2026, 2, 3) }] };
            ManualReviewEvaluator.Save(checklist, path);
            var loaded = ManualReviewEvaluator.Load(path);
            Assert.Equal(new DateOnly(2026, 2, 3), loaded.Items[0].Date);
            Assert.Contains("\"Passed\"", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
