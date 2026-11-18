using BlazorAccessibilityToolkit.DevTools;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlazorAccessibilityToolkit.Tests;

/// <summary>The markup of <see cref="AccessibilityDevOverlay"/>; the overlay's behavior is tested in the browser.</summary>
public sealed class DevOverlayRenderingTests
{
    [Fact]
    public async Task Renders_the_module_script_in_development_only()
    {
        Assert.Equal(
            "<script type=\"module\" src=\"_blazor-a11y/devtools.js\" data-ba11y-devtools=\"\" data-ba11y-position=\"bottom-end\" data-ba11y-delay=\"1000\"></script>",
            await RenderAsync("Development", ParameterView.Empty));
        Assert.Equal(string.Empty, await RenderAsync("Production", ParameterView.Empty));
        Assert.Equal(string.Empty, await RenderAsync(null, ParameterView.Empty));
    }

    [Fact]
    public async Task Enabled_overrides_the_environment()
    {
        Assert.Equal(string.Empty, await RenderAsync("Development", Parameters(("Enabled", false))));
        Assert.StartsWith("<script type=\"module\"", await RenderAsync("Production", Parameters(("Enabled", true))), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Settings_become_data_attributes()
    {
        var html = await RenderAsync("Development", Parameters(
            ("Tags", new[] { "wcag2a", "wcag2aa" }),
            ("Exclude", new[] { "#chat-widget", ":is(.ad, [data-x=\"a,b\"])" }),
            ("Position", DevOverlayPosition.TopStart),
            ("Delay", TimeSpan.FromMilliseconds(250)),
            ("LogToConsole", false),
            ("ScriptUrl", "/tools/devtools.js"),
            ("AxeScriptUrl", "/lib/axe.min.js")));

        Assert.Contains("src=\"/tools/devtools.js\"", html, StringComparison.Ordinal);
        Assert.Contains("data-ba11y-position=\"top-start\"", html, StringComparison.Ordinal);
        Assert.Contains("data-ba11y-delay=\"250\"", html, StringComparison.Ordinal);
        Assert.Contains("data-ba11y-tags=\"wcag2a,wcag2aa\"", html, StringComparison.Ordinal);
        Assert.Contains("data-ba11y-console=\"false\"", html, StringComparison.Ordinal);
        Assert.Contains("data-ba11y-axe-src=\"/lib/axe.min.js\"", html, StringComparison.Ordinal);

        // A JSON array, because selectors can contain commas.
        var exclude = System.Text.RegularExpressions.Regex.Match(html, "data-ba11y-exclude=\"([^\"]*)\"").Groups[1].Value;
        Assert.Equal(["#chat-widget", ":is(.ad, [data-x=\"a,b\"])"], System.Text.Json.JsonSerializer.Deserialize<string[]>(System.Net.WebUtility.HtmlDecode(exclude))!);
    }

    private static ParameterView Parameters(params (string Name, object Value)[] values) =>
        ParameterView.FromDictionary(values.ToDictionary(v => v.Name, v => (object?)v.Value));

    private static async Task<string> RenderAsync(string? environment, ParameterView parameters)
    {
        var services = new ServiceCollection();
        if (environment is not null)
        {
            services.AddSingleton<IHostEnvironment>(new TestEnvironment { EnvironmentName = environment });
        }

        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<AccessibilityDevOverlay>(parameters)).ToHtmlString());
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";

        public string ApplicationName { get; set; } = "Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
