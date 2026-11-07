using System.Net;
using BlazorAccessibilityToolkit.Playwright;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

[assembly: AssemblyFixture(typeof(SampleApp.EndToEndTests.AppFixture))]

namespace SampleApp.EndToEndTests;

/// <summary>Runs the real application on Kestrel (random port) and shares one browser.</summary>
public sealed class AppFixture : IAsyncLifetime
{
    private WebApplicationFactory<Program>? _factory;

    public AccessibilityBrowser Browser { get; private set; } = null!;

    public Uri BaseAddress { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development"));

        // Port 0 on the loopback interface: the OS picks a free port, so parallel runs never collide.
        _factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        _factory.StartServer();
        BaseAddress = _factory.ClientOptions.BaseAddress;
        Browser = await AccessibilityBrowser.LaunchAsync();
    }

    /// <summary>Opens <paramref name="path"/> in a new isolated browser context and waits for Blazor to become interactive.</summary>
    public async Task<BrowserSession> OpenAsync(string path)
    {
        var session = await Browser.NewSessionAsync();
        await session.Page.GotoAsync(new Uri(BaseAddress, path).ToString());

        // Prerendered markup appears before the circuit connects; key presses sent earlier would be lost.
        // The app's layout exposes when it has become interactive (see MainLayout.razor).
        await session.Page.Locator("main[data-interactive=true]").WaitForAsync();
        return session;
    }

    public async ValueTask DisposeAsync()
    {
        await Browser.DisposeAsync();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }
}
