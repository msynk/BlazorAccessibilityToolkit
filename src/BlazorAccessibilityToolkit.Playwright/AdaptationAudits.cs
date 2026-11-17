using System.Globalization;
using System.Text.Json;
using BlazorAccessibilityToolkit.Playwright.Internal;
using BlazorAccessibilityToolkit.Rules;
using Microsoft.Playwright;

namespace BlazorAccessibilityToolkit.Playwright;

/// <summary>Settings for <see cref="AdaptationAudits.AuditReflowAsync"/>.</summary>
public sealed record ReflowOptions
{
    /// <summary>Viewport width in CSS pixels. Default 320 (a 1280 pixel wide window at 400% zoom).</summary>
    public int Width { get; init; } = 320;

    /// <summary>Viewport height in CSS pixels. Default 256 (a 1024 pixel high window at 400% zoom).</summary>
    public int Height { get; init; } = 256;

    /// <summary>
    /// Report overflowing content inside tables, grids, toolbars, <c>pre</c>, <c>svg</c>, <c>canvas</c>, video, maps and
    /// elements marked with <c>data-a11y-allow-horizontal-scroll</c>. Default <c>false</c>: WCAG 1.4.10 exempts content
    /// that needs a two-dimensional layout.
    /// </summary>
    public bool IncludeTwoDimensionalContent { get; init; }

    /// <summary>Also run a full audit (axe-core and toolkit diagnostics) at the reflow width. Default <c>false</c>.</summary>
    public bool AuditAtWidth { get; init; }

    /// <summary>Audit options for the audit at the reflow width and for excluded selectors.</summary>
    public AccessibilityAuditOptions? AuditOptions { get; init; }

    /// <summary>How long to let the layout settle after resizing (media queries, resize observers, Blazor re-renders). Default 300 ms.</summary>
    public TimeSpan SettleTime { get; init; } = TimeSpan.FromMilliseconds(300);

    /// <summary>Maximum number of overflowing elements reported. Default 20.</summary>
    public int MaxFindings { get; init; } = 20;
}

/// <summary>Settings for <see cref="AdaptationAudits.AuditTextSpacingAsync"/>.</summary>
public sealed record TextSpacingOptions
{
    /// <summary>Audit options (excluded selectors, suppressions, timeout).</summary>
    public AccessibilityAuditOptions? AuditOptions { get; init; }

    /// <summary>Maximum number of clipping elements reported. Default 20.</summary>
    public int MaxFindings { get; init; } = 20;
}

/// <summary>
/// User preference and display conditions to emulate for an audit: color scheme, forced colors (Windows high contrast),
/// increased contrast and reduced motion. <c>null</c> leaves a condition unchanged.
/// </summary>
public sealed record DisplayConditions
{
    /// <summary>Dark color scheme (<c>prefers-color-scheme: dark</c>).</summary>
    public static DisplayConditions Dark { get; } = new() { ColorScheme = Microsoft.Playwright.ColorScheme.Dark };

    /// <summary>Light color scheme (<c>prefers-color-scheme: light</c>).</summary>
    public static DisplayConditions Light { get; } = new() { ColorScheme = Microsoft.Playwright.ColorScheme.Light };

    /// <summary>Forced colors, as in Windows high contrast themes (<c>forced-colors: active</c>; Chromium only).</summary>
    public static DisplayConditions ForcedColorsActive { get; } = new() { ForcedColors = Microsoft.Playwright.ForcedColors.Active };

    /// <summary>Increased contrast (<c>prefers-contrast: more</c>).</summary>
    public static DisplayConditions MoreContrast { get; } = new() { Contrast = Microsoft.Playwright.Contrast.More };

    /// <summary>Color scheme to emulate.</summary>
    public ColorScheme? ColorScheme { get; init; }

    /// <summary>Forced colors to emulate.</summary>
    public ForcedColors? ForcedColors { get; init; }

    /// <summary><c>prefers-contrast</c> to emulate.</summary>
    public Contrast? Contrast { get; init; }

    /// <summary><c>prefers-reduced-motion</c> to emulate.</summary>
    public ReducedMotion? ReducedMotion { get; init; }

