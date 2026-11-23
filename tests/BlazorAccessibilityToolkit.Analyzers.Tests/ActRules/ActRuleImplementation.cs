using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace BlazorAccessibilityToolkit.Analyzers.Tests.ActRules;

/// <summary>An ACT rule and the analyzer rule that implements it.</summary>
internal sealed record ActRuleMapping(string ActRuleId, string AnalyzerRuleId);

/// <summary>A W3C ACT test case: an HTML page and the outcome the rule must report for it.</summary>
internal sealed record ActTestCase(
    string RuleId,
    string RuleName,
    string TestcaseId,
    string TestcaseTitle,
    string Expected,
    string Url,
    [property: JsonPropertyName("file")] string File);

internal sealed record ActTestCaseSet(string Source, string License, string Retrieved, IReadOnlyList<ActTestCase> Testcases);

/// <summary>The analyzer's outcome for one test case.</summary>
internal sealed record ActOutcome(ActTestCase TestCase, string AnalyzerRuleId, string Outcome)
{
    /// <summary>
    /// ACT consistency: failed examples must be reported as failed; passed and inapplicable examples must not be. The
    /// analyzer cannot tell "passed" from "inapplicable", so it reports passed when it finds no failure.
    /// </summary>
    public bool IsFalseAlarm => TestCase.Expected != "failed" && Outcome == "failed";

    public bool IsMiss => TestCase.Expected == "failed" && Outcome != "failed";
}

/// <summary>
/// Runs the Razor analyzer on the W3C ACT Rules test cases of the ACT rules it implements and writes the implementation
/// report: an EARL (JSON-LD) file in the format the ACT Task Force collects, and a summary page.
/// </summary>
internal static class ActRuleImplementation
{
    public const string TestCaseSource = "https://www.w3.org/WAI/content-assets/wcag-act-rules/testcases.json";

    /// <summary>
    /// The ACT rules the analyzer implements, each with the analyzer rule that checks the same requirement. Only rules
    /// whose requirement the markup states are listed; rules that need rendering (contrast, visibility, focus) are
    /// covered by axe-core and the toolkit's browser checks, which Deque reports for axe-core separately.
    /// </summary>
    public static IReadOnlyList<ActRuleMapping> Mappings { get; } =
    [
        new("674b10", "BA11Y005"), // Role attribute has valid value
        new("5f99a7", "BA11Y004"), // ARIA attribute is defined in WAI-ARIA
        new("6a7281", "BA11Y022"), // ARIA state or property has valid value
        new("4e8ab6", "BA11Y021"), // Element with role attribute has required states and properties
        new("6cfa84", "BA11Y006"), // Element with aria-hidden has no content in sequential focus navigation
        new("b5c3f8", "BA11Y010"), // HTML page has lang attribute
        new("bf051a", "BA11Y028"), // HTML page lang attribute has valid language tag
        new("de46e4", "BA11Y028"), // Element with lang attribute has valid language tag
        new("73f2c2", "BA11Y019"), // autocomplete attribute has valid value
        new("97a4e1", "BA11Y008"), // Button has non-empty accessible name
        new("c487ae", "BA11Y008"), // Link has non-empty accessible name
        new("e086e5", "BA11Y007"), // Form field has non-empty accessible name
        new("23a2a8", "BA11Y002"), // Image has non-empty accessible name
        new("59796f", "BA11Y002"), // Image button has non-empty accessible name
        new("ffd0e9", "BA11Y009"), // Heading has non-empty accessible name
        new("cae760", "BA11Y011"), // Iframe element has non-empty accessible name
        new("b4f0c3", "BA11Y029"), // Meta viewport allows for zoom
        new("bc659a", "BA11Y030"), // Meta element has no refresh delay (with the 20-hour exception, as WCAG 2.2.1 has)
    ];

