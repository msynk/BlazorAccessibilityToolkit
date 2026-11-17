using BlazorAccessibilityToolkit.Cli;
using BlazorAccessibilityToolkit.ManualReview;
using BlazorAccessibilityToolkit.Reporting;

namespace BlazorAccessibilityToolkit.Tests;

public sealed class ConformanceExportTests
{
    private static AccessibilityReport Report()
    {
        var checklist = new ManualReviewChecklist
        {
            Name = "Checkout",
            Items =
            [
                new ManualReviewItem { Id = "contrast", Title = "Contrast", Wcag = ["1.4.3"], Status = ManualReviewStatus.Passed, Tester = "A. Tester", Date = new DateOnly(2026, 11, 1) },
                new ManualReviewItem { Id = "media", Title = "Media", Wcag = ["1.2.2"], Status = ManualReviewStatus.NotApplicable, Justification = "No media" },
            ],
        };
        return AccessibilityReport.Create([TestData.Audit(findings: TestData.Finding())], [checklist], timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 11, 17, 8, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void OpenAcr_draft_maps_coverage_to_adherence_levels()
    {
        var yaml = new OpenAcrReportFormatter { Info = new ConformanceReportInfo { ProductName = "Contoso \"Shop\"", ProductVersion = "3.1", ContactEmail = "a11y@contoso.example" } }.Format(Report());
        var lines = yaml.ReplaceLineEndings("\n").Split('\n');

        Assert.Contains("title: \"Contoso \\\"Shop\\\" Accessibility Conformance Report\"", lines);
        Assert.Contains("catalog: \"2.5-edition-wcag-2.2-en\"", lines);
        Assert.Contains("report_date: \"2026-11-17\"", lines);
        Assert.Contains("  email: \"a11y@contoso.example\"", lines);
        Assert.Contains("  success_criteria_level_a:", lines);
        Assert.Contains("  success_criteria_level_aa:", lines);
        Assert.DoesNotContain("  success_criteria_level_aaa:", lines);

        string LevelOf(string criterion)
        {
            var index = Array.IndexOf(lines, $"      - num: \"{criterion}\"");
            Assert.True(index >= 0, criterion);
            return lines[index + 4].Trim();
        }

        Assert.Equal("level: \"does-not-support\"", LevelOf("4.1.2"));
        Assert.Equal("level: \"supports\"", LevelOf("1.4.3"));
        Assert.Equal("level: \"not-applicable\"", LevelOf("1.2.2"));
        Assert.Equal("level: \"not-evaluated\"", LevelOf("2.4.2"));
        Assert.Contains("DRAFT: not evaluated", yaml, StringComparison.Ordinal);
        Assert.Contains("rules: button-name", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("num: \"4.1.1\"", yaml, StringComparison.Ordinal);
        Assert.Contains("2.5.8", yaml, StringComparison.Ordinal);
        Assert.Contains("axe-core 4.14.0", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Statement_draft_never_claims_full_compliance()
    {
        var statement = new AccessibilityStatementFormatter { Info = new ConformanceReportInfo { ProductName = "Contoso Shop", Organization = "Contoso" } }.Format(Report());

        Assert.StartsWith("# Accessibility statement for Contoso Shop", statement, StringComparison.Ordinal);
        Assert.Contains("**partially compliant**", statement, StringComparison.Ordinal);
        Assert.Contains("**4.1.2 Name, Role, Value** (level A, EN 301 549 9.4.1.2)", statement, StringComparison.Ordinal);
        Assert.Contains("TODO email", statement, StringComparison.Ordinal);
        Assert.Contains("## Enforcement procedure", statement, StringComparison.Ordinal);

        var clean = AccessibilityReport.Create([TestData.Audit()], []);
        var draft = new AccessibilityStatementFormatter().Format(clean);
        Assert.Contains("TODO: state whether", draft, StringComparison.Ordinal);
        Assert.DoesNotContain("fully compliant**", draft, StringComparison.Ordinal);
    }

    [Fact]
    public void Cli_writes_conformance_drafts_only_on_request()
    {
        var root = Path.Combine(Path.GetTempPath(), "a11y-acr-" + Guid.NewGuid().ToString("N"));
        try
        {
            new AccessibilityResultStore(Path.Combine(root, "results")).Save(TestData.Audit());
            var output = Path.Combine(root, "report");
            using var writer = new StringWriter();
            Assert.Equal(0, CliApp.Run(["report", "--input", Path.Combine(root, "results"), "--output", output], writer, writer));
            Assert.False(File.Exists(Path.Combine(output, "accessibility-conformance-report.openacr.yaml")));

            Assert.Equal(0, CliApp.Run(["report", "--input", Path.Combine(root, "results"), "--output", output, "--format", "openacr,statement", "--product", "Contoso Shop", "--level", "AAA"], writer, writer));
            var yaml = File.ReadAllText(Path.Combine(output, "accessibility-conformance-report.openacr.yaml"));
            Assert.Contains("name: \"Contoso Shop\"", yaml, StringComparison.Ordinal);
            Assert.Contains("success_criteria_level_aaa:", yaml, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(output, "accessibility-statement.md")));

            Assert.Equal(QualityGateResult.InvalidUsage, CliApp.Run(["report", "--input", Path.Combine(root, "results"), "--format", "openacr", "--level", "AAAA"], writer, writer));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
