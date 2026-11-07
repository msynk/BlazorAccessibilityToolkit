using System.Text;
using System.Text.Json;
using BlazorAccessibilityToolkit.Reporting;

namespace BlazorAccessibilityToolkit.Baselines;

/// <summary>A known finding recorded in a baseline.</summary>
public sealed record BaselineEntry
{
    /// <summary>Finding fingerprint.</summary>
    public required string Fingerprint { get; init; }

    /// <summary>Rule identifier (informational; matching uses the fingerprint).</summary>
    public required string RuleId { get; init; }

    /// <summary>Engine.</summary>
    public required string Engine { get; init; }

    /// <summary>Finding status.</summary>
    public FindingStatus Status { get; init; }

    /// <summary>Audit name (informational).</summary>
    public string? Audit { get; init; }

    /// <summary>Component type (informational).</summary>
    public string? Component { get; init; }

    /// <summary>Scenario (informational).</summary>
    public string? Scenario { get; init; }

    /// <summary>State (informational).</summary>
    public string? State { get; init; }

    /// <summary>Selector (informational).</summary>
    public string? Selector { get; init; }

    /// <summary>How many occurrences of this fingerprint are known. Additional occurrences are new findings.</summary>
    public int Count { get; init; } = 1;

    /// <summary>Optional note (ticket link, owner).</summary>
    public string? Note { get; init; }
}

/// <summary>
/// A committed list of known findings. A baseline lets CI fail only on <em>new</em> problems while existing ones are
/// being fixed. Matching is fingerprint and count based, so a new occurrence of a known rule on another element,
/// component, scenario or state is always reported as new.
/// </summary>
public sealed record AccessibilityBaseline
{
    /// <summary>Baseline schema version.</summary>
    public string SchemaVersion { get; init; } = "1.0";

    /// <summary>Fingerprint algorithm version the entries were computed with.</summary>
    public string FingerprintVersion { get; init; } = FindingFingerprint.Version;

    /// <summary>Entries, sorted by fingerprint for stable diffs.</summary>
    public IReadOnlyList<BaselineEntry> Entries { get; init; } = [];

    /// <summary>Findings tracked by baselines: unsuppressed violations, potential issues and inconclusive results.</summary>
    internal static bool IsTracked(AccessibilityFinding finding) =>
        !finding.IsSuppressed && finding.Fingerprint is not null && finding.Status is FindingStatus.Violation or FindingStatus.PotentialIssue or FindingStatus.Inconclusive;

    /// <summary>Creates a baseline from the findings of completed audits. Failed and skipped audits are ignored.</summary>
    public static AccessibilityBaseline FromAudits(IEnumerable<AccessibilityAudit> audits)
    {
        ArgumentNullException.ThrowIfNull(audits);
        var entries = audits
            .Where(a => a.Status == AuditStatus.Completed)
            .SelectMany(a => a.Findings.Where(IsTracked).Select(f => (Audit: a, Finding: f)))
            .GroupBy(x => x.Finding.Fingerprint!, StringComparer.Ordinal)
            .Select(g =>
            {
                var (audit, finding) = g.First();
                return new BaselineEntry
                {
                    Fingerprint = g.Key,
                    RuleId = finding.RuleId,
                    Engine = finding.Engine,
                    Status = finding.Status,
                    Audit = audit.Name,
                    Component = finding.Component?.ComponentType ?? audit.Context.ComponentType,
                    Scenario = audit.Context.Scenario,
                    State = audit.Context.State,
                    Selector = finding.Target.Selector,
                    Count = g.Count(),
                };
            })
            .OrderBy(e => e.Fingerprint, StringComparer.Ordinal)
            .ToArray();
        return new AccessibilityBaseline { Entries = entries };
    }

    /// <summary>Reads a baseline file.</summary>
    public static AccessibilityBaseline Load(string path)
    {
        using var stream = File.OpenRead(path);
        var baseline = JsonSerializer.Deserialize<AccessibilityBaseline>(stream, ReportJson.Options)
            ?? throw new InvalidDataException($"'{path}' does not contain a baseline.");
        if (baseline.FingerprintVersion != FindingFingerprint.Version)
        {
            throw new InvalidDataException($"Baseline '{path}' uses fingerprint version {baseline.FingerprintVersion}, but this toolkit computes {FindingFingerprint.Version}. Regenerate the baseline.");
        }

        return baseline;
    }

