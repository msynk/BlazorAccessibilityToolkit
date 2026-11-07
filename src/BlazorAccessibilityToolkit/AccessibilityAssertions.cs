using System.Globalization;
using System.Text;

namespace BlazorAccessibilityToolkit;

/// <summary>Thrown when an accessibility expectation is not met. Recognized as a test failure by every test framework.</summary>
public class AccessibilityAssertionException : Exception
{
    /// <summary>Creates the exception.</summary>
    public AccessibilityAssertionException()
    {
    }

    /// <summary>Creates the exception.</summary>
    public AccessibilityAssertionException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    public AccessibilityAssertionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Findings that caused the failure.</summary>
    public IReadOnlyList<AccessibilityFinding> Findings { get; init; } = [];
}

/// <summary>Thrown when asserting on an audit that did not execute completely. A failed audit never counts as a pass.</summary>
public sealed class AccessibilityAuditFailedException : AccessibilityAssertionException
{
    /// <summary>Creates the exception.</summary>
    public AccessibilityAuditFailedException()
    {
    }

    /// <summary>Creates the exception.</summary>
    public AccessibilityAuditFailedException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    public AccessibilityAuditFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The audit that failed to execute.</summary>
    public AccessibilityAudit? Audit { get; init; }
}

/// <summary>Which findings an assertion treats as failures.</summary>
public sealed record AccessibilityExpectation
{
    /// <summary>Default: unsuppressed violations of any impact fail; potential issues and inconclusive results do not.</summary>
    public static AccessibilityExpectation Default { get; } = new();

    /// <summary>Strict: violations, potential issues and inconclusive results all fail.</summary>
    public static AccessibilityExpectation Strict { get; } = new() { FailOnPotentialIssues = true, FailOnInconclusive = true };

    /// <summary>Minimum impact that fails. Findings without an impact always fail (conservative).</summary>
    public AccessibilityImpact MinimumImpact { get; init; } = AccessibilityImpact.Minor;

    /// <summary>Also fail on toolkit heuristics.</summary>
    public bool FailOnPotentialIssues { get; init; }

    /// <summary>Also fail on inconclusive (needs review) engine results.</summary>
    public bool FailOnInconclusive { get; init; }

    /// <summary>Also fail on best-practice findings. Default <c>true</c> (they only appear when best practices were requested).</summary>
    public bool IncludeBestPractices { get; init; } = true;

    internal bool Fails(AccessibilityFinding finding)
    {
        if (finding.IsSuppressed)
        {
            return false;
        }

        if (finding.IsBestPractice && !IncludeBestPractices)
        {
            return false;
        }

        var statusFails = finding.Status switch
        {
            FindingStatus.Violation => true,
            FindingStatus.PotentialIssue => FailOnPotentialIssues,
            FindingStatus.Inconclusive => FailOnInconclusive,
            _ => false,
        };

        return statusFails && (finding.Impact is null || finding.Impact >= MinimumImpact);
    }
}

/// <summary>Framework-agnostic assertions for audits. They throw <see cref="AccessibilityAssertionException"/>.</summary>
public static class AccessibilityAssertions
{
    /// <summary>
    /// Asserts that the audit executed completely and has no failing findings.
    /// Throws <see cref="AccessibilityAuditFailedException"/> when the audit failed or was skipped.
    /// </summary>
    public static AccessibilityAudit ShouldHaveNoViolations(this AccessibilityAudit audit, AccessibilityExpectation? expectation = null)
    {
        ArgumentNullException.ThrowIfNull(audit);
        audit.ShouldHaveExecuted();
        expectation ??= AccessibilityExpectation.Default;
        var failing = audit.Findings.Where(expectation.Fails).ToArray();
        if (failing.Length > 0)
        {
            throw new AccessibilityAssertionException(FindingFormatter.FormatFailure(audit, failing)) { Findings = failing };
        }

        return audit;
    }

    /// <summary>Asserts that every audit executed and has no failing findings. Reports all failures together.</summary>
    public static IReadOnlyList<AccessibilityAudit> ShouldHaveNoViolations(this IEnumerable<AccessibilityAudit> audits, AccessibilityExpectation? expectation = null)
    {
        ArgumentNullException.ThrowIfNull(audits);
        var list = audits.ToArray();
        if (list.Length == 0)
        {
            throw new AccessibilityAuditFailedException("No audits were executed; an empty set of audits cannot pass.");
        }

        var messages = new List<string>();
        var findings = new List<AccessibilityFinding>();
        foreach (var audit in list)
        {
            try
            {
                audit.ShouldHaveNoViolations(expectation);
            }
            catch (AccessibilityAssertionException ex)
            {
                messages.Add(ex.Message);
                findings.AddRange(ex.Findings);
            }
        }

        if (messages.Count > 0)
        {
            throw new AccessibilityAssertionException(string.Join(Environment.NewLine + Environment.NewLine, messages)) { Findings = findings };
        }

        return list;
    }

    /// <summary>Asserts that the audit completed (not failed, not skipped).</summary>
    public static AccessibilityAudit ShouldHaveExecuted(this AccessibilityAudit audit)
    {
        ArgumentNullException.ThrowIfNull(audit);
        if (audit.Status == AuditStatus.Completed)
        {
            return audit;
        }

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"Accessibility audit '{audit.Name}' did not complete (status: {audit.Status}). Its results cannot be trusted as a pass.");
        foreach (var error in audit.Errors)
        {
            sb.AppendLine().Append(CultureInfo.InvariantCulture, $"  [{error.Source}] {error.Message}");
        }

