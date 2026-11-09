using BlazorAccessibilityToolkit.Demo.Components.Accessible;
using BlazorAccessibilityToolkit.Demo.Components.Defects;
using BlazorAccessibilityToolkit.Playwright;

namespace BlazorAccessibilityToolkit.Demo.WebApp.Lab.Examples;

/// <summary>Accessibility-tree snapshots: roles, names, states and hierarchy compared with a reviewed template.</summary>
public static class SnapshotExamples
{
    public static readonly LabExample TabsSnapshot = new()
    {
        Id = "tabs-snapshot",
        NeedsBrowser = true,
        Run = async env =>
        {
            #region snippet:tabs-snapshot
            await using var component = await env.Host.RenderAsync<Tabs>(env.Browser, p => p
                .Add(c => c.Label, "Sections")
                .Add(c => c.Items, DemoData.Tabs));
            env.Show(new LabText(await component.Root.CaptureAriaSnapshotAsync(), "yaml"), "Captured accessibility tree");

            // In a test project, ShouldMatchStoredAriaSnapshotAsync("tabs-initial") keeps this template in a file.
            await component.Root.ShouldMatchAriaSnapshotAsync("""
                - tablist "Sections":
                  - tab "Profile" [selected]
                  - tab "Notifications"
                  - tab "Privacy"
                - tabpanel "Profile":
                  - paragraph: Name and photo.
                """, timeout: TimeSpan.FromSeconds(1));
            #endregion
        },
    };

    public static readonly LabExample NaiveTabsSnapshot = new()
    {
        Id = "naive-tabs-snapshot",
        ExpectFailure = true,
        NeedsBrowser = true,
        Run = async env =>
        {
            #region snippet:naive-tabs-snapshot
            await using var component = await env.Host.RenderAsync<NaiveTabs>(env.Browser, p => p
                .Add(c => c.Label, "Sections")
                .Add(c => c.Items, DemoData.Tabs));
            env.Show(new LabText(await component.Root.CaptureAriaSnapshotAsync(), "yaml"), "Captured accessibility tree");

            // The same reviewed template as for the accessible Tabs component.
            await component.Root.ShouldMatchAriaSnapshotAsync("""
                - tablist "Sections":
                  - tab "Profile" [selected]
                  - tab "Notifications"
                  - tab "Privacy"
                - tabpanel "Profile":
                  - paragraph: Name and photo.
                """, timeout: TimeSpan.FromSeconds(1));
            #endregion
        },
    };
}
