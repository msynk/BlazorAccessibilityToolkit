using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BlazorAccessibilityToolkit.DevTools;

/// <summary>Where the overlay's button and panel appear. Start and end follow the page's writing direction.</summary>
public enum DevOverlayPosition
{
    /// <summary>The bottom corner at the end of the line (bottom right in left-to-right pages).</summary>
    BottomEnd,

    /// <summary>The bottom corner at the start of the line.</summary>
    BottomStart,

    /// <summary>The top corner at the end of the line.</summary>
    TopEnd,

    /// <summary>The top corner at the start of the line.</summary>
    TopStart,
}

/// <summary>
/// Adds the development accessibility overlay to the page: axe-core runs after each change of the page and the violations
/// appear in a panel and in the browser console. Put it in <c>App.razor</c>, after the Blazor script, and map the
/// endpoints with <see cref="AccessibilityDevToolsEndpoints.MapAccessibilityDevTools"/>.
/// </summary>
/// <remarks>
/// By default the overlay renders only in the Development environment. It stays off under browser automation, so that it
/// does not run axe-core while a test audits the page. It renders a module script; script elements are executed when
/// the page loads, so render it statically (in <c>App.razor</c>), not inside an interactive component.
/// </remarks>
public sealed class AccessibilityDevOverlay : ComponentBase
{
    [Inject] private IServiceProvider Services { get; set; } = null!;

    /// <summary>
    /// Whether to render the overlay. Default: only when the host environment is Development. Set it to <see langword="false"/>
    /// to turn the overlay off, or to <see langword="true"/> to render it in other environments (then also map the endpoints there).
    /// </summary>
    [Parameter] public bool? Enabled { get; set; }

    /// <summary>
    /// The axe-core tags to check, for example <c>wcag2a, wcag2aa</c>. Default: WCAG 2.0 to 2.2 A and AA and best practices.
    /// </summary>
    [Parameter] public IReadOnlyCollection<string>? Tags { get; set; }

    /// <summary>CSS selectors of content that is not checked, for example third-party widgets you cannot change.</summary>
    [Parameter] public IReadOnlyCollection<string>? Exclude { get; set; }

    /// <summary>Where the button and the panel appear. Default <see cref="DevOverlayPosition.BottomEnd"/>.</summary>
    [Parameter] public DevOverlayPosition Position { get; set; } = DevOverlayPosition.BottomEnd;

    /// <summary>How long the page must stay unchanged before it is checked again. Default 1 second.</summary>
    [Parameter] public TimeSpan Delay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Whether new violations are also written to the browser console. Default <see langword="true"/>.</summary>
    [Parameter] public bool LogToConsole { get; set; } = true;

    /// <summary>
    /// The URL of the overlay script. Default <c>_blazor-a11y/devtools.js</c>, relative to the page's base address.
    /// </summary>
    [Parameter] public string ScriptUrl { get; set; } = AccessibilityDevToolsEndpoints.BasePath + "/devtools.js";

    /// <summary>The URL of axe-core. Default: <c>axe.js</c> next to the overlay script.</summary>
    [Parameter] public string? AxeScriptUrl { get; set; }

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (!(Enabled ?? Services.GetService<IHostEnvironment>()?.IsDevelopment() ?? false))
        {
            return;
        }

        builder.OpenElement(0, "script");
        builder.AddAttribute(1, "type", "module");
        builder.AddAttribute(2, "src", ScriptUrl);
        builder.AddAttribute(3, "data-ba11y-devtools", string.Empty);
        builder.AddAttribute(4, "data-ba11y-position", Position switch
        {
            DevOverlayPosition.BottomStart => "bottom-start",
            DevOverlayPosition.TopEnd => "top-end",
            DevOverlayPosition.TopStart => "top-start",
            _ => "bottom-end",
        });
        builder.AddAttribute(5, "data-ba11y-delay", ((long)Math.Max(0, Delay.TotalMilliseconds)).ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (Tags is { Count: > 0 })
        {
            builder.AddAttribute(6, "data-ba11y-tags", string.Join(",", Tags));
        }

        if (Exclude is { Count: > 0 })
        {
            builder.AddAttribute(7, "data-ba11y-exclude", System.Text.Json.JsonSerializer.Serialize(Exclude));
        }

        if (!LogToConsole)
        {
            builder.AddAttribute(8, "data-ba11y-console", "false");
        }

        if (AxeScriptUrl is not null)
        {
            builder.AddAttribute(9, "data-ba11y-axe-src", AxeScriptUrl);
        }

        builder.CloseElement();
    }
}
