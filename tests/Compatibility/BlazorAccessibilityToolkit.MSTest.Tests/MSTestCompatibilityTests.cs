using BlazorAccessibilityToolkit.Compatibility;
using BlazorAccessibilityToolkit.ComponentTesting;
using BlazorAccessibilityToolkit.Playwright;
using BlazorAccessibilityToolkit.Playwright.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace BlazorAccessibilityToolkit.MSTestTests;

[TestClass]
public sealed class MSTestCompatibilityTests
{
    private static AccessibilityBrowser browser = null!;
    private static BlazorTestHost host = null!;

    [AssemblyInitialize]
    public static async Task StartAsync(TestContext context)
    {
        browser = await AccessibilityBrowser.LaunchAsync();
        host = await BlazorTestHost.StartAsync();
    }

    [AssemblyCleanup]
    public static async Task StopAsync()
    {
        await host.DisposeAsync();
        await browser.DisposeAsync();
    }

    [TestMethod]
    public async Task Markup_audit_of_an_accessible_component_passes()
    {
        await using var fixture = await AccessibilityFixture.For<CounterWidget>().BuildAsync();
        (await fixture.AuditAsync()).ShouldHaveNoViolations();
    }

    [TestMethod]
    public async Task Violations_surface_as_assertion_exceptions()
    {
        await using var fixture = await AccessibilityFixture.For<UnnamedIconButton>().BuildAsync();
        var audit = await fixture.AuditAsync();

        var ex = Assert.ThrowsExactly<AccessibilityAssertionException>(() => audit.ShouldHaveNoViolations());
        StringAssert.Contains(ex.Message, "blazor-markup-control-name");
    }

    [TestMethod]
    public async Task Failed_audits_cannot_pass()
    {
        await using var session = await browser.NewSessionAsync();
        await session.Page.SetContentAsync("<main><h1>x</h1></main>");
        var audit = await session.Page.Locator("#missing").AuditAccessibilityAsync();

        Assert.ThrowsExactly<AccessibilityAuditFailedException>(() => audit.ShouldHaveNoViolations());
    }

    [TestMethod]
    public async Task Interactive_host_keyboard_scenario()
    {
        Assert.IsTrue(host.SupportsInteractiveRendering, host.InteractiveUnavailableReason);
        await using var component = await host.RenderAsync<CounterWidget>(browser, p => p.Add(c => c.Label, "Increment"));

        await component.Keyboard()
            .StartFromDocument()
            .PressTab()
            .ExpectFocused("button", "Increment")
            .PressEnter()
            .ExpectLiveRegionUpdate("Count: 1")
            .Audit("after increment")
            .RunAsync();

        Assert.HasCount(1, component.Audits);
    }
}
