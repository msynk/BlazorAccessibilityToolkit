using System.Globalization;
using System.Text.Json;
using Microsoft.Playwright;

namespace BlazorAccessibilityToolkit.Playwright.Internal;

/// <summary>
/// Runs the IBM Equal Access accessibility checker engine (accessibility-checker-engine, Apache-2.0) as a second rule
/// engine next to axe-core. Its findings on an element that axe-core already reports as a violation of the same WCAG
/// success criterion are merged into the axe-core finding, so that one problem does not fail a gate twice.
/// </summary>
internal static class EqualAccessRunner
{
    internal const string EngineName = "ibm-equal-access";

    /// <summary>The bundled accessibility-checker-engine version (Scripts/EqualAccess/ace.js).</summary>
    internal const string EngineVersion = "4.0.34";

    private static readonly Lazy<string> Source = new(() =>
    {
        using var stream = typeof(EqualAccessRunner).Assembly.GetManifestResourceStream("BlazorAccessibilityToolkit.ace.js")
            ?? throw new InvalidOperationException("The IBM Equal Access engine resource is missing from the assembly.");
        using var reader = new StreamReader(stream);

        // Inside a function, the engine's top-level "var ace" stays local: pages that use the Ace code editor own window.ace.
        return "(() => {\n" + reader.ReadToEnd() + "\n;window.__blazorA11yAce = ace; return true; })()";
    });

    private const string RunScript = """
        async o => {
          const ace = window.__blazorA11yAce;
          const root = o.root ? document.querySelector(o.root) : document;
          if (!root) throw new Error('The audit scope was not found.');
          const checker = new ace.Checker();
          const report = await checker.check(root, [o.ruleset]);
          const excluded = node => node && node.nodeType === 1 && o.exclude.some(s => { try { return node.closest(s) !== null; } catch { return false; } });

          // The axe-core violations, by element, to merge findings on the same element.
          const axeByElement = new Map();
          o.axe.forEach((selector, index) => {
            try {
              const element = document.querySelector(selector);
              if (element) axeByElement.set(element, [...(axeByElement.get(element) || []), index]);
            } catch { }
          });

          // WCAG success criteria of each rule in this ruleset.
          const criteria = {};
          const guideline = checker.getGuidelines().find(g => g.id === o.ruleset);
          for (const checkpoint of (guideline && guideline.checkpoints) || []) {
            if (!/^\d+\.\d+\.\d+$/.test(checkpoint.num)) continue;
            for (const rule of checkpoint.rules || []) (criteria[rule.id] = criteria[rule.id] || []).push(checkpoint.num);
          }

          const passed = new Set(), items = [];
          for (const result of report.results) {
            const [level, outcome] = result.value;
            if (outcome === 'PASS') { passed.add(result.ruleId); continue; }
            const node = result.node && result.node.nodeType === 1 ? result.node : null;
            if (excluded(node) || node?.closest?.('[data-a11y-focus-sentinel]')) continue;
            let help = null;
            try { help = checker.engine.getHelp(result.ruleId, result.reasonId); } catch { }
            items.push({
              ruleId: result.ruleId, reasonId: result.reasonId, level, outcome,
              message: result.message, snippet: result.snippet, xpath: result.path && result.path.dom,
              selector: node ? window.__blazorA11y.selectorFor(node) : null,
              axe: node ? (axeByElement.get(node) || []) : [],
              help: typeof help === 'string' ? help : null,
            });
          }
          // A string: Playwright's object serialization adds reference ids to objects it sees more than once.
          return JSON.stringify({ items, passed: [...passed], criteria });
        }
        """;

    /// <summary>The ruleset that matches the audit's tags: IBM Accessibility (with recommendations) or a WCAG version.</summary>
    internal static string RulesetFor(AccessibilityAuditOptions options)
    {
        if (options.IncludeBestPractices)
        {
            return "IBM_Accessibility";
        }

        var tags = options.EffectiveTags();
        return tags.Any(t => t.StartsWith("wcag22", StringComparison.Ordinal)) ? "WCAG_2_2"
            : tags.Any(t => t.StartsWith("wcag21", StringComparison.Ordinal)) ? "WCAG_2_1"
            : "WCAG_2_0";
    }

    public static async Task RunAsync(IPage page, string? scopeSelector, AccessibilityAuditOptions options, EngineOutput output)
    {
        if (!await page.EvaluateAsync<bool>("() => typeof window.__blazorA11yAce !== 'undefined'").ConfigureAwait(false))
        {
            await page.EvaluateAsync(Source.Value).ConfigureAwait(false);
        }

        // Only axe-core violations whose selector is valid in this document can be matched by element.
        var axeViolations = output.Findings
            .Select((finding, index) => (Finding: finding, Index: index))
            .Where(x => x.Finding.Engine == AxeRunner.EngineName && x.Finding.Status == FindingStatus.Violation && x.Finding.Target.SelectorPath.Count == 0 && x.Finding.Target.Selector is not null)
            .ToArray();
        var ruleset = RulesetFor(options);
        var text = await page.EvaluateAsync<string>(RunScript, new
        {
            root = scopeSelector,
            ruleset,
            exclude = options.ExcludeSelectors.ToArray(),
            axe = axeViolations.Select(x => x.Finding.Target.Selector).ToArray(),
        }).ConfigureAwait(false);
        using var document = JsonDocument.Parse(text);
        var json = document.RootElement;

        output.Engines.Add(new EngineInfo { Name = EngineName, Version = EngineVersion });
        var criteria = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var rule in json.GetProperty("criteria").EnumerateObject())
        {
            criteria[rule.Name] = Array(rule.Value, "criteria." + rule.Name).Select(c => c.GetString()!).Distinct(StringComparer.Ordinal).ToArray();
        }

