namespace BlazorAccessibilityToolkit.Playwright.Internal;

internal sealed class FocusInfoDto
{
    public bool HasFocus { get; set; }

    public AccessibleElementInfo? Element { get; set; }

    public string? Modal { get; set; }

    public string? Indicator { get; set; }

    public bool FocusVisible { get; set; }

    public string? IndicatorMethod { get; set; }

    public string? Obscured { get; set; }

    public string? ObscuredBy { get; set; }

    public FocusBounds? Bounds { get; set; }

    public bool Rtl { get; set; }
}

internal sealed class ReflowDto
{
    public int ViewportWidth { get; set; }

    public int ScrollWidth { get; set; }

    public List<LayoutOffenderDto> Offenders { get; set; } = [];
}

internal sealed class LayoutOffenderDto
{
    public string? Selector { get; set; }

    public string? Html { get; set; }

    public string? Role { get; set; }

    public string? Name { get; set; }

    public int? Ordinal { get; set; }

    public bool Exempt { get; set; }

    public int Width { get; set; }

    public int Right { get; set; }

    public string? Axis { get; set; }

    public string? Text { get; set; }
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

internal sealed class MotionDto
{
    public string? Selector { get; set; }

    public string? Html { get; set; }

    public string? Role { get; set; }

    public string? Name { get; set; }

    public int? Ordinal { get; set; }

    public string? Kind { get; set; }

    public string? Name2 { get; set; }

    public string? Properties { get; set; }

    public int Duration { get; set; }

    public bool Infinite { get; set; }

    public bool Controls { get; set; }
}

internal sealed class OrientationDto
{
    public int Text { get; set; }

    public bool Rotated { get; set; }

    public List<string> Messages { get; set; } = [];
}

internal sealed class TargetDto
{
    public string? Selector { get; set; }

    public string? Html { get; set; }

    public string? Role { get; set; }

    public string? Name { get; set; }

    public int? Ordinal { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }
}