    /// <summary>A short label for audit states, for example <c>dark, forced-colors</c>.</summary>
    public string Describe()
    {
        var parts = new List<string>();
        if (ColorScheme is { } scheme)
        {
            parts.Add(scheme == Microsoft.Playwright.ColorScheme.Dark ? "dark" : scheme == Microsoft.Playwright.ColorScheme.Light ? "light" : "no color scheme");
        }

        if (ForcedColors == Microsoft.Playwright.ForcedColors.Active)
        {
            parts.Add("forced colors");
        }

        if (Contrast is { } contrast && contrast != Microsoft.Playwright.Contrast.NoPreference)
        {
            parts.Add(contrast == Microsoft.Playwright.Contrast.More ? "more contrast" : "contrast " + contrast.ToString().ToLowerInvariant());
        }

        if (ReducedMotion == Microsoft.Playwright.ReducedMotion.Reduce)
        {
            parts.Add("reduced motion");
        }

        return parts.Count == 0 ? "default display" : string.Join(", ", parts);
    }
}

/// <summary>
/// Audits of how content adapts to users' display needs: reflow at 320 CSS pixels (WCAG 1.4.10), increased text
/// spacing (WCAG 1.4.12) and preference media features (dark mode, forced colors, increased contrast). Findings are
/// <see cref="FindingStatus.PotentialIssue"/>: they show where content overflows or is clipped, which usually means
/// lost content, but the decision whether content is lost or exempt needs a person.
/// </summary>
public static class AdaptationAudits
{
    /// <summary>
    /// Resizes the viewport to <see cref="ReflowOptions.Width"/> (320 CSS pixels by default), reports content that makes
    /// the page scroll horizontally, and restores the viewport. Content that needs a two-dimensional layout (data
    /// tables, grids, toolbars, maps, diagrams, video) is exempt, as in WCAG 1.4.10, and only noted as a warning.
    /// </summary>
    public static async Task<AccessibilityAudit> AuditReflowAsync(this IPage page, ReflowOptions? options = null, AuditLabel? label = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        options ??= new ReflowOptions();
        var auditOptions = options.AuditOptions ?? AccessibilityAuditOptions.Default;
        var state = label?.State ?? string.Create(CultureInfo.InvariantCulture, $"reflow {options.Width}px");
        var builder = CreateBuilder(page, auditOptions, label, state);
        var original = page.ViewportSize;
        try
        {
            await page.SetViewportSizeAsync(options.Width, options.Height).ConfigureAwait(false);
            await SettleAsync(page, options.SettleTime).ConfigureAwait(false);
            await ToolkitScript.EnsureAsync(page).ConfigureAwait(false);
            var json = await page.EvaluateAsync<JsonElement>(
                "o => window.__blazorA11y.reflowReport(o)",
                new { exclude = auditOptions.ExcludeSelectors.ToArray(), max = Math.Max(1, options.MaxFindings) * 2 }).ConfigureAwait(false);
            var report = ToolkitScript.Deserialize<ReflowDto>(json) ?? new ReflowDto();
            builder.AddEngine(ToolkitRules.EngineName, ToolkitScript.ToolkitVersion);

            var reported = report.Offenders.Where(o => options.IncludeTwoDimensionalContent || !o.Exempt).Take(options.MaxFindings).ToArray();
            foreach (var offender in reported)
            {
                builder.AddFinding(ToolkitRules.ReflowHorizontalScroll.CreateFinding(
                    Target(offender),
                    remediation: string.Create(CultureInfo.InvariantCulture, $"This element ends at {offender.Right}px in a {report.ViewportWidth}px wide viewport, so the page scrolls horizontally. {ToolkitRules.ReflowHorizontalScroll.Remediation}"),
                    data: new Dictionary<string, string>
                    {
                        ["viewportWidth"] = report.ViewportWidth.ToString(CultureInfo.InvariantCulture),
                        ["scrollWidth"] = report.ScrollWidth.ToString(CultureInfo.InvariantCulture),
                        ["elementWidth"] = offender.Width.ToString(CultureInfo.InvariantCulture),
                    }));
            }

            var exempt = report.Offenders.Count(o => o.Exempt);
            if (!options.IncludeTwoDimensionalContent && exempt > 0)
            {
                builder.AddWarning(string.Create(CultureInfo.InvariantCulture, $"{exempt} overflowing element(s) inside content that may need a two-dimensional layout (tables, grids, toolbars, media) were not reported; confirm they are exempt."));
            }

            if (report.ScrollWidth > report.ViewportWidth + 1 && report.Offenders.Count == 0)
            {
                builder.AddFinding(ToolkitRules.ReflowHorizontalScroll.CreateFinding(
                    new FindingTarget { Selector = "html" },
                    remediation: string.Create(CultureInfo.InvariantCulture, $"The page is {report.ScrollWidth}px wide in a {report.ViewportWidth}px viewport, but no single element could be identified (for example a margin or a pseudo-element). {ToolkitRules.ReflowHorizontalScroll.Remediation}")));
            }
            else if (reported.Length == 0)
            {
                builder.AddPassedRule(ToolkitRules.ReflowHorizontalScroll.Id);
            }

            if (options.AuditAtWidth)
            {
                var (output, _) = await PlaywrightAccessibility.RunEnginesAsync(page, null, auditOptions, AuditTargetKind.Page, null).WaitAsync(auditOptions.Timeout).ConfigureAwait(false);
                output.AddTo(builder);
            }
        }
        catch (Exception ex) when (ex is PlaywrightException or InvalidOperationException or JsonException or TimeoutException)
        {
            builder.AddError(ExecutionError.FromException(ex is PlaywrightException ? "browser" : "engine", ex));
        }
        finally
        {
            await RestoreViewportAsync(page, original).ConfigureAwait(false);
        }

        return PlaywrightAccessibility.Persist(builder.Build());
    }