    /// <summary>Writes the baseline (sorted, no timestamps, so diffs stay reviewable).</summary>
    public void Save(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        var sorted = this with { Entries = Entries.OrderBy(e => e.Fingerprint, StringComparer.Ordinal).ToArray() };
        File.WriteAllText(path, JsonSerializer.Serialize(sorted, ReportJson.Options) + "\n", new UTF8Encoding(false));
    }
}

/// <summary>Reference to one finding occurrence in a report.</summary>
public sealed record BaselineFindingRef
{
    /// <summary>Audit id.</summary>
    public required string AuditId { get; init; }

    /// <summary>Audit name.</summary>
    public required string Audit { get; init; }

    /// <summary>Fingerprint.</summary>
    public required string Fingerprint { get; init; }

    /// <summary>Rule id.</summary>
    public required string RuleId { get; init; }

    /// <summary>Status.</summary>
    public FindingStatus Status { get; init; }

    /// <summary>Selector.</summary>
    public string? Selector { get; init; }
}

/// <summary>Result of comparing current findings with a baseline.</summary>
public sealed record BaselineComparison
{
    /// <summary>Occurrences not covered by the baseline.</summary>
    public IReadOnlyList<BaselineFindingRef> New { get; init; } = [];

    /// <summary>Occurrences covered by the baseline.</summary>
    public IReadOnlyList<BaselineFindingRef> Existing { get; init; } = [];

    /// <summary>Baseline entries (with the number of missing occurrences as <see cref="BaselineEntry.Count"/>) that no longer occur.</summary>
    public IReadOnlyList<BaselineEntry> Resolved { get; init; } = [];

    /// <summary>
    /// True when the comparison can be trusted for resolved findings: all audits completed. When audits failed,
    /// missing findings may simply not have been checked, so they are not reported as resolved.
    /// </summary>
    public bool ResolvedIsReliable { get; init; }

    /// <summary>Compares the tracked findings of <paramref name="audits"/> with <paramref name="baseline"/>.</summary>
    public static BaselineComparison Compare(AccessibilityBaseline baseline, IEnumerable<AccessibilityAudit> audits)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(audits);
        var allowance = baseline.Entries
            .GroupBy(e => e.Fingerprint, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(e => Math.Max(1, e.Count)), StringComparer.Ordinal);
        var used = new Dictionary<string, int>(StringComparer.Ordinal);
        var newRefs = new List<BaselineFindingRef>();
        var existing = new List<BaselineFindingRef>();
        var auditList = audits.ToArray();

        foreach (var audit in auditList)
        {
            foreach (var finding in audit.Findings.Where(AccessibilityBaseline.IsTracked))
            {
                var reference = new BaselineFindingRef
                {
                    AuditId = audit.Id,
                    Audit = audit.Name,
                    Fingerprint = finding.Fingerprint!,
                    RuleId = finding.RuleId,
                    Status = finding.Status,
                    Selector = finding.Target.Selector,
                };
                var usedCount = used.GetValueOrDefault(reference.Fingerprint);
                if (usedCount < allowance.GetValueOrDefault(reference.Fingerprint))
                {
                    used[reference.Fingerprint] = usedCount + 1;
                    existing.Add(reference);
                }
                else
                {
                    newRefs.Add(reference);
                }
            }
        }

        var resolved = baseline.Entries
            .GroupBy(e => e.Fingerprint, StringComparer.Ordinal)
            .Select(g => (Entry: g.First(), Missing: allowance[g.Key] - used.GetValueOrDefault(g.Key)))
            .Where(x => x.Missing > 0)
            .Select(x => x.Entry with { Count = x.Missing })
            .OrderBy(e => e.Fingerprint, StringComparer.Ordinal)
            .ToArray();

        return new BaselineComparison
        {
            New = newRefs,
            Existing = existing,
            Resolved = resolved,
            ResolvedIsReliable = auditList.Length > 0 && auditList.All(a => a.Status == AuditStatus.Completed),
        };
    }
}
