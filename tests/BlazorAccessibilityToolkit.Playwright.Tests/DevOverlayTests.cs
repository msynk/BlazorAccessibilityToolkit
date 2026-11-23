using System.Net;
using BlazorAccessibilityToolkit.DevTools;
using BlazorAccessibilityToolkit.Playwright.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Playwright;

namespace BlazorAccessibilityToolkit.Playwright.Tests;

/// <summary>A host that maps the development overlay's endpoints and serves plain test pages that load it.</summary>
public sealed class DevOverlayHostFixture : IAsyncLifetime
{
    public BlazorTestHost Host { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var options = new BlazorTestHostOptions
        {
            ConfigureApp = app =>
            {
                app.MapAccessibilityDevTools();
                app.MapGet("/overlay-page", (HttpContext context) =>
                {
                    var query = context.Request.Query;
                    var attributes = string.Concat(
                        " data-ba11y-delay=\"", query["delay"].FirstOrDefault() ?? "100", "\"",
                        " data-ba11y-exclude='[\"#ignored\"]'",
                        query["axe"].FirstOrDefault() is { } axe ? $" data-ba11y-axe-src=\"{WebUtility.HtmlEncode(axe)}\"" : string.Empty);
                    var broken = query["clean"].Count > 0 ? string.Empty : "<img id=\"broken\" src=\"data:,\">";
                    var frame = query["frame"].Count > 0 ? "<iframe id=\"frame\" srcdoc=\"&lt;p&gt;Framed&lt;/p&gt;\" width=\"200\" height=\"80\"></iframe>" : string.Empty;
                    return Results.Content($$"""
                        <!DOCTYPE html>
                        <html lang="en">
                        <head>
                          <meta charset="utf-8">
                          <title>Overlay test</title>
                          <script type="module" src="/_blazor-a11y/devtools.js" data-ba11y-devtools{{attributes}}></script>
                        </head>
                        <body>
                          <main>
                            <h1>Overlay test</h1>
                            {{broken}}
                            {{frame}}
                            <div id="ignored"><img src="data:,"></div>
                            <p style="margin-top: 1500px" id="far">Far down</p>
                          </main>
                        </body>
                        </html>
                        """, "text/html; charset=utf-8");
                });
            },
        };
        Host = await BlazorTestHost.StartAsync(options);
    }

    public async ValueTask DisposeAsync() => await Host.DisposeAsync();
}

public sealed class DevOverlayTests(BrowserFixture browser, DevOverlayHostFixture fixture) : IClassFixture<DevOverlayHostFixture>
{
    [Fact]
    public async Task Overlay_stays_off_under_browser_automation_unless_turned_on()
    {
        await using var session = await browser.Browser.NewSessionAsync();
        await session.Page.GotoAsync(Url("overlay-page"));
        await session.Page.WaitForFunctionAsync("() => window.blazorA11yDevTools && window.blazorA11yDevTools.status !== 'starting'");

        Assert.Equal("off: automation", await session.Page.EvaluateAsync<string>("() => window.blazorA11yDevTools.status"));
        Assert.Equal(0, await session.Page.Locator("[data-ba11y-devtools-host]").CountAsync());
    }

