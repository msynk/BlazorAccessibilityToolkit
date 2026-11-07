using System.Globalization;
using System.Text;
using System.Text.Json;
using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Newtonsoft.Json;

namespace BlazorAccessibilityToolkit.Playwright.Internal;

/// <summary>Runs axe-core through Deque.AxeCore.Playwright and normalizes the results.</summary>
internal static class AxeRunner
{
    internal const string EngineName = "axe-core";

    private const int MaxDescribedSelectors = 300;

    public static async Task RunAsync(IPage page, string? scopeSelector, AccessibilityAuditOptions options, AuditTargetKind kind, EngineOutput output)
    {
        var runOptions = new AxeRunOptions
        {
            ResultTypes = [ResultType.Violations, ResultType.Incomplete],
            Iframes = true,
            Selectors = true,
            Ancestry = false,
            XPath = false,
        };

        var tags = options.EffectiveTags();
        if (tags.Count > 0)
        {
            runOptions.RunOnly = RunOnlyOptions.Tags([.. tags]);
        }

        var rules = new Dictionary<string, RuleOptions>(StringComparer.Ordinal);
        foreach (var rule in options.EnabledRules)
        {
            rules[rule] = new RuleOptions { Enabled = true };
        }

        foreach (var rule in options.EffectiveDisabledRules(kind))
        {
            rules[rule] = new RuleOptions { Enabled = false };
        }

        if (rules.Count > 0)
        {
            runOptions.Rules = rules;
        }

        var context = new AxeRunContext();
        if (scopeSelector is not null)
        {
            context.Include = [new AxeSelector(scopeSelector)];
        }

        // The toolkit's temporary focus sentinel (see KeyboardScenario.StartFromDocument) is never audited.
        context.Exclude = options.ExcludeSelectors.Append("[data-a11y-focus-sentinel]").Select(s => new AxeSelector(s)).ToList();

        AxeResult result;
        try
        {
            result = await RunInPageAsync(page, scopeSelector, options, tags, rules).ConfigureAwait(false)
                ?? (context.Include is null && context.Exclude is null
                    ? await page.RunAxe(runOptions).ConfigureAwait(false)
                    : await page.RunAxe(context, runOptions, ToolkitScript.AxeSource).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is not PlaywrightException and not OperationCanceledException and not TimeoutException)
        {
            // Configuration errors from axe-core (unknown rule ids, invalid selectors) surface as generic exceptions.
            throw new InvalidOperationException("axe-core failed: " + ex.Message, ex);
        }

        output.Engines.Add(new EngineInfo { Name = EngineName, Version = result.TestEngine?.Version ?? ToolkitScript.AxeVersion });

        var executed = (result.Violations?.Length ?? 0) + (result.Incomplete?.Length ?? 0) + (result.Passes?.Length ?? 0) + (result.Inapplicable?.Length ?? 0);
        if (executed == 0)
        {
            throw new InvalidOperationException("axe-core executed no rules. Check the configured tags and rule ids.");
        }

        var pending = new List<(AccessibilityFinding Finding, string? Selector)>();
        Collect(result.Violations, FindingStatus.Violation, options, pending, output);
        Collect(result.Incomplete, FindingStatus.Inconclusive, options, pending, output);

        foreach (var item in result.Passes ?? [])
        {
            output.Passed.Add(item.Id);
        }

        foreach (var item in result.Inapplicable ?? [])
        {
            output.Inapplicable.Add(item.Id);
        }

        // Enrich with role, accessible name and component ordinal (single-document selectors only).
        var described = pending.Where(p => p.Selector is not null).Take(MaxDescribedSelectors).ToArray();
        DescribedSelectorDto?[] details = [];
        if (described.Length > 0)
        {
            var json = await page.EvaluateAsync<JsonElement>("s => window.__blazorA11y.describeSelectors(s)", described.Select(d => d.Selector).ToArray()).ConfigureAwait(false);
            details = ToolkitScript.Deserialize<DescribedSelectorDto?[]>(json) ?? [];
        }

        var lookup = new Dictionary<AccessibilityFinding, DescribedSelectorDto?>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < described.Length && i < details.Length; i++)
        {
            lookup[described[i].Finding] = details[i];
        }

        foreach (var (finding, _) in pending)
        {
            if (lookup.TryGetValue(finding, out var d) && d is not null)
            {
                output.Findings.Add(finding with
                {
                    Target = finding.Target with { Role = d.Role, AccessibleName = d.Name, ElementOrdinal = d.Ordinal },
                });
            }
            else
            {
                output.Findings.Add(finding);
            }
        }
    }

