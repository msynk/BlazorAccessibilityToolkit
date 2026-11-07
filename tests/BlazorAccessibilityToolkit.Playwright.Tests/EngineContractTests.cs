using System.Runtime.CompilerServices;
using System.Text;

namespace BlazorAccessibilityToolkit.Playwright.Tests;

/// <summary>
/// Pins the axe-core version and the normalized findings for a fixed document. Upgrading Deque.AxeCore.Playwright (and
/// therefore axe-core) changes rule coverage; this test makes that change explicit and reviewable: update the golden file
/// deliberately (run with BLAZOR_A11Y_UPDATE_SNAPSHOTS=1) and describe the differences in the changelog.
/// </summary>
public sealed class EngineContractTests(BrowserFixture browser)
{
    public const string PinnedAxeMajorMinor = "4.14";

    private const string Document = """
        <main>
          <h1>Engine contract</h1>
          <button class="icon"></button>
          <img src="logo.png">
          <a href="/x"></a>
          <input type="text" class="unlabelled">
          <div role="buton">typo role</div>
          <span aria-labeledby="x">typo attribute</span>
          <ul><div>invalid child</div></ul>
          <p style="color:#aaa;background:#fff">low contrast</p>
          <div aria-hidden="true"><button>focusable hidden</button></div>
          <label for="missing">Orphan label</label><input aria-label="ok">
          <select><option>a</option></select>
          <table><tr><th></th><td>cell</td></tr></table>
        </main>
        """;

    [Fact]
    public async Task Axe_core_version_is_pinned()
    {
        await using var session = await browser.PageAsync("<main><h1>x</h1></main>");
        var audit = await session.Page.AuditAccessibilityAsync();
        var axe = Assert.Single(audit.Engines, e => e.Name == "axe-core");
        Assert.StartsWith(PinnedAxeMajorMinor + ".", axe.Version, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Normalized_findings_match_the_golden_file()
    {
        await using var session = await browser.PageAsync(Document);
        var audit = await session.Page.AuditAccessibilityAsync(new AccessibilityAuditOptions { IncludeBestPractices = true });
        Assert.Equal(AuditStatus.Completed, audit.Status);

        var actual = new StringBuilder();
        foreach (var finding in audit.Findings.OrderBy(f => f.Engine, StringComparer.Ordinal).ThenBy(f => f.RuleId, StringComparer.Ordinal).ThenBy(f => f.Target.Selector, StringComparer.Ordinal))
        {
            actual.Append(finding.Engine).Append(" | ").Append(finding.RuleId).Append(" | ").Append(finding.Status).Append(" | ")
                .Append(finding.Impact?.ToString() ?? "-").Append(" | ").Append(finding.Target.Selector).Append(" | ")
                .Append(string.Join(",", finding.Wcag.Select(w => w.Id))).Append('\n');
        }

        var path = GoldenPath();
        if (Environment.GetEnvironmentVariable("BLAZOR_A11Y_UPDATE_SNAPSHOTS") is "1" or "true")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, actual.ToString());
        }

        Assert.True(File.Exists(path), $"Golden file '{path}' is missing. Run once with BLAZOR_A11Y_UPDATE_SNAPSHOTS=1, review and commit it.{Environment.NewLine}{actual}");

        var expected = (await File.ReadAllTextAsync(path)).ReplaceLineEndings("\n");
        if (expected != actual.ToString())
        {
            Assert.Fail("axe-core output changed for the contract document. Review the differences, then update the golden file with BLAZOR_A11Y_UPDATE_SNAPSHOTS=1." + Environment.NewLine + AriaSnapshots.Diff(expected, actual.ToString()));
        }
    }

    private static string GoldenPath([CallerFilePath] string source = "") =>
        Path.Combine(Path.GetDirectoryName(source)!, "EngineContract", $"axe-{PinnedAxeMajorMinor}-{BrowserFixture.Current}.txt");
}
