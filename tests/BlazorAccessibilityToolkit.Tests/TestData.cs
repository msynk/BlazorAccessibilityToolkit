using BlazorAccessibilityToolkit;

namespace BlazorAccessibilityToolkit.Tests;

internal static class TestData
{
    public static AccessibilityFinding Finding(
        string ruleId = "button-name",
        FindingStatus status = FindingStatus.Violation,
        AccessibilityImpact? impact = AccessibilityImpact.Serious,
        string selector = "button.icon",
        string engine = "axe-core",
        string? html = "<button class=\"icon\"></button>",
        bool bestPractice = false) => new()
        {
            RuleId = ruleId,
            Engine = engine,
            Status = status,
            Impact = impact,
            Help = "Buttons must have discernible text",
            Description = "Ensures buttons have discernible text",
            Remediation = "Add text",
            HelpUrl = "https://dequeuniversity.com/rules/axe/4.14/button-name",
            Wcag = [WcagCatalog.Get("4.1.2")],
            Tags = ["wcag2a", "wcag412"],
            IsBestPractice = bestPractice,
            Target = new FindingTarget { Selector = selector, Html = html },
        };

    public static AccessibilityContext ComponentContext(string component = "Sample.IconButton", string? scenario = "default", string? state = null) => new()
    {
        Kind = AuditTargetKind.Component,
        ComponentType = component,
        Scenario = scenario,
        State = state,
        RenderMode = "bunit-markup",
    };

    public static AccessibilityAudit Audit(string name = "IconButton", AccessibilityContext? context = null, AccessibilityAuditOptions? options = null, params AccessibilityFinding[] findings)
    {
        var builder = new AccessibilityAuditBuilder(name, context ?? ComponentContext(), options);
        builder.AddEngine("axe-core", "4.14.0");
        builder.AddFindings(findings);
        builder.AddPassedRule("image-alt");
        return builder.Build();
    }

    public static AccessibilityAudit FailedAudit(string name = "Broken")
    {
        var builder = new AccessibilityAuditBuilder(name, ComponentContext());
        builder.AddError(new ExecutionError { Source = "axe-core", Message = "axe is not defined" });
        return builder.Build();
    }
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