    /// <summary>
    /// Runs axe-core directly in a page without frames, injecting the bundled script only when the page does not already
    /// have the same axe-core version. Pages with frames return <c>null</c> and go through Deque.AxeCore.Playwright, which
    /// coordinates the run across frames. The raw result is parsed by Deque's <see cref="AxeResult"/>, so both paths
    /// produce identical findings (guarded by the engine contract test).
    /// </summary>
    private static async Task<AxeResult?> RunInPageAsync(IPage page, string? scopeSelector, AccessibilityAuditOptions options, IReadOnlyList<string> tags, Dictionary<string, RuleOptions> rules)
    {
        if (await page.EvaluateAsync<bool>("() => document.querySelector('iframe, frame') !== null").ConfigureAwait(false))
        {
            return null;
        }

        await ToolkitScript.EnsureAsync(page, includeAxe: true).ConfigureAwait(false);

        var axeOptions = new Dictionary<string, object>
        {
            ["resultTypes"] = new[] { "violations", "incomplete" },
            ["selectors"] = true,
            ["ancestry"] = false,
            ["xpath"] = false,
            ["iframes"] = false,
        };
        if (tags.Count > 0)
        {
            axeOptions["runOnly"] = new { type = "tag", values = tags.ToArray() };
        }

        if (rules.Count > 0)
        {
            axeOptions["rules"] = rules.ToDictionary(r => r.Key, r => (object)new { enabled = r.Value.Enabled ?? true }, StringComparer.Ordinal);
        }

        var axeContext = new Dictionary<string, object>
        {
            ["exclude"] = options.ExcludeSelectors.Append("[data-a11y-focus-sentinel]").ToArray(),
        };
        if (scopeSelector is not null)
        {
            axeContext["include"] = new[] { scopeSelector };
        }

        string json;
        try
        {
            json = await page.EvaluateAsync<string>(
                "async a => JSON.stringify(await window.axe.run(a.context, a.options))",
                new { context = axeContext, options = axeOptions }).ConfigureAwait(false);
        }
        catch (PlaywrightException ex) when (!page.IsClosed && !ex.Message.Contains("closed", StringComparison.OrdinalIgnoreCase))
        {
            // axe-core rejects invalid configuration (unknown rule ids, invalid selectors).
            throw new InvalidOperationException("axe-core failed: " + ex.Message, ex);
        }

        return new AxeResult(Newtonsoft.Json.Linq.JObject.Parse(json));
    }

    private static void Collect(AxeResultItem[]? items, FindingStatus status, AccessibilityAuditOptions options, List<(AccessibilityFinding, string?)> pending, EngineOutput output)
    {
        foreach (var item in items ?? [])
        {
            var tags = item.Tags ?? [];
            var wcag = WcagCatalog.FromTags(tags);
            var bestPractice = tags.Contains(AccessibilityStandard.BestPracticeTag) && wcag.Count == 0;
            var nodes = item.Nodes ?? [];
            if (nodes.Length > options.MaxNodesPerRule)
            {
                output.Warnings.Add(string.Create(CultureInfo.InvariantCulture, $"{item.Id}: {nodes.Length} nodes reported; only the first {options.MaxNodesPerRule} are included."));
            }

            foreach (var node in nodes.Take(options.MaxNodesPerRule))
            {
                var (selector, path) = SelectorOf(node.Target);
                var finding = new AccessibilityFinding
                {
                    RuleId = item.Id,
                    Engine = EngineName,
                    Status = status,
                    Impact = ParseImpact(node.Impact ?? item.Impact),
                    Description = item.Description,
                    Help = item.Help,
                    HelpUrl = item.HelpUrl,
                    Remediation = Summary(node, status),
                    Wcag = wcag,
                    Tags = tags,
                    IsBestPractice = bestPractice,
                    Target = new FindingTarget { Selector = selector, SelectorPath = path, Html = node.Html },
                    Checks = Checks(node),
                };
                pending.Add((finding, path.Count > 1 ? null : selector));
            }
        }
    }

    private static (string? Selector, IReadOnlyList<string> Path) SelectorOf(AxeSelector? target)
    {
        if (target is null)
        {
            return (null, []);
        }

        var segments = target.FrameShadowSelectors?.SelectMany(s => s).ToArray() ?? [];
        if (segments.Length > 1)
        {
            return (string.Join(" >>> ", segments), segments);
        }

        return (target.Selector ?? target.ToString(), []);
    }

    internal static AccessibilityImpact? ParseImpact(string? impact) => impact?.ToLowerInvariant() switch
    {
        "critical" => AccessibilityImpact.Critical,
        "serious" => AccessibilityImpact.Serious,
        "moderate" => AccessibilityImpact.Moderate,
        "minor" => AccessibilityImpact.Minor,
        _ => null,
    };

    private static List<FindingCheck> Checks(AxeResultNode node)
    {
        var checks = new List<FindingCheck>();
        void Add(AxeResultCheck[]? source, string group)
        {
            foreach (var check in source ?? [])
            {
                checks.Add(new FindingCheck
                {
                    Id = check.Id,
                    Group = group,
                    Message = check.Message,
                    Impact = ParseImpact(check.Impact),
                    DataJson = check.Data is null ? null : JsonConvert.SerializeObject(check.Data, Formatting.None),
                    RelatedSelectors = check.RelatedNodes?.Select(r => r.Target?.ToString()).OfType<string>().ToArray() ?? [],
                });
            }
        }

        Add(node.Any, "any");
        Add(node.All, "all");
        Add(node.None, "none");
        return checks;
    }

    // Mirrors axe-core's failureSummary.
    private static string? Summary(AxeResultNode node, FindingStatus status)
    {
        var sb = new StringBuilder();
        var any = (node.Any ?? []).Select(c => c.Message).Where(m => !string.IsNullOrWhiteSpace(m)).ToArray();
        var all = (node.All ?? []).Concat(node.None ?? []).Select(c => c.Message).Where(m => !string.IsNullOrWhiteSpace(m)).ToArray();
        var prefix = status == FindingStatus.Inconclusive ? "Review manually; the engine could not decide. " : string.Empty;
        if (any.Length > 0)
        {
            sb.Append(prefix).Append("Fix any of the following:").AppendLine();
            foreach (var message in any)
            {
                sb.Append("  ").Append(message).AppendLine();
            }

            prefix = string.Empty;
        }

        if (all.Length > 0)
        {
            sb.Append(prefix).Append("Fix all of the following:").AppendLine();
            foreach (var message in all)
            {
                sb.Append("  ").Append(message).AppendLine();
            }
        }

        var text = sb.ToString().TrimEnd();
        return text.Length == 0 ? null : text;
    }
}
