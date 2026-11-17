using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.Playwright;

namespace BlazorAccessibilityToolkit.Playwright;

/// <summary>A routable component found by <see cref="BlazorRoutes.Discover(Assembly[])"/>.</summary>
public sealed record DiscoveredRoute
{
    /// <summary>The route template as declared (<c>/orders/{id:int}</c>).</summary>
    public required string Template { get; init; }

    /// <summary>The component type that handles the route.</summary>
    public required Type ComponentType { get; init; }

    /// <summary>Route parameter names (without constraints), in template order.</summary>
    public IReadOnlyList<string> Parameters { get; init; } = [];

    /// <summary>Required parameters, which need a value from <see cref="RouteDiscoveryOptions.ParameterValues"/> to build a URL.</summary>
    public IReadOnlyList<string> RequiredParameters { get; init; } = [];

    /// <summary>
    /// A relative URL for the route (without the leading slash, so it combines with a base address), or <c>null</c> when
    /// a required parameter has no value.
    /// </summary>
    public string? Url { get; init; }

    /// <inheritdoc />
    public override string ToString() => $"{Template} ({ComponentType.Name})";
}

/// <summary>Values and filters for route discovery.</summary>
public sealed record RouteDiscoveryOptions
{
    /// <summary>
    /// Values for route parameters, by parameter name (case-insensitive) or by full template
    /// (<c>["/orders/{id:int}"] = "/orders/42"</c>, which wins over parameter values).
    /// </summary>
    public IReadOnlyDictionary<string, string> ParameterValues { get; init; } = new Dictionary<string, string>();

    /// <summary>Templates to leave out, with <c>*</c> wildcards (for example <c>/admin/*</c> or <c>/error</c>).</summary>
    public IReadOnlyList<string> Exclude { get; init; } = [];
}

/// <summary>Result of auditing every discovered route.</summary>
public sealed record RouteAuditResult
{
    /// <summary>One audit per route with a URL, in route order.</summary>
    public IReadOnlyList<AccessibilityAudit> Audits { get; init; } = [];

    /// <summary>Routes that were not audited because a required parameter had no value.</summary>
    public IReadOnlyList<DiscoveredRoute> Skipped { get; init; } = [];

    /// <summary>
    /// Throws when any audit has violations or did not complete, listing every failing route. Routes that could not be
    /// audited raise <see cref="AccessibilityAuditFailedException"/>.
    /// </summary>
    public RouteAuditResult ShouldHaveNoViolations(AccessibilityExpectation? expectation = null)
    {
        var failures = new List<string>();
        var notRun = 0;
        foreach (var audit in Audits)
        {
            try
            {
                audit.ShouldHaveNoViolations(expectation);
            }
            catch (AccessibilityAuditFailedException ex)
            {
                notRun++;
                failures.Add(ex.Message);
            }
            catch (AccessibilityAssertionException ex)
            {
                failures.Add(ex.Message);
            }
        }

        if (failures.Count == 0)
        {
            return this;
        }

        var details = Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine + Environment.NewLine, failures);
        if (notRun > 0)
        {
            // An audit that did not run never counts as a pass.
            throw new AccessibilityAuditFailedException($"{notRun} of {Audits.Count} routes could not be audited{(failures.Count > notRun ? $" and {failures.Count - notRun} have accessibility violations" : string.Empty)}.{details}");
        }

        throw new AccessibilityAssertionException($"{failures.Count} of {Audits.Count} routes have accessibility violations.{details}");
    }
}

/// <summary>
/// Finds the routable components (<c>@page</c> or <c>[Route]</c>) of a Blazor app by reflection and audits each page,
/// like pa11y-ci's sitemap mode. Routes with required parameters are audited once you supply values; the others are
/// listed in <see cref="RouteAuditResult.Skipped"/> so that no page is silently left out.
/// </summary>
public static partial class BlazorRoutes
{
    /// <summary>Discovers the routes of the components in <paramref name="assemblies"/> (default options).</summary>
    public static IReadOnlyList<DiscoveredRoute> Discover(params Assembly[] assemblies) => Discover(assemblies, null);

