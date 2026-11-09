using BlazorAccessibilityToolkit.Demo.Components.Accessible;
using BlazorAccessibilityToolkit.Demo.Components.Defects;
using Microsoft.Playwright;

namespace BlazorAccessibilityToolkit.Demo.WebApp.Lab.Examples;

/// <summary>Live-region monitoring: DOM evidence of status messages, and patterns that commonly silence them.</summary>
public static class LiveRegionExamples
{
    public static readonly LabExample ToastLiveRegion = new()
    {
        Id = "toast-live-region",
        ExpectFailure = true,
        NeedsBrowser = true,
        Run = async env =>
        {
            #region snippet:toast-live-region
            await using var component = await env.Host.RenderAsync<ToastSaveButton>(env.Browser);
            var monitor = await component.MonitorLiveRegionsAsync();    // start before the interaction

            await component.Page.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();
            await monitor.ExpectUpdateAsync("Changes saved");             // the message appeared ...
            env.Show(await monitor.GetChangesAsync(), "Live-region changes recorded in the browser");

            var findings = await monitor.AnalyzeAsync();                  // ... but will it be announced?
            env.Show(findings);
            Assert.Empty(findings);
            #endregion
        },
    };

    public static readonly LabExample SaveLiveRegion = new()
    {
        Id = "save-live-region",
        NeedsBrowser = true,
        Run = async env =>
        {
            #region snippet:save-live-region
            await using var component = await env.Host.RenderAsync<SaveButton>(env.Browser);
            var monitor = await component.MonitorLiveRegionsAsync();    // start before the interaction

            await component.Page.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();
            await monitor.ExpectUpdateAsync("Saving");
            await monitor.ExpectUpdateAsync("Changes saved");             // the message appeared ...
            env.Show(await monitor.GetChangesAsync(), "Live-region changes recorded in the browser");

            var findings = await monitor.AnalyzeAsync();                  // ... and nothing blocks the announcement
            env.Show(findings);
            Assert.Empty(findings);
            #endregion
        },
    };
}
