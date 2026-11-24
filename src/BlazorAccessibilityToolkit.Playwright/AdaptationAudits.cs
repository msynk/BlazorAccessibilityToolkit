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

/// <summary>Settings for <see cref="AdaptationAudits.AuditMotionAsync"/>.</summary>
public sealed record MotionOptions
{
    /// <summary>Audit options (excluded selectors, suppressions).</summary>
    public AccessibilityAuditOptions? AuditOptions { get; init; }

    /// <summary>How long to let the page react to the reduced-motion preference. Default 500 ms.</summary>
    public TimeSpan SettleTime { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Maximum number of animations reported. Default 20.</summary>
    public int MaxFindings { get; init; } = 20;
}

/// <summary>Settings for <see cref="AdaptationAudits.AuditOrientationAsync"/>.</summary>
public sealed record OrientationOptions
{
    /// <summary>The shorter side of the emulated device in CSS pixels. Default 390.</summary>
    public int ShortSide { get; init; } = 390;

    /// <summary>The longer side of the emulated device in CSS pixels. Default 844.</summary>
    public int LongSide { get; init; } = 844;

    /// <summary>How long to let the layout settle after rotating. Default 300 ms.</summary>
    public TimeSpan SettleTime { get; init; } = TimeSpan.FromMilliseconds(300);

    /// <summary>Audit options (suppressions).</summary>
    public AccessibilityAuditOptions? AuditOptions { get; init; }
}

/// <summary>Settings for <see cref="AdaptationAudits.AuditTargetSizeAsync"/>.</summary>
public sealed record TargetSizeOptions
{
    /// <summary>Minimum width and height in CSS pixels. Default 44 (WCAG 2.5.5 and the Apple and Android touch guidelines; Android Material uses 48).</summary>
    public int Minimum { get; init; } = 44;

    /// <summary>Audit options (excluded selectors, suppressions).</summary>
    public AccessibilityAuditOptions? AuditOptions { get; init; }

    /// <summary>Maximum number of targets reported. Default 20.</summary>
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
/// spacing (WCAG 1.4.12), orientation (WCAG 1.3.4), reduced motion (WCAG 2.2.2, 2.3.3) and preference media features
/// (dark mode, forced colors, increased contrast). Findings are
/// <see cref="FindingStatus.PotentialIssue"/>: they show where content overflows or is clipped, which usually means
/// lost content, but the decision whether content is lost or exempt needs a person.
/// </summary>
public static partial class AdaptationAudits
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
    /// Emulates <c>prefers-reduced-motion: reduce</c>, waits for the page to react, and reports animations that still
    /// move content (transforms, position, size) or run for more than five seconds, and media that still plays for
    /// more than five seconds (WCAG 2.2.2, 2.3.3). Loading indicators are not reported. The preference is restored.
    /// </summary>
    public static async Task<AccessibilityAudit> AuditMotionAsync(this IPage page, MotionOptions? options = null, AuditLabel? label = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        options ??= new MotionOptions();
        var auditOptions = options.AuditOptions ?? AccessibilityAuditOptions.Default;
        var builder = CreateBuilder(page, auditOptions, label, label?.State ?? "reduced motion");
        DisplayConditions? previous = null;
        try
        {
            previous = await CurrentConditionsAsync(page).ConfigureAwait(false);
            await page.EmulateMediaAsync(new PageEmulateMediaOptions { ReducedMotion = Microsoft.Playwright.ReducedMotion.Reduce }).ConfigureAwait(false);
            await SettleAsync(page, options.SettleTime).ConfigureAwait(false);
            await ToolkitScript.EnsureAsync(page).ConfigureAwait(false);
            var json = await page.EvaluateAsync<JsonElement>(
                "o => window.__blazorA11y.motionReport(o)",
                new { exclude = auditOptions.ExcludeSelectors.ToArray(), max = Math.Max(1, options.MaxFindings) }).ConfigureAwait(false);
            var offenders = ToolkitScript.Deserialize<MotionDto[]>(json) ?? [];
            builder.AddEngine(ToolkitRules.EngineName, ToolkitScript.ToolkitVersion);
            foreach (var offender in offenders)
            {
                builder.AddFinding(ToolkitRules.MotionNotReduced.CreateFinding(
                    new FindingTarget { Selector = offender.Selector, Html = offender.Html, Role = offender.Role, AccessibleName = offender.Name, ElementOrdinal = offender.Ordinal },
                    remediation: DescribeMotion(offender) + " " + ToolkitRules.MotionNotReduced.Remediation,
                    data: new Dictionary<string, string>
                    {
                        ["kind"] = offender.Kind ?? string.Empty,
                        ["properties"] = offender.Properties ?? string.Empty,
                        ["durationMs"] = offender.Duration.ToString(CultureInfo.InvariantCulture),
                        ["infinite"] = offender.Infinite.ToString(),
                    }));
            }

            if (offenders.Length == 0)
            {
                builder.AddPassedRule(ToolkitRules.MotionNotReduced.Id);
            }
        }
        catch (Exception ex) when (ex is PlaywrightException or InvalidOperationException or JsonException)
        {
            builder.AddError(ExecutionError.FromException(ex is PlaywrightException ? "browser" : "engine", ex));
        }
        finally
        {
            if (previous is not null && !page.IsClosed)
            {
                try
                {
                    await page.EmulateMediaAsync(new PageEmulateMediaOptions { ReducedMotion = previous.ReducedMotion }).ConfigureAwait(false);
                }
                catch (PlaywrightException)
                {
                    // The page closed meanwhile.
                }
            }
        }

        return PlaywrightAccessibility.Persist(builder.Build());
    }

