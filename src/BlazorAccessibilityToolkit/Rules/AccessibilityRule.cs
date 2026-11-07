namespace BlazorAccessibilityToolkit.Rules;

/// <summary>Metadata for a rule implemented by the toolkit (engine <see cref="ToolkitRules.EngineName"/>).</summary>
public sealed record AccessibilityRule
{
    /// <summary>Stable rule identifier.</summary>
    public required string Id { get; init; }

    /// <summary>What the rule checks.</summary>
    public required string Description { get; init; }

    /// <summary>Short summary of the problem when the rule reports.</summary>
    public required string Help { get; init; }

    /// <summary>Default remediation guidance.</summary>
    public required string Remediation { get; init; }

    /// <summary>Status reported by the rule (heuristics report <see cref="FindingStatus.PotentialIssue"/>).</summary>
    public required FindingStatus Status { get; init; }

    /// <summary>Default impact.</summary>
    public required AccessibilityImpact Impact { get; init; }

    /// <summary>Associated WCAG success criteria (empty for best practices).</summary>
    public IReadOnlyList<string> Wcag { get; init; } = [];

    /// <summary>True when the rule is a best practice.</summary>
    public bool IsBestPractice { get; init; }

    /// <summary>Authoritative documentation link.</summary>
    public string? HelpUrl { get; init; }

    /// <summary>
    /// Where the rule can run: <c>markup</c> (static Razor markup, no browser), <c>browser</c> (live DOM), or both.
    /// </summary>
    public IReadOnlyList<string> Environments { get; init; } = [];

    /// <summary>Creates a finding for this rule.</summary>
    public AccessibilityFinding CreateFinding(FindingTarget target, string? remediation = null, FindingStatus? status = null, AccessibilityImpact? impact = null, IReadOnlyDictionary<string, string>? data = null)
    {
        var wcag = Wcag.Select(WcagCatalog.Get).ToArray();
        var tags = new List<string> { "blazor-a11y" };
        if (IsBestPractice)
        {
            tags.Add(AccessibilityStandard.BestPracticeTag);
        }

        tags.AddRange(Wcag.Select(id => "wcag" + id.Replace(".", string.Empty, StringComparison.Ordinal)));

        return new AccessibilityFinding
        {
            RuleId = Id,
            Engine = ToolkitRules.EngineName,
            Status = status ?? Status,
            Impact = impact ?? Impact,
            Description = Description,
            Help = Help,
            Remediation = remediation ?? Remediation,
            HelpUrl = HelpUrl,
            Wcag = wcag,
            Tags = tags,
            IsBestPractice = IsBestPractice,
            Target = target,
            Data = data ?? new Dictionary<string, string>(),
        };
    }
}
