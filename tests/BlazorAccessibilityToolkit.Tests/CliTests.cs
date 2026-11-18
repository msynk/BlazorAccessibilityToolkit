using System.Text.Json;
using System.Xml.Linq;
using BlazorAccessibilityToolkit.Cli;
using BlazorAccessibilityToolkit.ManualReview;
using BlazorAccessibilityToolkit.Reporting;

namespace BlazorAccessibilityToolkit.Tests;

public sealed class CliTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "a11y-cli-" + Guid.NewGuid().ToString("N"));

    public CliTests() => Directory.CreateDirectory(_root);

    private string Results => Path.Combine(_root, "results");

    private string Output => Path.Combine(_root, "report");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private (int Code, string Out, string Err) Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = CliApp.Run(args, output, error);
        return (code, output.ToString(), error.ToString());
    }

    private void Save(params AccessibilityAudit[] audits)
    {
        var store = new AccessibilityResultStore(Results);
        foreach (var audit in audits)
        {
            store.Save(audit);
        }
    }

    [Fact]
    public void Report_writes_all_formats_and_fails_on_violations()
    {
        Save(TestData.Audit(findings: TestData.Finding()), TestData.Audit(name: "Clean"));
        var (code, output, error) = Run("report", "--input", Results, "--output", Output);

        Assert.Equal(QualityGateResult.FindingsFailed, code);
        Assert.Contains("Accessibility gate FAILED", output, StringComparison.Ordinal);
        Assert.Contains("button-name", error, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(Output, "accessibility-report.json")));
        Assert.True(File.Exists(Path.Combine(Output, "accessibility-report.html")));
        Assert.True(File.Exists(Path.Combine(Output, "accessibility.sarif")));
        XDocument.Load(Path.Combine(Output, "accessibility-junit.xml"));
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Output, "accessibility-report.json")));
        Assert.Equal(2, json.RootElement.GetProperty("summary").GetProperty("audits").GetInt32());
    }

    [Fact]
    public void Clean_results_pass()
    {
        Save(TestData.Audit(name: "Clean"));
        Assert.Equal(QualityGateResult.Success, Run("report", "--input", Results, "--output", Output, "--format", "json").Code);
    }

    [Fact]
    public void Missing_results_and_failed_audits_are_infrastructure_failures()
    {
        Assert.Equal(QualityGateResult.ExecutionFailed, Run("report", "--input", Path.Combine(_root, "nothing"), "--output", Output, "--format", "console").Code);

        Directory.CreateDirectory(Results);
        Assert.Equal(QualityGateResult.ExecutionFailed, Run("report", "--input", Results, "--output", Output, "--format", "console").Code);
        Assert.Equal(QualityGateResult.Success, Run("report", "--input", Results, "--output", Output, "--format", "console", "--allow-empty").Code);

        Save(TestData.Audit(name: "Clean"), TestData.FailedAudit());
        var (code, output, error) = Run("report", "--input", Results, "--output", Output, "--format", "console");
        Assert.Equal(QualityGateResult.ExecutionFailed, code);
        Assert.Contains("failed to execute", error, StringComparison.Ordinal);
        Assert.Contains("cannot be trusted", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Corrupt_result_files_fail_the_run()
    {
        Save(TestData.Audit(name: "Clean"));
        File.WriteAllText(Path.Combine(Results, "broken" + AccessibilityResultStore.FileSuffix), "{");
        Assert.Equal(QualityGateResult.ExecutionFailed, Run("report", "--input", Results, "--output", Output, "--format", "console").Code);
    }

    [Fact]
    public void Baseline_workflow_fails_only_on_new_findings()
    {
        var baseline = Path.Combine(_root, "a11y-baseline.json");
        Save(TestData.Audit(findings: TestData.Finding()));
        Assert.Equal(QualityGateResult.Success, Run("baseline", "--input", Results, "--baseline", baseline).Code);
        Assert.Equal(QualityGateResult.Success, Run("report", "--input", Results, "--baseline", baseline, "--output", Output, "--format", "console").Code);
        Assert.Equal(QualityGateResult.FindingsFailed, Run("report", "--input", Results, "--baseline", baseline, "--output", Output, "--format", "console", "--strict").Code);

        Save(TestData.Audit(name: "Other", findings: TestData.Finding(ruleId: "label", selector: "input")));
        var (code, _, error) = Run("report", "--input", Results, "--baseline", baseline, "--output", Output, "--format", "console");
        Assert.Equal(QualityGateResult.FindingsFailed, code);
        Assert.Contains("[new]", error, StringComparison.Ordinal);
        Assert.Contains("label", error, StringComparison.Ordinal);
        Assert.DoesNotContain("button-name", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Baseline_is_not_written_from_incomplete_results()
    {
        Save(TestData.Audit(findings: TestData.Finding()), TestData.FailedAudit());
        var baseline = Path.Combine(_root, "baseline.json");
        Assert.Equal(QualityGateResult.ExecutionFailed, Run("baseline", "--input", Results, "--baseline", baseline).Code);
        Assert.False(File.Exists(baseline));
        Assert.Equal(QualityGateResult.Success, Run("baseline", "--input", Results, "--baseline", baseline, "--allow-failed").Code);
    }

    [Fact]
    public void Thresholds_control_failures()
    {
        Save(TestData.Audit(findings: [TestData.Finding(impact: AccessibilityImpact.Moderate), TestData.Finding(ruleId: "x", status: FindingStatus.PotentialIssue)]));
        Assert.Equal(QualityGateResult.Success, Run("report", "--input", Results, "--output", Output, "--format", "console", "--fail-on", "serious").Code);
        Assert.Equal(QualityGateResult.FindingsFailed, Run("report", "--input", Results, "--output", Output, "--format", "console", "--fail-on", "serious", "--fail-on-potential").Code);
        Assert.Equal(QualityGateResult.Success, Run("report", "--input", Results, "--output", Output, "--format", "console", "--fail-on", "none").Code);
    }

    [Fact]
    public void Manual_review_init_validate_and_report()
    {
        var checklist = Path.Combine(_root, "manual", "dialog.json");
        Assert.Equal(QualityGateResult.Success, Run("manual", "init", "--name", "Dialog", "--scope", "Sample.Dialog", "--output", checklist).Code);
        Assert.Equal(QualityGateResult.InvalidUsage, Run("manual", "init", "--name", "Dialog", "--output", checklist).Code);
        Assert.Equal(QualityGateResult.Success, Run("manual", "validate", checklist).Code);

        var loaded = ManualReviewEvaluator.Load(checklist);
        ManualReviewEvaluator.Save(loaded with { Items = [loaded.Items[0] with { Status = ManualReviewStatus.Passed }] }, checklist);
        var (code, _, error) = Run("manual", "validate", checklist);
        Assert.Equal(QualityGateResult.FindingsFailed, code);
        Assert.Contains("without a tester", error, StringComparison.Ordinal);

        Save(TestData.Audit(name: "Clean"));
        Assert.Equal(QualityGateResult.Success, Run("report", "--input", Results, "--manual-review", checklist, "--output", Output, "--format", "html").Code);
        Assert.Equal(QualityGateResult.FindingsFailed, Run("report", "--input", Results, "--manual-review", checklist, "--output", Output, "--format", "html", "--fail-on-manual-review").Code);
        Assert.Contains("Manual review", File.ReadAllText(Path.Combine(Output, "accessibility-report.html")), StringComparison.Ordinal);
    }

    [Fact]
    public void Sarif_source_root_maps_components_to_razor_files()
    {
        var source = Path.Combine(_root, "src", "Components");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "IconButton.razor"), "<button></button>");
        Save(TestData.Audit(findings: TestData.Finding()));
        Run("report", "--input", Results, "--output", Output, "--format", "sarif", "--source-root", Path.Combine(_root, "src"));
        using var sarif = JsonDocument.Parse(File.ReadAllText(Path.Combine(Output, "accessibility.sarif")));
        var uri = sarif.RootElement.GetProperty("runs")[0].GetProperty("results")[0].GetProperty("locations")[0].GetProperty("physicalLocation").GetProperty("artifactLocation").GetProperty("uri").GetString();
        Assert.Equal("Components/IconButton.razor", uri);
    }

    [Theory]
    [InlineData("frobnicate")]
    [InlineData("report", "--bogus", "x")]
    [InlineData("report", "--fail-on", "extreme", "--input", ".")]
    [InlineData("report", "--input")]
    public void Usage_errors_return_exit_code_3(params string[] args) =>
        Assert.Equal(QualityGateResult.InvalidUsage, Run(args).Code);

    [Fact]
    public void Help_and_version()
    {
        Assert.Equal(QualityGateResult.Success, Run("--help").Code);
        Assert.Equal(QualityGateResult.Success, Run("--version").Code);
    }

    [Fact]
    public void Fix_applies_the_safe_fixes_and_keeps_the_byte_order_mark()
    {
        var pages = Directory.CreateDirectory(Path.Combine(_root, "src", "Pages")).FullName;
        var orders = Path.Combine(pages, "Orders.razor");
        File.WriteAllText(orders, "@page \"/orders\"\r\n<a href=\"#list\">Skip</a>\r\n<input id=\"q\" aria-labeledby=\"l\">\r\n<input id=\"p\" type=\"password\" autocomplete=\"off\">\r\n", new System.Text.UTF8Encoding(true));
        var generated = Directory.CreateDirectory(Path.Combine(_root, "src", "obj")).FullName;
        File.WriteAllText(Path.Combine(generated, "Copy.razor"), "<input id=\"q\" aria-labeledby=\"l\">");

        var (code, output, _) = Run("fix", Path.Combine(_root, "src"));

        Assert.Equal(QualityGateResult.Success, code);
        var bytes = File.ReadAllBytes(orders);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.Equal("@page \"/orders\"\r\n<a href=\"/orders#list\">Skip</a>\r\n<input id=\"q\" aria-labelledby=\"l\">\r\n<input id=\"p\" type=\"password\" autocomplete=\"off\">\r\n", System.Text.Encoding.UTF8.GetString(bytes[3..]));
        Assert.Contains("Orders.razor(3,15): BA11Y004: Rename to 'aria-labelledby'", output, StringComparison.Ordinal);
        Assert.Contains("BA11Y033: not changed, choose one: Change to autocomplete=\"current-password\" or Change to autocomplete=\"new-password\"", output, StringComparison.Ordinal);
        Assert.Contains("Fixed 2 finding(s) in 1 of 1 file(s). 1 finding(s) offer a choice of fixes and were left for you.", output, StringComparison.Ordinal);
        Assert.Equal("<input id=\"q\" aria-labeledby=\"l\">", File.ReadAllText(Path.Combine(generated, "Copy.razor")));
    }

    [Fact]
    public void Fix_dry_run_lists_without_changing_and_fails_when_something_is_fixable()
    {
        var file = Path.Combine(_root, "Menu.razor");
        const string markup = "<button role=\"button\" aria-labeledby=\"t\">Menu</button>";
        File.WriteAllText(file, markup);

        var (code, output, _) = Run("fix", file, "--rule", "BA11Y023", "--dry-run");

        Assert.Equal(QualityGateResult.FindingsFailed, code);
        Assert.Equal(markup, File.ReadAllText(file));
        Assert.Contains("BA11Y023: Remove role=\"button\"", output, StringComparison.Ordinal);
        Assert.DoesNotContain("BA11Y004", output, StringComparison.Ordinal);
        Assert.Contains("Would fix 1 finding(s) in 1 of 1 file(s).", output, StringComparison.Ordinal);

        Assert.Equal(QualityGateResult.Success, Run("fix", file, "--rule", "BA11Y003", "--dry-run").Code);
    }

    [Fact]
    public void Fix_rejects_rules_without_a_fix_and_missing_paths()
    {
        var (code, _, error) = Run("fix", _root, "--rule", "BA11Y002");
        Assert.Equal(QualityGateResult.InvalidUsage, code);
        Assert.Contains("BA11Y002 has no automatic fix", error, StringComparison.Ordinal);

        Assert.Equal(QualityGateResult.InvalidUsage, Run("fix", Path.Combine(_root, "missing")).Code);
    }

    [Fact]
    public void Fix_rejects_a_file_that_is_not_razor()
    {
        var file = Path.Combine(_root, "Page.cs");
        File.WriteAllText(file, "var html = \"<button role=\\\"button\\\">Save</button>\";");

        var (code, _, error) = Run("fix", file);

        Assert.Equal(QualityGateResult.InvalidUsage, code);
        Assert.Contains("is not a .razor or .cshtml file", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Fix_skips_files_it_cannot_write_back_unchanged_and_keeps_utf16()
    {
        var latin1 = Path.Combine(_root, "Legacy.razor");
        byte[] cp1252 = [.. System.Text.Encoding.ASCII.GetBytes("<p>Caf"), 0xE9, .. System.Text.Encoding.ASCII.GetBytes("</p><button role=\"button\">Save</button>")];
        File.WriteAllBytes(latin1, cp1252);
        var utf16 = Path.Combine(_root, "Wide.razor");
        File.WriteAllText(utf16, "<p>Café</p><button role=\"button\">Save</button>", new System.Text.UnicodeEncoding(false, true));

        var (code, output, error) = Run("fix", _root);

        Assert.Equal(QualityGateResult.Success, code);
        Assert.Equal(cp1252, File.ReadAllBytes(latin1));
        Assert.Contains("Legacy.razor: skipped", error, StringComparison.Ordinal);
        var bytes = File.ReadAllBytes(utf16);
        Assert.Equal([0xFF, 0xFE], bytes[..2]);
        Assert.Equal("<p>Café</p><button>Save</button>", System.Text.Encoding.Unicode.GetString(bytes[2..]));
        Assert.Contains("Fixed 1 finding(s) in 1 of 2 file(s). 1 file(s) skipped.", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Fix_follows_editorconfig_and_skips_hidden_and_build_folders()
    {
        File.WriteAllText(Path.Combine(_root, ".editorconfig"), "root = true\n\n[*.razor]\ndotnet_diagnostic.BA11Y023.severity = none\n\n[Legacy/**.razor]\nblazor_a11y_analyzer.enabled = false\n");
        const string markup = "<button role=\"button\" aria-labeledby=\"t\">Menu</button>";
        foreach (var folder in new[] { "Pages", "Legacy", ".git", "node_modules" })
        {
            File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(_root, folder)).FullName, "Menu.razor"), markup);
        }

        var (code, output, _) = Run("fix", _root);

        Assert.Equal(QualityGateResult.Success, code);
        Assert.Equal("<button role=\"button\" aria-labelledby=\"t\">Menu</button>", File.ReadAllText(Path.Combine(_root, "Pages", "Menu.razor")));
        Assert.Equal(markup, File.ReadAllText(Path.Combine(_root, "Legacy", "Menu.razor")));
        Assert.Equal(markup, File.ReadAllText(Path.Combine(_root, ".git", "Menu.razor")));
        Assert.Equal(markup, File.ReadAllText(Path.Combine(_root, "node_modules", "Menu.razor")));
        Assert.Contains("Fixed 1 finding(s) in 1 of 2 file(s).", output, StringComparison.Ordinal);
    }
}