    /// <summary>
    /// Reports pointer targets smaller than <see cref="TargetSizeOptions.Minimum"/> (44 CSS pixels by default) in either
    /// dimension: the stricter profile of WCAG 2.5.5 (AAA) and of the iOS, Android and Flutter touch guidelines. axe-core
    /// already checks the 24 pixel minimum of WCAG 2.5.8 (AA). Inline links in text are exempt, and a label that wraps a
    /// checkbox or radio button counts as its target.
    /// </summary>
    public static async Task<AccessibilityAudit> AuditTargetSizeAsync(this IPage page, TargetSizeOptions? options = null, AuditLabel? label = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        options ??= new TargetSizeOptions();
        var auditOptions = options.AuditOptions ?? AccessibilityAuditOptions.Default;
        var builder = CreateBuilder(page, auditOptions, label, label?.State ?? string.Create(CultureInfo.InvariantCulture, $"targets {options.Minimum}px"));
        try
        {
            await ToolkitScript.EnsureAsync(page).ConfigureAwait(false);
            var json = await page.EvaluateAsync<JsonElement>(
                "o => window.__blazorA11y.targetSizeReport(o)",
                new { min = Math.Max(1, options.Minimum), exclude = auditOptions.ExcludeSelectors.ToArray(), max = Math.Max(1, options.MaxFindings) }).ConfigureAwait(false);
            var offenders = ToolkitScript.Deserialize<TargetDto[]>(json) ?? [];
            builder.AddEngine(ToolkitRules.EngineName, ToolkitScript.ToolkitVersion);
            foreach (var offender in offenders)
            {
                builder.AddFinding(ToolkitRules.TargetSizeEnhanced.CreateFinding(
                    new FindingTarget { Selector = offender.Selector, Html = offender.Html, Role = offender.Role, AccessibleName = offender.Name, ElementOrdinal = offender.Ordinal },
                    remediation: string.Create(CultureInfo.InvariantCulture, $"The target is {offender.Width} x {offender.Height} CSS pixels, smaller than {options.Minimum} x {options.Minimum}. {ToolkitRules.TargetSizeEnhanced.Remediation}"),
                    data: new Dictionary<string, string>
                    {
                        ["width"] = offender.Width.ToString(CultureInfo.InvariantCulture),
                        ["height"] = offender.Height.ToString(CultureInfo.InvariantCulture),
                        ["minimum"] = options.Minimum.ToString(CultureInfo.InvariantCulture),
                    }));
            }

            if (offenders.Length == 0)
            {
                builder.AddPassedRule(ToolkitRules.TargetSizeEnhanced.Id);
            }
        }
        catch (Exception ex) when (ex is PlaywrightException or InvalidOperationException or JsonException)
        {
            builder.AddError(ExecutionError.FromException(ex is PlaywrightException ? "browser" : "engine", ex));
        }

        return PlaywrightAccessibility.Persist(builder.Build());
    }