    /// <summary>
    /// Applies the WCAG 1.4.12 text-spacing values (line height 1.5, paragraph spacing 2, letter spacing 0.12 and word
    /// spacing 0.16 times the font size), reports elements whose text becomes clipped (<c>overflow: hidden</c> or line
    /// clamping with content that no longer fits), and removes the spacing again. Elements that already clipped before
    /// the change are not reported.
    /// </summary>
    public static async Task<AccessibilityAudit> AuditTextSpacingAsync(this IPage page, TextSpacingOptions? options = null, AuditLabel? label = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        options ??= new TextSpacingOptions();
        var auditOptions = options.AuditOptions ?? AccessibilityAuditOptions.Default;
        var builder = CreateBuilder(page, auditOptions, label, label?.State ?? "text spacing");
        try
        {
            await ToolkitScript.EnsureAsync(page).ConfigureAwait(false);
            var json = await page.EvaluateAsync<JsonElement>(
                "o => window.__blazorA11y.textSpacingReport(o)",
                new { exclude = auditOptions.ExcludeSelectors.ToArray(), max = Math.Max(1, options.MaxFindings) }).ConfigureAwait(false);
            var offenders = ToolkitScript.Deserialize<LayoutOffenderDto[]>(json) ?? [];
            builder.AddEngine(ToolkitRules.EngineName, ToolkitScript.ToolkitVersion);
            foreach (var offender in offenders)
            {
                builder.AddFinding(ToolkitRules.TextSpacingClipped.CreateFinding(
                    Target(offender),
                    remediation: $"Text \"{offender.Text}\" overflows its container ({offender.Axis}) when text spacing is increased. {ToolkitRules.TextSpacingClipped.Remediation}",
                    data: new Dictionary<string, string> { ["axis"] = offender.Axis ?? string.Empty }));
            }

            if (offenders.Length == 0)
            {
                builder.AddPassedRule(ToolkitRules.TextSpacingClipped.Id);
            }
        }
        catch (Exception ex) when (ex is PlaywrightException or InvalidOperationException or JsonException)
        {
            builder.AddError(ExecutionError.FromException(ex is PlaywrightException ? "browser" : "engine", ex));
        }

        return PlaywrightAccessibility.Persist(builder.Build());
    }

