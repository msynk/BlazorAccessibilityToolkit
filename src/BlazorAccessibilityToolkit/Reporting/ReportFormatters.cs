using System.Globalization;
using System.Text;
using System.Text.Json;

namespace BlazorAccessibilityToolkit.Reporting;

/// <summary>Writes a report in a specific format.</summary>
public interface IReportFormatter
{
    /// <summary>Format name used on the command line (for example <c>sarif</c>).</summary>
    string Name { get; }

    /// <summary>Default file name for the format.</summary>
    string DefaultFileName { get; }

    /// <summary>Writes the report.</summary>
    void Write(AccessibilityReport report, TextWriter writer);
}

/// <summary>Registry of built-in report formatters.</summary>
public static class ReportFormatters
{
    /// <summary>Built-in formatters: console, json, junit, sarif, html.</summary>
    public static IReadOnlyList<IReportFormatter> All { get; } =
    [
        new ConsoleReportFormatter(),
        new JsonReportFormatter(),
        new JUnitReportFormatter(),
        new SarifReportFormatter(),
        new HtmlReportFormatter(),
    ];

    /// <summary>Finds a formatter by name (case-insensitive).</summary>
    public static IReportFormatter? Find(string name) =>
        All.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Formats the report as a string.</summary>
    public static string Format(this IReportFormatter formatter, AccessibilityReport report)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        formatter.Write(report, writer);
        return writer.ToString();
    }

    /// <summary>Writes the report to a file (UTF-8 without BOM), creating the directory.</summary>
    public static void WriteToFile(this IReportFormatter formatter, AccessibilityReport report, string path)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        using var writer = new StreamWriter(path, append: false, new UTF8Encoding(false));
        formatter.Write(report, writer);
    }
}

/// <summary>JSON report with a versioned schema (<see cref="AccessibilityReport.SchemaVersion"/>).</summary>
public sealed class JsonReportFormatter : IReportFormatter
{
    /// <inheritdoc />
    public string Name => "json";

    /// <inheritdoc />
    public string DefaultFileName => "accessibility-report.json";

    /// <inheritdoc />
    public void Write(AccessibilityReport report, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(JsonSerializer.Serialize(report, ReportJson.Options));
        writer.Write('\n');
    }
}

/// <summary>Human readable console summary.</summary>
public sealed class ConsoleReportFormatter : IReportFormatter
{
    /// <inheritdoc />
    public string Name => "console";

    /// <inheritdoc />
    public string DefaultFileName => "accessibility-report.txt";

    /// <summary>Maximum findings printed per audit.</summary>
    public int MaxFindingsPerAudit { get; init; } = 20;

    /// <inheritdoc />
    public void Write(AccessibilityReport report, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(writer);
        var s = report.Summary;
        writer.WriteLine($"{report.Tool.Name} {report.Tool.Version} — accessibility report");
        writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Audits: {s.Audits} ({s.CompletedAudits} completed, {s.FailedAudits} failed, {s.SkippedAudits} skipped)"));
        writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Violations: {s.Violations}  Potential issues: {s.PotentialIssues}  Inconclusive: {s.Inconclusive}  Manual review: {s.ManualReviewRequired}  Suppressed: {s.Suppressed}"));
        if (s.ViolationsByImpact.Count > 0)
        {
            writer.WriteLine("By impact: " + string.Join(", ", s.ViolationsByImpact.Select(kv => $"{kv.Key} {kv.Value}")));
        }

        if (s.ViolationsByWcag.Count > 0)
        {
            writer.WriteLine("By WCAG criterion: " + string.Join(", ", s.ViolationsByWcag.Select(kv => $"{kv.Key} {kv.Value}")));
        }

        if (report.Baseline is { } baseline)
        {
            writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Baseline: {baseline.New.Count} new, {baseline.Existing.Count} existing, {baseline.Resolved.Sum(r => r.Count)} resolved{(baseline.ResolvedIsReliable ? string.Empty : " (not reliable: some audits did not complete)")}"));
        }

        if (s.ManualReview is { } coverage)
        {
            writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Manual review: {coverage.Passed} passed, {coverage.Failed} failed, {coverage.NotApplicable} not applicable, {coverage.Outstanding} outstanding of {coverage.Total}"));
        }

        foreach (var audit in report.Audits)
        {
            var active = audit.Findings.Where(f => !f.IsSuppressed).ToArray();
            if (audit.Status == AuditStatus.Completed && active.Length == 0)
            {
                continue;
            }

            writer.WriteLine();
            writer.WriteLine($"== {audit.Name} [{audit.Status}]{(audit.Context.Url is null ? string.Empty : " " + audit.Context.Url)}");
            foreach (var error in audit.Errors)
            {
                writer.WriteLine($"   ERROR [{error.Source}] {error.Message}");
            }

            foreach (var finding in active.Take(MaxFindingsPerAudit))
            {
                writer.WriteLine("  - " + FindingFormatter.Format(finding).ReplaceLineEndings(Environment.NewLine + "  "));
            }

            if (active.Length > MaxFindingsPerAudit)
            {
                writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  ... {active.Length - MaxFindingsPerAudit} more finding(s); see the JSON or HTML report."));
            }
        }

        writer.WriteLine();
        writer.WriteLine("Automated checks find a subset of accessibility problems and cannot prove WCAG conformance.");
    }
}