    /// <summary>Why the analyzer's outcome differs from the expected one, by test case id. Every difference needs one.</summary>
    public static IReadOnlyDictionary<string, string> KnownDifferences { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["d343bc6a2877b62d80153453c3781debc33e0b1d"] = "A focusable link in aria-hidden content passes only because a script moves focus away from it, which a markup check cannot see. Prefer `inert` or `tabindex=\"-1\"` for such focus sentinels.",
        ["c1570fd31970f22abcca6f32d75c1906058c1535"] = "An `<area>` without alt text is reported by BA11Y002 (Image has no text alternative) rather than BA11Y008.",
    };

    public static string Directory([System.Runtime.CompilerServices.CallerFilePath] string source = "") => Path.GetDirectoryName(source)!;

    public static string RepositoryRoot() => Path.GetFullPath(Path.Combine(Directory(), "..", "..", ".."));

    public static ActTestCaseSet LoadTestCases() =>
        JsonSerializer.Deserialize<ActTestCaseSet>(System.IO.File.ReadAllText(Path.Combine(Directory(), "act-testcases.json")), JsonOptions)!;

    public static IReadOnlyList<ActOutcome> Run(ActTestCaseSet set)
    {
        var outcomes = new List<ActOutcome>();
        foreach (var mapping in Mappings)
        {
            foreach (var testCase in set.Testcases.Where(t => t.RuleId == mapping.ActRuleId))
            {
                var markup = System.IO.File.ReadAllText(Path.Combine(Directory(), testCase.File));
                string outcome;
                try
                {
                    outcome = RazorAccessibilityAnalyzer.Analyze(markup).Any(r => r.Descriptor.Id == mapping.AnalyzerRuleId) ? "failed" : "passed";
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    outcome = "cantTell";
                }

                outcomes.Add(new ActOutcome(testCase, mapping.AnalyzerRuleId, outcome));
            }
        }

        return outcomes;
    }