        foreach (var warning in audit.Warnings)
        {
            sb.AppendLine().Append(CultureInfo.InvariantCulture, $"  warning: {warning}");
        }

        throw new AccessibilityAuditFailedException(sb.ToString()) { Audit = audit };
    }

    /// <summary>Asserts that the audit executed and contains no inconclusive results.</summary>
    public static AccessibilityAudit ShouldBeConclusive(this AccessibilityAudit audit)
    {
        ArgumentNullException.ThrowIfNull(audit);
        audit.ShouldHaveExecuted();
        var inconclusive = audit.Inconclusive;
        if (inconclusive.Count > 0)
        {
            throw new AccessibilityAssertionException(FindingFormatter.FormatFailure(audit, inconclusive, "inconclusive result(s) need review")) { Findings = inconclusive };
        }

        return audit;
    }

    /// <summary>Asserts that the audit reports an unsuppressed finding for <paramref name="ruleId"/> (useful for regression tests of known defects).</summary>
    public static AccessibilityFinding ShouldHaveFinding(this AccessibilityAudit audit, string ruleId, FindingStatus? status = null)
    {
        ArgumentNullException.ThrowIfNull(audit);
        audit.ShouldHaveExecuted();
        var finding = audit.Findings.FirstOrDefault(f => f.RuleId == ruleId && !f.IsSuppressed && (status is null || f.Status == status));
        if (finding is null)
        {
            var found = audit.Findings.Count == 0 ? "none" : string.Join(", ", audit.Findings.Select(f => $"{f.RuleId} ({f.Status})").Distinct());
            throw new AccessibilityAssertionException($"Expected audit '{audit.Name}' to report '{ruleId}'{(status is null ? string.Empty : $" with status {status}")}, but it did not. Findings: {found}.");
        }

        return finding;
    }

    /// <summary>Asserts that the audit has no unsuppressed finding for <paramref name="ruleId"/>.</summary>
    public static AccessibilityAudit ShouldNotHaveFinding(this AccessibilityAudit audit, string ruleId)
    {
        ArgumentNullException.ThrowIfNull(audit);
        audit.ShouldHaveExecuted();
        var findings = audit.Findings.Where(f => f.RuleId == ruleId && !f.IsSuppressed).ToArray();
        if (findings.Length > 0)
        {
            throw new AccessibilityAssertionException(FindingFormatter.FormatFailure(audit, findings, $"unexpected '{ruleId}' finding(s)")) { Findings = findings };
        }

        return audit;
    }
}

/// <summary>Formats findings as human readable text.</summary>
public static class FindingFormatter
{
    /// <summary>Formats an assertion failure message.</summary>
    public static string FormatFailure(AccessibilityAudit audit, IReadOnlyCollection<AccessibilityFinding> findings, string? what = null)
    {
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(findings);
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"Accessibility audit '{audit.Name}' reported {findings.Count} {what ?? "failing finding(s)"}");
        if (audit.Context.Url is { } url)
        {
            sb.Append(CultureInfo.InvariantCulture, $" at {url}");
        }

        sb.Append(':');
        var index = 1;
        foreach (var finding in findings)
        {
            sb.AppendLine().AppendLine();
            sb.Append(CultureInfo.InvariantCulture, $"{index++}. ").Append(Format(finding));
        }

        sb.AppendLine().AppendLine().Append("Automated checks cannot prove WCAG conformance; fix these findings and review the remaining criteria manually.");
        return sb.ToString();
    }

    /// <summary>Formats one finding over several lines.</summary>
    public static string Format(AccessibilityFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        var sb = new StringBuilder();
        var impact = finding.Impact?.ToString().ToLowerInvariant() ?? "unknown";
        sb.Append(CultureInfo.InvariantCulture, $"[{impact}] {finding.RuleId} ({finding.Engine}, {finding.Status}): {finding.Help ?? finding.Description}");
        if (finding.Wcag.Count > 0)
        {
            sb.AppendLine().Append("   WCAG: ").Append(string.Join("; ", finding.Wcag.Select(w => w.ToString())));
        }
        else if (finding.IsBestPractice)
        {
            sb.AppendLine().Append("   Best practice (not a WCAG requirement)");
        }

        if (finding.Target.Selector is { } selector)
        {
            sb.AppendLine().Append("   Element: ").Append(selector);
            if (finding.Target.Role is not null || finding.Target.AccessibleName is not null)
            {
                sb.Append(CultureInfo.InvariantCulture, $" (role: {finding.Target.Role ?? "none"}, name: \"{finding.Target.AccessibleName}\")");
            }
        }

        if (finding.Component is { } component)
        {
            sb.AppendLine().Append("   Rendered by: ").Append(string.Join(" > ", component.ComponentPath.Select(ShortTypeName)));
        }

        if (finding.Target.Html is { } html)
        {
            sb.AppendLine().Append("   HTML: ").Append(html.ReplaceLineEndings(" "));
        }

        if (finding.Remediation is { } remediation)
        {
            sb.AppendLine().Append("   Fix: ").Append(remediation.ReplaceLineEndings(Environment.NewLine + "        "));
        }

        if (finding.HelpUrl is { } helpUrl)
        {
            sb.AppendLine().Append("   More: ").Append(helpUrl);
        }

        return sb.ToString();
    }

    internal static string ShortTypeName(string typeName)
    {
        var generic = typeName.IndexOf('`', StringComparison.Ordinal);
        var name = generic >= 0 ? typeName[..generic] : typeName;
        var dot = name.LastIndexOf('.');
        return dot >= 0 ? name[(dot + 1)..] : name;
    }
}
