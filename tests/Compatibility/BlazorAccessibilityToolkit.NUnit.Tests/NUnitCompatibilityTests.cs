using BlazorAccessibilityToolkit.Compatibility;
using BlazorAccessibilityToolkit.ComponentTesting;
using BlazorAccessibilityToolkit.Playwright;
using BlazorAccessibilityToolkit.Playwright.Hosting;
using NUnit.Framework;

namespace BlazorAccessibilityToolkit.NUnitTests;

/// <summary>One browser and one host for the assembly, as NUnit users typically structure shared infrastructure.</summary>
[SetUpFixture]
public sealed class Environment
{
    public static AccessibilityBrowser Browser { get; private set; } = null!;

    public static BlazorTestHost Host { get; private set; } = null!;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        Browser = await AccessibilityBrowser.LaunchAsync();
        Host = await BlazorTestHost.StartAsync();
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        await Host.DisposeAsync();
        await Browser.DisposeAsync();
    }
}

[TestFixture]
[Parallelizable(ParallelScope.All)]
public sealed class NUnitCompatibilityTests
{
    [Test]
    public async Task Markup_audit_of_an_accessible_component_passes()
    {
        await using var fixture = await AccessibilityFixture.For<CounterWidget>().BuildAsync();
        (await fixture.AuditAsync()).ShouldHaveNoViolations();
    }

    [Test]
    public async Task Violations_surface_as_assertion_exceptions()
    {
        await using var fixture = await AccessibilityFixture.For<UnnamedIconButton>().BuildAsync();
        var audit = await fixture.AuditAsync();

        var ex = Assert.Throws<AccessibilityAssertionException>(() => audit.ShouldHaveNoViolations());
        Assert.That(ex!.Message, Does.Contain("blazor-markup-control-name"));
    }

    [Test]
    public async Task Failed_audits_cannot_pass()
    {
        await using var session = await Environment.Browser.NewSessionAsync();
        await session.Page.SetContentAsync("<main><h1>x</h1></main>");
        var audit = await session.Page.Locator("#missing").AuditAccessibilityAsync();

        Assert.Throws<AccessibilityAuditFailedException>(() => audit.ShouldHaveNoViolations());
    }

    [Test]
    public async Task Interactive_host_keyboard_scenario()
    {
        Assert.That(Environment.Host.SupportsInteractiveRendering, Is.True, Environment.Host.InteractiveUnavailableReason);
        await using var component = await Environment.Host.RenderAsync<CounterWidget>(Environment.Browser, p => p.Add(c => c.Label, "Increment"));

        await component.Keyboard()
            .StartFromDocument()
            .PressTab()
            .ExpectFocused("button", "Increment")
            .PressEnter()
            .ExpectLiveRegionUpdate("Count: 1")
            .Audit("after increment")
            .RunAsync();

        Assert.That(component.Audits, Has.Count.EqualTo(1));
    }
}
