namespace BlazorAccessibilityToolkit.Playwright.Internal;

/// <summary>Raw output of the browser engines before it is assembled into an audit.</summary>
internal sealed class EngineOutput
{
    public List<AccessibilityFinding> Findings { get; } = [];

    public List<string> Passed { get; } = [];

    public List<string> Inapplicable { get; } = [];

    public List<EngineInfo> Engines { get; } = [];

    public List<string> Warnings { get; } = [];

    public void AddTo(AccessibilityAuditBuilder builder)
    {
        foreach (var engine in Engines)
        {
            builder.AddEngine(engine.Name, engine.Version);
        }

        builder.AddFindings(Findings);
        foreach (var rule in Passed)
        {
            builder.AddPassedRule(rule);
        }

        foreach (var rule in Inapplicable)
        {
            builder.AddInapplicableRule(rule);
        }

        foreach (var warning in Warnings)
        {
            builder.AddWarning(warning);
        }
    }
}
