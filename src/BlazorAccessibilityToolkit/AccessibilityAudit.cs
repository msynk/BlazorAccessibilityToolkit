using System.Text.Json.Serialization;

namespace BlazorAccessibilityToolkit;

/// <summary>What an audit was executed against.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AuditTargetKind>))]
public enum AuditTargetKind
{
    /// <summary>An isolated Razor component (bUnit markup or the interactive component host).</summary>
    Component,

    /// <summary>A complete page of a running application.</summary>
    Page,

    /// <summary>A part of a page selected by a locator or selector.</summary>
    Region,
}

/// <summary>Describes the component, page, interaction state and environment of an audit.</summary>
public sealed record AccessibilityContext
{
    /// <summary>What was audited.</summary>
    public AuditTargetKind Kind { get; init; } = AuditTargetKind.Page;

    /// <summary>Full type name of the component under test (component audits).</summary>
    public string? ComponentType { get; init; }

    /// <summary>Name of the fixture or scenario that produced the audit.</summary>
    public string? Scenario { get; init; }

    /// <summary>Label of the UI state that was audited (for example <c>dialog open</c>).</summary>
    public string? State { get; init; }

    /// <summary>Page URL (page audits). Host and port are excluded from fingerprints.</summary>
    public string? Url { get; init; }

    /// <summary>Browser name (for example <c>chromium</c>), when a browser was used.</summary>
    public string? Browser { get; init; }

    /// <summary>Browser version, when a browser was used.</summary>
    public string? BrowserVersion { get; init; }

    /// <summary>How the UI was rendered: <c>bunit-markup</c>, <c>static-ssr</c>, <c>interactive-server</c> or <c>page</c>.</summary>
    public string? RenderMode { get; init; }

    /// <summary>Scope selector, when only part of the page was audited.</summary>
    public string? ScopeSelector { get; init; }

    /// <summary>Free-form metadata (string values).</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

/// <summary>Outcome of executing an audit (independent of whether accessibility problems were found).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AuditStatus>))]
public enum AuditStatus
{
    /// <summary>All engines ran to completion.</summary>
    Completed,

    /// <summary>At least one engine failed, timed out or scanned nothing. Results are not trustworthy.</summary>
    Failed,

    /// <summary>The audit was intentionally not executed (for example the browser is unavailable and skipping was configured).</summary>
    Skipped,
}

/// <summary>An engine or infrastructure error that prevented an audit from completing.</summary>
public sealed record ExecutionError
{
    /// <summary>Component that failed (engine name, <c>browser</c>, <c>host</c> ...).</summary>
    public required string Source { get; init; }

    /// <summary>Error message.</summary>
    public required string Message { get; init; }

    /// <summary>Exception type name, when the error came from an exception.</summary>
    public string? ExceptionType { get; init; }

    /// <summary>Creates an error from an exception.</summary>
    public static ExecutionError FromException(string source, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new ExecutionError { Source = source, Message = exception.Message, ExceptionType = exception.GetType().FullName };
    }
}

/// <summary>Name and version of an engine that participated in an audit.</summary>
public sealed record EngineInfo
{
    /// <summary>Engine name.</summary>
    public required string Name { get; init; }

    /// <summary>Engine version.</summary>
    public string? Version { get; init; }
}

/// <summary>Rule configuration that was in effect for an audit.</summary>
public sealed record AuditConfiguration
{
    /// <summary>Standard tags that selected the rules.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Rules explicitly enabled.</summary>
    public IReadOnlyList<string> EnabledRules { get; init; } = [];

    /// <summary>Rules explicitly disabled.</summary>
    public IReadOnlyList<string> DisabledRules { get; init; } = [];

    /// <summary>Selectors excluded from the audit.</summary>
    public IReadOnlyList<string> ExcludedSelectors { get; init; } = [];
}

/// <summary>One audit execution: findings plus the information needed to trust (or distrust) them.</summary>
public sealed record AccessibilityAudit
{
    /// <summary>Unique identifier of this execution.</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Display name (for example <c>SettingsDialog [dialog open]</c>).</summary>
    public required string Name { get; init; }

    /// <summary>What was audited and in which state.</summary>
    public AccessibilityContext Context { get; init; } = new();

    /// <summary>Whether the audit executed completely.</summary>
    public AuditStatus Status { get; init; } = AuditStatus.Completed;

    /// <summary>Start time (UTC).</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>Execution time in milliseconds.</summary>
    public double DurationMs { get; init; }

    /// <summary>Engines that ran.</summary>
    public IReadOnlyList<EngineInfo> Engines { get; init; } = [];

    /// <summary>Rule configuration in effect.</summary>
    public AuditConfiguration Configuration { get; init; } = new();

    /// <summary>All findings, including suppressed ones, sorted deterministically.</summary>
    public IReadOnlyList<AccessibilityFinding> Findings { get; init; } = [];

    /// <summary>Identifiers of rules that ran and found no problem.</summary>
    public IReadOnlyList<string> PassedRules { get; init; } = [];

    /// <summary>Identifiers of rules that did not apply to the audited content.</summary>
    public IReadOnlyList<string> InapplicableRules { get; init; } = [];

    /// <summary>Errors that made the audit fail.</summary>
    public IReadOnlyList<ExecutionError> Errors { get; init; } = [];

    /// <summary>Non-fatal warnings (expired suppressions, attribution gaps ...).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Unsuppressed confirmed violations.</summary>
    [JsonIgnore]
    public IReadOnlyList<AccessibilityFinding> Violations => Active(FindingStatus.Violation);

    /// <summary>Unsuppressed heuristic findings.</summary>
    [JsonIgnore]
    public IReadOnlyList<AccessibilityFinding> PotentialIssues => Active(FindingStatus.PotentialIssue);

    /// <summary>Unsuppressed inconclusive results that need review.</summary>
    [JsonIgnore]
    public IReadOnlyList<AccessibilityFinding> Inconclusive => Active(FindingStatus.Inconclusive);

    /// <summary>Checks that need manual review.</summary>
    [JsonIgnore]
    public IReadOnlyList<AccessibilityFinding> ManualReviewRequired => Active(FindingStatus.ManualReviewRequired);

    /// <summary>Findings hidden by documented suppressions.</summary>
    [JsonIgnore]
    public IReadOnlyList<AccessibilityFinding> Suppressed => Findings.Where(f => f.IsSuppressed).ToArray();

    private AccessibilityFinding[] Active(FindingStatus status) =>
        Findings.Where(f => f.Status == status && !f.IsSuppressed).ToArray();
}
