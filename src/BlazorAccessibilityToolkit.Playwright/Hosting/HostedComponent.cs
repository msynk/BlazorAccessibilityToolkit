using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Components;
using Microsoft.Playwright;

namespace BlazorAccessibilityToolkit.Playwright.Hosting;

/// <summary>
/// A component rendered by <see cref="BlazorTestHost"/> in a real browser. Audits are scoped to the component root and
/// findings are attributed to the component type (fixture root; nested components are not resolved in the browser).
/// </summary>
public sealed class HostedComponent<TComponent> : IAsyncDisposable
    where TComponent : IComponent
{
    private readonly BlazorTestHost _host;
    private readonly ComponentRegistration _registration;
    private readonly IReadOnlyCollection<string> _consoleErrors;
    private readonly List<AccessibilityAudit> _audits = [];
    private int _disposed;

    internal HostedComponent(BlazorTestHost host, IPage page, BrowserSession? session, string id, ComponentRegistration registration, HostedRenderMode renderMode, Uri url, string name, IReadOnlyCollection<string> consoleErrors)
    {
        _host = host;
        Page = page;
        OwnedSession = session;
        Id = id;
        _registration = registration;
        RenderMode = renderMode;
        Url = url;
        Name = name;
        _consoleErrors = consoleErrors;
    }

    /// <summary>The page showing the component.</summary>
    public IPage Page { get; }

    /// <summary>Locator of the element that wraps the component's markup.</summary>
    public ILocator Root => Page.Locator("[data-a11y-host-root]");

    /// <summary>Registration id.</summary>
    public string Id { get; }

    /// <summary>Render mode.</summary>
    public HostedRenderMode RenderMode { get; }

    /// <summary>URL of the host page.</summary>
    public Uri Url { get; }

    /// <summary>Scenario name used for audits (default: component type name).</summary>
    public string Name { get; }

    /// <summary>Audits executed through this fixture.</summary>
    public IReadOnlyList<AccessibilityAudit> Audits => _audits;

    /// <summary>Browser console errors observed since rendering.</summary>
    public IReadOnlyCollection<string> ConsoleErrors => _consoleErrors;

    internal BrowserSession? OwnedSession { get; set; }

    /// <summary>
    /// Audits the component's current state. Page-level rules (landmarks, page heading ...) are disabled unless
    /// <see cref="AccessibilityAuditOptions.PageLevelRulesForComponents"/> is set.
    /// </summary>
    public async Task<AccessibilityAudit> AuditAsync(string? state = null, AccessibilityAuditOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Without generic arguments (which carry assembly versions), like bUnit audits, so fingerprints are stable across
        // versions and match between the two hosts.
        var componentType = (typeof(TComponent).FullName ?? typeof(TComponent).Name).Split('[')[0];
        var context = new AccessibilityContext
        {
            Kind = AuditTargetKind.Component,
            ComponentType = componentType,
            RenderMode = RenderMode == HostedRenderMode.InteractiveServer ? "interactive-server" : "static-ssr",
        };
        var attribution = new ComponentAttribution { ComponentType = componentType, ComponentPath = [componentType], Method = "fixture-root" };
        var audit = await PlaywrightAccessibility.AuditCoreAsync(
            Page,
            Root,
            context,
            options,
            new AuditLabel { Name = state is null ? Name : $"{Name} [{state}]", Scenario = Name, State = state },
            cancellationToken,
            f => f with { Component = attribution }).ConfigureAwait(false);
        _audits.Add(audit);
        return audit;
    }

    /// <summary>Starts a keyboard scenario whose audit steps audit this component.</summary>
    public KeyboardScenario Keyboard(KeyboardScenarioOptions? options = null)
    {
        options ??= new KeyboardScenarioOptions();
        options = options with { Name = options.Name ?? Name };
        return new KeyboardScenario(Page, options, (state, auditOptions) => AuditAsync(state, auditOptions ?? options.AuditOptions));
    }

    /// <summary>Captures the focus order of the page hosting the component.</summary>
    public Task<FocusOrderResult> CaptureFocusOrderAsync(FocusOrderOptions? options = null) => Page.CaptureFocusOrderAsync(options);

    /// <summary>Starts monitoring live regions.</summary>
    public Task<LiveRegionMonitor> MonitorLiveRegionsAsync() => LiveRegionMonitor.StartAsync(Page);

    /// <summary>Compares the component's ARIA snapshot with a stored snapshot.</summary>
    public Task ShouldMatchStoredAriaSnapshotAsync(string name, AriaSnapshotOptions? options = null, [CallerFilePath] string callerFilePath = "") =>
        Root.ShouldMatchStoredAriaSnapshotAsync(name, options, callerFilePath);

    /// <summary>
    /// Runs <paramref name="work"/> on the component's renderer (circuit) dispatcher, for example to call a public method
    /// or change state. Interactive mode only.
    /// </summary>
    public async Task InvokeAsync(Func<TComponent, Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (RenderMode != HostedRenderMode.InteractiveServer)
        {
            throw new InvalidOperationException("InvokeAsync requires interactive rendering; statically rendered components are disposed after the response.");
        }

        var (instance, invoke) = await _registration.Attached.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        await invoke(() => work((TComponent)instance)).ConfigureAwait(false);
    }

    /// <summary>Runs <paramref name="work"/> on the component's renderer dispatcher.</summary>
    public Task InvokeAsync(Action<TComponent> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        return InvokeAsync(c =>
        {
            work(c);
            return Task.CompletedTask;
        });
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _host.Unregister(Id);
        if (OwnedSession is { } session)
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }
}
