using System.Diagnostics;
using System.Globalization;
using BlazorAccessibilityToolkit.ComponentTesting.Tests.Components;

namespace BlazorAccessibilityToolkit.ComponentTesting.Tests;

/// <summary>
/// Execution time and memory of representative component suites. The bounds are deliberately generous (they catch
/// order-of-magnitude regressions, not noise); the measured numbers are written to the test output and to
/// <c>performance.txt</c> in the test output directory.
/// </summary>
public sealed class PerformanceTests(BrowserEngineFixture browser)
{
    [Fact]
    public async Task Browserless_markup_audits_are_fast_and_do_not_leak()
    {
        const int count = 200;
        await WarmUpAsync(ComponentAuditSettings.Default);
        var before = Memory();
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < count; i++)
        {
            await using var fixture = await AccessibilityFixture.For<MarkupProblems>().BuildAsync();
            Assert.Equal(AuditStatus.Completed, (await fixture.AuditAsync()).Status);
        }

        stopwatch.Stop();
        var growth = Memory() - before;
        var perAudit = stopwatch.Elapsed.TotalMilliseconds / count;
        Report($"browserless: {count} fixtures+audits, {perAudit:0.0} ms each, managed memory growth {growth / 1024.0 / 1024.0:0.0} MB");

        Assert.True(perAudit < 250, $"Browserless audits took {perAudit:0.0} ms each.");
        Assert.True(growth < 64 * 1024 * 1024, $"Managed memory grew by {growth / 1024 / 1024} MB after {count} audits.");
    }

    [Fact]
    public async Task Browser_engine_markup_audits_reuse_the_browser()
    {
        const int count = 30;
        await WarmUpAsync(browser.Settings);
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < count; i++)
        {
            await using var fixture = await AccessibilityFixture.For<Toolbar>().WithSettings(browser.Settings).BuildAsync();
            Assert.Equal(AuditStatus.Completed, (await fixture.AuditAsync()).Status);
        }

        stopwatch.Stop();
        var perAudit = stopwatch.Elapsed.TotalMilliseconds / count;
        Report($"browser ({browser.Browser.Name}): {count} fixtures+audits with axe-core, {perAudit:0.0} ms each");
        Assert.True(perAudit < 5000, $"Browser audits took {perAudit:0.0} ms each.");
    }

    [Fact]
    public async Task Parallel_browser_audits_complete()
    {
        var stopwatch = Stopwatch.StartNew();
        var audits = await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
        {
            await using var fixture = await AccessibilityFixture.For<Toolbar>().WithSettings(browser.Settings).BuildAsync();
            return await fixture.AuditAsync();
        }));
        Report($"browser ({browser.Browser.Name}): 12 parallel audits in {stopwatch.Elapsed.TotalMilliseconds:0} ms");
        Assert.All(audits, a => a.ShouldHaveExecuted());
        Assert.All(audits, a => a.ShouldHaveFinding("button-name"));
    }

    private static async Task WarmUpAsync(ComponentAuditSettings settings)
    {
        await using var fixture = await AccessibilityFixture.For<Toolbar>().WithSettings(settings).BuildAsync();
        await fixture.AuditAsync();
    }

    private static long Memory()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return GC.GetTotalMemory(forceFullCollection: true);
    }

    private static void Report(string line)
    {
        Xunit.TestContext.Current.TestOutputHelper?.WriteLine(line);
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "performance.txt"), string.Create(CultureInfo.InvariantCulture, $"{DateTime.UtcNow:O} {line}{Environment.NewLine}"));
    }
}
