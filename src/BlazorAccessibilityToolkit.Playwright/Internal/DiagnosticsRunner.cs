using System.Text.Json;
using BlazorAccessibilityToolkit.Rules;
using Microsoft.Playwright;

namespace BlazorAccessibilityToolkit.Playwright.Internal;

/// <summary>Runs the toolkit's in-browser diagnostics.</summary>
internal static class DiagnosticsRunner
{
    public static async Task RunAsync(IPage page, string? scopeSelector, AccessibilityAuditOptions options, EngineOutput output)
    {
        var disabled = options.DisabledRules.ToHashSet(StringComparer.Ordinal);
        var json = await page.EvaluateAsync<JsonElement>(
            "o => window.__blazorA11y.runDiagnostics(o)",
            new { root = scopeSelector, exclude = options.ExcludeSelectors.ToArray() }).ConfigureAwait(false);
        var results = ToolkitScript.Deserialize<DiagnosticDto[]>(json) ?? [];

        output.Engines.Add(new EngineInfo { Name = ToolkitRules.EngineName, Version = ToolkitScript.ToolkitVersion });
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var result in results)
        {
            var rule = ToolkitRules.Find(result.RuleId);
            if (rule is null || disabled.Contains(rule.Id))
            {
                continue;
            }

            if (rule.IsBestPractice && !options.IncludeBestPractices)
            {
                continue;
            }

            reported.Add(rule.Id);
            output.Findings.Add(rule.CreateFinding(
                new FindingTarget
                {
                    Selector = result.Selector,
                    Html = result.Html,
                    Role = result.Role,
                    AccessibleName = result.Name,
                    ElementOrdinal = result.Ordinal,
                },
                remediation: result.Message,
                data: result.Data));
        }

        foreach (var rule in ToolkitRules.All.Where(r => r.Environments.Contains(ToolkitRules.Browser) && Executed.Contains(r.Id) && !reported.Contains(r.Id) && !disabled.Contains(r.Id)))
        {
            output.Passed.Add(rule.Id);
        }
    }

    // Rules implemented by runDiagnostics in toolkit.js (focus and live-region rules run in their own components).
    private static readonly HashSet<string> Executed = new(StringComparer.Ordinal)
    {
        ToolkitRules.DuplicateId.Id,
        ToolkitRules.IdReferenceMissing.Id,
        ToolkitRules.ExpandedStateMismatch.Id,
        ToolkitRules.TabSelectionMismatch.Id,
        ToolkitRules.ValidationMessageAssociation.Id,
        ToolkitRules.InvalidWithoutErrorReference.Id,
        ToolkitRules.RadioGroupUngrouped.Id,
        ToolkitRules.FieldsetMissingLegend.Id,
    };
}
