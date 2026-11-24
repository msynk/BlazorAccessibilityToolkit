using System.Globalization;
using System.Text.Json;
using BlazorAccessibilityToolkit.Playwright.Internal;
using BlazorAccessibilityToolkit.Rules;
using Microsoft.Playwright;

namespace BlazorAccessibilityToolkit.Playwright;

/// <summary>Settings for <see cref="AdaptationAudits.AuditApcaContrastAsync"/>.</summary>
public sealed record ApcaContrastOptions
{
    /// <summary>
    /// One minimum |Lc| for all text, instead of the level that the APCA guidance gives for each text's size and weight
    /// (<see cref="Apca.RequiredLc"/>). For example 60 for a "content text" profile.
    /// </summary>
    public int? MinimumLc { get; init; }

    /// <summary>Audit options (excluded selectors, suppressions).</summary>
    public AccessibilityAuditOptions? AuditOptions { get; init; }

    /// <summary>Maximum number of text elements reported. Default 50.</summary>
    public int MaxFindings { get; init; } = 50;
}

public static partial class AdaptationAudits
{
    // axe-core's color-contrast rule measures the text and background color of every text element (blending
    // transparency, following the stacking of elements); its check data is reused here, for passing text too.
    private const string ApcaScript = """
        async o => {
          const results = await window.axe.run({ exclude: o.exclude.map(s => [s]) }, {
            runOnly: { type: 'rule', values: ['color-contrast'] },
            resultTypes: ['passes', 'violations', 'incomplete'],
            selectors: true, iframes: false, ancestry: false, xpath: false,
          });
          const items = [];
          for (const rule of [...results.passes, ...results.violations]) {
            for (const node of rule.nodes) {
              const check = [...node.any, ...node.all, ...node.none].find(c => c.id === 'color-contrast' && c.data && c.data.fgColor && c.data.bgColor);
              if (!check) continue;
              const selector = node.target.length === 1 && typeof node.target[0] === 'string' ? node.target[0] : null;
              const element = selector ? document.querySelector(selector) : null;
              const style = element ? getComputedStyle(element) : null;
              const weight = style ? (style.fontWeight === 'bold' ? 700 : style.fontWeight === 'normal' ? 400 : parseInt(style.fontWeight, 10) || 400)
                : (check.data.fontWeight === 'bold' ? 700 : 400);
              const size = style ? parseFloat(style.fontSize) : parseFloat((/\(([\d.]+)px\)/.exec(check.data.fontSize) || [])[1] || '16');
              items.push({
                selector: selector || node.target.flat().join(' '),
                html: node.html,
                foreground: check.data.fgColor,
                background: check.data.bgColor,
                ratio: Number(check.data.contrastRatio) || 0,
                size, weight,
              });
            }
          }
          return { items, incomplete: results.incomplete.reduce((n, r) => n + r.nodes.length, 0) };
        }
        """;

