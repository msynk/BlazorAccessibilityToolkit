namespace BlazorAccessibilityToolkit.Playwright.Tests;

public sealed class VisualFocusOrderTests(BrowserFixture browser)
{
    private static readonly FocusOrderOptions Visual = new() { ReportVisualOrderMismatch = true };

    [Fact]
    public async Task A_stop_moved_by_css_order_is_reported()
    {
        const string html = """
            <main><h1>Checkout</h1>
              <div style="display:flex; flex-direction:column; gap:12px; width:200px">
                <button style="order:1">Back</button>
                <button style="order:3">Pay now</button>
                <button style="order:2">Apply coupon</button>
              </div>
            </main>
            """;
        await using var session = await browser.PageAsync(html);
        var result = await session.Page.CaptureFocusOrderAsync(Visual);

        var finding = Assert.Single(result.Findings, f => f.RuleId == "blazor-focus-order-visual-mismatch");
        Assert.Equal("Pay now", finding.Target.AccessibleName);
        Assert.All(result.Stops, s => Assert.NotNull(s.Bounds));
    }

    [Fact]
    public async Task Columns_and_natural_order_are_not_reported()
    {
        const string html = """
            <div style="display:grid; grid-template-columns: 200px 1fr; gap: 16px">
              <nav aria-label="Sections"><a href="#a">Alpha</a><br><a href="#b">Beta</a><br><a href="#c">Gamma</a></nav>
              <main><h1>Main</h1><button>One</button> <button>Two</button><p><button>Three</button></p></main>
            </div>
            """;
        await using var session = await browser.PageAsync(html);
        var result = await session.Page.CaptureFocusOrderAsync(Visual);

        Assert.DoesNotContain(result.Findings, f => f.RuleId == "blazor-focus-order-visual-mismatch");

        // Off by default.
        await using var other = await browser.PageAsync(html.Replace("<button>One</button> <button>Two</button>", "<button>Two</button>", StringComparison.Ordinal));
        Assert.DoesNotContain((await other.Page.CaptureFocusOrderAsync()).Findings, f => f.RuleId == "blazor-focus-order-visual-mismatch");
    }

    [Fact]
    public async Task Tab_stop_overlay_is_saved_as_a_screenshot_and_removed()
    {
        await using var session = await browser.PageAsync("<main><h1>x</h1><button>One</button> <button>Two</button> <button>Three</button></main>");
        var result = await session.Page.CaptureFocusOrderAsync();
        var path = Path.Combine(Path.GetTempPath(), "a11y-overlay-" + Guid.NewGuid().ToString("N"), "tab-stops.png");
        try
        {
            var saved = await result.SaveTabStopOverlayAsync(session.Page, path);

            var bytes = await File.ReadAllBytesAsync(saved);
            Assert.True(bytes.Length > 1000);
            Assert.Equal([0x89, 0x50, 0x4E, 0x47], bytes[..4]);
            Assert.Equal(0, await session.Page.Locator("[data-ba11y-tab-stops]").CountAsync());

            var audit = await result.ToAuditWithTabStopsAsync("buttons", session.Page, Path.Combine(Path.GetDirectoryName(path)!, "audit.png"));
            var attachment = Assert.Single(audit.Attachments);
            Assert.Equal("Tab stops of buttons: 3 stops in focus order.", attachment.Description);
            Assert.True(File.Exists(attachment.Path));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public async Task Reading_down_a_column_then_the_next_is_not_reported()
    {
        const string html = """
            <main><h1>Form</h1>
              <div style="display:grid; grid-template-columns: 200px 200px; grid-auto-flow: column; grid-template-rows: auto auto; gap: 12px">
                <button>Name</button><button>Email</button><button>Phone</button><button>Address</button>
              </div>
            </main>
            """;
        await using var session = await browser.PageAsync(html);
        var result = await session.Page.CaptureFocusOrderAsync(Visual);

        Assert.DoesNotContain(result.Findings, f => f.RuleId == "blazor-focus-order-visual-mismatch");
    }
}