    [Fact]
    public async Task Overlay_lists_violations_and_works_with_the_keyboard()
    {
        var messages = new System.Collections.Concurrent.ConcurrentQueue<string>();
        await using var session = await OpenAsync("overlay-page", page => page.Console += (_, message) => messages.Enqueue(message.Text));
        var page = session.Page;

        // The image inside #ignored is excluded, so exactly one element fails.
        var badge = page.GetByRole(AriaRole.Button, new() { Name = "1 a11y issue", Exact = true });
        await badge.WaitForAsync();
        await badge.ShouldHaveStateAsync("expanded", "false");

        var panel = page.GetByRole(AriaRole.Region, new() { Name = "Accessibility issues" });
        await KeyboardScenario.For(page)
            .FocusOn(badge)
            .PressEnter()
            .ExpectState(badge, "expanded", "true")
            .PressEscape()
            .ExpectFocused(badge)
            .ExpectState(badge, "expanded", "false")
            .RunAsync();

        await badge.ClickAsync();
        Assert.True(await panel.IsVisibleAsync());
        var text = await panel.InnerTextAsync();
        Assert.Contains("image-alt", text, StringComparison.Ordinal);
        Assert.Contains("WCAG 1.1.1", text, StringComparison.Ordinal);
        Assert.Contains("#broken", text, StringComparison.Ordinal);
        Assert.DoesNotContain("#ignored", text, StringComparison.Ordinal);
        Assert.Equal("_blank", await panel.GetByRole(AriaRole.Link, new() { Name = "Rule help for image-alt (opens in a new tab)" }).GetAttributeAsync("target"));

        await page.WaitForConditionAsync(() => messages.Any(m => m.Contains("[a11y] critical", StringComparison.Ordinal) && m.Contains("image-alt", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Overlay_checks_again_after_the_page_changes()
    {
        await using var session = await OpenAsync("overlay-page");
        var page = session.Page;
        await page.GetByRole(AriaRole.Button, new() { Name = "1 a11y issue", Exact = true }).WaitForAsync();

        await page.EvaluateAsync("() => document.getElementById('broken').alt = 'Logo'");
        await page.GetByRole(AriaRole.Button, new() { Name = "No a11y issues", Exact = true }).WaitForAsync();

        await page.EvaluateAsync("() => document.querySelector('main').insertAdjacentHTML('beforeend', '<input id=\"unlabeled\"><button id=\"empty\"></button>')");
        // The empty button fails button-name and target-size, the input fails label.
        await page.GetByRole(AriaRole.Button, new() { Name = "3 a11y issues", Exact = true }).WaitForAsync();
        Assert.Equal("button-name,label,target-size", await page.EvaluateAsync<string>("() => window.blazorA11yDevTools.results.violations.map(v => v.id).sort().join()"));

        // A changed count is announced through a status region.
        Assert.Equal("Accessibility check: 3 a11y issues", await page.GetByRole(AriaRole.Status).TextContentAsync());
    }

    [Fact]
    public async Task Show_scrolls_to_the_element_and_outlines_it()
    {
        await using var session = await OpenAsync("overlay-page");
        var page = session.Page;
        await page.EvaluateAsync("() => { const far = document.getElementById('far'); far.insertAdjacentHTML('afterend', '<img id=\"far-image\" src=\"data:,\" width=\"40\" height=\"40\">'); }");
        var badge = page.GetByRole(AriaRole.Button, new() { Name = "2 a11y issues", Exact = true });
        await badge.ClickAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Show element 2 failing image-alt" }).ClickAsync();

        var target = page.Locator("#far-image");
        await Assertions.Expect(target).ToBeInViewportAsync();
        var outline = page.Locator("[data-ba11y-devtools-host] .highlight");
        await page.WaitForConditionAsync(async () =>
        {
            var box = await outline.BoundingBoxAsync();
            var expected = await target.BoundingBoxAsync();
            return box is not null && expected is not null && Math.Abs(box.X - expected.X) < 2 && Math.Abs(box.Y - expected.Y) < 2 && Math.Abs(box.Width - expected.Width) < 2;
        });

        // Closing the panel removes the outline.
        await page.Keyboard.PressAsync("Escape");
        Assert.False(await outline.IsVisibleAsync());
    }

    [Fact]
    public async Task Show_outlines_a_frame_that_is_itself_the_failing_element()
    {
        await using var session = await OpenAsync("overlay-page?frame=1");
        var page = session.Page;
        await page.GetByRole(AriaRole.Button, new() { Name = "2 a11y issues", Exact = true }).ClickAsync();

        // frame-title reports the iframe element itself (target ["#frame"]), not an element inside it.
        await page.GetByRole(AriaRole.Button, new() { Name = "Show element 1 failing frame-title" }).ClickAsync();

        var outline = page.Locator("[data-ba11y-devtools-host] .highlight");
        var frame = page.Locator("#frame");
        await page.WaitForConditionAsync(async () =>
        {
            var box = await outline.BoundingBoxAsync();
            var expected = await frame.BoundingBoxAsync();
            return box is not null && expected is not null && Math.Abs(box.X - expected.X) < 2 && Math.Abs(box.Width - expected.Width) < 2;
        });
    }

    [Fact]
    public async Task Overlay_itself_has_no_detectable_violations()
    {
        // A long delay: the toolkit's own audit below must not run at the same time as the overlay's check.
        await using var session = await OpenAsync("overlay-page?delay=60000");
        var page = session.Page;
        var badge = page.GetByRole(AriaRole.Button, new() { Name = "1 a11y issue", Exact = true });
        await badge.ClickAsync();
        await page.WaitForFunctionAsync("() => window.blazorA11yDevTools.status === 'idle'");

        // The open panel, its rule list and buttons, in light and dark color schemes. The page's own defect is excluded.
        var options = new AccessibilityAuditOptions { ExcludeSelectors = ["#broken", "#ignored"] };
        (await page.AuditAccessibilityAsync(options, new AuditLabel { Scenario = "dev overlay", State = "open" })).ShouldHaveNoViolations(AccessibilityExpectation.Strict);
        await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });
        (await page.AuditAccessibilityAsync(options, new AuditLabel { Scenario = "dev overlay", State = "open, dark" })).ShouldHaveNoViolations(AccessibilityExpectation.Strict);
    }

    [Fact]
    public async Task Overlay_reports_when_axe_core_cannot_be_loaded()
    {
        await using var session = await OpenAsync("overlay-page?axe=/missing/axe.js");

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "a11y check failed", Exact = true }).WaitForAsync();
        Assert.Equal("error", await session.Page.EvaluateAsync<string>("() => window.blazorA11yDevTools.status"));

        // Page changes do not request the missing script again.
        await session.Page.EvaluateAsync("() => document.querySelector('main').append('changed')");
        await Task.Delay(400);
        Assert.Equal(1, await session.Page.Locator("script[data-ba11y-devtools-host]").CountAsync());
    }