    /// <summary>Discovers the routes of the components in <paramref name="assemblies"/>, ordered by template.</summary>
    public static IReadOnlyList<DiscoveredRoute> Discover(IEnumerable<Assembly> assemblies, RouteDiscoveryOptions? options)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        options ??= new RouteDiscoveryOptions();
        var exclude = options.Exclude.Select(p => new Regex("^" + Regex.Escape(p).Replace("\\*", ".*", StringComparison.Ordinal) + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToArray();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in options.ParameterValues)
        {
            values[key] = value;
        }

        var routes = new List<DiscoveredRoute>();

        foreach (var type in assemblies.Distinct().SelectMany(LoadableTypes))
        {
            if (type.IsAbstract || !typeof(IComponent).IsAssignableFrom(type))
            {
                continue;
            }

            foreach (var route in type.GetCustomAttributes<RouteAttribute>(inherit: false))
            {
                var template = "/" + route.Template.Trim().TrimStart('/');
                if (exclude.Any(e => e.IsMatch(template)))
                {
                    continue;
                }

                routes.Add(Build(template, type, values));
            }
        }

        return routes.OrderBy(r => r.Template, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.ComponentType.FullName, StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// Opens every discovered route with a URL in <paramref name="page"/> (relative to <paramref name="baseAddress"/>),
    /// waits for the page to load and settle, and audits it. Each audit is labelled with the route template.
    /// </summary>
    /// <param name="page">The page to navigate.</param>
    /// <param name="baseAddress">The app's base address, for example <c>http://127.0.0.1:5000/</c>.</param>
    /// <param name="routes">Routes from <see cref="Discover(IEnumerable{Assembly}, RouteDiscoveryOptions?)"/>.</param>
    /// <param name="options">Audit options applied to every page.</param>
    /// <param name="beforeAudit">Optional work after navigation, for example waiting for interactivity.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public static async Task<RouteAuditResult> AuditRoutesAsync(
        this IPage page,
        Uri baseAddress,
        IEnumerable<DiscoveredRoute> routes,
        AccessibilityAuditOptions? options = null,
        Func<IPage, DiscoveredRoute, Task>? beforeAudit = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(baseAddress);
        ArgumentNullException.ThrowIfNull(routes);
        var audits = new List<AccessibilityAudit>();
        var skipped = new List<DiscoveredRoute>();
        var root = baseAddress.AbsoluteUri.EndsWith('/') ? baseAddress : new Uri(baseAddress.AbsoluteUri + "/");

        foreach (var route in routes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (route.Url is null)
            {
                skipped.Add(route);
                continue;
            }

            var url = new Uri(root, route.Url).AbsoluteUri;
            try
            {
                await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle }).ConfigureAwait(false);
                if (beforeAudit is not null)
                {
                    await beforeAudit(page, route).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
            {
                // The page could not be opened: record a failed audit for the route and continue with the next one.
                var builder = new AccessibilityAuditBuilder(route.Template, new AccessibilityContext { Kind = AuditTargetKind.Page, Url = url, Scenario = "route", State = "loaded", RenderMode = "page" }, options);
                builder.AddError(ExecutionError.FromException("browser", ex));
                audits.Add(PlaywrightAccessibility.Persist(builder.Build()));
                continue;
            }

            audits.Add(await page.AuditAccessibilityAsync(options, new AuditLabel { Name = route.Template, Scenario = "route", State = "loaded" }, cancellationToken).ConfigureAwait(false));
        }

        return new RouteAuditResult { Audits = audits, Skipped = skipped };
    }

    private static DiscoveredRoute Build(string template, Type type, Dictionary<string, string> values)
    {
        var parameters = new List<string>();
        var required = new List<string>();
        foreach (Match match in RouteParameter().Matches(template))
        {
            var name = match.Groups["name"].Value;
            parameters.Add(name);
            if (match.Groups["optional"].Success is false && match.Groups["catchall"].Success is false)
            {
                required.Add(name);
            }
        }

        string? url;
        if (values.TryGetValue(template, out var explicitUrl))
        {
            url = explicitUrl.TrimStart('/');
        }
        else
        {
            var missing = false;
            var path = RouteParameter().Replace(template, m =>
            {
                if (values.TryGetValue(m.Groups["name"].Value, out var value))
                {
                    return Uri.EscapeDataString(value).Replace("%2F", "/", StringComparison.Ordinal);
                }

                if (m.Groups["optional"].Success || m.Groups["catchall"].Success)
                {
                    return string.Empty;
                }

                missing = true;
                return m.Value;
            });
            url = missing ? null : CollapseSlashes(path).TrimStart('/');
        }

        return new DiscoveredRoute { Template = template, ComponentType = type, Parameters = parameters, RequiredParameters = required, Url = url };
    }

    private static string CollapseSlashes(string path)
    {
        var sb = new StringBuilder(path.Length);
        foreach (var c in path)
        {
            if (c == '/' && sb.Length > 0 && sb[^1] == '/')
            {
                continue;
            }

            sb.Append(c);
        }

        return sb.Length > 1 ? sb.ToString().TrimEnd('/') : sb.ToString();
    }

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }

    [GeneratedRegex(@"\{(?<catchall>\*\*?)?(?<name>[A-Za-z_][A-Za-z0-9_]*)(?::[^}?]*)?(?<optional>\?)?\}", RegexOptions.CultureInvariant)]
    private static partial Regex RouteParameter();
}
