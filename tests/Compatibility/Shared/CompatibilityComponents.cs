using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorAccessibilityToolkit.Compatibility;

/// <summary>An accessible counter written in C# (the compatibility projects contain no .razor files).</summary>
public sealed class CounterWidget : ComponentBase
{
    private int count;

    [Parameter]
    public string Label { get; set; } = "Add one";

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "button");
        builder.AddAttribute(1, "type", "button");
        builder.AddAttribute(2, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, () => count++));
        builder.AddContent(3, Label);
        builder.CloseElement();
        builder.OpenElement(4, "p");
        builder.AddAttribute(5, "role", "status");
        builder.AddContent(6, count == 0 ? string.Empty : $"Count: {count}");
        builder.CloseElement();
    }
}

/// <summary>An icon-only button without an accessible name (intentional defect).</summary>
public sealed class UnnamedIconButton : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "button");
        builder.AddAttribute(1, "type", "button");
        builder.OpenElement(2, "span");
        builder.AddAttribute(3, "aria-hidden", "true");
        builder.AddContent(4, "★");
        builder.CloseElement();
        builder.CloseElement();
    }
}
