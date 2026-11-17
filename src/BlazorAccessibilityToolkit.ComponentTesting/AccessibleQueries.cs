using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using BlazorAccessibilityToolkit.ComponentTesting.Internal;
using Bunit;
using Microsoft.AspNetCore.Components;

namespace BlazorAccessibilityToolkit.ComponentTesting;

/// <summary>Filters for <see cref="AccessibleQueries.FindByRole{TComponent}(IRenderedComponent{TComponent}, string, ByRoleOptions?)"/>, modeled on Testing Library's <c>*ByRole</c> options.</summary>
public sealed record ByRoleOptions
{
    /// <summary>Exact accessible name (whitespace normalized, case-sensitive).</summary>
    public string? Name { get; init; }

    /// <summary>Pattern the accessible name must match.</summary>
    public Regex? NamePattern { get; init; }

    /// <summary>Exact accessible description (whitespace normalized).</summary>
    public string? Description { get; init; }

    /// <summary>Also match elements that are hidden from assistive technologies. Default <c>false</c>.</summary>
    public bool Hidden { get; init; }

    /// <summary>Checked state (<c>aria-checked</c> or a native checkbox or radio button). A mixed checkbox matches neither value.</summary>
    public bool? Checked { get; init; }

    /// <summary>Pressed state (<c>aria-pressed</c>). A mixed toggle button matches neither value.</summary>
    public bool? Pressed { get; init; }

    /// <summary>Expanded state (<c>aria-expanded</c>). Elements without the attribute match neither value.</summary>
    public bool? Expanded { get; init; }

    /// <summary>Selected state (<c>aria-selected</c> or a selected option).</summary>
    public bool? Selected { get; init; }

    /// <summary>Disabled state (native <c>disabled</c>, a disabled fieldset, or <c>aria-disabled</c>).</summary>
    public bool? Disabled { get; init; }

    /// <summary>Busy state (<c>aria-busy</c>).</summary>
    public bool? Busy { get; init; }

    /// <summary>Heading level (<c>h1</c>..<c>h6</c> or <c>aria-level</c>).</summary>
    public int? Level { get; init; }

    /// <summary><c>aria-current</c>: <c>"true"</c> matches any current value, <c>"false"</c> matches none, or a token such as <c>"page"</c>.</summary>
    public string? Current { get; init; }

    /// <summary>Match any role in the <c>role</c> attribute, not only the first valid one.</summary>
    public bool QueryFallbacks { get; init; }
}

/// <summary>
/// Testing Library style queries and accessible name, description, role and state helpers for bUnit markup. Names follow
/// the W3C accessible name computation (accname 1.2) without CSS; see <see href="https://www.w3.org/TR/accname-1.2/"/>.
/// </summary>
/// <remarks>
/// Querying by role and name tests what assistive technologies expose, so the test fails when a control loses its
/// role or its label. Without CSS, content hidden by stylesheets counts as visible and names do not include CSS
/// generated content; verify those cases in a browser.
/// </remarks>
public static class AccessibleQueries
{
    /// <summary>Finds the single element with <paramref name="role"/> that matches <paramref name="options"/>. Throws when none or several match.</summary>
    public static IElement FindByRole<TComponent>(this IRenderedComponent<TComponent> cut, string role, ByRoleOptions? options = null)
        where TComponent : IComponent => Single(Computation(cut), role, options);

    /// <summary>Finds the single element with <paramref name="role"/> and accessible name <paramref name="name"/>.</summary>
    public static IElement FindByRole<TComponent>(this IRenderedComponent<TComponent> cut, string role, string name)
        where TComponent : IComponent => Single(Computation(cut), role, new ByRoleOptions { Name = name });

    /// <summary>Finds every element with <paramref name="role"/> that matches <paramref name="options"/>, in document order. Throws when none match.</summary>
    public static IReadOnlyList<IElement> FindAllByRole<TComponent>(this IRenderedComponent<TComponent> cut, string role, ByRoleOptions? options = null)
        where TComponent : IComponent
    {
        var computation = Computation(cut);
        var matches = Match(computation, role, options);
        return matches.Count > 0 ? matches : throw new AccessibilityAssertionException(NotFound(computation, role, options));
    }

