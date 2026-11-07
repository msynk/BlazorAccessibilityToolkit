using BlazorAccessibilityToolkit.Playwright.Hosting;

namespace BlazorAccessibilityToolkit.Playwright.Tests;

public sealed class HostConfigurationTests
{
    private static readonly System.Reflection.Assembly TestAssembly = typeof(HostConfigurationTests).Assembly;
    private static readonly System.Reflection.Assembly NoManifest = typeof(object).Assembly;

    [Fact]
    public void Configured_application_assembly_wins() =>
        Assert.Equal(NoManifest.GetName().Name, BlazorTestHost.ResolveApplicationName(NoManifest, TestAssembly, TestAssembly));

    [Fact]
    public void Entry_assembly_with_a_manifest_is_used() =>
        Assert.Equal(TestAssembly.GetName().Name, BlazorTestHost.ResolveApplicationName(null, TestAssembly, NoManifest));

    [Fact]
    public void Caller_is_used_when_the_entry_assembly_has_no_manifest()
    {
        // VSTest runs tests inside testhost: its entry assembly has no static web assets manifest.
        Assert.Equal(TestAssembly.GetName().Name, BlazorTestHost.ResolveApplicationName(null, NoManifest, TestAssembly));
    }

    [Fact]
    public void Falls_back_to_the_entry_assembly_when_nothing_has_a_manifest() =>
        Assert.Equal(NoManifest.GetName().Name, BlazorTestHost.ResolveApplicationName(null, NoManifest, NoManifest));
}
