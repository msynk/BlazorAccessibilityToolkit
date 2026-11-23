using System.Text.Json;
using System.Text.Json.Nodes;

namespace BlazorAccessibilityToolkit.Analyzers.Tests.ActRules;

/// <summary>
/// The Razor analyzer against the W3C ACT Rules test cases (a snapshot in this folder). The generated implementation
/// report is committed (docs/act-rules.md and docs/act-rules/razor-analyzer.earl.json); a change in any outcome fails
/// this test until the report is regenerated with BLAZOR_A11Y_UPDATE_SNAPSHOTS=1 and the difference is reviewed.
/// </summary>
public sealed class ActRulesTests
{
    private static readonly string SummaryPath = Path.Combine(ActRuleImplementation.RepositoryRoot(), "docs", "act-rules.md");

    private static readonly string EarlPath = Path.Combine(ActRuleImplementation.RepositoryRoot(), "docs", "act-rules", "razor-analyzer.earl.json");

    private static bool Update => Environment.GetEnvironmentVariable("BLAZOR_A11Y_UPDATE_SNAPSHOTS") is "1" or "true";

    [Fact]
    public void Implementation_report_matches_the_committed_one()
    {
        var set = ActRuleImplementation.LoadTestCases();
        var outcomes = ActRuleImplementation.Run(set);
        var summary = ActRuleImplementation.Summary(set, outcomes);
        var earl = ActRuleImplementation.Earl(outcomes);

        if (Update)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(EarlPath)!);
            File.WriteAllText(SummaryPath, summary);
            File.WriteAllText(EarlPath, earl);
        }

        Assert.Equal(File.ReadAllText(SummaryPath).ReplaceLineEndings("\n"), summary);
        Assert.Equal(File.ReadAllText(EarlPath).ReplaceLineEndings("\n"), earl);
    }

    [Fact]
    public void Every_difference_from_the_expected_outcome_is_explained()
    {
        var differences = ActRuleImplementation.Run(ActRuleImplementation.LoadTestCases())
            .Where(o => o.IsFalseAlarm || o.IsMiss)
            .Select(o => o.TestCase.TestcaseId)
            .Order(StringComparer.Ordinal);

        Assert.Equal(ActRuleImplementation.KnownDifferences.Keys.Order(StringComparer.Ordinal), differences);
    }

    [Fact]
    public void Every_mapped_rule_has_test_cases_and_the_analyzer_rule_exists()
    {
        var set = ActRuleImplementation.LoadTestCases();
        var analyzerRules = new RazorAccessibilityAnalyzer().SupportedDiagnostics.Select(d => d.Id).ToHashSet();
        foreach (var mapping in ActRuleImplementation.Mappings)
        {
            Assert.Contains(mapping.AnalyzerRuleId, analyzerRules);
            Assert.Contains(set.Testcases, t => t.RuleId == mapping.ActRuleId && t.Expected == "failed");
        }

        Assert.All(set.Testcases, t => Assert.True(File.Exists(Path.Combine(ActRuleImplementation.Directory(), t.File)), t.File));
        Assert.Equal(set.Testcases.Count, set.Testcases.Select(t => (t.RuleId, t.TestcaseId)).Distinct().Count());
    }

    [Fact]
    public void Earl_report_is_valid_json_ld_with_one_subject_per_test_case()
    {
        var outcomes = ActRuleImplementation.Run(ActRuleImplementation.LoadTestCases());
        var report = JsonNode.Parse(ActRuleImplementation.Earl(outcomes))!;

        Assert.Equal("https://act-rules.github.io/earl-context.json", report["@context"]!.GetValue<string>());
        var subjects = report["@graph"]!.AsArray().Where(n => n!["@type"]!.ToJsonString() == "\"TestSubject\"").ToArray();
        Assert.Equal(outcomes.Select(o => o.TestCase.Url).Distinct().Count(), subjects.Length);
        Assert.All(subjects, s => Assert.All(s!["assertions"]!.AsArray(), a => Assert.Matches("^earl:(passed|failed|cantTell)$", a!["result"]!["outcome"]!.GetValue<string>())));
    }

    /// <summary>Refreshes the test case snapshot from the W3C. Runs only with BLAZOR_A11Y_UPDATE_ACT_TESTCASES=1.</summary>
    [Fact]
    public async Task Update_the_test_case_snapshot()
    {
        if (Environment.GetEnvironmentVariable("BLAZOR_A11Y_UPDATE_ACT_TESTCASES") is not ("1" or "true"))
        {
            Assert.Skip("Set BLAZOR_A11Y_UPDATE_ACT_TESTCASES=1 to download the W3C ACT test cases.");
        }

        var cancellationToken = TestContext.Current.CancellationToken;
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(1) };

        // The W3C server answers 429 to clients that ask too often: wait between requests and as long as it asks.
        async Task<byte[]> GetAsync(string url)
        {
            for (var attempt = 1; ; attempt++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
                using var response = await http.GetAsync(new Uri(url), cancellationToken);
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests && attempt < 10)
                {
                    await Task.Delay(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(15 * attempt), cancellationToken);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsByteArrayAsync(cancellationToken);
            }
        }

        using var index = JsonDocument.Parse(await GetAsync(ActRuleImplementation.TestCaseSource));
        var ruleIds = ActRuleImplementation.Mappings.Select(m => m.ActRuleId).ToHashSet(StringComparer.Ordinal);
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var testCases = new List<ActTestCase>();
        foreach (var item in index.RootElement.GetProperty("testcases").EnumerateArray())
        {
            var ruleId = item.GetProperty("ruleId").GetString()!;
            var id = item.GetProperty("testcaseId").GetString()!;
            if (!ruleIds.Contains(ruleId) || testCases.Any(t => t.RuleId == ruleId && t.TestcaseId == id))
            {
                continue;
            }

            // Some rules list a few test cases twice, under an older name; the first name is the current one.
            var name = names.TryGetValue(ruleId, out var known) ? known : names[ruleId] = item.GetProperty("ruleName").GetString()!.Replace("`", string.Empty, StringComparison.Ordinal);
            var url = item.GetProperty("url").GetString()!;
            testCases.Add(new ActTestCase(ruleId, name, id, item.GetProperty("testcaseTitle").GetString()!, item.GetProperty("expected").GetString()!, url, $"testcases/{ruleId}/{id}{Path.GetExtension(url)}"));
        }

        // Test case files never change: a changed test case gets a new id (a hash of its content). So only new files are
        // downloaded, slowly enough for the W3C server, and files of removed test cases are deleted.
        var folder = Path.Combine(ActRuleImplementation.Directory(), "testcases");
        var wanted = testCases.Select(t => Path.GetFullPath(Path.Combine(ActRuleImplementation.Directory(), t.File))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(folder))
        {
            foreach (var stale in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Where(f => !wanted.Contains(Path.GetFullPath(f))).ToArray())
            {
                File.Delete(stale);
            }
        }

        foreach (var testCase in testCases)
        {
            var path = Path.Combine(ActRuleImplementation.Directory(), testCase.File);
            if (File.Exists(path))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, await GetAsync(testCase.Url), cancellationToken);
        }

        var set = new ActTestCaseSet(
            ActRuleImplementation.TestCaseSource,
            index.RootElement.GetProperty("license").GetString()!,
            DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            [.. testCases.OrderBy(t => t.RuleId, StringComparer.Ordinal)]);
        await File.WriteAllTextAsync(Path.Combine(ActRuleImplementation.Directory(), "act-testcases.json"), JsonSerializer.Serialize(set, ActRuleImplementation.JsonOptions).ReplaceLineEndings("\n") + "\n", cancellationToken);
    }
}
