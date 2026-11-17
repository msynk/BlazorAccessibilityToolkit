using BlazorAccessibilityToolkit.Cli;
using BlazorAccessibilityToolkit.ManualReview;
using BlazorAccessibilityToolkit.Reporting;
using BlazorAccessibilityToolkit.Rules;

namespace BlazorAccessibilityToolkit.Tests;

public sealed class CoverageAndStandardsTests
{
    [Fact]
    public void Standard_presets_select_the_documented_axe_tags()
    {
        Assert.Equal(AccessibilityStandard.Wcag21A, AccessibilityStandard.Wcag22A);
        Assert.Contains("wcag2aaa", AccessibilityStandard.Wcag22AAA);
        Assert.All(AccessibilityStandard.Wcag22AA, t => Assert.Contains(t, AccessibilityStandard.Wcag22AAA));
        Assert.Equal(["EN-301-549"], AccessibilityStandard.En301549);
        Assert.Contains("section508", AccessibilityStandard.Section508);
        Assert.Equal(["RGAAv4"], AccessibilityStandard.Rgaa);
        Assert.Equal(["TTv5"], AccessibilityStandard.TrustedTester);
    }

    [Fact]
    public void En_301_549_clauses_follow_wcag_2_1_a_and_aa()
    {
        Assert.Equal("9.1.4.3", WcagCatalog.Get("1.4.3").En301549Clause);
        Assert.Equal("9.4.1.3", WcagCatalog.Get("4.1.3").En301549Clause);
        Assert.Null(WcagCatalog.Get("2.4.11").En301549Clause);
        Assert.Null(WcagCatalog.Get("1.4.6").En301549Clause);
    }

    [Fact]
    public void Coverage_never_reports_support_without_a_manual_review()
    {
        var potential = TestData.Finding(ruleId: "blazor-input-purpose-missing", status: FindingStatus.PotentialIssue) with { Wcag = [WcagCatalog.Get("1.3.5")] };
        var checklist = new ManualReviewChecklist
        {
            Name = "Checkout",
            Items =
            [
                new ManualReviewItem { Id = "contrast", Title = "Contrast", Wcag = ["1.4.3"], Status = ManualReviewStatus.Passed, Tester = "A. Tester", Date = new DateOnly(2026, 11, 1) },
                new ManualReviewItem { Id = "media", Title = "Media", Wcag = ["1.2.2"], Status = ManualReviewStatus.NotApplicable, Justification = "No media" },
                new ManualReviewItem { Id = "keyboard", Title = "Keyboard", Wcag = ["2.1.1"], Status = ManualReviewStatus.Passed },
                new ManualReviewItem { Id = "errors", Title = "Errors", Wcag = ["3.3.1"], Status = ManualReviewStatus.Failed, Notes = "Errors not announced" },
            ],
        };
        var report = AccessibilityReport.Create([TestData.Audit(findings: [TestData.Finding(), potential])], [checklist]);

        var rows = WcagCoverage.Create(report).ToDictionary(r => r.Criterion.Id);
        Assert.Equal(CriterionConformance.DoesNotSupport, rows["4.1.2"].Conformance);
        Assert.Equal(["button-name"], rows["4.1.2"].Rules);
        Assert.Equal(CriterionConformance.NeedsReview, rows["1.3.5"].Conformance);
        Assert.Equal(CriterionConformance.Supports, rows["1.4.3"].Conformance);
        Assert.Equal(CriterionConformance.NotApplicable, rows["1.2.2"].Conformance);
        Assert.Equal(CriterionConformance.NeedsReview, rows["2.1.1"].Conformance); // a pass without tester and date does not count
        Assert.Equal(CriterionConformance.DoesNotSupport, rows["3.3.1"].Conformance);
        Assert.Equal(CriterionConformance.NotEvaluated, rows["2.4.2"].Conformance);
        Assert.DoesNotContain("4.1.1", rows.Keys);
        Assert.DoesNotContain("1.4.6", rows.Keys);
        Assert.Contains("1.4.6", WcagCoverage.Create(report, WcagLevel.AAA).Select(r => r.Criterion.Id));
    }

