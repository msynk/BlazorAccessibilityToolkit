using System.Collections.Concurrent;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace BlazorAccessibilityToolkit.Playwright.Hosting;

/// <summary>How a hosted component is rendered.</summary>
public enum HostedRenderMode
{
    /// <summary>Interactive Server (without prerendering): events, focus and dynamic updates work.</summary>
    InteractiveServer,

    /// <summary>Static server-side rendering: no interactivity; the markup a static SSR page would produce.</summary>
    Static,
}

/// <summary>Settings of the in-process component host.</summary>
public sealed class BlazorTestHostOptions
{
    /// <summary>Registers services for the components (authentication state, HTTP clients, localization, fakes ...).</summary>
    public Action<IServiceCollection>? ConfigureServices { get; set; }

    /// <summary>Adds middleware before the Razor component endpoints (for example authentication).</summary>
    public Action<WebApplication>? ConfigureApp { get; set; }

    /// <summary>Stylesheet URLs added to the host page (for example <c>_content/MyLib/MyLib.bundle.scp.css</c> or <c>css/app.css</c>).</summary>
    public IList<string> Stylesheets { get; } = [];

    /// <summary>Script URLs added after blazor.web.js.</summary>
    public IList<string> Scripts { get; } = [];

    /// <summary>Raw markup appended to the head.</summary>
    public string? HeadMarkup { get; set; }

    /// <summary><c>lang</c> of the host page. Default <c>en</c>.</summary>
    public string Language { get; set; } = "en";

    /// <summary>Title of the host page.</summary>
    public string Title { get; set; } = "Component under test";

    /// <summary>Culture for requests and circuits (request localization). Default: invariant behavior of the server.</summary>
    public CultureInfo? Culture { get; set; }

    /// <summary>
    /// Assembly whose static web assets manifest is used (it provides <c>_framework/blazor.web.js</c> and <c>_content/*</c> files).
    /// Default: the entry assembly when it has a static web assets manifest (xUnit v3, Microsoft.Testing.Platform),
    /// otherwise the assembly that called <see cref="BlazorTestHost.StartAsync"/> (VSTest runs tests inside testhost).
    /// </summary>
    public Assembly? ApplicationAssembly { get; set; }

    /// <summary>Hosting environment name. Default <c>Development</c> (detailed circuit errors).</summary>
    public string EnvironmentName { get; set; } = "Development";

    /// <summary>Maximum time to wait for a component to become interactive. Default 30 s.</summary>
    public TimeSpan RenderTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>Thrown when a hosted component cannot be rendered.</summary>
public sealed class HostedComponentException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    public HostedComponentException()
    {
    }

    /// <summary>Creates the exception.</summary>
    public HostedComponentException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    public HostedComponentException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Parameters and cascading values for a hosted component. Values are passed in-process, so any type works.</summary>
public sealed class HostedComponentParameters<TComponent>
    where TComponent : IComponent
{
    internal Dictionary<string, object?> Values { get; } = new(StringComparer.Ordinal);

    internal List<CascadingValueRegistration> CascadingValues { get; } = [];

    /// <summary>Sets a parameter selected by expression.</summary>
    public HostedComponentParameters<TComponent> Add<TValue>(Expression<Func<TComponent, TValue>> parameter, TValue value)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        var member = parameter.Body is MemberExpression m ? m.Member : throw new ArgumentException("Select a component parameter property, for example p => p.Title.", nameof(parameter));
        Values[member.Name] = value;
        return this;
    }

    /// <summary>Sets a parameter by name (including unmatched attributes).</summary>
    public HostedComponentParameters<TComponent> Add(string name, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Values[name] = value;
        return this;
    }

    /// <summary>Sets <c>ChildContent</c>.</summary>
    public HostedComponentParameters<TComponent> AddChildContent(RenderFragment content) => Add("ChildContent", content);

    /// <summary>Sets <c>ChildContent</c> to raw markup.</summary>
    public HostedComponentParameters<TComponent> AddChildContent(string markup) => Add("ChildContent", (RenderFragment)(b => b.AddMarkupContent(0, markup)));

