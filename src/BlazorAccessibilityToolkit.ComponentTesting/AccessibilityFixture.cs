using System.Linq.Expressions;
using BlazorAccessibilityToolkit.Engines;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;

namespace BlazorAccessibilityToolkit.ComponentTesting;

/// <summary>Entry point for component accessibility fixtures.</summary>
public static class AccessibilityFixture
{
    /// <summary>Starts configuring a fixture for <typeparamref name="TComponent"/>.</summary>
    public static AccessibilityFixtureBuilder<TComponent> For<TComponent>()
        where TComponent : IComponent => new();
}

/// <summary>
/// Configures how a component is rendered for accessibility tests. Each fixture owns a new bUnit context, so fixtures
/// share no mutable state and can run in parallel.
/// </summary>
public sealed class AccessibilityFixtureBuilder<TComponent>
    where TComponent : IComponent
{
    private readonly List<Action<ComponentParameterCollectionBuilder<TComponent>>> _parameters = [];
    private readonly List<Action<BunitContext>> _context = [];
    private ComponentAuditSettings _settings = ComponentAuditSettings.Default;
    private string? _name;

    internal AccessibilityFixtureBuilder()
    {
    }

    /// <summary>Sets a component parameter.</summary>
    public AccessibilityFixtureBuilder<TComponent> WithParameter<TValue>(Expression<Func<TComponent, TValue>> parameter, TValue value)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        _parameters.Add(p => p.Add(parameter, value));
        return this;
    }

    /// <summary>Configures parameters with bUnit's parameter builder (callbacks, templates, binding ...).</summary>
    public AccessibilityFixtureBuilder<TComponent> WithParameters(Action<ComponentParameterCollectionBuilder<TComponent>> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        _parameters.Add(parameters);
        return this;
    }

    /// <summary>Sets <c>ChildContent</c> markup.</summary>
    public AccessibilityFixtureBuilder<TComponent> WithChildContent(string markup)
    {
        _parameters.Add(p => p.AddChildContent(markup));
        return this;
    }

    /// <summary>Sets <c>ChildContent</c>.</summary>
    public AccessibilityFixtureBuilder<TComponent> WithChildContent(RenderFragment content)
    {
        _parameters.Add(p => p.AddChildContent(content));
        return this;
    }

    /// <summary>Supplies a cascading value (optionally named).</summary>
    public AccessibilityFixtureBuilder<TComponent> WithCascadingValue<TValue>(TValue value, string? name = null)
        where TValue : notnull
    {
        _parameters.Add(p =>
        {
            if (name is null)
            {
                p.AddCascadingValue(value);
            }
            else
            {
                p.AddCascadingValue(name, value);
            }
        });
        return this;
    }

    /// <summary>Registers services (fakes, localization, HTTP clients ...).</summary>
    public AccessibilityFixtureBuilder<TComponent> WithServices(Action<BunitServiceProvider> services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _context.Add(ctx => services(ctx.Services));
        return this;
    }

    /// <summary>Configures the bUnit context directly (JS interop setup, component factories, renderer info ...).</summary>
    public AccessibilityFixtureBuilder<TComponent> WithContext(Action<BunitContext> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _context.Add(configure);
        return this;
    }

    /// <summary>Adds bUnit's test authorization and configures the user (for example <c>auth.SetAuthorized("ana")</c>).</summary>
    public AccessibilityFixtureBuilder<TComponent> WithAuthorization(Action<BunitAuthorizationContext> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _context.Add(ctx => configure(ctx.AddAuthorization()));
        return this;
    }

    /// <summary>Uses the given audit settings (engines, options, document).</summary>
    public AccessibilityFixtureBuilder<TComponent> WithSettings(ComponentAuditSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        return this;
    }

    /// <summary>Uses the given rule options.</summary>
    public AccessibilityFixtureBuilder<TComponent> WithOptions(AccessibilityAuditOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _settings = _settings with { Options = options };
        return this;
    }

    /// <summary>Adds a browser engine (for example <c>new PlaywrightMarkupAuditEngine(browser)</c>).</summary>
    public AccessibilityFixtureBuilder<TComponent> WithEngine(IMarkupAuditEngine engine)
    {
        _settings = _settings.WithEngine(engine);
        return this;
    }

    /// <summary>Sets the fixture (scenario) name used in reports. Default: component type name.</summary>
    public AccessibilityFixtureBuilder<TComponent> WithName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = name;
        return this;
    }

    /// <summary>
    /// Creates the bUnit context (JS interop in loose mode unless configured otherwise), renders the component and
    /// returns the fixture. The context is disposed if rendering throws.
    /// </summary>
    public async Task<AccessibilityFixture<TComponent>> BuildAsync()
    {
        var context = new BunitContext();
        try
        {
            context.JSInterop.Mode = JSRuntimeMode.Loose;
            foreach (var configure in _context)
            {
                configure(context);
            }

            var parameters = _parameters.ToArray();
            var component = context.Render<TComponent>(p =>
            {
                foreach (var parameter in parameters)
                {
                    parameter(p);
                }
            });
            return new AccessibilityFixture<TComponent>(context, component, _settings, _name ?? typeof(TComponent).Name);
        }
        catch
        {
            await context.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}

/// <summary>A rendered component plus the audits taken of it. Dispose to release the bUnit context.</summary>
public sealed class AccessibilityFixture<TComponent> : IAsyncDisposable
    where TComponent : IComponent
{
    private readonly List<AccessibilityAudit> _audits = [];

    internal AccessibilityFixture(BunitContext context, IRenderedComponent<TComponent> component, ComponentAuditSettings settings, string name)
    {
        Context = context;
        Component = component;
        Settings = settings;
        Name = name;
    }

    /// <summary>The bUnit context.</summary>
    public BunitContext Context { get; }

    /// <summary>The rendered component; use bUnit APIs (<c>Find</c>, <c>Click</c>, <c>WaitForState</c> ...) to change its state.</summary>
    public IRenderedComponent<TComponent> Component { get; }

    /// <summary>Audit settings.</summary>
    public ComponentAuditSettings Settings { get; }

    /// <summary>Fixture name.</summary>
    public string Name { get; }

    /// <summary>Audits executed so far, in order.</summary>
    public IReadOnlyList<AccessibilityAudit> Audits => _audits;

    /// <summary>Audits the component's current markup and records the audit under <paramref name="state"/>.</summary>
    public async Task<AccessibilityAudit> AuditAsync(string? state = null, AccessibilityAuditOptions? options = null, CancellationToken cancellationToken = default)
    {
        var settings = options is null ? Settings : Settings with { Options = options };
        var audit = await Component.AuditAccessibilityAsync(state, settings, Name, cancellationToken).ConfigureAwait(false);
        _audits.Add(audit);
        return audit;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => Context.DisposeAsync();
}
