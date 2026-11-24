using System.Diagnostics;
using System.Runtime.CompilerServices;
using BlazorAccessibilityToolkit.Playwright;
using Microsoft.Playwright;

namespace BlazorAccessibilityToolkit.Hybrid.Tests;

/// <summary>
/// A Blazor Hybrid app (BlazorWebView in Windows Forms) audited through WebView2 with the same API as a page in a
/// browser. Windows only; skipped where WebView2 is not available.
/// </summary>
public sealed class BlazorHybridAppTests
{
    [Fact]
    public async Task Audits_the_blazor_content_of_a_hybrid_app()
    {
        SkipUnlessWebView2();
        await using var app = await BlazorHybridApp.LaunchAsync(TestAppPath());
        await app.Page.GetByRole(AriaRole.Heading, new() { Name = "Orders" }).WaitForAsync();

        var audit = await app.Page.AuditAccessibilityAsync(label: new AuditLabel { Name = "hybrid orders", Scenario = "hybrid" });

        audit.ShouldHaveExecuted();
        var finding = audit.ShouldHaveFinding("image-alt");
        Assert.Equal("#logo", finding.Target.Selector);
        Assert.Single(audit.Findings, f => f.Status == FindingStatus.Violation);
        Assert.StartsWith("https://0.0.0.", app.Page.Url, StringComparison.Ordinal); // 0.0.0.1 since .NET 10, 0.0.0.0 before
    }

    [Fact]
    public async Task Keyboard_scenarios_and_focus_order_work_in_the_webview()
    {
        SkipUnlessWebView2();
        await using var app = await BlazorHybridApp.LaunchAsync(TestAppPath());
        var page = app.Page;
        await page.GetByRole(AriaRole.Button, new() { Name = "Add order" }).WaitForAsync();

        await KeyboardScenario.For(page)
            .FocusOn(page.GetByRole(AriaRole.Button, new() { Name = "Add order" }))
            .PressEnter()
            .ExpectFocused("button", "Add order")
            .RunAsync();
        await page.GetByRole(AriaRole.Status).Filter(new() { HasText = "1 orders" }).WaitForAsync();

        var order = await page.CaptureFocusOrderAsync();
        order.ShouldMatch("button Add order", "textbox Customer").ShouldNotTrapFocus();
    }

    [Fact]
    public async Task Connects_to_an_app_that_is_already_running_and_leaves_it_running()
    {
        SkipUnlessWebView2();
        var port = 9400 + Random.Shared.Next(500);
        var userData = Path.Combine(Path.GetTempPath(), "blazor-a11y-hybrid-" + Guid.NewGuid().ToString("N"));
        var start = new ProcessStartInfo(TestAppPath()) { UseShellExecute = false };
        start.Environment[BlazorHybridApp.AdditionalArgumentsVariable] = $"--remote-debugging-port={port}";
        start.Environment["WEBVIEW2_USER_DATA_FOLDER"] = userData;
        using var process = Process.Start(start)!;
        try
        {
            BlazorHybridApp? app = null;
            for (var attempt = 0; app is null; attempt++)
            {
                try
                {
                    app = await BlazorHybridApp.ConnectAsync(new Uri($"http://127.0.0.1:{port}"), new BlazorHybridAppOptions { StartupTimeout = TimeSpan.FromSeconds(5) });
                }
                catch (PlaywrightException) when (attempt < 20)
                {
                    await Task.Delay(500, TestContext.Current.CancellationToken);
                }
            }

            await using (app)
            {
                Assert.Null(app.Process);
                Assert.Equal("Orders", await app.Page.TitleAsync());
            }

            Assert.False(process.HasExited);
        }
        finally
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(10_000);
            try
            {
                Directory.Delete(userData, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task Reports_a_missing_app_or_platform_clearly()
    {
        if (!OperatingSystem.IsWindows())
        {
            await Assert.ThrowsAsync<PlatformNotSupportedException>(() => BlazorHybridApp.LaunchAsync("app.exe"));
            return;
        }

        await Assert.ThrowsAsync<FileNotFoundException>(() => BlazorHybridApp.LaunchAsync(Path.Combine(Path.GetTempPath(), "missing-hybrid-app.exe")));
    }

    private static void SkipUnlessWebView2()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Blazor Hybrid apps run in WebView2, which is Windows only.");
        }
        else if (!WebView2RuntimeInstalled())
        {
            Assert.Skip("The WebView2 runtime is not installed.");
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool WebView2RuntimeInstalled()
    {
        const string client = @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
        using var machine = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(client) ?? Microsoft.Win32.Registry.CurrentUser.OpenSubKey(client.Replace(@"WOW6432Node\", string.Empty, StringComparison.Ordinal));
        return machine?.GetValue("pv") is string version && version != "0.0.0.0";
    }

    // The app is built with the tests (same configuration), in its own output folder.
    private static string TestAppPath([CallerFilePath] string source = "")
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        return Path.Combine(Path.GetDirectoryName(source)!, "..", "BlazorAccessibilityToolkit.HybridTestApp", "bin", configuration, "net10.0-windows", "BlazorAccessibilityToolkit.HybridTestApp.exe");
    }
}
