using System.Diagnostics;
using BlazorAccessibilityToolkit.Playwright;
using BlazorAccessibilityToolkit.Playwright.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Playwright;

namespace BlazorAccessibilityToolkit.Demo.WebApp.Lab;

/// <summary>Runs live examples and turns their exceptions into outcomes the page can explain.</summary>
public sealed class LabRunner(LabBrowser browser, IServer server)
{
    // Browser runs are heavy; a few at a time keeps a shared demo responsive.
    private readonly SemaphoreSlim concurrency = new(3, 3);

    public async Task<LabResult> RunAsync(LabExample example, CancellationToken cancellationToken = default)
    {
        await concurrency.WaitAsync(cancellationToken);
        var stopwatch = Stopwatch.StartNew();
        LabEnvironment? env = null;
        try
        {
            env = new LabEnvironment(example.NeedsBrowser ? await browser.GetAsync(cancellationToken) : null, AppAddress());

            // Off the circuit's synchronization context, like a test runner: bUnit has its own renderer and dispatcher.
            await Task.Run(() => example.Run(env), cancellationToken);
            return Result(LabOutcome.Passed, null);
        }
        catch (BrowserNotAvailableException ex)
        {
            return Result(LabOutcome.Unavailable, ex.Message);
        }
        catch (AccessibilityAuditFailedException ex)
        {
            // The audit itself could not run (engine error, timeout): an error, never a pass or a finding.
            return Result(LabOutcome.Error, ex.Message);
        }
        catch (AccessibilityAssertionException ex)
        {
            return Result(LabOutcome.Failed, ex.Message);
        }
        catch (Exception ex) when (ex is PlaywrightException or HostedComponentException or TimeoutException or InvalidOperationException)
        {
            return Result(LabOutcome.Error, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            concurrency.Release();
        }

        LabResult Result(LabOutcome outcome, string? message) => new(outcome, message, env?.Artifacts ?? [], stopwatch.Elapsed);
    }

    // Where this app listens, so page-audit examples can open its pages. Wildcard bindings are browsed through localhost.
    private Uri? AppAddress()
    {
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
        var address = addresses.FirstOrDefault(a => a.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) ?? addresses.FirstOrDefault();
        if (address is null)
        {
            return null;
        }

        var builder = new UriBuilder(address.Replace("://+", "://localhost").Replace("://*", "://localhost"));
        if (builder.Host is "[::]" or "0.0.0.0")
        {
            builder.Host = "localhost";
        }

        builder.Path = "/";
        return builder.Uri;
    }
}
