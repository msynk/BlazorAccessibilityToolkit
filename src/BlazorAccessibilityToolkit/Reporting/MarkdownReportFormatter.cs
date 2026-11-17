using System.Globalization;
using System.Text;

namespace BlazorAccessibilityToolkit.Reporting;

/// <summary>
/// GitHub-flavored Markdown report for pull request comments and CI job summaries (for example
/// <c>$GITHUB_STEP_SUMMARY</c>): summary, baseline changes, failing findings and the WCAG / EN 301 549 coverage.
/// </summary>
public sealed class MarkdownReportFormatter : IReportFormatter
{
    /// <inheritdoc />
    public string Name => "markdown";

    /// <inheritdoc />
    public string DefaultFileName => "accessibility-report.md";

    /// <summary>Maximum findings listed per audit. Default 10.</summary>
    public int MaxFindingsPerAudit { get; init; } = 10;

    /// <summary>Highest WCAG level in the coverage table. Default AA.</summary>
    public WcagLevel CoverageLevel { get; init; } = WcagLevel.AA;

    /// <summary>Include the WCAG coverage table. Default <c>true</c>.</summary>
    public bool IncludeCoverage { get; init; } = true;

    /// <inheritdoc />
    public void Write(AccessibilityReport report, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(writer);
        var s = report.Summary;
        var w = new StringBuilder();
        var failing = s.Violations > 0 || s.FailedAudits > 0;
        w.Append("## ").Append(failing ? "❌" : "✅").Append(" Accessibility report\n\n");
        w.Append(CultureInfo.InvariantCulture, $"{Cell(report.Tool.Name)} {Cell(report.Tool.Version)}, generated {report.GeneratedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)}.\n\n");

        w.Append("| Audits | Violations | Potential issues | Inconclusive | Manual review | Suppressed |\n");
        w.Append("| --- | --- | --- | --- | --- | --- |\n");
        w.Append(CultureInfo.InvariantCulture, $"| {s.Audits} ({s.CompletedAudits} completed, {s.FailedAudits} failed, {s.SkippedAudits} skipped) | {s.Violations} | {s.PotentialIssues} | {s.Inconclusive} | {s.ManualReviewRequired} | {s.Suppressed} |\n\n");

        if (report.Baseline is { } baseline)
        {
            w.Append(CultureInfo.InvariantCulture, $"**Baseline:** {baseline.New.Count} new, {baseline.Existing.Count} existing, {baseline.Resolved.Sum(r => r.Count)} resolved{(baseline.ResolvedIsReliable ? string.Empty : " (resolved count not reliable: some audits did not complete)")}.\n\n");
        }

        if (s.ViolationsByRule.Count > 0)
        {
            w.Append("### Violations by rule\n\n| Rule | Count |\n| --- | --- |\n");
            foreach (var (rule, count) in s.ViolationsByRule.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal))
            {
                w.Append(CultureInfo.InvariantCulture, $"| `{Code(rule)}` | {count} |\n");
            }

            w.Append('\n');
        }

        var problems = report.Audits.Where(a => a.Status != AuditStatus.Completed || a.Findings.Any(f => !f.IsSuppressed && f.Status == FindingStatus.Violation)).ToArray();
        if (problems.Length > 0)
        {
            w.Append("### Failing audits\n\n");
            foreach (var audit in problems)
            {
                var violations = audit.Findings.Where(f => !f.IsSuppressed && f.Status == FindingStatus.Violation).ToArray();
                w.Append(CultureInfo.InvariantCulture, $"<details><summary><strong>{Html(audit.Name)}</strong> — {(audit.Status == AuditStatus.Completed ? $"{violations.Length} violation(s)" : audit.Status.ToString())}</summary>\n\n");
                foreach (var error in audit.Errors)
                {
                    w.Append(CultureInfo.InvariantCulture, $"- **Execution error** [{Cell(error.Source)}]: {Cell(error.Message)}\n");
                }

                foreach (var f in violations.Take(MaxFindingsPerAudit))
                {
                    var wcag = f.Wcag.Count == 0 ? string.Empty : $" (WCAG {string.Join(", ", f.Wcag.Select(x => x.Id))})";
                    var component = f.Component is null ? string.Empty : $" in `{Code(string.Join(" > ", f.Component.ComponentPath.Select(FindingFormatter.ShortTypeName)))}`";
                    w.Append(CultureInfo.InvariantCulture, $"- **{f.Impact?.ToString().ToLowerInvariant() ?? "unknown"}** `{Code(f.RuleId)}`{wcag}: {Cell(f.Help ?? f.Description)} — `{Code(f.Target.Selector)}`{component}\n");
                }

                if (violations.Length > MaxFindingsPerAudit)
                {
                    w.Append(CultureInfo.InvariantCulture, $"- … {violations.Length - MaxFindingsPerAudit} more; see the HTML or JSON report.\n");
                }

                w.Append("\n</details>\n\n");
            }
        }

        if (IncludeCoverage)
        {
            var rows = WcagCoverage.Create(report, CoverageLevel);
            var counts = rows.GroupBy(r => r.Conformance).ToDictionary(g => g.Key, g => g.Count());
            w.Append(CultureInfo.InvariantCulture, $"### WCAG 2.2 coverage (level {CoverageLevel})\n\n");
            w.Append(string.Join(" · ", Enum.GetValues<CriterionConformance>().Reverse().Where(counts.ContainsKey).Select(c => $"{WcagCoverage.Label(c)}: {counts[c]}"))).Append("\n\n");
            w.Append("<details><summary>Criteria</summary>\n\n| Criterion | Level | EN 301 549 | Result | Evidence |\n| --- | --- | --- | --- | --- |\n");
            foreach (var row in rows)
            {
                var evidence = new List<string>();
                if (row.Violations > 0)
                {
                    evidence.Add(string.Create(CultureInfo.InvariantCulture, $"{row.Violations} violation(s)"));
                }

                if (row.PotentialIssues > 0)
                {
                    evidence.Add(string.Create(CultureInfo.InvariantCulture, $"{row.PotentialIssues} potential"));
                }

                if (row.Inconclusive > 0)
                {
                    evidence.Add(string.Create(CultureInfo.InvariantCulture, $"{row.Inconclusive} inconclusive"));
                }

                evidence.AddRange(row.ManualChecks);
                w.Append(CultureInfo.InvariantCulture, $"| [{row.Criterion.Id} {Cell(row.Criterion.Title)}]({row.Criterion.Url}) | {row.Criterion.Level} | {row.En301549Clause ?? "—"} | {WcagCoverage.Label(row.Conformance)} | {Cell(string.Join("; ", evidence))} |\n");
            }

            w.Append("\n</details>\n\n");
        }

        w.Append("_Automated checks find a subset of accessibility problems and cannot prove WCAG conformance. \"Supports\" is only shown for criteria a person verified in a manual review._\n");
        writer.Write(w.ToString());
    }

    private static string Cell(string? text) =>
        (text ?? string.Empty).ReplaceLineEndings(" ").Replace("|", "\\|", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace("`", "'", StringComparison.Ordinal);

    // Text inside a code span: only the table separator and backticks need care.
    private static string Code(string? text) =>
        (text ?? string.Empty).ReplaceLineEndings(" ").Replace("|", "\\|", StringComparison.Ordinal).Replace("`", "'", StringComparison.Ordinal);

    private static string Html(string? text) => System.Net.WebUtility.HtmlEncode(text ?? string.Empty);
}
