using BlazorAccessibilityToolkit.ComponentTesting;
using BlazorAccessibilityToolkit.Engines;
using BlazorAccessibilityToolkit.Playwright;
using BlazorAccessibilityToolkit.Playwright.Hosting;

[assembly: AssemblyFixture(typeof(SampleApp.ComponentTests.TestEnvironment))]

namespace SampleApp.ComponentTests;

/// <summary>
/// Shared, read-only test infrastructure: one browser, one component host, and immutable audit settings.
/// Every test still gets its own browser context, bUnit context and component registration.
/// </summary>
public sealed class TestEnvironment : IAsyncLifetime
{
    public AccessibilityBrowser Browser { get; private set; } = null!;

    public BlazorTestHost Host { get; private set; } = null!;

    /// <summary>bUnit markup audits with axe-core in a real browser engine and the component stylesheet.</summary>
    public ComponentAuditSettings BrowserMarkup { get; private set; } = null!;

    private PlaywrightMarkupAuditEngine markupEngine = null!;

    public async ValueTask InitializeAsync()
    {
        Browser = await AccessibilityBrowser.LaunchAsync();

        var hostOptions = new BlazorTestHostOptions { Title = "Sample components" };
        hostOptions.Stylesheets.Add("_content/SampleComponents/components.css");
        Host = await BlazorTestHost.StartAsync(hostOptions);

        markupEngine = new PlaywrightMarkupAuditEngine(Browser);
        BrowserMarkup = new ComponentAuditSettings
        {
            Engines = [markupEngine],
            Document = new MarkupDocumentOptions { StylesheetFiles = [Path.Combine(AppContext.BaseDirectory, "assets", "components.css")] },
        };
    }

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
        await markupEngine.DisposeAsync();
        await Browser.DisposeAsync();
    }
}