    /// <summary>
    /// Reports text whose APCA lightness contrast (Lc) is below the level that APCA's guidance gives for its font size
    /// and weight (<see cref="Apca.RequiredLc"/>), as information next to WCAG 2: APCA is a candidate method for WCAG 3.
    /// The text and background colors come from axe-core's color-contrast rule, so text that axe-core cannot measure
    /// (on background images or gradients, or covered by other elements) is not reported; the audit's warnings say how
    /// much. Findings are best-practice potential issues and never fail the default quality gate's WCAG checks.
    /// </summary>
    /// <remarks>See <see cref="Apca"/> for the license terms of APCA, which differ from the toolkit's.</remarks>
    public static async Task<AccessibilityAudit> AuditApcaContrastAsync(this IPage page, ApcaContrastOptions? options = null, AuditLabel? label = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        options ??= new ApcaContrastOptions();
        var auditOptions = options.AuditOptions ?? AccessibilityAuditOptions.Default;
        var builder = CreateBuilder(page, auditOptions, label, label?.State ?? "APCA contrast");
        try
        {
            await ToolkitScript.EnsureAsync(page, includeAxe: true).ConfigureAwait(false);
            var json = await page.EvaluateAsync<JsonElement>(ApcaScript, new { exclude = auditOptions.ExcludeSelectors.Append("[data-a11y-focus-sentinel]").ToArray() }).ConfigureAwait(false);
            builder.AddEngine(ToolkitRules.EngineName, ToolkitScript.ToolkitVersion);
            builder.AddEngine("APCA", Apca.AlgorithmVersion);

            var measured = 0;
            var reported = 0;
            var below = 0;
            foreach (var item in json.GetProperty("items").EnumerateArray())
            {
                measured++;
                var foreground = item.GetProperty("foreground").GetString()!;
                var background = item.GetProperty("background").GetString()!;
                double lc;
                try
                {
                    lc = Apca.Contrast(foreground, background);
                }
                catch (FormatException)
                {
                    continue;
                }

                var size = item.GetProperty("size").GetDouble();
                var weight = item.GetProperty("weight").GetInt32();
                var required = Apca.RequiredLc(size, weight, out var tooSmall);
                if (options.MinimumLc is { } minimum)
                {
                    (required, tooSmall) = (minimum, false);
                }

                if (Math.Abs(lc) >= required)
                {
                    continue;
                }

                below++;
                if (reported >= Math.Max(1, options.MaxFindings))
                {
                    continue;
                }

                reported++;
                var polarity = lc >= 0 ? "dark text on a light background" : "light text on a dark background";
                var ratio = item.GetProperty("ratio").GetDouble();
                builder.AddFinding(ToolkitRules.ApcaContrast.CreateFinding(
                    new FindingTarget { Selector = item.GetProperty("selector").GetString(), Html = item.GetProperty("html").GetString() },
                    remediation: string.Create(
                        CultureInfo.InvariantCulture,
                        $"Lc {Math.Abs(lc):0.0} ({polarity}, {foreground} on {background}) for {size:0.#} px text at weight {weight}; the APCA guidance asks for Lc {required}{(tooSmall ? ", and the text is smaller than its minimum for body text (14 px at weight 400)" : string.Empty)}. The WCAG 2 contrast ratio is {ratio:0.##}:1. {ToolkitRules.ApcaContrast.Remediation}"),
                    data: new Dictionary<string, string>
                    {
                        ["lc"] = lc.ToString("0.0", CultureInfo.InvariantCulture),
                        ["requiredLc"] = required.ToString(CultureInfo.InvariantCulture),
                        ["foreground"] = foreground,
                        ["background"] = background,
                        ["fontSize"] = size.ToString("0.#", CultureInfo.InvariantCulture),
                        ["fontWeight"] = weight.ToString(CultureInfo.InvariantCulture),
                        ["wcag2Ratio"] = ratio.ToString("0.##", CultureInfo.InvariantCulture),
                    }));
            }

            if (below > reported)
            {
                builder.AddWarning(string.Create(CultureInfo.InvariantCulture, $"{below - reported} more text elements are below the APCA guidance; raise ApcaContrastOptions.MaxFindings to list them."));
            }

            if (json.GetProperty("incomplete").GetInt32() is > 0 and var incomplete)
            {
                builder.AddWarning(string.Create(CultureInfo.InvariantCulture, $"{incomplete} text elements were not measured: axe-core could not determine their background (background images, gradients or overlapping elements)."));
            }

            if (measured > 0 && below == 0)
            {
                builder.AddPassedRule(ToolkitRules.ApcaContrast.Id);
            }
            else if (measured == 0)
            {
                builder.AddInapplicableRule(ToolkitRules.ApcaContrast.Id);
            }
        }
        catch (Exception ex) when (ex is PlaywrightException or InvalidOperationException or JsonException)
        {
            builder.AddError(ExecutionError.FromException(ex is PlaywrightException ? "browser" : "engine", ex));
        }

        return PlaywrightAccessibility.Persist(builder.Build());
    }
}