    /// <summary>
    /// Shows the page in portrait and in landscape (WCAG 1.3.4) and reports when one orientation hides most of the
    /// content, rotates the whole page, or shows a "rotate your device" message. The viewport is restored afterwards.
    /// </summary>
    public static async Task<AccessibilityAudit> AuditOrientationAsync(this IPage page, OrientationOptions? options = null, AuditLabel? label = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        options ??= new OrientationOptions();
        var auditOptions = options.AuditOptions ?? AccessibilityAuditOptions.Default;
        var builder = CreateBuilder(page, auditOptions, label, label?.State ?? "orientation");
        var original = page.ViewportSize;
        if (original is null)
        {
            builder.AddError(new ExecutionError { Source = "browser", Message = "The page has no fixed viewport (the context was created with ViewportSize.NoViewport), so it cannot be rotated and restored." });
            return PlaywrightAccessibility.Persist(builder.Build());
        }

        try
        {
            var portrait = await OrientationSnapshotAsync(page, options.ShortSide, options.LongSide, options.SettleTime).ConfigureAwait(false);
            var landscape = await OrientationSnapshotAsync(page, options.LongSide, options.ShortSide, options.SettleTime).ConfigureAwait(false);
            builder.AddEngine(ToolkitRules.EngineName, ToolkitScript.ToolkitVersion);

            var problems = new List<(string Orientation, string Problem, string? Selector)>();
            foreach (var (name, snapshot, other) in new[] { ("portrait", portrait, landscape), ("landscape", landscape, portrait) })
            {
                if (snapshot.Rotated && !other.Rotated)
                {
                    problems.Add((name, "the page is rotated by 90 degrees", "html"));
                }

                foreach (var message in snapshot.Messages.Except(other.Messages, StringComparer.Ordinal))
                {
                    problems.Add((name, "a message asks users to rotate their device", message));
                }

                if (other.Text >= 200 && snapshot.Text < other.Text / 4)
                {
                    problems.Add((name, string.Create(CultureInfo.InvariantCulture, $"only {snapshot.Text} characters of text are shown, compared with {other.Text}"), "body"));
                }
            }

            foreach (var (orientation, problem, selector) in problems)
            {
                builder.AddFinding(ToolkitRules.OrientationLocked.CreateFinding(
                    new FindingTarget { Selector = selector },
                    remediation: $"In {orientation} orientation, {problem}. {ToolkitRules.OrientationLocked.Remediation}",
                    data: new Dictionary<string, string> { ["orientation"] = orientation }));
            }

            if (problems.Count == 0)
            {
                builder.AddPassedRule(ToolkitRules.OrientationLocked.Id);
            }
        }
        catch (Exception ex) when (ex is PlaywrightException or InvalidOperationException or JsonException)
        {
            builder.AddError(ExecutionError.FromException(ex is PlaywrightException ? "browser" : "engine", ex));
        }
        finally
        {
            await RestoreViewportAsync(page, original).ConfigureAwait(false);
        }

        return PlaywrightAccessibility.Persist(builder.Build());
    }

    private static async Task<OrientationDto> OrientationSnapshotAsync(IPage page, int width, int height, TimeSpan settle)
    {
        await page.SetViewportSizeAsync(width, height).ConfigureAwait(false);
        await SettleAsync(page, settle).ConfigureAwait(false);
        await ToolkitScript.EnsureAsync(page).ConfigureAwait(false);
        return ToolkitScript.Deserialize<OrientationDto>(await page.EvaluateAsync<JsonElement>("() => window.__blazorA11y.orientationSnapshot()").ConfigureAwait(false)) ?? new OrientationDto();
    }

    private static string DescribeMotion(MotionDto offender)
    {
        if (offender.Kind == "media")
        {
            return "Media keeps playing" + (offender.Infinite ? " in a loop" : string.Empty) + (offender.Controls ? "." : " and has no controls to pause it.");
        }

        var name = string.IsNullOrEmpty(offender.Name2) ? string.Empty : $" '{offender.Name2}'";
        var properties = string.IsNullOrEmpty(offender.Properties) ? "properties unknown" : offender.Properties;
        var infinite = offender.Infinite ? ", infinite" : string.Empty;
        return string.Create(CultureInfo.InvariantCulture, $"Animation{name} ({properties}, {offender.Duration} ms{infinite}) keeps running with reduced motion requested.");
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
