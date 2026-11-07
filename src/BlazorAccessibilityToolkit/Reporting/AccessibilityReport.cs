using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlazorAccessibilityToolkit.Baselines;
using BlazorAccessibilityToolkit.ManualReview;

namespace BlazorAccessibilityToolkit.Reporting;

/// <summary>Shared JSON settings for reports, audits, baselines and checklists.</summary>
public static class ReportJson
{
    /// <summary>Serializer options: camelCase, indented, enums as strings, nulls omitted.</summary>
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}

/// <summary>Name and version of the tool that produced a report.</summary>
public sealed record ToolInfo
{
    /// <summary>Tool name.</summary>
    public required string Name { get; init; }

    /// <summary>Tool version.</summary>
    public required string Version { get; init; }

    /// <summary>Information URL.</summary>
    public string? InformationUri { get; init; }

    /// <summary>The toolkit itself.</summary>
    public static ToolInfo Toolkit { get; } = new()
    {
        Name = "Blazor Accessibility Toolkit",
        Version = typeof(ToolInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "0.0.0",
        InformationUri = "https://github.com/msynk/BlazorAccessibilityToolkit",
    };
}

/// <summary>Aggregated counts for a report.</summary>
public sealed record ReportSummary
{
    /// <summary>Number of audits.</summary>
    public int Audits { get; init; }

    /// <summary>Audits that completed.</summary>
    public int CompletedAudits { get; init; }

    /// <summary>Audits that failed to execute.</summary>
    public int FailedAudits { get; init; }

    /// <summary>Audits that were skipped.</summary>
    public int SkippedAudits { get; init; }

    /// <summary>Unsuppressed violations.</summary>
    public int Violations { get; init; }

    /// <summary>Unsuppressed potential issues.</summary>
    public int PotentialIssues { get; init; }

    /// <summary>Unsuppressed inconclusive results.</summary>
    public int Inconclusive { get; init; }

    /// <summary>Outstanding manual-review findings.</summary>
    public int ManualReviewRequired { get; init; }

    /// <summary>Suppressed findings.</summary>
    public int Suppressed { get; init; }

    /// <summary>Unsuppressed violations by impact.</summary>
    public IReadOnlyDictionary<string, int> ViolationsByImpact { get; init; } = new Dictionary<string, int>();

    /// <summary>Unsuppressed violations by rule id.</summary>
    public IReadOnlyDictionary<string, int> ViolationsByRule { get; init; } = new Dictionary<string, int>();

    /// <summary>Unsuppressed violations by WCAG success criterion.</summary>
    public IReadOnlyDictionary<string, int> ViolationsByWcag { get; init; } = new Dictionary<string, int>();

    /// <summary>Manual-review coverage, when checklists were included.</summary>
    public ManualReviewCoverage? ManualReview { get; init; }

    /// <summary>Computes a summary.</summary>
    public static ReportSummary Compute(IReadOnlyCollection<AccessibilityAudit> audits, IReadOnlyCollection<ManualReviewChecklist> checklists)
    {
        ArgumentNullException.ThrowIfNull(audits);
        ArgumentNullException.ThrowIfNull(checklists);
        var findings = audits.SelectMany(a => a.Findings).ToArray();
        var violations = findings.Where(f => f.Status == FindingStatus.Violation && !f.IsSuppressed).ToArray();
        return new ReportSummary
        {
            Audits = audits.Count,
            CompletedAudits = audits.Count(a => a.Status == AuditStatus.Completed),
            FailedAudits = audits.Count(a => a.Status == AuditStatus.Failed),
            SkippedAudits = audits.Count(a => a.Status == AuditStatus.Skipped),
            Violations = violations.Length,
            PotentialIssues = findings.Count(f => f.Status == FindingStatus.PotentialIssue && !f.IsSuppressed),
            Inconclusive = findings.Count(f => f.Status == FindingStatus.Inconclusive && !f.IsSuppressed),
            ManualReviewRequired = findings.Count(f => f.Status == FindingStatus.ManualReviewRequired && !f.IsSuppressed),
            Suppressed = findings.Count(f => f.IsSuppressed),
            ViolationsByImpact = Count(violations.Select(v => v.Impact?.ToString().ToLowerInvariant() ?? "unknown")),
            ViolationsByRule = Count(violations.Select(v => v.RuleId)),
            ViolationsByWcag = Count(violations.SelectMany(v => v.Wcag.Select(w => w.Id))),
            ManualReview = checklists.Count == 0 ? null : ManualReviewEvaluator.Coverage(checklists),
        };
    }

    private static SortedDictionary<string, int> Count(IEnumerable<string> keys)
    {
        var result = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            result[key] = result.TryGetValue(key, out var n) ? n + 1 : 1;
        }

        return result;
    }
}

/// <summary>
/// A versioned report of audits, manual-review checklists and an optional baseline comparison.
/// </summary>
public sealed record AccessibilityReport
{
    /// <summary>Current report schema version.</summary>
    public const string CurrentSchemaVersion = "1.0";

    /// <summary>Schema version of this report.</summary>
    public string SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Tool that produced the report.</summary>
    public ToolInfo Tool { get; init; } = ToolInfo.Toolkit;

    /// <summary>Generation time (UTC).</summary>
    public DateTimeOffset GeneratedAt { get; init; }

    /// <summary>Summary counts.</summary>
    public ReportSummary Summary { get; init; } = new();

    /// <summary>Audits, sorted by name then start time.</summary>
    public IReadOnlyList<AccessibilityAudit> Audits { get; init; } = [];

    /// <summary>Manual-review checklists.</summary>
    public IReadOnlyList<ManualReviewChecklist> ManualReviews { get; init; } = [];

    /// <summary>Baseline comparison, when a baseline was supplied.</summary>
    public BaselineComparison? Baseline { get; init; }

    /// <summary>Creates a report with a computed summary and deterministic audit order.</summary>
    public static AccessibilityReport Create(
        IEnumerable<AccessibilityAudit> audits,
        IEnumerable<ManualReviewChecklist>? manualReviews = null,
        AccessibilityBaseline? baseline = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(audits);
        var ordered = audits
            .OrderBy(a => a.Name, StringComparer.Ordinal)
            .ThenBy(a => a.Context.Scenario, StringComparer.Ordinal)
            .ThenBy(a => a.Context.State, StringComparer.Ordinal)
            .ThenBy(a => a.StartedAt)
            .ThenBy(a => a.Id, StringComparer.Ordinal)
            .ToArray();
        var checklists = (manualReviews ?? []).OrderBy(c => c.Name, StringComparer.Ordinal).ToArray();
        return new AccessibilityReport
        {
            GeneratedAt = (timeProvider ?? TimeProvider.System).GetUtcNow(),
            Audits = ordered,
            ManualReviews = checklists,
            Summary = ReportSummary.Compute(ordered, checklists),
            Baseline = baseline is null ? null : BaselineComparison.Compare(baseline, ordered),
        };
    }

    /// <summary>Reads a report or a single-audit result file.</summary>
    public static AccessibilityReport Load(string path) => ReportLoader.LoadReport(path);
}
