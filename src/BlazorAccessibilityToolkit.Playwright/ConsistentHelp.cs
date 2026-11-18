using System.Globalization;
using System.Text.Json;
using BlazorAccessibilityToolkit.Playwright.Internal;
using BlazorAccessibilityToolkit.Rules;
using Microsoft.Playwright;

namespace BlazorAccessibilityToolkit.Playwright;

/// <summary>Settings for <see cref="ConsistentHelp.AuditConsistentHelpAsync"/>.</summary>
public sealed record ConsistentHelpOptions
{
    /// <summary>
    /// Regular expression (JavaScript syntax, case-insensitive) for the accessible names of help links and buttons.
    /// Default <c>\b(help|contact|support|faq|chat)\b</c>. <c>mailto:</c> and <c>tel:</c> links always count.
    /// </summary>
    public string NamePattern { get; init; } = "\\b(help|contact|support|faq|chat)\\b";

    /// <summary>A CSS selector for other help mechanisms, for example a chat widget (<c>#chat-launcher</c>).</summary>
    public string? Selector { get; init; }

    /// <summary>Work after each navigation, for example waiting for interactivity.</summary>
    public Func<IPage, Task>? BeforeAudit { get; init; }

    /// <summary>Audit options (suppressions).</summary>
    public AccessibilityAuditOptions? AuditOptions { get; init; }
}

/// <summary>
/// Consistent help (WCAG 3.2.6): help mechanisms that appear on several pages must keep the same relative order and
/// region. The audit opens each page, lists its help links, buttons, <c>mailto:</c> and <c>tel:</c> links in document
/// order with the landmark that contains them, and compares every page with the first page that has the most of them.
/// </summary>
public static class ConsistentHelp
{
    /// <summary>Audits the pages at <paramref name="paths"/> (relative to <paramref name="baseAddress"/>), for example the URLs of <see cref="BlazorRoutes.Discover(System.Reflection.Assembly[])"/>.</summary>
    public static async Task<AccessibilityAudit> AuditConsistentHelpAsync(this IPage page, Uri baseAddress, IEnumerable<string> paths, ConsistentHelpOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(baseAddress);
        ArgumentNullException.ThrowIfNull(paths);
        options ??= new ConsistentHelpOptions();
        var root = baseAddress.AbsoluteUri.EndsWith('/') ? baseAddress : new Uri(baseAddress.AbsoluteUri + "/");
        var builder = new AccessibilityAuditBuilder(
            "consistent help",
            new AccessibilityContext { Kind = AuditTargetKind.Page, Url = root.AbsoluteUri, Scenario = "consistent help", RenderMode = "page", Browser = page.Context.Browser?.BrowserType.Name, BrowserVersion = page.Context.Browser?.Version },
            options.AuditOptions);
        builder.AddEngine(ToolkitRules.EngineName, ToolkitScript.ToolkitVersion);

        var pages = new List<(string Path, HelpDto[] Mechanisms)>();
        foreach (var path in paths)
        {
            try
            {
                await page.GotoAsync(new Uri(root, path.TrimStart('/')).AbsoluteUri, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle }).ConfigureAwait(false);
                if (options.BeforeAudit is not null)
                {
                    await options.BeforeAudit(page).ConfigureAwait(false);
                }

                await ToolkitScript.EnsureAsync(page).ConfigureAwait(false);
                var json = await page.EvaluateAsync<JsonElement>("o => window.__blazorA11y.helpMechanisms(o)", new { pattern = options.NamePattern, selector = options.Selector }).ConfigureAwait(false);
                pages.Add((path, ToolkitScript.Deserialize<HelpDto[]>(json) ?? []));
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException or JsonException)
            {
                builder.AddError(ExecutionError.FromException("browser", ex));
            }
        }

        var reference = pages.Where(p => p.Mechanisms.Length > 0).OrderByDescending(p => p.Mechanisms.Length).FirstOrDefault();
        var reported = false;
        if (reference.Mechanisms is { Length: > 0 })
        {
            var referenceOrder = reference.Mechanisms.Select(m => m.Key).Distinct().ToList();
            foreach (var (path, mechanisms) in pages.Where(p => p.Path != reference.Path))
            {
                // Only mechanisms that appear on both pages are compared: 3.2.6 does not require every page to offer help.
                var shared = mechanisms.Where(m => referenceOrder.Contains(m.Key)).ToArray();
                var expectedOrder = referenceOrder.Where(k => shared.Any(m => m.Key == k)).Distinct().ToArray();
                var actualOrder = shared.Select(m => m.Key).Distinct().ToArray();
                var problems = new List<string>();
                if (!expectedOrder.SequenceEqual(actualOrder))
                {
                    problems.Add($"the order is {Names(actualOrder, mechanisms)}, but on {reference.Path} it is {Names(expectedOrder, reference.Mechanisms)}");
                }

                // A mechanism repeated on the page (in the header and the footer) is compared occurrence by occurrence.
                foreach (var group in shared.GroupBy(m => m.Key))
                {
                    var expectedRegions = reference.Mechanisms.Where(m => m.Key == group.Key).ToArray();
                    foreach (var (mechanism, expected) in group.Zip(expectedRegions))
                    {
                        if (expected.Region != mechanism.Region)
                        {
                            problems.Add($"\"{mechanism.Name}\" is in {Region(mechanism.Region)}, but on {reference.Path} in {Region(expected.Region)}");
                        }
                    }
                }

                if (problems.Count > 0)
                {
                    reported = true;
                    builder.AddFinding(ToolkitRules.HelpInconsistent.CreateFinding(
                        new FindingTarget { Selector = shared.FirstOrDefault()?.Selector },
                        remediation: $"On {path}, {string.Join("; ", problems)}. {ToolkitRules.HelpInconsistent.Remediation}",
                        data: new Dictionary<string, string> { ["page"] = path, ["reference"] = reference.Path, ["problems"] = problems.Count.ToString(CultureInfo.InvariantCulture) }));
                }
            }
        }

        if (!reported && !builder.HasErrors)
        {
            builder.AddPassedRule(ToolkitRules.HelpInconsistent.Id);
            if (reference.Mechanisms is not { Length: > 0 })
            {
                builder.AddWarning("No help mechanism was found on any page; pass ConsistentHelpOptions.NamePattern or Selector if your help links have other names.");
            }
        }

        return PlaywrightAccessibility.Persist(builder.Build());
    }

    private static string Names(IEnumerable<string> keys, IReadOnlyList<HelpDto> mechanisms) =>
        string.Join(", ", keys.Select(k => $"\"{mechanisms.First(m => m.Key == k).Name}\""));

    private static string Region(string region) => region == "none" ? "no landmark" : $"the {region} landmark";

    private sealed class HelpDto
    {
        public string Key { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Kind { get; set; } = string.Empty;

        public string Region { get; set; } = string.Empty;

        public string? Selector { get; set; }
    }
}
