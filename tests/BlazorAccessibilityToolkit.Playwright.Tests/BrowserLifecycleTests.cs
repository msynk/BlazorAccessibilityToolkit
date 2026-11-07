namespace BlazorAccessibilityToolkit.Playwright.Tests;

public sealed class BrowserLifecycleTests
{
    [Fact]
    public async Task Browser_is_relaunched_after_it_exits_and_running_work_fails_visibly()
    {
        await using var browser = await AccessibilityBrowser.LaunchAsync();
        var session = await browser.NewSessionAsync();
        await session.Page.SetContentAsync("<main><h1>x</h1></main>");

        await browser.Browser.CloseAsync();   // simulate a crashed browser process

        var orphaned = await session.Page.AuditAccessibilityAsync();
        Assert.Equal(AuditStatus.Failed, orphaned.Status);

        await using var next = await browser.NewSessionAsync();
        await next.Page.SetContentAsync("<main><h1>x</h1><button></button></main>");
        var audit = await next.Page.AuditAccessibilityAsync();
        audit.ShouldHaveFinding("button-name");
        Assert.Equal(1, browser.Relaunches);
    }

    [Fact]
    public async Task Unknown_browser_names_are_rejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => AccessibilityBrowser.LaunchAsync(new AccessibilityBrowserOptions { Browser = "netscape" }));
    }
}