    /// <summary>Returns the single matching element, or <c>null</c> when none matches. Throws when several match.</summary>
    public static IElement? QueryByRole<TComponent>(this IRenderedComponent<TComponent> cut, string role, ByRoleOptions? options = null)
        where TComponent : IComponent
    {
        var computation = Computation(cut);
        var matches = Match(computation, role, options);
        return matches.Count switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new AccessibilityAssertionException(Multiple(computation, role, options, matches)),
        };
    }

    /// <summary>Finds the single element with <paramref name="role"/> among the descendants of <paramref name="container"/>.</summary>
    public static IElement FindByRole(this IElement container, string role, ByRoleOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(container);
        return Single(new AccessibilityComputation(container.ChildNodes), role, options, AccessibilityComputation.For(container));
    }

    /// <summary>Finds the single form control or element labelled <paramref name="text"/> (by a label, aria-labelledby or aria-label).</summary>
    public static IElement FindByLabelText<TComponent>(this IRenderedComponent<TComponent> cut, string text)
        where TComponent : IComponent
    {
        ArgumentNullException.ThrowIfNull(text);
        var computation = Computation(cut);
        var expected = AccessibilityComputation.Normalize(text);
        var matches = computation.Elements
            .Where(e => !AccessibilityComputation.IsHidden(e) && IsLabelledElement(e, computation) && computation.Name(e) == expected)
            .ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new AccessibilityAssertionException($"No element is labelled \"{expected}\".{Environment.NewLine}{Environment.NewLine}{DescribeRoles(computation)}"),
            _ => throw new AccessibilityAssertionException($"{matches.Length} elements are labelled \"{expected}\"; labels must identify one control.{Environment.NewLine}{string.Join(Environment.NewLine, matches.Select(Describe))}"),
        };
    }

    /// <summary>The accessible name of the element.</summary>
    public static string GetAccessibleName(this IElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return AccessibilityComputation.For(element).Name(element);
    }

    /// <summary>The accessible description of the element.</summary>
    public static string GetAccessibleDescription(this IElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return AccessibilityComputation.For(element).Description(element);
    }

    /// <summary>The text referenced by <c>aria-errormessage</c>, when the element is invalid (empty otherwise).</summary>
    public static string GetAccessibleErrorMessage(this IElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return AccessibilityComputation.For(element).ErrorMessage(element);
    }

    /// <summary>The computed role (explicit or implicit), or <c>null</c> for generic elements.</summary>
    public static string? GetRole(this IElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return AccessibilityComputation.For(element).Role(element);
    }

    /// <summary>
    /// A state without the <c>aria-</c> prefix (<c>checked</c>, <c>expanded</c>, <c>pressed</c>, <c>selected</c>,
    /// <c>disabled</c>, <c>required</c>, <c>invalid</c>, <c>level</c>, <c>current</c> ...), combining ARIA and native semantics.
    /// </summary>
    public static string? GetAccessibleState(this IElement element, string state)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        return AccessibilityComputation.For(element).State(element, state.StartsWith("aria-", StringComparison.Ordinal) ? state[5..] : state);
    }

    /// <summary>Lists the elements of the component by role with their names, like Testing Library's <c>logRoles</c>.</summary>
    public static string DescribeRoles<TComponent>(this IRenderedComponent<TComponent> cut)
        where TComponent : IComponent => DescribeRoles(Computation(cut));

    internal static AccessibilityComputation Computation<TComponent>(IRenderedComponent<TComponent> cut)
        where TComponent : IComponent
    {
        ArgumentNullException.ThrowIfNull(cut);
        return new AccessibilityComputation(cut.Nodes);
    }

    private static bool IsLabelledElement(IElement element, AccessibilityComputation computation) =>
        element.HasAttribute("aria-label") || element.HasAttribute("aria-labelledby")
        || (element.LocalName is "input" or "select" or "textarea" or "meter" or "progress" or "output" or "button"
            && (element.Ancestors<IElement>().Any(a => a.LocalName == "label") || (!string.IsNullOrEmpty(element.Id) && computation.Elements.Any(l => l.LocalName == "label" && l.GetAttribute("for") == element.Id))));

    private static IElement Single(AccessibilityComputation computation, string role, ByRoleOptions? options, AccessibilityComputation? context = null)
    {
        var matches = Match(computation, role, options, context);
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new AccessibilityAssertionException(NotFound(computation, role, options, context)),
            _ => throw new AccessibilityAssertionException(Multiple(computation, role, options, matches, context)),
        };
    }

    private static List<IElement> Match(AccessibilityComputation computation, string role, ByRoleOptions? options, AccessibilityComputation? context = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        options ??= new ByRoleOptions();
        var names = context ?? computation;
        var result = new List<IElement>();
        foreach (var element in computation.Elements)
        {
            var roles = options.QueryFallbacks
                ? AccessibilityComputation.Tokens(element.GetAttribute("role")).Select(r => r.ToLowerInvariant()).Append(names.Role(element) ?? string.Empty)
                : [names.Role(element) ?? string.Empty];
            if (!roles.Contains(role.ToLowerInvariant()))
            {
                continue;
            }

            if (!options.Hidden && AccessibilityComputation.IsHidden(element))
            {
                continue;
            }

            if (options.Name is { } name && names.Name(element) != AccessibilityComputation.Normalize(name))
            {
                continue;
            }

            if (options.NamePattern is { } pattern && !pattern.IsMatch(names.Name(element)))
            {
                continue;
            }

            if (options.Description is { } description && names.Description(element) != AccessibilityComputation.Normalize(description))
            {
                continue;
            }

            if (!StateMatches(names.State(element, "checked"), options.Checked)
                || !StateMatches(names.State(element, "pressed"), options.Pressed)
                || !StateMatches(names.State(element, "expanded"), options.Expanded)
                || !StateMatches(names.State(element, "selected"), options.Selected)
                || !StateMatches(names.State(element, "busy") ?? "false", options.Busy)
                || (options.Disabled is { } disabled && (names.State(element, "disabled") == "true") != disabled))
            {
                continue;
            }

            if (options.Level is { } level && names.State(element, "level") != level.ToString(CultureInfo.InvariantCulture))
            {
                continue;
            }

            if (options.Current is { } current)
            {
                var actual = names.State(element, "current");
                var matches = current.Trim().ToLowerInvariant() switch
                {
                    "true" => actual is not null,
                    "false" => actual is null,
                    var token => actual == token,
                };
                if (!matches)
                {
                    continue;
                }
            }

            result.Add(element);
        }

        return result;
    }

    // A filter matches "true" or "false" exactly; "mixed" and missing states match neither.
    private static bool StateMatches(string? actual, bool? expected) =>
        expected is not { } value || actual == (value ? "true" : "false");

    private static string NotFound(AccessibilityComputation computation, string role, ByRoleOptions? options, AccessibilityComputation? context = null) =>
        $"No element with role \"{role}\"{Criteria(options)} was found.{Environment.NewLine}{Environment.NewLine}{DescribeRoles(computation, context)}";

    private static string Multiple(AccessibilityComputation computation, string role, ByRoleOptions? options, IReadOnlyList<IElement> matches, AccessibilityComputation? context = null) =>
        $"{matches.Count} elements with role \"{role}\"{Criteria(options)} were found; narrow the query with a name or state, or use FindAllByRole.{Environment.NewLine}{string.Join(Environment.NewLine, matches.Select(m => Describe(m, context ?? computation)))}";

    private static string Criteria(ByRoleOptions? options)
    {
        if (options is null)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        if (options.Name is not null)
        {
            parts.Add($"name \"{options.Name}\"");
        }

        if (options.NamePattern is not null)
        {
            parts.Add($"name matching /{options.NamePattern}/");
        }

        if (options.Description is not null)
        {
            parts.Add($"description \"{options.Description}\"");
        }

        foreach (var (label, value) in new[] { ("checked", options.Checked), ("pressed", options.Pressed), ("expanded", options.Expanded), ("selected", options.Selected), ("disabled", options.Disabled), ("busy", options.Busy) })
        {
            if (value is { } v)
            {
                parts.Add($"{label}={v.ToString().ToLowerInvariant()}");
            }
        }

        if (options.Level is { } level)
        {
            parts.Add($"level={level}");
        }

        if (options.Current is { } current)
        {
            parts.Add($"current={current}");
        }

        return parts.Count == 0 ? string.Empty : " and " + string.Join(", ", parts);
    }

    private static string Describe(IElement element) => Describe(element, AccessibilityComputation.For(element));

    private static string Describe(IElement element, AccessibilityComputation computation)
    {
        var name = computation.Name(element);
        return $"  {computation.Role(element) ?? "generic"} \"{name}\": <{element.LocalName}{(string.IsNullOrEmpty(element.Id) ? string.Empty : $" id=\"{element.Id}\"")}>";
    }

    private static string DescribeRoles(AccessibilityComputation computation, AccessibilityComputation? context = null)
    {
        var names = context ?? computation;
        var groups = computation.Elements
            .Where(e => !AccessibilityComputation.IsHidden(e))
            .Select(e => (Element: e, Role: names.Role(e)))
            .Where(x => x.Role is not null and not "none" and not "generic")
            .GroupBy(x => x.Role!, StringComparer.Ordinal)
            .ToArray();
        if (groups.Length == 0)
        {
            return "The markup contains no accessible roles.";
        }

        var sb = new StringBuilder("Accessible roles:");
        foreach (var group in groups)
        {
            sb.AppendLine().Append(group.Key).Append(':');
            foreach (var (element, _) in group)
            {
                sb.AppendLine().Append("  \"").Append(names.Name(element)).Append("\" <").Append(element.LocalName).Append('>');
            }
        }

        return sb.ToString();
    }
}

