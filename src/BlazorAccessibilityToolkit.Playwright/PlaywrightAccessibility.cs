using System.Text.Json;
using BlazorAccessibilityToolkit.Playwright.Internal;
using BlazorAccessibilityToolkit.Reporting;
using Microsoft.Playwright;

namespace BlazorAccessibilityToolkit.Playwright;

/// <summary>Labels that identify an audit in reports and fingerprints.</summary>
public sealed record AuditLabel
{
    /// <summary>Display name. Defaults to the page path, scope selector or component name.</summary>
    public string? Name { get; init; }

    /// <summary>Scenario (test or fixture) name.</summary>
    public string? Scenario { get; init; }

    /// <summary>UI state (for example <c>menu open</c>).</summary>
    public string? State { get; init; }

    /// <summary>Additional metadata.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>Accessibility audits and inspection for Playwright pages and locators.</summary>
public static class PlaywrightAccessibility
{
    /// <summary>
    /// Audits the whole page with axe-core plus the toolkit's browser diagnostics.
    /// The audit is <see cref="AuditStatus.Failed"/> (never silently passing) when the engine errors or times out.
    /// </summary>
    public static Task<AccessibilityAudit> AuditAccessibilityAsync(this IPage page, AccessibilityAuditOptions? options = null, AuditLabel? label = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        var context = new AccessibilityContext { Kind = AuditTargetKind.Page, RenderMode = "page" };
        return AuditCoreAsync(page, null, context, options, label, cancellationToken);
    }

    /// <summary>Audits the element matched by <paramref name="locator"/> and its descendants. The locator must match exactly one element.</summary>
    public static Task<AccessibilityAudit> AuditAccessibilityAsync(this ILocator locator, AccessibilityAuditOptions? options = null, AuditLabel? label = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(locator);
        var context = new AccessibilityContext { Kind = AuditTargetKind.Region, RenderMode = "page" };
        return AuditCoreAsync(locator.Page, locator, context, options, label, cancellationToken);
    }

    /// <summary>Returns role, accessible name, states and visibility of the element matched by <paramref name="locator"/>.</summary>
    public static async Task<AccessibleElementInfo> GetAccessibilityInfoAsync(this ILocator locator)
    {
        ArgumentNullException.ThrowIfNull(locator);
        await ToolkitScript.EnsureAsync(locator.Page).ConfigureAwait(false);
        var json = await locator.EvaluateAsync<JsonElement>("el => window.__blazorA11y.inspectElement(el)").ConfigureAwait(false);
        return ToolkitScript.Deserialize<AccessibleElementInfo>(json)
            ?? throw new InvalidOperationException("Could not inspect the element.");
    }

    /// <summary>Returns the currently focused element (piercing shadow roots and same-origin iframes).</summary>
    public static async Task<FocusSnapshot> GetFocusAsync(this IPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        await ToolkitScript.EnsureAsync(page).ConfigureAwait(false);
        var json = await page.EvaluateAsync<JsonElement>("() => window.__blazorA11y.focusInfo()").ConfigureAwait(false);
        return FocusSnapshot.From(ToolkitScript.Deserialize<FocusInfoDto>(json));
    }

    /// <summary>Asserts the role of the element.</summary>
    public static async Task<AccessibleElementInfo> ShouldHaveRoleAsync(this ILocator locator, string role)
    {
        var info = await locator.GetAccessibilityInfoAsync().ConfigureAwait(false);
        if (!string.Equals(info.Role, role, StringComparison.Ordinal))
        {
            throw new AccessibilityAssertionException($"Expected role '{role}' but the element has role '{info.Role ?? "(none)"}': {info}");
        }

        return info;
    }

    /// <summary>Asserts the accessible name of the element (whitespace-normalized, exact).</summary>
    public static async Task<AccessibleElementInfo> ShouldHaveAccessibleNameAsync(this ILocator locator, string name)
    {
        var info = await locator.GetAccessibilityInfoAsync().ConfigureAwait(false);
        if (!string.Equals(info.Name, Normalize(name), StringComparison.Ordinal))
        {
            throw new AccessibilityAssertionException($"Expected accessible name \"{name}\" but it is \"{info.Name}\": {info}");
        }

        return info;
    }

    /// <summary>Asserts an ARIA or native state (name without <c>aria-</c>, for example <c>expanded</c>). <c>null</c> asserts the state is absent.</summary>
    public static async Task<AccessibleElementInfo> ShouldHaveStateAsync(this ILocator locator, string state, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        var key = state.StartsWith("aria-", StringComparison.Ordinal) ? state[5..] : state;
        var info = await locator.GetAccessibilityInfoAsync().ConfigureAwait(false);
        var actual = info.State(key);
        if (!string.Equals(actual, value, StringComparison.Ordinal))
        {
            throw new AccessibilityAssertionException($"Expected state '{key}' to be {(value is null ? "absent" : $"'{value}'")} but it is {(actual is null ? "absent" : $"'{actual}'")}: {info}");
        }

        return info;
    }

