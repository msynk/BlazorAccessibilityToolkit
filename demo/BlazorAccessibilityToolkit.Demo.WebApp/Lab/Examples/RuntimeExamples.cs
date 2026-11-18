using BlazorAccessibilityToolkit.Demo.Components.Accessible;
using BlazorAccessibilityToolkit.Playwright;
using Microsoft.Playwright;

namespace BlazorAccessibilityToolkit.Demo.WebApp.Lab.Examples;

/// <summary>Components of BlazorAccessibilityToolkit.Components, verified with the toolkit's own patterns.</summary>
public static class RuntimeExamples
{
    public static readonly LabExample AnnouncedCart = new()
    {
        Id = "announced-cart",
        NeedsBrowser = true,
        Run = async env =>
        {
            #region snippet:announced-cart
            await using var component = await env.Host.RenderAsync<AnnouncedCartButton>(env.Browser);
            var monitor = await component.MonitorLiveRegionsAsync();

            var add = component.Page.GetByRole(AriaRole.Button, new() { Name = "Add to cart" });
            await add.ClickAsync();
            await monitor.ExpectUpdateAsync("1 item in the cart", TextMatch.Exact);
            await add.ClickAsync();
            await monitor.ExpectUpdateAsync("2 items in the cart", TextMatch.Exact);
            env.Show(await monitor.GetChangesAsync(), "Live-region changes recorded in the browser");

            var findings = await monitor.AnalyzeAsync();
            env.Show(findings);
            Assert.Empty(findings);
            #endregion
        },
    };

    public static readonly LabExample RovingToolbarPattern = new()
    {
        Id = "roving-toolbar-pattern",
        NeedsBrowser = true,
        Run = async env =>
        {
            #region snippet:roving-toolbar-pattern
            await using var component = await env.Host.RenderAsync<RovingToolbar>(env.Browser);

            var result = await AriaPatterns.VerifyToolbarAsync(component.Page.GetByRole(AriaRole.Toolbar, new() { Name = "Text formatting" }));
            env.Show(result);

            result.ShouldPass();
            #endregion
        },
    };

    public static readonly LabExample FruitPickerPattern = new()
    {
        Id = "fruit-picker-pattern",
        NeedsBrowser = true,
        Run = async env =>
        {
            #region snippet:fruit-picker-pattern
            await using var component = await env.Host.RenderAsync<FruitPicker>(env.Browser);

            var result = await AriaPatterns.VerifyComboboxAsync(component.Page.GetByRole(AriaRole.Combobox, new() { Name = "Favorite fruit" }));
            env.Show(result);

            result.ShouldPass();
            #endregion
        },
    };

    public static readonly LabExample FiltersDrawerPattern = new()
    {
        Id = "filters-drawer-pattern",
        NeedsBrowser = true,
        Run = async env =>
        {
            #region snippet:filters-drawer-pattern
            await using var component = await env.Host.RenderAsync<FiltersDrawer>(env.Browser);

            var result = await AriaPatterns.VerifyModalDialogAsync(component.Page.GetByRole(AriaRole.Button, new() { Name = "Filters" }));
            env.Show(result);

            result.ShouldPass();
            #endregion
        },
    };
}