    [Fact]
    public async Task Endpoints_serve_the_scripts_with_validation_caching()
    {
        using var http = new HttpClient { BaseAddress = fixture.Host.BaseAddress };

        using var axe = await http.GetAsync(new Uri("_blazor-a11y/axe.js", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, axe.StatusCode);
        Assert.Equal("text/javascript", axe.Content.Headers.ContentType?.MediaType);
        Assert.Contains("axe v4.", (await axe.Content.ReadAsStringAsync())[..300], StringComparison.Ordinal);
        Assert.True(axe.Headers.CacheControl?.NoCache);

        using var request = new HttpRequestMessage(HttpMethod.Get, "_blazor-a11y/devtools.js");
        request.Headers.IfNoneMatch.Add(axe.Headers.ETag!);
        using var different = await http.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, different.StatusCode);

        using var again = new HttpRequestMessage(HttpMethod.Get, "_blazor-a11y/devtools.js");
        again.Headers.IfNoneMatch.Add(different.Headers.ETag!);
        using var notModified = await http.SendAsync(again);
        Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
    }

    private string Url(string path) => new Uri(fixture.Host.BaseAddress, path).ToString();

    // Turns the overlay on despite browser automation, as a developer would with localStorage.
    // beforeNavigation subscribes to page events (such as console messages) before the overlay can raise them.
    private async Task<BrowserSession> OpenAsync(string path, Action<IPage>? beforeNavigation = null)
    {
        var session = await browser.Browser.NewSessionAsync();
        await session.Context.AddInitScriptAsync("localStorage.setItem('blazor-a11y-devtools', 'on')");
        beforeNavigation?.Invoke(session.Page);
        await session.Page.GotoAsync(Url(path));
        return session;
    }
}

internal static class PageConditionExtensions
{
    public static Task WaitForConditionAsync(this IPage page, Func<bool> condition) => page.WaitForConditionAsync(() => Task.FromResult(condition()));

    public static async Task WaitForConditionAsync(this IPage page, Func<Task<bool>> condition)
    {
        _ = page;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The condition was not met within 10 seconds.");
            }

            await Task.Delay(50);
        }
    }
}