        var disabled = options.DisabledRules.ToHashSet(StringComparer.Ordinal);
        var failedRules = new HashSet<string>(StringComparer.Ordinal);
        var perRule = new Dictionary<string, int>(StringComparer.Ordinal);
        var limit = options.MaxNodesPerRule > 0 ? options.MaxNodesPerRule : int.MaxValue;
        int merged = 0, manual = 0;
        foreach (var item in Array(json.GetProperty("items"), "items"))
        {
            var ruleId = item.GetProperty("ruleId").GetString()!;
            if (disabled.Contains(ruleId))
            {
                continue;
            }

            var level = item.GetProperty("level").GetString();
            var outcome = item.GetProperty("outcome").GetString();
            var recommendation = level is "RECOMMENDATION" or "INFORMATION";
            failedRules.Add(ruleId);
            if (outcome == "MANUAL")
            {
                // "Verify that ..." checks for a person: counted, not listed (the manual review checklist covers them).
                manual++;
                continue;
            }

            if (recommendation && !options.IncludeBestPractices)
            {
                continue;
            }

            var wcag = criteria.TryGetValue(ruleId, out var ids) ? ids.Select(WcagCatalog.Find).OfType<WcagReference>().ToArray() : [];

            // The same problem on the same element: confirm the axe-core finding instead of reporting it twice.
            var axeIndexes = Array(item.GetProperty("axe"), "axe").Select(i => axeViolations[i.GetInt32()].Index).ToArray();
            var duplicate = axeIndexes.FirstOrDefault(i => output.Findings[i].Wcag.Any(w => wcag.Any(c => c.Id == w.Id)), -1);
            if (duplicate >= 0 && outcome == "FAIL")
            {
                var axeFinding = output.Findings[duplicate];
                var confirmedBy = axeFinding.Data.TryGetValue("confirmedBy", out var existing) ? existing + ", " : string.Empty;
                output.Findings[duplicate] = axeFinding with
                {
                    Data = new Dictionary<string, string>(axeFinding.Data) { ["confirmedBy"] = confirmedBy + EngineName + ":" + ruleId },
                };
                merged++;
                continue;
            }

            perRule[ruleId] = perRule.GetValueOrDefault(ruleId) + 1;
            if (perRule[ruleId] > limit)
            {
                continue;
            }

            var message = item.GetProperty("message").GetString();
            output.Findings.Add(new AccessibilityFinding
            {
                RuleId = ruleId,
                Engine = EngineName,
                Status = outcome == "FAIL" && !recommendation ? FindingStatus.Violation : FindingStatus.PotentialIssue,

                // The engine has no impact scale: violations count as serious, potential violations as moderate.
                Impact = recommendation ? AccessibilityImpact.Minor : outcome == "FAIL" ? AccessibilityImpact.Serious : AccessibilityImpact.Moderate,
                Description = message,
                Help = message,
                Remediation = outcome == "POTENTIAL" ? "Review: the engine could not confirm this. " + message : message,
                HelpUrl = item.GetProperty("help").GetString(),
                Wcag = wcag,
                IsBestPractice = recommendation || wcag.Length == 0,
                Target = new FindingTarget
                {
                    Selector = item.GetProperty("selector").GetString(),
                    Html = item.GetProperty("snippet").GetString(),
                },
                Data = new Dictionary<string, string>
                {
                    ["reasonId"] = item.GetProperty("reasonId").GetString() ?? string.Empty,
                    ["xpath"] = item.GetProperty("xpath").GetString() ?? string.Empty,
                    ["ruleset"] = ruleset,
                },
            });
        }

        foreach (var (ruleId, count) in perRule.Where(r => r.Value > limit))
        {
            output.Warnings.Add(string.Create(CultureInfo.InvariantCulture, $"{EngineName} {ruleId}: {count} elements reported; only the first {limit} are included."));
        }

        foreach (var ruleId in Array(json.GetProperty("passed"), "passed").Select(p => p.GetString()!).Where(r => !failedRules.Contains(r) && !disabled.Contains(r)))
        {
            output.Passed.Add(ruleId);
        }

        if (merged > 0)
        {
            output.Warnings.Add(string.Create(CultureInfo.InvariantCulture, $"{merged} {EngineName} findings repeat axe-core violations on the same element and success criterion; they are recorded as \"confirmedBy\" on those findings."));
        }

        if (manual > 0)
        {
            output.Warnings.Add(string.Create(CultureInfo.InvariantCulture, $"{EngineName} lists {manual} checks for a person to make; use the manual review checklist for them."));
        }
    }

    private static JsonElement.ArrayEnumerator Array(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Array ? element.EnumerateArray() : throw new InvalidOperationException($"Unexpected IBM Equal Access result: '{name}' is {element.ValueKind} ({element.GetRawText()[..Math.Min(200, element.GetRawText().Length)]}).");
}
