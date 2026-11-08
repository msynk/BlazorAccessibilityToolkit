using System.Reflection;
using System.Text;
using AngleSharp.Dom;
using BlazorAccessibilityToolkit.ComponentTesting.Internal;
using BlazorAccessibilityToolkit.Engines;
using BlazorAccessibilityToolkit.Reporting;
using BlazorAccessibilityToolkit.Rules;
using Bunit;
using Microsoft.AspNetCore.Components;

#pragma warning disable BL0006 // Renderer is only passed through to MarkupSnapshot.

namespace BlazorAccessibilityToolkit.ComponentTesting;

/// <summary>How bUnit-rendered markup is audited. Immutable; share one instance across tests.</summary>
public sealed record ComponentAuditSettings
{
    /// <summary>Browserless defaults: Razor markup rules plus the conservative fallback rules, no browser engine.</summary>
    public static ComponentAuditSettings Default { get; } = new();

    /// <summary>Rule selection and suppressions.</summary>
    public AccessibilityAuditOptions Options { get; init; } = AccessibilityAuditOptions.Default;

    /// <summary>
    /// Browser engines that audit the rendered markup (for example <c>PlaywrightMarkupAuditEngine</c> from
    /// BlazorAccessibilityToolkit.Playwright, which runs axe-core). When empty, only browserless rules run.
    /// </summary>
    public IReadOnlyList<IMarkupAuditEngine> Engines { get; init; } = [];

    /// <summary>Document settings (language, title, stylesheets) used by browser engines.</summary>
    public MarkupDocumentOptions Document { get; init; } = MarkupDocumentOptions.Default;

    /// <summary>Run the Razor-aware markup rules (Blazor event handler semantics). Default <c>true</c>.</summary>
    public bool RazorRules { get; init; } = true;

    /// <summary>Run the browserless fallback rules when no engine is configured. Default <c>true</c>.</summary>
    public bool BrowserlessFallbackRules { get; init; } = true;

    /// <summary>Returns a copy that adds <paramref name="engine"/>.</summary>
    public ComponentAuditSettings WithEngine(IMarkupAuditEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return this with { Engines = [.. Engines, engine] };
    }
}

