using System.ComponentModel;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorAccessibilityToolkit.Playwright.Hosting;

/// <summary>Document settings of the host page.</summary>
internal sealed record HostDocumentSettings(string Language, string Title, IReadOnlyList<string> Stylesheets, IReadOnlyList<string> Scripts, string? HeadMarkup);

/// <summary>Interactive Server render mode without prerendering, so the component is rendered once, interactively.</summary>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class InteractiveServerWithoutPrerenderAttribute : RenderModeAttribute
{
    public override IComponentRenderMode Mode { get; } = new InteractiveServerRenderMode(prerender: false);
}

/// <summary>Root component of the test host. Infrastructure; not intended for direct use.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class AccessibilityHostApp : ComponentBase
{
    [Inject]
    internal HostDocumentSettings Settings { get; set; } = null!;

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.AddMarkupContent(0, "<!DOCTYPE html>");
        builder.OpenElement(1, "html");
        builder.AddAttribute(2, "lang", Settings.Language);
        builder.OpenElement(3, "head");
        builder.AddMarkupContent(4, "<meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><base href=\"/\">");
        builder.OpenElement(5, "title");
        builder.AddContent(6, Settings.Title);
        builder.CloseElement();
        foreach (var href in Settings.Stylesheets)
        {
            builder.OpenElement(7, "link");
            builder.AddAttribute(8, "rel", "stylesheet");
            builder.AddAttribute(9, "href", href);
            builder.CloseElement();
        }

        if (Settings.HeadMarkup is { } head)
        {
            builder.AddMarkupContent(10, head);
        }

        builder.CloseElement();
        builder.OpenElement(11, "body");
        builder.OpenComponent<Router>(12);
        builder.AddComponentParameter(13, nameof(Router.AppAssembly), typeof(AccessibilityHostApp).Assembly);
        builder.AddComponentParameter(14, nameof(Router.Found), (RenderFragment<RouteData>)(routeData => b =>
        {
            b.OpenComponent<RouteView>(0);
            b.AddComponentParameter(1, nameof(RouteView.RouteData), routeData);
            b.CloseComponent();
        }));
        builder.CloseComponent();
        builder.OpenElement(15, "script");
        builder.AddAttribute(16, "src", "_framework/blazor.web.js");
        builder.CloseElement();
        foreach (var src in Settings.Scripts)
        {
            builder.OpenElement(17, "script");
            builder.AddAttribute(18, "src", src);
            builder.CloseElement();
        }

        builder.CloseElement();
        builder.CloseElement();
    }
}

/// <summary>Base class of the host pages. Infrastructure; not intended for direct use.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class AccessibilityHostPageBase : ComponentBase
{
    private ComponentRegistration? _registration;
    private DynamicComponent? _dynamic;

    /// <summary>Registration id.</summary>
    [Parameter]
    public string? Id { get; set; }

    [Inject]
    internal ComponentRegistry Registry { get; set; } = null!;

    /// <inheritdoc />
    protected override void OnParametersSet() => _registration = Registry.Find(Id);

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (_registration is null)
        {
            builder.OpenElement(0, "p");
            builder.AddAttribute(1, "data-a11y-host-state", "missing");
            builder.AddContent(2, "The component registration was not found (it may have been disposed).");
            builder.CloseElement();
            return;
        }

        builder.OpenElement(3, "div");
        builder.AddAttribute(4, "data-a11y-host-root", string.Empty);
        builder.AddAttribute(5, "data-a11y-host-state", RendererInfo.IsInteractive ? "interactive" : "static");
        builder.AddContent(6, Wrap(_registration.CascadingValues, 0, RenderComponent));
        builder.CloseElement();
    }

    /// <inheritdoc />
    protected override void OnAfterRender(bool firstRender)
    {
        if (RendererInfo.IsInteractive && _registration is not null && _dynamic?.Instance is Microsoft.AspNetCore.Components.IComponent instance)
        {
            _registration.Attach(instance, work => InvokeAsync(work));
        }
    }

    private void RenderComponent(RenderTreeBuilder builder)
    {
        builder.OpenComponent<DynamicComponent>(0);
        builder.AddComponentParameter(1, nameof(DynamicComponent.Type), _registration!.ComponentType);
        builder.AddComponentParameter(2, nameof(DynamicComponent.Parameters), _registration.Parameters.ToDictionary(kv => kv.Key, kv => kv.Value!));
        builder.AddComponentReferenceCapture(3, reference => _dynamic = (DynamicComponent)reference);
        builder.CloseComponent();
    }

    private static RenderFragment Wrap(IReadOnlyList<CascadingValueRegistration> values, int index, RenderFragment inner)
    {
        if (index >= values.Count)
        {
            return inner;
        }

        var value = values[index];
        return builder =>
        {
            builder.OpenComponent(0, typeof(CascadingValue<>).MakeGenericType(value.ValueType));
            builder.AddComponentParameter(1, "Value", value.Value);
            if (value.Name is not null)
            {
                builder.AddComponentParameter(2, "Name", value.Name);
            }

            builder.AddComponentParameter(3, "ChildContent", Wrap(values, index + 1, inner));
            builder.CloseComponent();
        };
    }
}

/// <summary>Host page rendering a registered component with Interactive Server. Infrastructure; not intended for direct use.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Route("/_a11y/interactive/{Id}")]
[InteractiveServerWithoutPrerender]
public sealed class AccessibilityInteractiveHostPage : AccessibilityHostPageBase
{
}

/// <summary>Host page rendering a registered component with static server-side rendering. Infrastructure; not intended for direct use.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Route("/_a11y/static/{Id}")]
public sealed class AccessibilityStaticHostPage : AccessibilityHostPageBase
{
}
