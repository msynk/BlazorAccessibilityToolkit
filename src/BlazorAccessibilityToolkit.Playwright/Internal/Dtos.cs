namespace BlazorAccessibilityToolkit.Playwright.Internal;

internal sealed class FocusInfoDto
{
    public bool HasFocus { get; set; }

    public AccessibleElementInfo? Element { get; set; }

    public string? Modal { get; set; }

    public string? Indicator { get; set; }

    public bool FocusVisible { get; set; }
}

internal sealed class DiagnosticDto
{
    public string RuleId { get; set; } = string.Empty;

    public string? Selector { get; set; }

    public string? Html { get; set; }

    public string? Role { get; set; }

    public string? Name { get; set; }

    public int? Ordinal { get; set; }

    public Dictionary<string, string>? Data { get; set; }

    public string? Message { get; set; }
}

internal sealed class DescribedSelectorDto
{
    public string? Role { get; set; }

    public string? Name { get; set; }

    public int? Ordinal { get; set; }
}

internal sealed class TabbableDto
{
    public int Key { get; set; }

    public string? Selector { get; set; }

    public int TabIndex { get; set; }
}
