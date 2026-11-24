using System.Runtime.CompilerServices;

namespace BlazorAccessibilityToolkit.Hybrid.Tests;

/// <summary>The Blazor Hybrid test app (tests/Hybrid/BlazorAccessibilityToolkit.HybridTestApp) and where it can run.</summary>
internal static class TestApp
{
    public static void SkipUnlessWebView2()
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
    public static string Path([CallerFilePath] string source = "")
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(source)!, "..", "BlazorAccessibilityToolkit.HybridTestApp", "bin", configuration, "net10.0-windows", "BlazorAccessibilityToolkit.HybridTestApp.exe");
    }
}
