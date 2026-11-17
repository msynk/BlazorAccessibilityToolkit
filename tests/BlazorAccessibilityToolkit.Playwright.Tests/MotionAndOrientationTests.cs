namespace BlazorAccessibilityToolkit.Playwright.Tests;

public sealed class MotionAndOrientationTests(BrowserFixture browser)
{
    private const string Animations = """
        <style>
          @keyframes slide { from { transform: translateX(0); } to { transform: translateX(40px); } }
          @keyframes spin { to { transform: rotate(360deg); } }
          .banner { animation: slide 2s infinite alternate; }
          .spinner { width: 20px; height: 20px; animation: spin 1s linear infinite; }
          .respectful { animation: slide 2s infinite alternate; }
          @media (prefers-reduced-motion: reduce) { .respectful { animation: none; } }
        </style>
        <main><h1>Deals</h1>
          <p class="banner">Big sale!</p>
          <p class="respectful">Also on sale</p>
          <div class="spinner" role="status" aria-label="Loading"></div>
        </main>
        """;

    [Fact]
    public async Task Animations_that_ignore_reduced_motion_are_reported()
    {
        await using var session = await browser.PageAsync(Animations);
        await session.Page.EmulateMediaAsync(new() { ReducedMotion = Microsoft.Playwright.ReducedMotion.NoPreference });
        var audit = await session.Page.AuditMotionAsync();

        var finding = Assert.Single(audit.Findings);
        Assert.Equal("blazor-motion-not-reduced", finding.RuleId);
        Assert.Equal("p.banner", finding.Target.Selector);
        Assert.Equal("True", finding.Data["infinite"]);
        Assert.Contains("transform", finding.Data["properties"], StringComparison.Ordinal);
        Assert.Equal(AuditStatus.Completed, audit.Status);

        // The preference the page had before is restored.
        Assert.False(await session.Page.EvaluateAsync<bool>("matchMedia('(prefers-reduced-motion: reduce)').matches"));
    }

    [Fact]
    public async Task Pages_that_honor_reduced_motion_pass()
    {
        await using var session = await browser.PageAsync(Animations.Replace("class=\"banner\"", "class=\"respectful\"", StringComparison.Ordinal));
        var audit = await session.Page.AuditMotionAsync();
        Assert.Empty(audit.Findings);
        Assert.Contains("blazor-motion-not-reduced", audit.PassedRules);
    }

    [Fact]
    public async Task Rotate_your_device_overlays_are_reported()
    {
        var text = string.Concat(Enumerable.Repeat("Account settings and preferences. ", 20));
        var html = $$"""
            <style>
              .rotate { display: none; }
              @media (orientation: portrait) { .content { display: none; } .rotate { display: block; } }
            </style>
            <main><h1>Settings</h1><p class="content">{{text}}</p><p class="rotate">Please rotate your device to landscape.</p></main>
            """;
        await using var session = await browser.PageAsync(html);
        var audit = await session.Page.AuditOrientationAsync();

        Assert.Contains(audit.Findings, f => f.RuleId == "blazor-orientation-locked" && f.Data["orientation"] == "portrait" && f.Target.Selector == "p.rotate");
        Assert.Contains(audit.Findings, f => f.RuleId == "blazor-orientation-locked" && f.Target.Selector == "body");
        Assert.All(audit.Findings, f => Assert.Equal(["1.3.4"], f.Wcag.Select(w => w.Id)));
    }

    [Fact]
    public async Task Responsive_pages_pass_the_orientation_audit()
    {
        await using var session = await browser.PageAsync("<main><h1>Settings</h1><p>Works in both orientations.</p></main>");
        var before = session.Page.ViewportSize;
        var audit = await session.Page.AuditOrientationAsync();
        Assert.Empty(audit.Findings);
        Assert.Contains("blazor-orientation-locked", audit.PassedRules);
        Assert.Equal(before!.Width, session.Page.ViewportSize!.Width);
    }
}
