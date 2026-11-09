using BlazorAccessibilityToolkit.Demo.Components.Accessible;
using BlazorAccessibilityToolkit.Demo.Components.Defects;
using BlazorAccessibilityToolkit.Playwright;
using Microsoft.Playwright;

namespace BlazorAccessibilityToolkit.Demo.WebApp.Lab.Examples;

/// <summary>Widgets verified against the WAI-ARIA Authoring Practices with real key presses.</summary>
public static class PatternExamples
{
    public static readonly LabExample NaiveTabsPattern = new()
    {
        Id = "naive-tabs-pattern",
        ExpectFailure = true,
        NeedsBrowser = true,
        Run = async env =>
        {
            #region snippet:naive-tabs-pattern
            await using var component = await env.Host.RenderAsync<NaiveTabs>(env.Browser, p => p
                .Add(c => c.Label, "Sections")
                .Add(c => c.Items, DemoData.Tabs));

            var result = await AriaPatterns.VerifyTabsAsync(
                component.Page.GetByRole(AriaRole.Tablist),
                new PatternOptions { Timeout = TimeSpan.FromSeconds(1) });
            env.Show(result);

            result.ShouldPass();
            #endregion
        },
    };

    public static readonly LabExample TabsPattern = new()
    {
        Id = "tabs-pattern",
        NeedsBrowser = true,
        Run = async env =>
        {
            #region snippet:tabs-pattern
            await using var component = await env.Host.RenderAsync<Tabs>(env.Browser, p => p
                .Add(c => c.Label, "Sections")
                .Add(c => c.Items, DemoData.Tabs));

            var result = await AriaPatterns.VerifyTabsAsync(
                component.Page.GetByRole(AriaRole.Tablist),
                new PatternOptions { Timeout = TimeSpan.FromSeconds(1) });
            env.Show(result);

            result.ShouldPass();
            #endregion
        },
    };

    public static readonly LabExample StaleDisclosurePattern = new()
    {
        Id = "stale-disclosure-pattern",
        ExpectFailure = true,
        NeedsBrowser = true,
        Run = async env =>
        {
            #region snippet:stale-disclosure-pattern
            await using var component = await env.Host.RenderAsync<StaleDisclosure>(env.Browser, p => p
                .Add(c => c.Title, "Shipping")
                .Add(c => c.Details, "Ships in 2 days."));
            var button = component.Page.GetByRole(AriaRole.Button, new() { Name = "Shipping" });

            var result = await AriaPatterns.VerifyDisclosureAsync(button, new PatternOptions { Timeout = TimeSpan.FromSeconds(1) });
            env.Show(result);

            await button.PressAsync("Enter");                    // open it, then audit the expanded state
            await component.Page.GetByText("Ships in 2 days.").WaitForAsync();
            var audit = await component.AuditAsync("expanded");
            env.Show(audit);

            result.ShouldPass();
            audit.ShouldHaveNoViolations(AccessibilityExpectation.Strict);
            #endregion
        },
    };

    public static readonly LabExample DisclosurePattern = new()
    {
        Id = "disclosure-pattern",
        NeedsBrowser = true,
        Run = async env =>
        {
            #region snippet:disclosure-pattern
            await using var component = await env.Host.RenderAsync<DisclosureCard>(env.Browser, p => p
                .Add(c => c.Title, "Shipping")
                .Add(c => c.Details, "Ships in 2 days."));
            var button = component.Page.GetByRole(AriaRole.Button, new() { Name = "Shipping" });

            var result = await AriaPatterns.VerifyDisclosureAsync(button, new PatternOptions { Timeout = TimeSpan.FromSeconds(1) });
            env.Show(result);

            await button.PressAsync("Enter");                    // open it, then audit the expanded state
            await component.Page.GetByText("Ships in 2 days.").WaitForAsync();
            var audit = await component.AuditAsync("expanded");
            env.Show(audit);

            result.ShouldPass();
            audit.ShouldHaveNoViolations(AccessibilityExpectation.Strict);
            #endregion
        },
    };

    public static readonly LabExample MenuButtonPattern = new()
    {
        Id = "menu-button-pattern",
        NeedsBrowser = true,
        Run = async env =>
        {
            #region snippet:menu-button-pattern
            await using var component = await env.Host.RenderAsync<MenuButton>(env.Browser, p => p
                .Add(c => c.Label, "Actions"));

            var result = await AriaPatterns.VerifyMenuButtonAsync(
                component.Page.GetByRole(AriaRole.Button, new() { Name = "Actions" }),
                options: new PatternOptions { Timeout = TimeSpan.FromSeconds(1) });
            env.Show(result);

            result.ShouldPass();
            #endregion
        },
    };
}
