using System.Text.Json.Serialization;

namespace BlazorAccessibilityToolkit;

/// <summary>
/// How confident the toolkit is that a finding represents a real accessibility problem.
/// </summary>
/// <remarks>
/// Only <see cref="Violation"/> is treated as a confirmed failure by default. The other statuses are kept
/// separate so that automated tooling never silently turns "could not decide" into "passed".
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<FindingStatus>))]
public enum FindingStatus
{
    /// <summary>A rule engine confirmed a failure (for example an axe-core violation).</summary>
    Violation,

    /// <summary>A toolkit heuristic detected a likely problem that needs human context to confirm.</summary>
    PotentialIssue,

    /// <summary>The engine could not decide (for example axe-core "incomplete" / needs review results).</summary>
    Inconclusive,

    /// <summary>The check can only be answered by a person; it is never counted as passed automatically.</summary>
    ManualReviewRequired,
}

/// <summary>User impact of a finding, using the axe-core impact scale.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AccessibilityImpact>))]
public enum AccessibilityImpact
{
    /// <summary>Minor impact.</summary>
    Minor = 1,

    /// <summary>Moderate impact.</summary>
    Moderate = 2,

    /// <summary>Serious impact.</summary>
    Serious = 3,

    /// <summary>Critical impact: blocks some users entirely.</summary>
    Critical = 4,
}

/// <summary>The element a finding refers to.</summary>
public sealed record FindingTarget
{
    /// <summary>CSS selector of the element. For elements inside frames or shadow roots the segments are joined with " >>> ".</summary>
    public string? Selector { get; init; }

    /// <summary>Selector segments when the element lives inside iframes or shadow roots (outermost first).</summary>
    public IReadOnlyList<string> SelectorPath { get; init; } = [];

    /// <summary>Computed ARIA role, when the engine reported it.</summary>
    public string? Role { get; init; }

    /// <summary>Computed accessible name, when the engine reported it.</summary>
    public string? AccessibleName { get; init; }

    /// <summary>HTML evidence, truncated and with form values redacted.</summary>
    public string? Html { get; init; }

    /// <summary>
    /// Document-order index of the element inside the rendered component markup (component audits only).
    /// Used internally to attribute findings to Razor components.
    /// </summary>
    public int? ElementOrdinal { get; init; }
}

/// <summary>An individual check result reported by an engine for a finding (for example an axe-core "any/all/none" check).</summary>
public sealed record FindingCheck
{
    /// <summary>Check identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Which check group the check belongs to (axe-core: any, all, none).</summary>
    public string? Group { get; init; }

    /// <summary>Human readable check message.</summary>
    public string? Message { get; init; }

    /// <summary>Check impact as reported by the engine.</summary>
    public AccessibilityImpact? Impact { get; init; }

    /// <summary>Engine specific check data serialized as JSON (bounded in size).</summary>
    public string? DataJson { get; init; }

    /// <summary>Selectors of related nodes reported by the engine.</summary>
    public IReadOnlyList<string> RelatedSelectors { get; init; } = [];
}

/// <summary>
/// Describes which Razor component rendered the element of a finding.
/// </summary>
/// <remarks>
/// Attribution is based on the component render tree (bUnit) or on the fixture root (interactive host).
/// It identifies the component that rendered the element, which for child content (render fragments) is the
/// component that hosts the fragment rather than the component that authored the markup. It never claims a
/// Razor source line.
/// </remarks>
public sealed record ComponentAttribution
{
    /// <summary>Full type name of the innermost component that rendered the element.</summary>
    public required string ComponentType { get; init; }

    /// <summary>Component type names from the fixture root to the innermost component.</summary>
    public IReadOnlyList<string> ComponentPath { get; init; } = [];

    /// <summary>How the attribution was established (for example <c>render-tree</c> or <c>fixture-root</c>).</summary>
    public required string Method { get; init; }
}

/// <summary>Records that a finding was suppressed by a documented suppression.</summary>
public sealed record AppliedSuppression
{
    /// <summary>Why the finding is suppressed.</summary>
    public required string Reason { get; init; }

    /// <summary>Who owns the suppression.</summary>
    public string? Owner { get; init; }

    /// <summary>When the suppression expires.</summary>
    public DateOnly? Expires { get; init; }
}

/// <summary>
/// A normalized accessibility issue or observation produced by an engine, a toolkit heuristic or a manual-review requirement.
/// </summary>
public sealed record AccessibilityFinding
{
    /// <summary>Stable rule identifier (for example <c>button-name</c> or <c>blazor-click-handler-semantics</c>).</summary>
    public required string RuleId { get; init; }

    /// <summary>Engine that produced the finding (for example <c>axe-core</c>, <c>blazor-a11y</c>).</summary>
    public required string Engine { get; init; }

    /// <summary>Confidence classification of the finding.</summary>
    public required FindingStatus Status { get; init; }

    /// <summary>User impact, when known.</summary>
    public AccessibilityImpact? Impact { get; init; }

    /// <summary>What the rule checks.</summary>
    public string? Description { get; init; }

    /// <summary>Short summary of the problem.</summary>
    public string? Help { get; init; }

    /// <summary>How to fix the problem for this element.</summary>
    public string? Remediation { get; init; }

    /// <summary>Link to authoritative remediation documentation.</summary>
    public string? HelpUrl { get; init; }

    /// <summary>WCAG success criteria associated with the rule (empty for best practices).</summary>
    public IReadOnlyList<WcagReference> Wcag { get; init; } = [];

    /// <summary>Engine tags (for example <c>wcag2aa</c>, <c>best-practice</c>).</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>True when the rule is a best practice rather than a WCAG conformance requirement.</summary>
    public bool IsBestPractice { get; init; }

    /// <summary>The affected element.</summary>
    public FindingTarget Target { get; init; } = new();

    /// <summary>Component that rendered the element, when it can be established.</summary>
    public ComponentAttribution? Component { get; init; }

    /// <summary>Engine checks that explain the finding.</summary>
    public IReadOnlyList<FindingCheck> Checks { get; init; } = [];

    /// <summary>Additional engine or toolkit data (string values only, bounded in size).</summary>
    public IReadOnlyDictionary<string, string> Data { get; init; } = new Dictionary<string, string>();

    /// <summary>Stable fingerprint used for baselines and de-duplication. Computed by the toolkit.</summary>
    public string? Fingerprint { get; init; }

    /// <summary>Set when a documented suppression applies. Suppressed findings are reported but do not fail gates.</summary>
    public AppliedSuppression? Suppression { get; init; }

    /// <summary>True when <see cref="Suppression"/> is set.</summary>
    [JsonIgnore]
    public bool IsSuppressed => Suppression is not null;
}
