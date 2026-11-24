using System.Diagnostics;
using BlazorAccessibilityToolkit.Playwright;
using BlazorAccessibilityToolkit.Windows;

namespace BlazorAccessibilityToolkit.Hybrid.Tests;

/// <summary>Axe.Windows audits of the native window around the BlazorWebView. Windows only (each test skips elsewhere).</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class NativeAuditTests
{
    [Fact]
    public async Task Reports_native_controls_without_a_name_and_skips_the_web_view()
    {
        TestApp.SkipUnlessWebView2();
        await using var app = await BlazorHybridApp.LaunchAsync(TestApp.Path());

        var audit = await app.AuditNativeWindowAsync();

        audit.ShouldHaveExecuted();
        Assert.Contains(audit.Engines, e => e.Name == "axe-windows");
        var finding = Assert.Single(audit.Findings);
        Assert.Equal("NameNotNull", finding.RuleId);
        Assert.Equal(FindingStatus.Violation, finding.Status);
        Assert.Equal("4.1.2", Assert.Single(finding.Wcag).Id);
        Assert.Equal("button", finding.Target.Role);
        Assert.EndsWith("button #refreshButton", finding.Target.Selector, StringComparison.Ordinal);
        Assert.Equal("WinForm", finding.Data["FrameworkId"]);
        Assert.False(string.IsNullOrWhiteSpace(finding.Remediation));

        // The web view's own findings belong to the browser audits.
        Assert.Contains(audit.Warnings, w => w.Contains("inside WebView2 were skipped", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rules_can_be_turned_off()
    {
        TestApp.SkipUnlessWebView2();
        await using var app = await BlazorHybridApp.LaunchAsync(TestApp.Path());

        var audit = await app.AuditNativeWindowAsync(new NativeAuditOptions { AuditOptions = new() { DisabledRules = ["NameNotNull"] } });

        audit.ShouldHaveNoViolations();
        Assert.Contains("axe-windows", audit.PassedRules);
    }

    [Fact]
    public async Task A_process_without_a_window_fails_the_audit_instead_of_passing()
    {
        TestApp.SkipUnlessWebView2();
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 30 127.0.0.1 > nul") { UseShellExecute = false, CreateNoWindow = true })!;
        try
        {
            var audit = await NativeAudits.AuditProcessAsync(process.Id);
            Assert.Equal(AuditStatus.Failed, audit.Status);
            Assert.NotEmpty(audit.Errors);
        }
        finally
        {
            process.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public async Task A_connected_app_is_audited_by_process_id()
    {
        TestApp.SkipUnlessWebView2();
        await using var launched = await BlazorHybridApp.LaunchAsync(TestApp.Path());
        await using var connected = await BlazorHybridApp.ConnectAsync(new Uri(DebugEndpoint(launched)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => connected.AuditNativeWindowAsync());
        var audit = await NativeAudits.AuditProcessAsync(launched.Process!.Id);
        Assert.Single(audit.Findings);
    }

    private static string DebugEndpoint(BlazorHybridApp app) =>
        app.Process!.StartInfo.Environment[BlazorHybridApp.AdditionalArgumentsVariable]!.Replace("--remote-debugging-port=", "http://127.0.0.1:", StringComparison.Ordinal);
}
