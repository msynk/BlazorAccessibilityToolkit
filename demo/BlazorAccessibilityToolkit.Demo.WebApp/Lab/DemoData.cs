using BlazorAccessibilityToolkit.Demo.Components.Accessible;

namespace BlazorAccessibilityToolkit.Demo.WebApp.Lab;

/// <summary>Parameters shared by the live components on the pages and the live tests.</summary>
public static class DemoData
{
    public static IReadOnlyList<Tabs.TabItem> Tabs { get; } =
    [
        new("Profile", b => b.AddMarkupContent(0, "<p>Name and photo.</p>")),
        new("Notifications", b => b.AddMarkupContent(0, "<p>Email preferences.</p>")),
        new("Privacy", b => b.AddMarkupContent(0, "<p>Visibility.</p>")),
    ];
}