    internal static string Normalize(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    internal static async Task<AccessibilityAudit> AuditCoreAsync(
        IPage page,
        ILocator? scope,
        AccessibilityContext context,
        AccessibilityAuditOptions? options,
        AuditLabel? label,
        CancellationToken cancellationToken,
        Func<AccessibilityFinding, AccessibilityFinding>? transform = null)
    {
        options ??= AccessibilityAuditOptions.Default;
        var name = label?.Name ?? DefaultName(page, scope, context);
        context = context with
        {
            Scenario = label?.Scenario ?? context.Scenario,
            State = label?.State ?? context.State,
            Metadata = label?.Metadata ?? context.Metadata,
            Url = SafeUrl(page),
            Browser = page.Context.Browser?.BrowserType.Name,
            BrowserVersion = page.Context.Browser?.Version,
        };

        var builder = new AccessibilityAuditBuilder(name, context, options);
        try
        {
            var output = await RunEnginesAsync(page, scope, options, context.Kind, null).WaitAsync(options.Timeout, cancellationToken).ConfigureAwait(false);
            if (scope is not null && output.ScopeSelector is not null)
            {
                builder.Context = builder.Context with { ScopeSelector = output.ScopeSelector };
            }

            if (transform is not null)
            {
                var transformed = output.Output.Findings.Select(transform).ToArray();
                output.Output.Findings.Clear();
                output.Output.Findings.AddRange(transformed);
            }

            output.Output.AddTo(builder);
        }
        catch (TimeoutException ex)
        {
            builder.AddError(new ExecutionError { Source = "audit", Message = $"The audit did not finish within {options.Timeout.TotalSeconds:0.#} s ({ex.Message}).", ExceptionType = ex.GetType().FullName });
        }
        catch (OperationCanceledException ex)
        {
            builder.AddError(ExecutionError.FromException("audit", ex));
        }
        catch (Exception ex) when (ex is PlaywrightException or InvalidOperationException or JsonException)
        {
            builder.AddError(ExecutionError.FromException(ex is PlaywrightException ? "browser" : "engine", ex));
        }

        return Persist(builder.Build());
    }

    internal static AccessibilityAudit Persist(AccessibilityAudit audit)
    {
        var warning = AccessibilityResultStore.TrySaveToDefault(audit);
        return warning is null ? audit : audit with { Warnings = [.. audit.Warnings, warning] };
    }

    internal static async Task<(EngineOutput Output, string? ScopeSelector)> RunEnginesAsync(IPage page, ILocator? scope, AccessibilityAuditOptions options, AuditTargetKind kind, string? scopeSelector)
    {
        if (page.IsClosed)
        {
            throw new InvalidOperationException("The page is closed.");
        }

        // axe-core itself is injected by the axe run below; only the toolkit helpers are needed first.
        await ToolkitScript.EnsureAsync(page, includeAxe: false).ConfigureAwait(false);
        if (scope is not null)
        {
            var count = await scope.CountAsync().ConfigureAwait(false);
            if (count != 1)
            {
                throw new InvalidOperationException(count == 0
                    ? "The audit scope matched no elements, so nothing could be audited."
                    : $"The audit scope matched {count} elements; use a locator that matches exactly one element.");
            }

            scopeSelector = await scope.EvaluateAsync<string>("el => window.__blazorA11y.selectorFor(el)").ConfigureAwait(false);
        }

        var output = new EngineOutput();
        await AxeRunner.RunAsync(page, scopeSelector, options, kind, output).ConfigureAwait(false);
        if (options.ToolkitDiagnostics)
        {
            await ToolkitScript.EnsureAsync(page).ConfigureAwait(false);
            await DiagnosticsRunner.RunAsync(page, scopeSelector, options, output).ConfigureAwait(false);
        }

        return (output, scopeSelector);
    }

    private static string DefaultName(IPage page, ILocator? scope, AccessibilityContext context)
    {
        if (context.ComponentType is { } component)
        {
            return FindingFormatter.ShortTypeName(component);
        }

        var path = FindingFingerprint.NormalizeUrl(SafeUrl(page));
        if (string.IsNullOrEmpty(path) || path.EndsWith(':'))
        {
            path = "page";
        }

        return scope is null ? path : $"{path} [region]";
    }

    private static string? SafeUrl(IPage page)
    {
        try
        {
            return page.Url;
        }
        catch (PlaywrightException)
        {
            return null;
        }
    }
}
