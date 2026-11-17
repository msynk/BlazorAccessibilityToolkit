using System.Text.Json.Serialization;
using BlazorAccessibilityToolkit.ManualReview;

namespace BlazorAccessibilityToolkit.Reporting;

/// <summary>
/// What the evidence in a report says about one success criterion. The terms follow accessibility conformance reports
/// (VPAT/ACR), but <see cref="Supports"/> is only ever based on a recorded manual review: automated checks alone never
/// establish that a criterion is supported.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CriterionConformance>))]
public enum CriterionConformance
{
    /// <summary>No automated finding maps to the criterion and no manual review covers it.</summary>
    NotEvaluated,

    /// <summary>Potential issues, inconclusive results or outstanding manual checks need a person's judgment.</summary>
    NeedsReview,

    /// <summary>A person verified the criterion (manual review passed) and no automated check reported a violation.</summary>
    Supports,

    /// <summary>Every manual check of the criterion was recorded as not applicable, with a justification.</summary>
    NotApplicable,

    /// <summary>An automated check reported a violation, or a manual check failed.</summary>
    DoesNotSupport,
}

/// <summary>The evidence about one WCAG success criterion.</summary>
public sealed record WcagCriterionCoverage
{
    /// <summary>The success criterion.</summary>
    public required WcagReference Criterion { get; init; }

    /// <summary>The EN 301 549 clause for web content, when EN 301 549 requires the criterion.</summary>
    public string? En301549Clause => Criterion.En301549Clause;

    /// <summary>The conclusion drawn from the evidence.</summary>
    public CriterionConformance Conformance { get; init; }

    /// <summary>Unsuppressed violations mapped to the criterion.</summary>
    public int Violations { get; init; }

    /// <summary>Unsuppressed potential issues mapped to the criterion.</summary>
    public int PotentialIssues { get; init; }

    /// <summary>Unsuppressed inconclusive results mapped to the criterion.</summary>
    public int Inconclusive { get; init; }

    /// <summary>Rules that reported findings for the criterion.</summary>
    public IReadOnlyList<string> Rules { get; init; } = [];

    /// <summary>Manual checks that cover the criterion, as <c>checklist/item: status</c>.</summary>
    public IReadOnlyList<string> ManualChecks { get; init; } = [];
}

/// <summary>
/// Rolls automated findings and manual-review results up to WCAG success criteria, as a starting point for an
/// accessibility conformance report (VPAT/ACR) or an EN 301 549 statement. It shows where evidence is missing; it is
/// not a conformance claim.
/// </summary>
public static class WcagCoverage
{
    /// <summary>
    /// Creates one row per WCAG 2.2 success criterion up to <paramref name="level"/> (obsolete 4.1.1 excluded), in
    /// criterion order.
    /// </summary>
    public static IReadOnlyList<WcagCriterionCoverage> Create(AccessibilityReport report, WcagLevel level = WcagLevel.AA)
    {
        ArgumentNullException.ThrowIfNull(report);
        var findings = report.Audits.SelectMany(a => a.Findings).Where(f => !f.IsSuppressed).ToArray();
        var manual = report.ManualReviews
            .SelectMany(c => c.Items.Select(i => (Checklist: c.Name, Item: i, Status: ManualReviewEvaluator.EffectiveStatus(i))))
            .ToArray();

        var rows = new List<WcagCriterionCoverage>();
        foreach (var criterion in WcagCatalog.All.Where(c => c.Level <= level && !c.IsObsolete))
        {
            var related = findings.Where(f => f.Wcag.Any(w => w.Id == criterion.Id)).ToArray();
            var violations = related.Count(f => f.Status == FindingStatus.Violation);
            var potential = related.Count(f => f.Status == FindingStatus.PotentialIssue);
            var inconclusive = related.Count(f => f.Status == FindingStatus.Inconclusive);
            var checks = manual.Where(m => m.Item.Wcag.Contains(criterion.Id, StringComparer.Ordinal)).ToArray();

            CriterionConformance conformance;
            if (violations > 0 || checks.Any(m => m.Status == ManualReviewStatus.Failed))
            {
                conformance = CriterionConformance.DoesNotSupport;
            }
            else if (checks.Length > 0 && checks.All(m => m.Status is ManualReviewStatus.Passed or ManualReviewStatus.NotApplicable))
            {
                conformance = checks.Any(m => m.Status == ManualReviewStatus.Passed) ? CriterionConformance.Supports : CriterionConformance.NotApplicable;
            }
            else if (checks.Length > 0 || potential > 0 || inconclusive > 0)
            {
                conformance = CriterionConformance.NeedsReview;
            }
            else
            {
                conformance = CriterionConformance.NotEvaluated;
            }

            rows.Add(new WcagCriterionCoverage
            {
                Criterion = criterion,
                Conformance = conformance,
                Violations = violations,
                PotentialIssues = potential,
                Inconclusive = inconclusive,
                Rules = related.Select(f => f.RuleId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                ManualChecks = checks.Select(m => $"{m.Checklist}/{m.Item.Id}: {m.Status}").ToArray(),
            });
        }

        return rows;
    }

    /// <summary>A short, human readable label for a conformance value.</summary>
    public static string Label(CriterionConformance conformance) => conformance switch
    {
        CriterionConformance.DoesNotSupport => "Does not support",
        CriterionConformance.Supports => "Supports (manually verified)",
        CriterionConformance.NotApplicable => "Not applicable",
        CriterionConformance.NeedsReview => "Needs review",
        _ => "Not evaluated",
    };
}
