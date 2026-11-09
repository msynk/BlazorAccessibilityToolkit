using BlazorAccessibilityToolkit.ComponentTesting;
using BlazorAccessibilityToolkit.Playwright;
using BlazorAccessibilityToolkit.Playwright.Hosting;

namespace BlazorAccessibilityToolkit.Demo.WebApp.Lab;

/// <summary>
/// What a live example receives as <c>env</c>. It mirrors the shared fixture of the demo test projects
/// (<c>TestEnvironment</c> in BlazorAccessibilityToolkit.Demo.ComponentTests), so the example code reads like a test.
/// </summary>
public sealed class LabEnvironment
{
    private readonly LabBrowserServices? browser;
    private readonly List<LabArtifact> artifacts = [];

    internal LabEnvironment(LabBrowserServices? browser) => this.browser = browser;

    /// <summary>The shared browser (Chromium unless <c>BLAZOR_A11Y_BROWSER</c> selects another one).</summary>
    public AccessibilityBrowser Browser => Services.Browser;

    /// <summary>The in-process component host: renders one component with Interactive Server in a real browser.</summary>
    public BlazorTestHost Host => Services.Host;

    /// <summary>bUnit markup audits with axe-core in the browser and the component stylesheet, so contrast is measured.</summary>
    public ComponentAuditSettings BrowserMarkup => Services.BrowserMarkup;

    internal IReadOnlyList<LabArtifact> Artifacts => artifacts;

    private LabBrowserServices Services =>
        browser ?? throw new InvalidOperationException("This example was not declared with NeedsBrowser = true.");

    /// <summary>Displays <paramref name="value"/> on the page. Snippets hide the lines that call it.</summary>
    public void Show(object value, string? caption = null) => artifacts.Add(new LabArtifact(value, caption));
}