    /// <summary>Supplies a cascading value.</summary>
    public HostedComponentParameters<TComponent> AddCascadingValue<TValue>(TValue value, string? name = null)
    {
        CascadingValues.Add(new CascadingValueRegistration(typeof(TValue), value, name));
        return this;
    }
}

/// <summary>
/// An in-process ASP.NET Core server (Kestrel on 127.0.0.1, random port) that renders individual Razor components in
/// a real browser with Interactive Server or static SSR, so keyboard, focus and live-region behavior can be tested in
/// isolation. Parameters are passed in-process: render fragments, event callbacks and arbitrary objects work.
/// </summary>
/// <remarks>
/// Requirements: the test project must use <c>Microsoft.NET.Sdk.Web</c>, produce an executable
/// (<c>&lt;OutputType&gt;Exe&lt;/OutputType&gt;</c>; xUnit v3 does this by default, and NUnit and MSTest work as executables
/// with VSTest or Microsoft.Testing.Platform), and either contain a .razor file or set
/// <c>&lt;RequiresAspNetWebAssets&gt;true&lt;/RequiresAspNetWebAssets&gt;</c>, so that <c>_framework/blazor.web.js</c>
/// and referenced libraries' <c>_content</c> assets are available as static web assets. Static rendering works without them.
/// Services are shared by all components rendered by one host; register per-test state as scoped (one scope per circuit).
/// Interactive WebAssembly is not supported in isolation; test WebAssembly behavior end-to-end against the app.
/// </remarks>
public sealed class BlazorTestHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ComponentRegistry _registry;
    private readonly ConcurrentQueue<string> _serverErrors;
    private readonly TimeSpan _renderTimeout;
    private int _disposed;

    private BlazorTestHost(WebApplication app, ComponentRegistry registry, ConcurrentQueue<string> serverErrors, Uri baseAddress, string? interactiveUnavailableReason, TimeSpan renderTimeout)
    {
        _app = app;
        _registry = registry;
        _serverErrors = serverErrors;
        BaseAddress = baseAddress;
        InteractiveUnavailableReason = interactiveUnavailableReason;
        _renderTimeout = renderTimeout;
    }

    /// <summary>Server address.</summary>
    public Uri BaseAddress { get; }

    /// <summary>Root service provider of the host.</summary>
    public IServiceProvider Services => _app.Services;

    /// <summary>True when Interactive Server rendering is available.</summary>
    public bool SupportsInteractiveRendering => InteractiveUnavailableReason is null;

    /// <summary>Why interactive rendering is unavailable, or <c>null</c>.</summary>
    public string? InteractiveUnavailableReason { get; }

    /// <summary>Errors logged by the server (circuit exceptions, rendering failures).</summary>
    public IReadOnlyCollection<string> ServerErrors => _serverErrors.ToArray();

    /// <summary>Starts the host.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Task<BlazorTestHost> StartAsync(BlazorTestHostOptions? options = null, CancellationToken cancellationToken = default) =>
        StartCoreAsync(options ?? new BlazorTestHostOptions(), Assembly.GetCallingAssembly(), cancellationToken);

    /// <summary>
    /// Picks the assembly whose static web assets manifest is used: the configured one, else the entry assembly (the
    /// test executable for xUnit v3 and Microsoft.Testing.Platform), else the assembly that started the host (VSTest
    /// runs tests inside testhost, whose entry assembly has no manifest).
    /// </summary>
    internal static string? ResolveApplicationName(Assembly? configured, Assembly? entry, Assembly? caller)
    {
        if (configured is not null)
        {
            return configured.GetName().Name;
        }

        foreach (var candidate in new[] { entry, caller })
        {
            var name = candidate?.GetName().Name;
            if (name is not null && File.Exists(Path.Combine(AppContext.BaseDirectory, name + ".staticwebassets.runtime.json")))
            {
                return name;
            }
        }

        return (entry ?? caller)?.GetName().Name;
    }

    private static async Task<BlazorTestHost> StartCoreAsync(BlazorTestHostOptions options, Assembly caller, CancellationToken cancellationToken)
    {
        var applicationName = ResolveApplicationName(options.ApplicationAssembly, Assembly.GetEntryAssembly(), caller);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = applicationName,
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = options.EnvironmentName,
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.WebHost.UseStaticWebAssets();

        var errors = new ConcurrentQueue<string>();
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Logging.AddProvider(new ErrorCollectingLoggerProvider(errors));

        var registry = new ComponentRegistry();
        builder.Services.AddSingleton(registry);
        builder.Services.AddSingleton(new HostDocumentSettings(options.Language, options.Title, options.Stylesheets.ToArray(), options.Scripts.ToArray(), options.HeadMarkup));
        builder.Services.AddRazorComponents().AddInteractiveServerComponents(o => o.DetailedErrors = true);
        builder.Services.AddAntiforgery();
        options.ConfigureServices?.Invoke(builder.Services);

        var app = builder.Build();
        if (options.Culture is { } culture)
        {
            app.UseRequestLocalization(new RequestLocalizationOptions
            {
                DefaultRequestCulture = new RequestCulture(culture),
                SupportedCultures = [culture],
                SupportedUICultures = [culture],
            });
        }

        options.ConfigureApp?.Invoke(app);
        app.UseAntiforgery();
        if (applicationName is not null && File.Exists(Path.Combine(AppContext.BaseDirectory, applicationName + ".staticwebassets.endpoints.json")))
        {
            app.MapStaticAssets();
        }
        else
        {
            app.UseStaticFiles();
        }

        app.MapRazorComponents<AccessibilityHostApp>().AddInteractiveServerRenderMode();

        try
        {
            await app.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await app.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault() ?? app.Urls.First();
        var baseAddress = new Uri(address.EndsWith('/') ? address : address + "/");

        string? interactiveUnavailable = null;
        using (var http = new HttpClient { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(10) })
        {
            using var response = await http.GetAsync(new Uri("_framework/blazor.web.js", UriKind.Relative), cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                interactiveUnavailable =
                    $"_framework/blazor.web.js is not available (HTTP {(int)response.StatusCode}). Interactive rendering needs a test project that " +
                    "uses <Project Sdk=\"Microsoft.NET.Sdk.Web\">, produces an executable (xUnit v3 test projects do), and contains .razor files " +
                    "or sets <RequiresAspNetWebAssets>true</RequiresAspNetWebAssets>, so the SDK publishes the Blazor framework script as a static web " +
                    $"asset of '{applicationName}'. Static rendering still works.";
            }
        }

        return new BlazorTestHost(app, registry, errors, baseAddress, interactiveUnavailable, options.RenderTimeout);
    }

    /// <summary>Renders <typeparamref name="TComponent"/> in <paramref name="page"/> and waits until it is ready.</summary>
    public async Task<HostedComponent<TComponent>> RenderAsync<TComponent>(
        IPage page,
        Action<HostedComponentParameters<TComponent>>? parameters = null,
        HostedRenderMode renderMode = HostedRenderMode.InteractiveServer,
        string? name = null)
        where TComponent : IComponent
    {
        ArgumentNullException.ThrowIfNull(page);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (renderMode == HostedRenderMode.InteractiveServer && InteractiveUnavailableReason is { } reason)
        {
            throw new HostedComponentException(reason);
        }

        var collected = new HostedComponentParameters<TComponent>();
        parameters?.Invoke(collected);
        var registration = new ComponentRegistration(typeof(TComponent), collected.Values, collected.CascadingValues);
        var id = _registry.Register(registration);
        var consoleErrors = new ConcurrentQueue<string>();
        var fatalErrors = new ConcurrentQueue<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
            {
                consoleErrors.Enqueue(message.Text);

                // Only errors logged by the Blazor client ("[timestamp] Error: ...", for example an exception thrown by a
                // component on the circuit) end the wait early; unrelated errors (such as a missing image) do not.
                if (message.Text.StartsWith('[') && message.Text.Contains("] Error: ", StringComparison.Ordinal))
                {
                    fatalErrors.Enqueue(message.Text);
                }
            }
        };
        page.PageError += (_, error) =>
        {
            consoleErrors.Enqueue(error);
            fatalErrors.Enqueue(error);
        };

        var segment = renderMode == HostedRenderMode.InteractiveServer ? "interactive" : "static";
        var url = new Uri(BaseAddress, $"_a11y/{segment}/{id}");
        var expectedState = renderMode == HostedRenderMode.InteractiveServer ? "interactive" : "static";
        try
        {
            var response = await page.GotoAsync(url.ToString()).ConfigureAwait(false);
            if (response is { Ok: false })
            {
                throw new HostedComponentException($"The host returned HTTP {response.Status} for {url}.");
            }

            // Poll for readiness, but fail fast when this page reports a circuit error (console errors are per page,
            // so failures of other components rendered in parallel cannot abort this render).
            var ready = page.Locator($"[data-a11y-host-root][data-a11y-host-state=\"{expectedState}\"]");
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            while (await ready.CountAsync().ConfigureAwait(false) == 0)
            {
                if (!fatalErrors.IsEmpty)
                {
                    throw new HostedComponentException($"{typeof(TComponent).FullName} failed while rendering ({renderMode}).{Details(consoleErrors)}");
                }

                if (stopwatch.Elapsed > _renderTimeout)
                {
                    throw new TimeoutException("The component did not become ready.");
                }

                await Task.Delay(50).ConfigureAwait(false);
            }

            if (renderMode == HostedRenderMode.InteractiveServer)
            {
                await registration.Attached.WaitAsync(_renderTimeout).ConfigureAwait(false);
            }
        }
        catch (HostedComponentException)
        {
            _registry.Remove(id);
            throw;
        }
        catch (Exception ex) when (ex is TimeoutException or PlaywrightException)
        {
            _registry.Remove(id);
            throw new HostedComponentException(
                $"{typeof(TComponent).FullName} did not finish rendering ({renderMode}) within {_renderTimeout.TotalSeconds:0} s.{Details(consoleErrors)}",
                ex);
        }

        return new HostedComponent<TComponent>(this, page, null, id, registration, renderMode, url, name ?? typeof(TComponent).Name, consoleErrors);
    }

    /// <summary>Renders <typeparamref name="TComponent"/> in a new isolated browser session that is closed when the component is disposed.</summary>
    public async Task<HostedComponent<TComponent>> RenderAsync<TComponent>(
        AccessibilityBrowser browser,
        Action<HostedComponentParameters<TComponent>>? parameters = null,
        HostedRenderMode renderMode = HostedRenderMode.InteractiveServer,
        string? name = null)
        where TComponent : IComponent
    {
        ArgumentNullException.ThrowIfNull(browser);
        var session = await browser.NewSessionAsync().ConfigureAwait(false);
        try
        {
            var hosted = await RenderAsync(session.Page, parameters, renderMode, name).ConfigureAwait(false);
            hosted.OwnedSession = session;
            return hosted;
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal void Unregister(string id) => _registry.Remove(id);

    private string Details(IEnumerable<string> consoleErrors)
    {
        var details = string.Join(Environment.NewLine, consoleErrors.Take(5).Select(e => "  [browser] " + e).Concat(_serverErrors.TakeLast(5).Select(e => "  [server] " + e)));
        return details.Length == 0 ? string.Empty : Environment.NewLine + "Errors:" + Environment.NewLine + details;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await _app.StopAsync(cts.Token).ConfigureAwait(false);
        }
        finally
        {
            await _app.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class ErrorCollectingLoggerProvider(ConcurrentQueue<string> errors) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, errors);

        public void Dispose()
        {
        }

        private sealed class Logger(string category, ConcurrentQueue<string> errors) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

                errors.Enqueue($"[{category}] {formatter(state, exception)}{(exception is null ? string.Empty : " " + exception.GetType().Name + ": " + exception.Message)}");
                while (errors.Count > 200)
                {
                    errors.TryDequeue(out _);
                }
            }
        }
    }
}