    /// <summary>The EARL report: one test subject per test case page, one assertion per analyzer rule run on it.</summary>
    public static string Earl(IReadOnlyList<ActOutcome> outcomes)
    {
        var graph = new JsonArray
        {
            new JsonObject
            {
                ["@id"] = "https://github.com/msynk/BlazorAccessibilityToolkit",
                ["@type"] = new JsonArray("earl:Assertor", "earl:Software", "doap:Project"),
                ["doap:name"] = "Blazor Accessibility Toolkit Razor analyzer",
                ["doap:homepage"] = "https://github.com/msynk/BlazorAccessibilityToolkit",
            },
        };
        foreach (var page in outcomes.GroupBy(o => o.TestCase.Url).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var assertions = new JsonArray();
            foreach (var outcome in page.DistinctBy(o => o.AnalyzerRuleId).OrderBy(o => o.AnalyzerRuleId, StringComparer.Ordinal))
            {
                assertions.Add(new JsonObject
                {
                    ["@type"] = "Assertion",
                    ["mode"] = "earl:automatic",
                    ["assertedBy"] = "https://github.com/msynk/BlazorAccessibilityToolkit",
                    ["test"] = new JsonObject
                    {
                        ["@type"] = "TestCase",
                        ["title"] = outcome.AnalyzerRuleId,
                        ["@id"] = "https://github.com/msynk/BlazorAccessibilityToolkit/blob/main/docs/analyzers.md#" + outcome.AnalyzerRuleId.ToLowerInvariant(),
                    },
                    ["result"] = new JsonObject
                    {
                        ["@type"] = "TestResult",
                        ["outcome"] = "earl:" + outcome.Outcome,
                    },
                });
            }

            graph.Add(new JsonObject
            {
                ["@type"] = "TestSubject",
                ["source"] = page.Key,
                ["assertions"] = assertions,
            });
        }

        var report = new JsonObject
        {
            ["@context"] = "https://act-rules.github.io/earl-context.json",
            ["@graph"] = graph,
        };
        return report.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }).ReplaceLineEndings("\n") + "\n";
    }

    /// <summary>The summary page: consistency per rule, and each test case the analyzer gets wrong.</summary>
    public static string Summary(ActTestCaseSet set, IReadOnlyList<ActOutcome> outcomes)
    {
        var sb = new StringBuilder();
        sb.Append("""
            # ACT rules implementation report

            <!-- Generated by ActRulesTests in tests/BlazorAccessibilityToolkit.Analyzers.Tests. Run the tests with BLAZOR_A11Y_UPDATE_SNAPSHOTS=1 to update it. -->

            The [W3C ACT Rules](https://www.w3.org/WAI/standards-guidelines/act/rules/) describe how to test accessibility requirements, and each comes with test cases: pages that the rule must report as failed, passed or inapplicable. This report runs the toolkit's [Razor analyzer](analyzers.md) on the test cases of the ACT rules it implements. The machine-readable results are in [act-rules/razor-analyzer.earl.json](act-rules/razor-analyzer.earl.json), in the EARL format that the ACT Task Force collects for its implementation pages.

            An implementation is **consistent** with a rule when it fails every failed example and fails no passed or inapplicable example. The analyzer cannot tell "passed" from "inapplicable", so it reports passed when it finds no failure.

            The analyzer only reads markup. It does not render the page, run scripts or apply style sheets, so it misses failures that depend on them, such as content hidden by a CSS class or a name set by a script. It reads inline `style` attributes, and it follows an `aria-labelledby` reference only to an element in the same file. axe-core runs on the rendered page in the toolkit's tests; Deque publishes [its own ACT implementation report](https://www.w3.org/WAI/standards-guidelines/act/implementations/axe-core/).

            """);
        sb.Append('\n');
        sb.Append(CultureInfo.InvariantCulture, $"Test cases: {set.Source}, retrieved {set.Retrieved}. License: {set.License}.\n\n");
        sb.Append("| ACT rule | Analyzer rule | Consistency | Failed examples found | False alarms |\n");
        sb.Append("| --- | --- | --- | --- | --- |\n");
        foreach (var mapping in Mappings)
        {
            var rows = outcomes.Where(o => o.TestCase.RuleId == mapping.ActRuleId && o.AnalyzerRuleId == mapping.AnalyzerRuleId).ToArray();
            var name = rows.FirstOrDefault()?.TestCase.RuleName ?? mapping.ActRuleId;
            var failed = rows.Count(o => o.TestCase.Expected == "failed");
            var found = rows.Count(o => o.TestCase.Expected == "failed" && o.Outcome == "failed");
            var falseAlarms = rows.Count(o => o.IsFalseAlarm);
            var consistency = falseAlarms > 0 ? "inconsistent" : found == failed ? "consistent" : found > 0 ? "incomplete" : "none found";
            sb.Append(CultureInfo.InvariantCulture, $"| [{Escape(name)}](https://www.w3.org/WAI/standards-guidelines/act/rules/{mapping.ActRuleId}/) | [{mapping.AnalyzerRuleId}](analyzers.md#{mapping.AnalyzerRuleId.ToLowerInvariant()}) | {consistency} | {found} of {failed} | {falseAlarms} |\n");
        }

        sb.Append("\n\"incomplete\" means the analyzer finds some failed examples and raises no false alarm, but reports the others as passed. ACT counts only rules with every failed example found as consistent; \"partially consistent\" needs the missed examples reported as cantTell.\n");

        var wrong = outcomes.Where(o => o.IsFalseAlarm || o.IsMiss).ToArray();
        if (wrong.Length > 0)
        {
            sb.Append("\n## Test cases with a different outcome\n\n");
            foreach (var outcome in wrong)
            {
                sb.Append(CultureInfo.InvariantCulture, $"- {Escape(outcome.TestCase.RuleName)}, [{Escape(outcome.TestCase.TestcaseTitle)}]({outcome.TestCase.Url}): {(outcome.IsMiss ? "not found" : "false alarm")}.");
                if (KnownDifferences.TryGetValue(outcome.TestCase.TestcaseId, out var reason))
                {
                    sb.Append(' ').Append(reason);
                }

                sb.Append('\n');
            }
        }

        return sb.ToString().ReplaceLineEndings("\n");
    }

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static string Escape(string text) => text.Replace("|", "\\|", StringComparison.Ordinal).Replace("`", string.Empty, StringComparison.Ordinal);
}
