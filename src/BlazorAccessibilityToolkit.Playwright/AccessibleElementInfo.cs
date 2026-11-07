using System.Text.Json.Serialization;

namespace BlazorAccessibilityToolkit.Playwright;

/// <summary>
/// Accessibility information about an element: role and name computed with axe-core's accessible name
/// implementation, ARIA/native states, visibility and focusability.
/// </summary>
/// <remarks>
/// This is a DOM-based computation, not the browser's platform accessibility tree. It is reliable for the common
/// accessible-name sources (content, aria-label, aria-labelledby, label, alt, title) but is not a substitute for
/// testing with assistive technologies.
/// </remarks>
public sealed record AccessibleElementInfo
{
    /// <summary>Lower-case tag name.</summary>
    public required string TagName { get; init; }

    /// <summary>Computed role (explicit or implicit), or <c>null</c>.</summary>
    public string? Role { get; init; }

    /// <summary>Computed accessible name (whitespace normalized).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Accessible description from aria-describedby (or title).</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>States without the <c>aria-</c> prefix (for example <c>expanded</c>, <c>checked</c>, <c>disabled</c>).</summary>
    public IReadOnlyDictionary<string, string> States { get; init; } = new Dictionary<string, string>();

    /// <summary>A CSS selector for the element.</summary>
    public string? Selector { get; init; }

    /// <summary>tabIndex, when the element is focusable or has a tabindex attribute.</summary>
    public int? TabIndex { get; init; }

    /// <summary>True when the element can receive focus.</summary>
    public bool Focusable { get; init; }

    /// <summary>True when the element is rendered and visible.</summary>
    public bool Visible { get; init; }

    /// <summary>True when the element is hidden from assistive technologies (aria-hidden, hidden, inert ...).</summary>
    [JsonPropertyName("hiddenForAT")]
    public bool HiddenFromAssistiveTechnologies { get; init; }

    /// <summary>Start tag and text excerpt.</summary>
    public string? Html { get; init; }

    /// <summary>Identity of the element within the page (stable while the element is connected).</summary>
    [JsonPropertyName("key")]
    public int ElementKey { get; init; }

    /// <summary>Component markup ordinal, when the page was created from component markup.</summary>
    [JsonPropertyName("ordinal")]
    public int? ElementOrdinal { get; init; }

    /// <summary>Gets a state value, or <c>null</c>.</summary>
    public string? State(string name) => States.TryGetValue(name, out var value) ? value : null;

    /// <inheritdoc />
    public override string ToString() => $"{Role ?? TagName} \"{Name}\"{(Selector is null ? string.Empty : $" ({Selector})")}";
}

/// <summary>Whether a focus indicator style was detected.</summary>
public enum FocusIndicator
{
    /// <summary>Not measured.</summary>
    Unknown,

    /// <summary>An outline or box-shadow was present on the focused element.</summary>
    Detected,

    /// <summary>Neither outline nor box-shadow was present. The indicator may still exist (for example a background change); verify manually.</summary>
    NotDetected,
}

/// <summary>The focused element at a point in time.</summary>
public sealed record FocusSnapshot
{
    /// <summary>True when the document has focus.</summary>
    public bool DocumentHasFocus { get; init; }

    /// <summary>The focused element (piercing shadow roots and same-origin iframes), or <c>null</c> when focus is on the body or outside the page.</summary>
    public AccessibleElementInfo? Element { get; init; }

    /// <summary>Selector of the modal dialog containing the focused element, when any.</summary>
    public string? ModalContainer { get; init; }

    /// <summary>Focus indicator observation.</summary>
    public FocusIndicator Indicator { get; init; }

    /// <summary>True when the element matched <c>:focus-visible</c>.</summary>
    public bool FocusVisible { get; init; }

    /// <inheritdoc />
    public override string ToString() => Element?.ToString() ?? "(no focused element)";

    internal static FocusSnapshot From(Internal.FocusInfoDto? dto) => dto is null
        ? new FocusSnapshot()
        : new FocusSnapshot
        {
            DocumentHasFocus = dto.HasFocus,
            Element = dto.Element,
            ModalContainer = dto.Modal,
            Indicator = dto.Indicator switch
            {
                "detected" => FocusIndicator.Detected,
                "not-detected" => FocusIndicator.NotDetected,
                _ => FocusIndicator.Unknown,
            },
            FocusVisible = dto.FocusVisible,
        };
}