    /// <summary>
    /// Emulates <paramref name="conditions"/> (for example dark mode or forced colors), runs a full audit labelled with
    /// the conditions, and restores the media features it changed to the values the page reported before. Contrast, focus indicators made from box-shadows and
    /// background images often fail only under these conditions.
    /// </summary>
    public static async Task<AccessibilityAudit> AuditUnderAsync(this IPage page, DisplayConditions conditions, AccessibilityAuditOptions? options = null, AuditLabel? label = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(conditions);
        var previous = await CurrentConditionsAsync(page).ConfigureAwait(false);
        await page.EmulateDisplayConditionsAsync(conditions).ConfigureAwait(false);
        try
        {
            await SettleAsync(page, TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);
            return await page.AuditAccessibilityAsync(options, (label ?? new AuditLabel()) with { State = label?.State ?? conditions.Describe() }).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                // Restore the media features this call changed to the values the page had before.
                await page.EmulateMediaAsync(new PageEmulateMediaOptions
                {
                    ColorScheme = conditions.ColorScheme is null ? null : previous.ColorScheme,
                    ForcedColors = conditions.ForcedColors is null ? null : previous.ForcedColors,
                    Contrast = conditions.Contrast is null ? null : previous.Contrast,
                    ReducedMotion = conditions.ReducedMotion is null ? null : previous.ReducedMotion,
                }).ConfigureAwait(false);
            }
            catch (PlaywrightException)
            {
                // The page closed; nothing to restore.
            }
        }
    }

    /// <summary>Emulates the given display conditions for the page until they are changed again.</summary>
    public static Task EmulateDisplayConditionsAsync(this IPage page, DisplayConditions conditions)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(conditions);
        return page.EmulateMediaAsync(new PageEmulateMediaOptions
        {
            ColorScheme = conditions.ColorScheme,
            ForcedColors = conditions.ForcedColors,
            Contrast = conditions.Contrast,
            ReducedMotion = conditions.ReducedMotion,
        });
    }

    // The media features the page currently reports, so that AuditUnderAsync can restore them.
    private static async Task<DisplayConditions> CurrentConditionsAsync(IPage page)
    {
        var state = ToolkitScript.Deserialize<string[]>(await page.EvaluateAsync<JsonElement>(
            "() => [matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light', matchMedia('(forced-colors: active)').matches ? 'active' : 'none', matchMedia('(prefers-contrast: more)').matches ? 'more' : 'no-preference', matchMedia('(prefers-reduced-motion: reduce)').matches ? 'reduce' : 'no-preference']").ConfigureAwait(false)) ?? [];
        string At(int i) => i < state.Length ? state[i] : string.Empty;
        return new DisplayConditions
        {
            ColorScheme = At(0) == "dark" ? Microsoft.Playwright.ColorScheme.Dark : Microsoft.Playwright.ColorScheme.Light,
            ForcedColors = At(1) == "active" ? Microsoft.Playwright.ForcedColors.Active : Microsoft.Playwright.ForcedColors.None,
            Contrast = At(2) == "more" ? Microsoft.Playwright.Contrast.More : Microsoft.Playwright.Contrast.NoPreference,
            ReducedMotion = At(3) == "reduce" ? Microsoft.Playwright.ReducedMotion.Reduce : Microsoft.Playwright.ReducedMotion.NoPreference,
        };
    }

    private static AccessibilityAuditBuilder CreateBuilder(IPage page, AccessibilityAuditOptions options, AuditLabel? label, string state)
    {
        string? url = null;
        try
        {
            url = page.Url;
        }
        catch (PlaywrightException)
        {
            // Closed page; the audit will fail below.
        }

        var path = FindingFingerprint.NormalizeUrl(url);
        var context = new AccessibilityContext
        {
            Kind = AuditTargetKind.Page,
            RenderMode = "page",
            Url = url,
            Scenario = label?.Scenario,
            State = state,
            Metadata = label?.Metadata ?? new Dictionary<string, string>(),
            Browser = page.Context.Browser?.BrowserType.Name,
            BrowserVersion = page.Context.Browser?.Version,
        };
        var name = label?.Name ?? $"{(string.IsNullOrEmpty(path) || path.EndsWith(':') ? "page" : path)} [{state}]";
        return new AccessibilityAuditBuilder(name, context, options);
    }

    private static FindingTarget Target(LayoutOffenderDto offender) => new()
    {
        Selector = offender.Selector,
        Html = offender.Html,
        Role = offender.Role,
        AccessibleName = offender.Name,
        ElementOrdinal = offender.Ordinal,
    };

    private static async Task SettleAsync(IPage page, TimeSpan time)
    {
        // Two animation frames let layout and resize observers run; the extra time covers Blazor re-renders.
        await page.EvaluateAsync("() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))").ConfigureAwait(false);
        if (time > TimeSpan.Zero)
        {
            await page.WaitForTimeoutAsync((float)time.TotalMilliseconds).ConfigureAwait(false);
        }
    }

    private static async Task RestoreViewportAsync(IPage page, PageViewportSizeResult? original)
    {
        if (original is null || page.IsClosed)
        {
            return;
        }

        try
        {
            await page.SetViewportSizeAsync(original.Width, original.Height).ConfigureAwait(false);
        }
        catch (PlaywrightException)
        {
            // The page closed meanwhile.
        }
    }
}