/// <summary>Assertions on the accessible name, description, error message, role and states of bUnit elements (jest-dom style).</summary>
public static class AccessibleElementAssertions
{
    /// <summary>Asserts the accessible name (whitespace normalized, exact).</summary>
    public static IElement ShouldHaveAccessibleName(this IElement element, string expected) =>
        Check(element, element.GetAccessibleName(), AccessibilityComputation.Normalize(expected), "accessible name");

    /// <summary>Asserts that the accessible name matches <paramref name="pattern"/>.</summary>
    public static IElement ShouldHaveAccessibleName(this IElement element, Regex pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        var actual = element.GetAccessibleName();
        return pattern.IsMatch(actual) ? element : throw new AccessibilityAssertionException($"Expected the accessible name of {Tag(element)} to match /{pattern}/, but it is \"{actual}\".");
    }

    /// <summary>Asserts the accessible description (whitespace normalized, exact).</summary>
    public static IElement ShouldHaveAccessibleDescription(this IElement element, string expected) =>
        Check(element, element.GetAccessibleDescription(), AccessibilityComputation.Normalize(expected), "accessible description");

    /// <summary>Asserts that the element is invalid and that <c>aria-errormessage</c> references <paramref name="expected"/>.</summary>
    public static IElement ShouldHaveAccessibleErrorMessage(this IElement element, string expected)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (!AccessibilityComputation.IsInvalid(element))
        {
            throw new AccessibilityAssertionException($"Expected {Tag(element)} to have the error message \"{expected}\", but it is not marked aria-invalid, so no error message is exposed.");
        }

