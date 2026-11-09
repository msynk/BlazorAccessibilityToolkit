using BlazorAccessibilityToolkit.ComponentTesting;
using BlazorAccessibilityToolkit.Engines;
using BlazorAccessibilityToolkit.Playwright;
using BlazorAccessibilityToolkit.Playwright.Hosting;

namespace BlazorAccessibilityToolkit.Demo.WebApp.Lab;

/// <summary>The browser-side infrastructure of live runs.</summary>
public sealed record LabBrowserServices(AccessibilityBrowser Browser, BlazorTestHost Host, ComponentAuditSettings BrowserMarkup);

/// <summary>
/// Starts the browser, the component host and the axe-core markup engine on first use and shares them, like an
/// assembly fixture in a test project. A missing browser is reported to the page and retried on the next run.
/// </summary>
public sealed class LabBrowser : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly ILogger<LabBrowser> logger;
    private AccessibilityBrowser? browser;
    private BlazorTestHost? host;
    private PlaywrightMarkupAuditEngine? engine;
    private LabBrowserServices? services;

    public LabBrowser(ILogger<LabBrowser> logger) => this.logger = logger;

    /// <summary>The browser name and version once started, for display.</summary>
    public string? Description => browser is null ? null : $"{browser.Name} {browser.Version}";

    public async Task<LabBrowserServices> GetAsync(CancellationToken cancellationToken = default)
    {
        if (services is not null)
        {
            return services;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (services is not null)
            {
                return services;
            }

            browser ??= await AccessibilityBrowser.LaunchAsync();

            // The host serves this app's static web assets (blazor.web.js and the component library's files).
            var options = new BlazorTestHostOptions { Title = "Showcase component", ApplicationAssembly = typeof(Program).Assembly };
            options.Stylesheets.Add("_content/BlazorAccessibilityToolkit.Demo.Components/components.css");
            host ??= await BlazorTestHost.StartAsync(options, cancellationToken);

            engine ??= new PlaywrightMarkupAuditEngine(browser);
            var markup = new ComponentAuditSettings
            {
                Engines = [engine],
                Document = new MarkupDocumentOptions { Styles = [SourceCatalog.Asset("components.css")] },
            };

            logger.LogInformation("Live runs use {Browser} {Version}; component host at {Address}.", browser.Name, browser.Version, host.BaseAddress);
            return services = new LabBrowserServices(browser, host, markup);
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (host is not null)
        {
            await host.DisposeAsync();
        }

        if (engine is not null)
        {
            await engine.DisposeAsync();
        }

        if (browser is not null)
        {
            await browser.DisposeAsync();
        }

        gate.Dispose();
    }
}