/// <summary>Accessibility audits for components rendered with bUnit.</summary>
/// <remarks>
/// What is checked: the rendered markup (structure, names, roles, ARIA, and Blazor event handlers that bUnit exposes).
/// What is not: keyboard behavior, focus, scripts and real CSS. Supply stylesheets in <see cref="MarkupDocumentOptions"/>
/// for meaningful color-contrast results, and use the interactive host in BlazorAccessibilityToolkit.Playwright for
/// keyboard, focus and live-region tests.
/// </remarks>
public static class ComponentAudits
{
    /// <summary>
    /// Audits the current markup of <paramref name="cut"/>. Findings are attributed to the component (from the render
    /// tree) that rendered the affected element. Wait for asynchronous rendering (for example with bUnit's
    /// <c>WaitForState</c>/<c>WaitForElement</c>) before auditing.
    /// </summary>
    public static async Task<AccessibilityAudit> AuditAccessibilityAsync<TComponent>(
        this IRenderedComponent<TComponent> cut,
        string? state = null,
        ComponentAuditSettings? settings = null,
        string? scenario = null,
        CancellationToken cancellationToken = default)
        where TComponent : IComponent
    {
        ArgumentNullException.ThrowIfNull(cut);
        settings ??= ComponentAuditSettings.Default;
        var options = settings.Options;
        var rootType = TypeName(typeof(TComponent));
        scenario ??= FindingFormatter.ShortTypeName(rootType);
        var context = new AccessibilityContext
        {
            Kind = AuditTargetKind.Component,
            ComponentType = rootType,
            Scenario = scenario,
            State = state,
            RenderMode = "bunit-markup",
        };
        var builder = new AccessibilityAuditBuilder(state is null ? scenario : $"{scenario} [{state}]", context, options);

        try
        {
            var renderer = RendererOf(cut);
            var snapshot = await cut.InvokeAsync(() => MarkupSnapshot.Capture(cut, renderer)).ConfigureAwait(false);
            if (snapshot.AttributionWarning is { } warning)
            {
                builder.AddWarning(warning);
            }

            if (!snapshot.HasContent)
            {
                builder.AddError(new ExecutionError { Source = "markup", Message = "The component rendered no markup, so nothing was audited. Wait for asynchronous rendering or audit a state that renders content." });
                return Finish(builder);
            }

            var browserless = settings.Engines.Count == 0 && settings.BrowserlessFallbackRules;
            if (settings.RazorRules || browserless)
            {
                builder.AddEngine(ToolkitRules.EngineName, typeof(ComponentAudits).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]);
                var results = RazorMarkupRules.Run(snapshot.Elements, browserless, options);
                if (!settings.RazorRules)
                {
                    results = results.Where(r => r.Rule != ToolkitRules.ClickHandlerSemantics).ToArray();
                }

                var ordinals = snapshot.Elements.Select((e, i) => (e, i)).ToDictionary(x => x.e, x => x.i);
                foreach (var result in results)
                {
                    var ordinal = ordinals[result.Element];
                    var finding = result.Rule.CreateFinding(
                        new FindingTarget { Selector = SelectorFor(result.Element, snapshot.Elements), Html = StartTag(result.Element), ElementOrdinal = ordinal },
                        remediation: result.Message,
                        status: result.Status,
                        data: result.Data);
                    builder.AddFinding(finding with { Component = snapshot.Attribute(ordinal, rootType) });
                }

                var reported = results.Select(r => r.Rule.Id).ToHashSet(StringComparer.Ordinal);
                foreach (var rule in RazorMarkupRules.Evaluated(browserless, options).Where(r => (settings.RazorRules || r != ToolkitRules.ClickHandlerSemantics) && !reported.Contains(r.Id)))
                {
                    builder.AddPassedRule(rule.Id);
                }
            }

            foreach (var engine in settings.Engines)
            {
                var result = await engine.AuditAsync(
                    new MarkupAuditRequest { BodyHtml = snapshot.BrowserMarkup, Options = options, Kind = AuditTargetKind.Component, Document = settings.Document },
                    cancellationToken).ConfigureAwait(false);

                foreach (var error in result.Errors)
                {
                    builder.AddError(error);
                }

                foreach (var info in result.Engines)
                {
                    builder.AddEngine(info.Name, info.Version);
                }

                foreach (var finding in result.Findings)
                {
                    builder.AddFinding(finding with { Component = snapshot.Attribute(finding.Target.ElementOrdinal, rootType) });
                }

                foreach (var rule in result.PassedRules)
                {
                    builder.AddPassedRule(rule);
                }

                foreach (var rule in result.InapplicableRules)
                {
                    builder.AddInapplicableRule(rule);
                }

                foreach (var engineWarning in result.Warnings)
                {
                    builder.AddWarning(engineWarning);
                }

                if (result.Browser is not null)
                {
                    builder.Context = builder.Context with { Browser = result.Browser, BrowserVersion = result.BrowserVersion };
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ElementNotFoundException or DomException or ArgumentException or OperationCanceledException)
        {
            builder.AddError("markup", ex);
        }

        return Finish(builder);
    }

    private static AccessibilityAudit Finish(AccessibilityAuditBuilder builder)
    {
        var audit = builder.Build();
        var warning = AccessibilityResultStore.TrySaveToDefault(audit);
        return warning is null ? audit : audit with { Warnings = [.. audit.Warnings, warning] };
    }

    // bUnit does not expose the renderer of a rendered component publicly; it is held in a field of the rendered
    // component. It is only needed for component attribution, so a missing renderer degrades to root-only attribution.
    private static Microsoft.AspNetCore.Components.RenderTree.Renderer? RendererOf<TComponent>(IRenderedComponent<TComponent> cut)
        where TComponent : IComponent
    {
        for (var type = cut.GetType(); type is not null; type = type.BaseType)
        {
            var field = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .FirstOrDefault(f => typeof(Microsoft.AspNetCore.Components.RenderTree.Renderer).IsAssignableFrom(f.FieldType));
            if (field?.GetValue(cut) is Microsoft.AspNetCore.Components.RenderTree.Renderer renderer)
            {
                return renderer;
            }
        }

        return null;
    }

    internal static string TypeName(Type type) => (type.FullName ?? type.Name).Split('[')[0];

    // Shortest selector, built bottom-up, that is unique within the rendered component.
    private static string SelectorFor(IElement element, IReadOnlyList<IElement> all)
    {
        var parts = new List<string>();
        for (var current = element; current is not null && current.LocalName is not ("body" or "html"); current = current.ParentElement)
        {
            string part;
            if (!string.IsNullOrEmpty(current.Id) && IsUnique(current, "#" + CssEscape(current.Id), all))
            {
                part = "#" + CssEscape(current.Id);
            }
            else
            {
                part = current.LocalName;
                var classes = current.ClassList.Where(c => !(c.Length == 12 && c.StartsWith("b-", StringComparison.Ordinal))).Take(2).ToArray();
                if (classes.Length > 0)
                {
                    part += "." + string.Join(".", classes.Select(CssEscape));
                }

                if (current.ParentElement is { } parent && parent.Children.Count(c => SafeMatches(c, part)) > 1)
                {
                    part += $":nth-child({parent.Children.Index(current) + 1})";
                }
            }

            parts.Insert(0, part);
            var candidate = string.Join(" > ", parts);
            if (IsUnique(element, candidate, all))
            {
                return candidate;
            }
        }

        return string.Join(" > ", parts);
    }

    // The rendered nodes are not attached to a document, so uniqueness is checked against the component's elements.
    private static bool IsUnique(IElement element, string selector, IReadOnlyList<IElement> all)
    {
        var count = 0;
        foreach (var candidate in all)
        {
            if (SafeMatches(candidate, selector) && ++count > 1)
            {
                return false;
            }
        }

        return count == 1 && SafeMatches(element, selector);
    }

    private static bool SafeMatches(IElement element, string selector)
    {
        try
        {
            return element.Matches(selector);
        }
        catch (DomException)
        {
            return false;
        }
    }

    private static string CssEscape(string value)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_' || c > 127)
            {
                if (i == 0 && char.IsAsciiDigit(c))
                {
                    sb.Append('\\').Append(((int)c).ToString("x", System.Globalization.CultureInfo.InvariantCulture)).Append(' ');
                }
                else
                {
                    sb.Append(c);
                }
            }
            else
            {
                sb.Append('\\').Append(c);
            }
        }

        return sb.ToString();
    }

    private static string StartTag(IElement element)
    {
        var sb = new StringBuilder("<").Append(element.LocalName);
        foreach (var attribute in element.Attributes.Where(a => !a.Name.StartsWith("blazor:", StringComparison.OrdinalIgnoreCase)))
        {
            sb.Append(' ').Append(attribute.Name);
            if (attribute.Value.Length > 0)
            {
                sb.Append("=\"").Append(attribute.Value.Replace("\"", "&quot;", StringComparison.Ordinal)).Append('"');
            }
        }

        sb.Append('>');
        var text = RazorMarkupRules.Normalize(element.TextContent);
        if (text.Length > 0)
        {
            sb.Append(text.Length > 80 ? text[..79] + "…" : text);
        }

        if (element.ChildElementCount > 0 || text.Length > 0)
        {
            sb.Append('…');
        }

        return sb.ToString();
    }
}