    [Fact]
    public void Markdown_report_summarizes_failures_and_coverage_safely()
    {
        var hostile = TestData.Finding(selector: "td | <script>") with { Help = "Pipes | and <b>tags</b>" };
        var report = AccessibilityReport.Create([TestData.Audit(name: "Checkout <page>", findings: [hostile]), TestData.FailedAudit()]);
        var markdown = new MarkdownReportFormatter().Format(report);

        Assert.StartsWith("## ❌ Accessibility report", markdown, StringComparison.Ordinal);
        Assert.Contains("| `button-name` | 1 |", markdown, StringComparison.Ordinal);
        Assert.Contains("Checkout &lt;page&gt;", markdown, StringComparison.Ordinal);
        Assert.Contains("`td \\| <script>`", markdown, StringComparison.Ordinal);
        Assert.Contains("Pipes \\| and &lt;b>tags&lt;/b>", markdown, StringComparison.Ordinal);
        Assert.Contains("**Execution error**", markdown, StringComparison.Ordinal);
        Assert.Contains("| [4.1.2 Name, Role, Value](https://www.w3.org/WAI/WCAG22/Understanding/name-role-value.html) | A | 9.4.1.2 | Does not support | 1 violation(s) |", markdown, StringComparison.Ordinal);
        Assert.Contains("cannot prove WCAG conformance", markdown, StringComparison.Ordinal);
        Assert.Same(ReportFormatters.Find("markdown")!.GetType(), typeof(MarkdownReportFormatter));
    }

    [Fact]
    public void Html_report_includes_the_coverage_table()
    {
        var html = new HtmlReportFormatter().Format(AccessibilityReport.Create([TestData.Audit(findings: TestData.Finding())]));
        Assert.Contains("<h2>WCAG 2.2 coverage (A and AA)</h2>", html, StringComparison.Ordinal);
        Assert.Contains("<td>9.4.1.2</td><td class=\"critical\">Does not support</td>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Wcag_template_has_one_valid_item_per_a_and_aa_criterion()
    {
        var checklist = ManualReviewTemplates.Wcag22AA("Product");
        var expected = WcagCatalog.All.Where(c => c.Level <= WcagLevel.AA && !c.IsObsolete).Select(c => c.Id).ToArray();
        Assert.Equal(expected, checklist.Items.SelectMany(i => i.Wcag));
        Assert.Empty(ManualReviewEvaluator.Validate(checklist));
        Assert.All(checklist.Items, i => Assert.False(string.IsNullOrWhiteSpace(i.Guidance)));
        Assert.DoesNotContain(checklist.Items, i => i.Guidance!.StartsWith("Review the criterion against", StringComparison.Ordinal));
    }

    [Fact]
    public void Standard_template_covers_the_new_wcag_2_2_criteria()
    {
        var covered = ManualReviewTemplates.Standard("x").Items.SelectMany(i => i.Wcag).ToHashSet();
        Assert.Superset(new HashSet<string> { "2.4.11", "2.5.7", "2.5.8", "3.2.6", "3.3.7", "3.3.8" }, covered);
    }

    [Fact]
    public void Every_toolkit_rule_has_valid_metadata()
    {
        Assert.Equal(ToolkitRules.All.Count, ToolkitRules.All.Select(r => r.Id).Distinct().Count());
        Assert.All(ToolkitRules.All, r =>
        {
            Assert.StartsWith("blazor-", r.Id, StringComparison.Ordinal);
            Assert.NotEmpty(r.Environments);
            Assert.All(r.Wcag, id => Assert.NotNull(WcagCatalog.Find(id)));
            Assert.Same(r, ToolkitRules.Find(r.Id));
        });
    }

    [Fact]
    public void Cli_lists_rules_initializes_wcag_checklists_and_writes_job_summaries()
    {
        var root = Path.Combine(Path.GetTempPath(), "a11y-cov-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var previous = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
        try
        {
            using (var output = new StringWriter())
            {
                Assert.Equal(0, CliApp.Run(["rules"], output, TextWriter.Null));
                Assert.Contains("blazor-focus-obscured", output.ToString(), StringComparison.Ordinal);
            }

            var checklistPath = Path.Combine(root, "wcag.json");
            Assert.Equal(0, CliApp.Run(["manual", "init", "--name", "Site", "--template", "wcag22aa", "--output", checklistPath], TextWriter.Null, TextWriter.Null));
            Assert.Contains(ManualReviewEvaluator.Load(checklistPath).Items, i => i.Id == "wcag-3.3.8");
            Assert.Equal(QualityGateResult.InvalidUsage, CliApp.Run(["manual", "init", "--name", "Site", "--template", "nope", "--output", Path.Combine(root, "x.json")], TextWriter.Null, TextWriter.Null));

            new AccessibilityResultStore(Path.Combine(root, "results")).Save(TestData.Audit());
            var summary = Path.Combine(root, "summary.md");
            Environment.SetEnvironmentVariable("GITHUB_STEP_SUMMARY", summary);
            Assert.Equal(0, CliApp.Run(["report", "--input", Path.Combine(root, "results"), "--output", Path.Combine(root, "report"), "--format", "markdown", "--step-summary"], TextWriter.Null, TextWriter.Null));
            Assert.StartsWith("## ✅ Accessibility report", File.ReadAllText(summary), StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(root, "report", "accessibility-report.md")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_STEP_SUMMARY", previous);
            Directory.Delete(root, recursive: true);
        }
    }
}
