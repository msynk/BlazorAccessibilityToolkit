using BlazorAccessibilityToolkit.Demo.Components.Accessible;
using BlazorAccessibilityToolkit.Demo.Components.Defects;
using BlazorAccessibilityToolkit.Playwright;
using Microsoft.Playwright;

namespace BlazorAccessibilityToolkit.Demo.WebApp.Lab.Examples;

/// <summary>Focus-order capture: the observed Tab sequence, compared with an authored expectation.</summary>
public static class FocusOrderExamples
{
    public static readonly LabExample AnimatedFilterOrder = new()
    {
        Id = "animated-filter-order",
        ExpectFailure = true,
        NeedsBrowser = true,
        Run = async env =>
        {
            #region snippet:animated-filter-order
            await using var component = await env.Host.RenderAsync<AnimatedFilterPanel>(env.Browser);

            var order = await component.CaptureFocusOrderAsync();   // presses Tab until focus leaves the component
            env.Show(order);

            order.ShouldMatch("button Filters", "button Show 24 results").ShouldNotTrapFocus();
            #endregion
        },
    };

    public static readonly LabExample FilterOrder = new()
    {
        Id = "filter-order",
        NeedsBrowser = true,
        Run = async env =>
        {
            #region snippet:filter-order
            await using var component = await env.Host.RenderAsync<FilterPanel>(env.Browser);

            var order = await component.CaptureFocusOrderAsync();   // presses Tab until focus leaves the component
            env.Show(order);

            order.ShouldMatch("button Filters", "button Show 24 results").ShouldNotTrapFocus();
            #endregion
        },
    };

    public static readonly LabExample DialogFocusCycle = new()
    {
        Id = "dialog-focus-cycle",
        NeedsBrowser = true,
        Run = async env =>
        {
            #region snippet:dialog-focus-cycle
            await using var component = await env.Host.RenderAsync<ModalDialog>(env.Browser, p => p
                .Add(c => c.TriggerText, "Delete account")
                .Add(c => c.Title, "Delete account?")
                .Add(c => c.ConfirmText, "Delete"));
            await component.Keyboard()
                .FocusOn(component.Page.GetByRole(AriaRole.Button, new() { Name = "Delete account" }))
                .PressEnter()
                .ExpectFocused("button", "Cancel")                     // open, and the dialog has taken focus
                .RunAsync();

            var order = await component.CaptureFocusOrderAsync();
            env.Show(order);

            // Focus cycles between Cancel and Delete. Inside an aria-modal dialog that is expected, not a trap.
            order.ShouldMatch("button Cancel", "button Delete").ShouldNotTrapFocus();
            #endregion
        },
    };

    public static readonly LabExample BareFocusIndicator = new()
    {
        Id = "bare-focus-indicator",
        ExpectFailure = true,
        NeedsBrowser = true,
        Run = async env =>
        {
            #region snippet:bare-focus-indicator
            await using var component = await env.Host.RenderAsync<OutlineFreeViewSwitcher>(env.Browser);

            // Opt in: at every stop, check that the focused element draws an outline or a box shadow.
            var order = await component.CaptureFocusOrderAsync(new FocusOrderOptions { ReportMissingFocusIndicator = true });
            env.Show(order);

            order.ShouldMatch("button List", "button Grid", "button Map");
            Assert.Empty(order.Findings);
            #endregion
        },
    };

    public static readonly LabExample VisibleFocusIndicator = new()
    {
        Id = "visible-focus-indicator",
        NeedsBrowser = true,
        Run = async env =>
        {
            #region snippet:visible-focus-indicator
            await using var component = await env.Host.RenderAsync<ViewSwitcher>(env.Browser);

            // Opt in: at every stop, check that the focused element draws an outline or a box shadow.
            var order = await component.CaptureFocusOrderAsync(new FocusOrderOptions { ReportMissingFocusIndicator = true });
            env.Show(order);

            order.ShouldMatch("button List", "button Grid", "button Map");
            Assert.Empty(order.Findings);
            #endregion
        },
    };
}
