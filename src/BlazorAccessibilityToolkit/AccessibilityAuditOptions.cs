using System.Text.Json.Serialization;

namespace BlazorAccessibilityToolkit;

/// <summary>Predefined axe-core tag sets for WCAG conformance targets. Sets are cumulative.</summary>
public static class AccessibilityStandard
{
    /// <summary>WCAG 2.0 Level A.</summary>
    public static IReadOnlyList<string> Wcag20A { get; } = ["wcag2a"];

    /// <summary>WCAG 2.0 Level A and AA.</summary>
    public static IReadOnlyList<string> Wcag20AA { get; } = ["wcag2a", "wcag2aa"];

    /// <summary>WCAG 2.1 Level A.</summary>
    public static IReadOnlyList<string> Wcag21A { get; } = ["wcag2a", "wcag21a"];

    /// <summary>WCAG 2.1 Level A and AA.</summary>
    public static IReadOnlyList<string> Wcag21AA { get; } = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"];

    /// <summary>WCAG 2.2 Level A and AA (default).</summary>
    public static IReadOnlyList<string> Wcag22AA { get; } = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"];

    /// <summary>The axe-core tag that marks best-practice rules (not WCAG requirements).</summary>
    public const string BestPracticeTag = "best-practice";

    /// <summary>The axe-core tag that marks experimental rules.</summary>
    public const string ExperimentalTag = "experimental";
}

/// <summary>
/// A documented, reviewable suppression of a known finding.
/// </summary>
/// <remarks>
/// Suppressions require a reason. They match on rule id plus optional selector, component and scenario filters.
/// Expired suppressions stop applying and produce a warning, so they cannot hide problems indefinitely.
/// Selector, component and scenario filters support <c>*</c> wildcards.
/// </remarks>
public sealed record Suppression
{
    /// <summary>Rule identifier to suppress (exact match).</summary>
    public required string RuleId { get; init; }

    /// <summary>Optional selector filter (wildcards allowed).</summary>
    public string? Selector { get; init; }

    /// <summary>Optional component filter, matched against the full or simple type name of any component in the attribution path.</summary>
    public string? Component { get; init; }

    /// <summary>Optional scenario filter.</summary>
    public string? Scenario { get; init; }

    /// <summary>Why the finding is acceptable or deferred. Required.</summary>
    public required string Reason { get; init; }

    /// <summary>Who owns the follow-up.</summary>
    public string? Owner { get; init; }

    /// <summary>Last day on which the suppression applies.</summary>
    public DateOnly? Expires { get; init; }
}

/// <summary>Rule selection and execution settings for an audit. Instances are immutable and safe to share between tests.</summary>
public sealed record AccessibilityAuditOptions
{
    /// <summary>Default options: WCAG 2.2 A/AA rules, toolkit diagnostics on, best practices off.</summary>
    public static AccessibilityAuditOptions Default { get; } = new();

    /// <summary>Standard tags that select engine rules. Defaults to <see cref="AccessibilityStandard.Wcag22AA"/>.</summary>
    public IReadOnlyList<string> Tags { get; init; } = AccessibilityStandard.Wcag22AA;

    /// <summary>Also run best-practice rules. Their findings are flagged with <see cref="AccessibilityFinding.IsBestPractice"/>.</summary>
    public bool IncludeBestPractices { get; init; }

    /// <summary>Also run experimental engine rules.</summary>
    public bool IncludeExperimental { get; init; }

    /// <summary>Rules to run in addition to the tag selection.</summary>
    public IReadOnlyList<string> EnabledRules { get; init; } = [];

    /// <summary>Rules to skip.</summary>
    public IReadOnlyList<string> DisabledRules { get; init; } = [];

    /// <summary>CSS selectors excluded from the audit.</summary>
    public IReadOnlyList<string> ExcludeSelectors { get; init; } = [];

    /// <summary>Documented suppressions.</summary>
    public IReadOnlyList<Suppression> Suppressions { get; init; } = [];

    /// <summary>Run the toolkit's own Razor/Blazor diagnostics in addition to the main engine. Default <c>true</c>.</summary>
    public bool ToolkitDiagnostics { get; init; } = true;

    /// <summary>
    /// Keep page-level rules (landmarks, <c>page-has-heading-one</c>, <c>bypass</c> ...) when auditing an isolated component.
    /// Default <c>false</c>: an isolated component is not a page, so those rules would only produce noise.
    /// </summary>
    public bool PageLevelRulesForComponents { get; init; }

    /// <summary>Maximum time for one audit. Exceeding it fails the audit; it never passes it.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Maximum length of HTML evidence and engine data per finding.</summary>
    public int MaxEvidenceLength { get; init; } = 1000;

    /// <summary>Maximum number of nodes reported per rule. Additional nodes are summarized in a warning.</summary>
    public int MaxNodesPerRule { get; init; } = 50;

    /// <summary>Redact <c>value</c> attributes and textarea content in evidence. Default <c>true</c>.</summary>
    public bool RedactFormValues { get; init; } = true;

    /// <summary>Clock used to evaluate suppression expiry. Defaults to the system clock.</summary>
    [JsonIgnore]
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>Rules that are not meaningful for an isolated component and are disabled unless <see cref="PageLevelRulesForComponents"/> is set.</summary>
    public static IReadOnlyList<string> PageLevelRules { get; } =
    [
        "region",
        "landmark-one-main",
        "page-has-heading-one",
        "bypass",
        "document-title",
        "html-has-lang",
        "landmark-complementary-is-top-level",
        "landmark-banner-is-top-level",
        "landmark-contentinfo-is-top-level",
        "landmark-main-is-top-level",
        "landmark-no-duplicate-banner",
        "landmark-no-duplicate-contentinfo",
        "landmark-no-duplicate-main",
        "landmark-unique",
        "meta-viewport",
        "meta-refresh",
    ];

    /// <summary>All tags to run, including best-practice/experimental tags when enabled.</summary>
    public IReadOnlyList<string> EffectiveTags()
    {
        var tags = new List<string>(Tags);
        if (IncludeBestPractices && !tags.Contains(AccessibilityStandard.BestPracticeTag))
        {
            tags.Add(AccessibilityStandard.BestPracticeTag);
        }

        if (IncludeExperimental && !tags.Contains(AccessibilityStandard.ExperimentalTag))
        {
            tags.Add(AccessibilityStandard.ExperimentalTag);
        }

        return tags;
    }

    /// <summary>Disabled rules, plus page-level rules when auditing a component.</summary>
    public IReadOnlyList<string> EffectiveDisabledRules(AuditTargetKind kind)
    {
        if (kind != AuditTargetKind.Component || PageLevelRulesForComponents)
        {
            return DisabledRules;
        }

        return DisabledRules.Concat(PageLevelRules.Where(r => !EnabledRules.Contains(r))).Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>Snapshot of the rule configuration for reporting.</summary>
    public AuditConfiguration ToConfiguration(AuditTargetKind kind) => new()
    {
        Tags = EffectiveTags(),
        EnabledRules = EnabledRules,
        DisabledRules = EffectiveDisabledRules(kind),
        ExcludedSelectors = ExcludeSelectors,
    };
}
