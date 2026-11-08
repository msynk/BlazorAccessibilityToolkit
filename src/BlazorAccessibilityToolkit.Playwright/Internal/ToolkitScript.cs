using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Deque.AxeCore.Commons;
using Microsoft.Playwright;

namespace BlazorAccessibilityToolkit.Playwright.Internal;

/// <summary>Loads axe-core and the toolkit helpers into pages.</summary>
internal static partial class ToolkitScript
{
    internal const string ScriptVersion = "8";

    private static readonly Lazy<string> Toolkit = new(() =>
    {
        using var stream = typeof(ToolkitScript).Assembly.GetManifestResourceStream("BlazorAccessibilityToolkit.toolkit.js")
            ?? throw new InvalidOperationException("The toolkit script resource is missing from the assembly.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    private static readonly Lazy<string> Axe = new(() => new BundledAxeScriptProvider().GetScript());

    private static readonly Lazy<string?> BundledAxeVersion = new(() =>
    {
        var header = Axe.Value.Length > 200 ? Axe.Value[..200] : Axe.Value;
        var match = AxeVersionPattern().Match(header);
        return match.Success ? match.Groups[1].Value : null;
    });

    /// <summary>Source of the bundled axe-core script.</summary>
    internal static string AxeSource => Axe.Value;

    /// <summary>Version of the bundled axe-core script.</summary>
    internal static string? AxeVersion => BundledAxeVersion.Value;

    /// <summary>Toolkit version reported as engine version.</summary>
    internal static string ToolkitVersion { get; } =
        typeof(ToolkitScript).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "0.0.0";

    /// <summary>
    /// Ensures the toolkit helpers (and, unless <paramref name="includeAxe"/> is false, axe-core) are present in the
    /// page's main frame. Audits skip axe here because Deque.AxeCore.Playwright injects it on every run.
    /// </summary>
    internal static async Task EnsureAsync(IPage page, bool includeAxe = true)
    {
        // axe-core counts as present only when it is the bundled version (a page may ship its own axe-core).
        var state = await page.EvaluateAsync<int>(
            "a => (typeof window.axe !== 'undefined' && window.axe.commons && window.axe.version === a.axe ? 1 : 0) + (window.__blazorA11y && window.__blazorA11y.version === a.toolkit ? 2 : 0)",
            new { axe = AxeVersion, toolkit = ScriptVersion }).ConfigureAwait(false);
        if (includeAxe && (state & 1) == 0)
        {
            await page.EvaluateAsync(Axe.Value).ConfigureAwait(false);
        }

        if ((state & 2) == 0)
        {
            await page.EvaluateAsync(Toolkit.Value).ConfigureAwait(false);
        }
    }

    /// <summary>Script that installs the helpers on every navigation (used by monitors).</summary>
    internal static string InitScript => Toolkit.Value;

    internal static T? Deserialize<T>(JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return default;
        }

        return element.Deserialize<T>(JsonOptions);
    }

    internal static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    [GeneratedRegex(@"axe v(\d+\.\d+\.\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex AxeVersionPattern();
}
