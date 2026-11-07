using BlazorAccessibilityToolkit.Playwright;

[assembly: AssemblyFixture(typeof(BlazorAccessibilityToolkit.Playwright.Tests.BrowserFixture))]

namespace BlazorAccessibilityToolkit.Playwright.Tests;

/// <summary>One browser per test assembly; each test gets its own isolated context.</summary>
public sealed class BrowserFixture : IAsyncLifetime
{
    /// <summary>Browser selected for this run (BLAZOR_A11Y_BROWSER, default chromium).</summary>
    public static string Current => (Environment.GetEnvironmentVariable(AccessibilityBrowserOptions.BrowserEnvironmentVariable) ?? "chromium").ToLowerInvariant();

    public AccessibilityBrowser Browser { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Browser = await AccessibilityBrowser.LaunchAsync();

    public async ValueTask DisposeAsync() => await Browser.DisposeAsync();

    public async Task<BrowserSession> PageAsync(string bodyHtml, string? head = null)
    {
        var session = await Browser.NewSessionAsync();
        await session.Page.SetContentAsync($"<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Test</title>{head}</head><body>{bodyHtml}</body></html>");
        return session;
    }
}
