using System.Globalization;
using System.Net;
using BlazorAccessibilityToolkit.ManualReview;

namespace BlazorAccessibilityToolkit.Reporting;

/// <summary>Self-contained HTML report for local investigation (no external resources, all content encoded).</summary>
public sealed class HtmlReportFormatter : IReportFormatter
{
    /// <inheritdoc />
    public string Name => "html";

    /// <inheritdoc />
    public string DefaultFileName => "accessibility-report.html";

    /// <inheritdoc />
    public void Write(AccessibilityReport report, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(writer);
        var s = report.Summary;
        writer.WriteLine("<!DOCTYPE html>");
        writer.WriteLine("<html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        writer.WriteLine("<title>Accessibility report</title>");
        writer.WriteLine("""
            <style>
            :root{color-scheme:light dark;--fg:#1b1b1b;--bg:#fff;--muted:#555;--line:#ccc;--crit:#a4000f;--serious:#9a3412;--moderate:#7a5b00;--minor:#1d4ed8;--ok:#0f6b2f}
            @media (prefers-color-scheme:dark){:root{--fg:#ececec;--bg:#161616;--muted:#b5b5b5;--line:#444;--crit:#ff8a8a;--serious:#ffb37a;--moderate:#f2d36b;--minor:#9cc2ff;--ok:#7fd99a}}
            body{font:16px/1.5 system-ui,sans-serif;color:var(--fg);background:var(--bg);margin:0 auto;max-width:72rem;padding:1rem}
            table{border-collapse:collapse;margin:.5rem 0 1rem}th,td{border:1px solid var(--line);padding:.25rem .5rem;text-align:left;vertical-align:top}
            code,pre{font-family:ui-monospace,Consolas,monospace;font-size:.875rem;white-space:pre-wrap;word-break:break-word}
            .muted{color:var(--muted)}.critical{color:var(--crit)}.serious{color:var(--serious)}.moderate{color:var(--moderate)}.minor{color:var(--minor)}.ok{color:var(--ok)}
            details{border:1px solid var(--line);border-radius:4px;margin:.5rem 0;padding:.5rem}summary{cursor:pointer;font-weight:600}
            a{color:inherit}:focus-visible{outline:3px solid currentColor;outline-offset:2px}
            </style>
            """);
        writer.WriteLine("</head><body><main>");
        writer.WriteLine("<h1>Accessibility report</h1>");
        writer.WriteLine($"<p class=\"muted\">{E(report.Tool.Name)} {E(report.Tool.Version)} · generated {E(report.GeneratedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture))} · schema {E(report.SchemaVersion)}</p>");
        writer.WriteLine("<p><strong>Automated checks find a subset of accessibility problems and cannot prove WCAG conformance.</strong> Inconclusive results and manual checks need human review.</p>");

        writer.WriteLine("<h2>Summary</h2><table><caption class=\"muted\">Totals across all audits</caption><tbody>");
        Row(writer, "Audits", $"{s.Audits} ({s.CompletedAudits} completed, {s.FailedAudits} failed, {s.SkippedAudits} skipped)");
        Row(writer, "Violations", N(s.Violations));
        Row(writer, "Potential issues", N(s.PotentialIssues));
        Row(writer, "Inconclusive (needs review)", N(s.Inconclusive));
        Row(writer, "Manual review required", N(s.ManualReviewRequired));
        Row(writer, "Suppressed", N(s.Suppressed));
        if (report.Baseline is { } baseline)
        {
            Row(writer, "Baseline", $"{baseline.New.Count} new, {baseline.Existing.Count} existing, {baseline.Resolved.Sum(r => r.Count)} resolved");
        }

        writer.WriteLine("</tbody></table>");

        if (s.ViolationsByWcag.Count > 0)
        {
            writer.WriteLine("<h3>Violations by WCAG success criterion</h3><table><thead><tr><th scope=\"col\">Criterion</th><th scope=\"col\">Count</th></tr></thead><tbody>");
            foreach (var (id, count) in s.ViolationsByWcag)
            {
                var criterion = WcagCatalog.Find(id);
                writer.WriteLine($"<tr><td>{(criterion is null ? E(id) : $"<a href=\"{E(criterion.Url)}\">{E(criterion.ToString())}</a>")}</td><td>{N(count)}</td></tr>");
            }

            writer.WriteLine("</tbody></table>");
        }

        writer.WriteLine("<h2>Audits</h2>");
        foreach (var audit in report.Audits)
        {
            var active = audit.Findings.Where(f => !f.IsSuppressed).ToArray();
            var label = audit.Status != AuditStatus.Completed
                ? $"<span class=\"critical\">{E(audit.Status.ToString())}</span>"
                : active.Length == 0 ? "<span class=\"ok\">no findings</span>" : $"{N(active.Length)} finding(s)";
            writer.WriteLine($"<details{(audit.Status != AuditStatus.Completed || active.Any(f => f.Status == FindingStatus.Violation) ? " open" : string.Empty)}><summary>{E(audit.Name)} — {label}</summary>");
            writer.WriteLine("<table><tbody>");
            Row(writer, "Kind", audit.Context.Kind.ToString());
            OptionalRow(writer, "Component", audit.Context.ComponentType);
            OptionalRow(writer, "Scenario", audit.Context.Scenario);
            OptionalRow(writer, "State", audit.Context.State);
            OptionalRow(writer, "URL", audit.Context.Url);
            OptionalRow(writer, "Render mode", audit.Context.RenderMode);
            OptionalRow(writer, "Browser", audit.Context.Browser is null ? null : $"{audit.Context.Browser} {audit.Context.BrowserVersion}");
            Row(writer, "Engines", string.Join(", ", audit.Engines.Select(e => $"{e.Name} {e.Version}")));
            Row(writer, "Rules", string.Join(", ", audit.Configuration.Tags));
            writer.WriteLine("</tbody></table>");

            foreach (var error in audit.Errors)
            {
                writer.WriteLine($"<p class=\"critical\">Execution error [{E(error.Source)}]: {E(error.Message)}</p>");
            }

            foreach (var warning in audit.Warnings)
            {
                writer.WriteLine($"<p class=\"muted\">Warning: {E(warning)}</p>");
            }

            if (audit.Findings.Count > 0)
            {
                writer.WriteLine("<table><thead><tr><th scope=\"col\">Impact</th><th scope=\"col\">Status</th><th scope=\"col\">Rule</th><th scope=\"col\">Element</th><th scope=\"col\">Details</th></tr></thead><tbody>");
                foreach (var f in audit.Findings)
                {
                    var impact = f.Impact?.ToString().ToLowerInvariant() ?? "unknown";
                    var wcag = f.Wcag.Count == 0 ? (f.IsBestPractice ? "Best practice" : string.Empty) : string.Join("; ", f.Wcag.Select(w => w.ToString()));
                    writer.Write($"<tr><td class=\"{E(impact)}\">{E(impact)}</td><td>{E(f.Status.ToString())}{(f.IsSuppressed ? $"<br><span class=\"muted\">suppressed: {E(f.Suppression!.Reason)}</span>" : string.Empty)}</td>");
                    writer.Write($"<td>{(f.HelpUrl is null ? E(f.RuleId) : $"<a href=\"{E(f.HelpUrl)}\">{E(f.RuleId)}</a>")}<br><span class=\"muted\">{E(f.Engine)}</span></td>");
                    writer.Write($"<td><code>{E(f.Target.Selector)}</code>{(f.Component is null ? string.Empty : $"<br><span class=\"muted\">{E(string.Join(" > ", f.Component.ComponentPath.Select(FindingFormatter.ShortTypeName)))}</span>")}</td>");
                    writer.Write($"<td>{E(f.Help ?? f.Description)}{(wcag.Length > 0 ? $"<br><span class=\"muted\">{E(wcag)}</span>" : string.Empty)}");
                    if (f.Target.Html is { } html)
                    {
                        writer.Write($"<pre>{E(html)}</pre>");
                    }

                    if (f.Remediation is { } remediation)
                    {
                        writer.Write($"<p>Fix: {E(remediation)}</p>");
                    }

                    writer.WriteLine("</td></tr>");
                }

                writer.WriteLine("</tbody></table>");
            }

            writer.WriteLine("</details>");
        }

        if (report.ManualReviews.Count > 0)
        {
            writer.WriteLine("<h2>Manual review</h2>");
            foreach (var checklist in report.ManualReviews)
            {
                writer.WriteLine($"<h3>{E(checklist.Name)}</h3>");
                writer.WriteLine("<table><thead><tr><th scope=\"col\">Check</th><th scope=\"col\">WCAG</th><th scope=\"col\">Status</th><th scope=\"col\">Tester / date</th><th scope=\"col\">Notes</th></tr></thead><tbody>");
                foreach (var item in checklist.Items)
                {
                    var effective = ManualReviewEvaluator.EffectiveStatus(item);
                    var status = effective == item.Status ? effective.ToString() : $"{effective} (recorded as {item.Status}, incomplete)";
                    writer.WriteLine($"<tr><td>{E(item.Title)}</td><td>{E(string.Join(", ", item.Wcag))}</td><td>{E(status)}</td><td>{E(item.Tester)} {E(item.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))}</td><td>{E(item.Notes ?? item.Justification)}</td></tr>");
                }

                writer.WriteLine("</tbody></table>");
            }
        }

        writer.WriteLine("</main></body></html>");
    }

    private static void Row(TextWriter writer, string header, string value) =>
        writer.WriteLine($"<tr><th scope=\"row\">{E(header)}</th><td>{E(value)}</td></tr>");

    private static void OptionalRow(TextWriter writer, string header, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            Row(writer, header, value);
        }
    }

    private static string N(int n) => n.ToString(CultureInfo.InvariantCulture);

    private static string E(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);
}