        return Check(element, element.GetAccessibleErrorMessage(), AccessibilityComputation.Normalize(expected), "accessible error message (aria-errormessage)");
    }

    /// <summary>Asserts the computed role.</summary>
    public static IElement ShouldHaveRole(this IElement element, string expected) =>
        Check(element, element.GetRole() ?? "generic", expected.Trim().ToLowerInvariant(), "role");

    /// <summary>Asserts that the element is marked invalid (<c>aria-invalid</c>).</summary>
    public static IElement ShouldBeInvalid(this IElement element) => CheckState(element, "invalid", v => v is not null, "invalid (aria-invalid)");

    /// <summary>Asserts that the element is not marked invalid.</summary>
    public static IElement ShouldBeValid(this IElement element) => CheckState(element, "invalid", v => v is null, "valid (no aria-invalid)");

    /// <summary>Asserts that the element is required (<c>required</c> or <c>aria-required</c>).</summary>
    public static IElement ShouldBeRequired(this IElement element) => CheckState(element, "required", v => v == "true", "required");

    /// <summary>Asserts that the element is disabled (natively, by a fieldset, or with <c>aria-disabled</c>).</summary>
    public static IElement ShouldBeDisabled(this IElement element) => CheckState(element, "disabled", v => v == "true", "disabled");

    /// <summary>Asserts that the element is not disabled.</summary>
    public static IElement ShouldBeEnabled(this IElement element) => CheckState(element, "disabled", v => v is null, "enabled");

    /// <summary>Asserts the checked state (<c>true</c>, <c>false</c> or <c>mixed</c>).</summary>
    public static IElement ShouldBeChecked(this IElement element, string expected = "true") => CheckState(element, "checked", v => v == expected, $"checked=\"{expected}\"");

    /// <summary>Asserts the pressed state of a toggle button (<c>true</c>, <c>false</c> or <c>mixed</c>).</summary>
    public static IElement ShouldBePressed(this IElement element, string expected = "true") => CheckState(element, "pressed", v => v == expected, $"aria-pressed=\"{expected}\"");

    /// <summary>Asserts the expanded state (<c>aria-expanded</c>).</summary>
    public static IElement ShouldBeExpanded(this IElement element, bool expected = true) => CheckState(element, "expanded", v => v == (expected ? "true" : "false"), expected ? "expanded (aria-expanded=\"true\")" : "collapsed (aria-expanded=\"false\")");

    private static IElement Check(IElement element, string actual, string expected, string what)
    {
        ArgumentNullException.ThrowIfNull(element);
        return actual == expected ? element : throw new AccessibilityAssertionException($"Expected the {what} of {Tag(element)} to be \"{expected}\", but it is \"{actual}\".");
    }

    private static IElement CheckState(IElement element, string state, Func<string?, bool> predicate, string expectation)
    {
        ArgumentNullException.ThrowIfNull(element);
        var actual = element.GetAccessibleState(state);
        return predicate(actual) ? element : throw new AccessibilityAssertionException($"Expected {Tag(element)} to be {expectation}, but its {state} state is {(actual is null ? "not set" : $"\"{actual}\"")}.");
    }

    private static string Tag(IElement element)
    {
        var name = element.GetAccessibleName();
        return $"<{element.LocalName}{(string.IsNullOrEmpty(element.Id) ? string.Empty : $" id=\"{element.Id}\"")}>{(name.Length > 0 ? $" \"{name}\"" : string.Empty)}";
    }
}
