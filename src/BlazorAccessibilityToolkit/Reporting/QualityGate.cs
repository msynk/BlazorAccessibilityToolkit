using System.Globalization;

namespace BlazorAccessibilityToolkit.Reporting;

/// <summary>CI failure policy.</summary>
public sealed record QualityGateOptions
{
    /// <summary>Minimum impact of a violation that fails the gate. <c>null</c> disables failing on violations. Default: any impact.</summary>
    public AccessibilityImpact? FailOnImpact { get; init; } = AccessibilityImpact.Minor;

    /// <summary>Fail on toolkit heuristics (potential issues).</summary>
    public bool FailOnPotentialIssues { get; init; }

    /// <summary>Fail on inconclusive engine results.</summary>
    public bool FailOnInconclusive { get; init; }

    /// <summary>Fail when manual checks are not tested or require review.</summary>
    public bool FailOnOutstandingManualReview { get; init; }

    /// <summary>Fail when a manual check failed. Default <c>true</c>.</summary>
    public bool FailOnFailedManualReview { get; init; } = true;

    /// <summary>
    /// Strict mode: baselines do not excuse findings, stale baseline entries fail, expired suppressions fail, and
    /// skipped audits fail.
    /// </summary>
    public bool Strict { get; init; }

    /// <summary>Allow a report without audits. Default <c>false</c>: no audits means the tests did not run.</summary>
    public bool AllowEmpty { get; init; }
}

/// <summary>Outcome of evaluating the gate.</summary>
public sealed record QualityGateResult
{
    /// <summary>Exit code for success.</summary>
    public const int Success = 0;

    /// <summary>Exit code when accessibility findings fail the gate.</summary>
    public const int FindingsFailed = 1;

    /// <summary>Exit code when audits failed to execute, no audits were found, or inputs were unreadable.</summary>
    public const int ExecutionFailed = 2;

    /// <summary>Exit code for invalid command-line usage or configuration.</summary>
    public const int InvalidUsage = 3;

    /// <summary>True when the gate passed.</summary>
    public bool Passed => ExitCode == Success;

    /// <summary>Process exit code (execution failures take precedence over findings).</summary>
    public int ExitCode { get; init; }

    /// <summary>Execution and infrastructure errors.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>Accessibility failures.</summary>
    public IReadOnlyList<string> Failures { get; init; } = [];

    /// <summary>Informational notes.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>Findings that failed the gate.</summary>
    public IReadOnlyList<AccessibilityFinding> FailingFindings { get; init; } = [];
}

/// <summary>Evaluates reports against a <see cref="QualityGateOptions"/> policy.</summary>
public static class QualityGate
{
    /// <summary>Evaluates the report. <paramref name="loadErrors"/> are treated as execution failures.</summary>
    public static QualityGateResult Evaluate(AccessibilityReport report, QualityGateOptions? options = null, IEnumerable<string>? loadErrors = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        options ??= new QualityGateOptions();
        var errors = new List<string>(loadErrors ?? []);
        var failures = new List<string>();
        var notes = new List<string>();
        var failing = new List<AccessibilityFinding>();

        if (report.Audits.Count == 0 && !options.AllowEmpty)
        {
            errors.Add("No audit results were found. Tests may not have run, or results were not written (set BLAZOR_A11Y_RESULTS_DIR).");
        }

        foreach (var audit in report.Audits.Where(a => a.Status == AuditStatus.Failed))
        {
            var reason = audit.Errors.Count == 0 ? "unknown error" : string.Join("; ", audit.Errors.Select(e => $"[{e.Source}] {e.Message}"));
            errors.Add($"Audit '{audit.Name}' failed to execute: {reason}");
        }

        foreach (var audit in report.Audits.Where(a => a.Status == AuditStatus.Skipped))
        {
            var message = $"Audit '{audit.Name}' was skipped: {audit.Warnings.FirstOrDefault() ?? "no reason recorded"}";
            if (options.Strict)
            {
                failures.Add(message);
            }
            else
            {
                notes.Add(message);
            }
        }

        // Count-aware set of new occurrences, keyed by audit id + fingerprint.
        Dictionary<(string, string), int>? newOccurrences = null;
        if (report.Baseline is { } baseline && !options.Strict)
        {
            newOccurrences = baseline.New
                .GroupBy(r => (r.AuditId, r.Fingerprint))
                .ToDictionary(g => g.Key, g => g.Count());
            notes.Add(string.Create(CultureInfo.InvariantCulture, $"Baseline: {baseline.New.Count} new, {baseline.Existing.Count} existing, {baseline.Resolved.Sum(r => r.Count)} resolved."));
        }

        foreach (var audit in report.Audits.Where(a => a.Status == AuditStatus.Completed))
        {
            foreach (var finding in audit.Findings.Where(f => !f.IsSuppressed && Fails(f, options)))
            {
                if (newOccurrences is not null)
                {
                    var key = (audit.Id, finding.Fingerprint ?? string.Empty);
                    if (!newOccurrences.TryGetValue(key, out var remaining) || remaining == 0)
                    {
                        continue;
                    }

                    newOccurrences[key] = remaining - 1;
                }

                failing.Add(finding);
                failures.Add($"{audit.Name}: [{finding.Impact?.ToString().ToLowerInvariant() ?? "unknown"}] {finding.RuleId} ({finding.Status}) at {finding.Target.Selector ?? "(no selector)"}{(newOccurrences is null ? string.Empty : " [new]")}");
            }

            foreach (var suppression in audit.ExpiredSuppressions)
            {
                var expires = suppression.Expires?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "an unknown date";
                var message = $"{audit.Name}: suppression for rule '{suppression.RuleId}' expired on {expires} and no longer applies (owner: {suppression.Owner ?? "unassigned"}).";
                if (options.Strict)
                {
                    failures.Add(message);
                }
                else
                {
                    notes.Add(message);
                }
            }
        }

        if (options.Strict && report.Baseline is { } strictBaseline && strictBaseline.ResolvedIsReliable && strictBaseline.Resolved.Count > 0)
        {
            failures.Add(string.Create(CultureInfo.InvariantCulture, $"Baseline contains {strictBaseline.Resolved.Count} entr(ies) that no longer occur; update the baseline to lock in the fixes."));
        }

        if (report.Summary.ManualReview is { } coverage)
        {
            if (coverage.Failed > 0 && options.FailOnFailedManualReview)
            {
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"{coverage.Failed} manual check(s) failed."));
            }

            if (coverage.Outstanding > 0)
            {
                var message = string.Create(CultureInfo.InvariantCulture, $"{coverage.Outstanding} of {coverage.Total} manual check(s) are not verified (not tested or requiring review).");
                if (options.FailOnOutstandingManualReview)
                {
                    failures.Add(message);
                }
                else
                {
                    notes.Add(message);
                }
            }
        }

        var exitCode = errors.Count > 0 ? QualityGateResult.ExecutionFailed : failures.Count > 0 ? QualityGateResult.FindingsFailed : QualityGateResult.Success;
        return new QualityGateResult { ExitCode = exitCode, Errors = errors, Failures = failures, Notes = notes, FailingFindings = failing };
    }

    private static bool Fails(AccessibilityFinding finding, QualityGateOptions options) => finding.Status switch
    {
        FindingStatus.Violation => options.FailOnImpact is { } min && (finding.Impact is null || finding.Impact >= min),
        FindingStatus.PotentialIssue => options.FailOnPotentialIssues,
        FindingStatus.Inconclusive => options.FailOnInconclusive,
        _ => false,
    };
}
