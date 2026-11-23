using System.Globalization;
using System.Text.Json;
using BlazorAccessibilityToolkit.Rules;
using Microsoft.Playwright;

namespace BlazorAccessibilityToolkit.Playwright;

/// <summary>
/// What changed on screen when an element received keyboard focus, measured by comparing screenshots of the element and
/// the area around it before and after it was focused (WCAG 2.4.7 Focus Visible and 2.4.13 Focus Appearance).
/// </summary>
public sealed record FocusAppearanceMeasurement
{
    /// <summary>Width of the unfocused element in CSS pixels.</summary>
    public double Width { get; init; }

    /// <summary>Height of the unfocused element in CSS pixels.</summary>
    public double Height { get; init; }

    /// <summary>CSS pixels whose color changed on focus, in the element and up to <see cref="FocusAppearanceOptions.Margin"/> around it.</summary>
    public int ChangedArea { get; init; }

    /// <summary>Changed CSS pixels whose focused and unfocused colors have a contrast ratio of at least 3:1.</summary>
    public int ContrastingArea { get; init; }

    /// <summary>
    /// The area of a 2 CSS pixel thick perimeter of the unfocused element, 4 × (width + height): the minimum
    /// <see cref="ContrastingArea"/> for WCAG 2.4.13.
    /// </summary>
    public double RequiredArea => 4 * (Width + Height);

    /// <summary>Whether the element matched <c>:focus-visible</c>, as it does when focused with the keyboard.</summary>
    public bool FocusVisibleMatched { get; init; }

    /// <summary>
    /// The element is not visible until it receives focus, like a skip link. Its size and the compared area are then
    /// taken from the focused element.
    /// </summary>
    public bool HiddenUntilFocused { get; init; }

    /// <summary>
    /// The browser's own focus ring is drawn (<c>outline-style: auto</c> when focused). WCAG 2.4.13 exempts indicators
    /// determined by the user agent, so no <see cref="ToolkitRules.FocusAppearance"/> finding is reported for it.
    /// </summary>
    public bool UserAgentIndicator { get; init; }

    /// <summary>Something near the element changed visibly when it received focus (WCAG 2.4.7).</summary>
    public bool IsVisible => ChangedArea > 0;

    /// <summary>The indicator's contrasting area is at least <see cref="RequiredArea"/> (WCAG 2.4.13, AAA).</summary>
    public bool MeetsFocusAppearance => ContrastingArea >= RequiredArea;

    /// <inheritdoc />
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{ChangedArea} px² changed, {ContrastingArea} px² with 3:1 contrast, {RequiredArea:0} px² needed for 2.4.13");
}

/// <summary>Settings for <see cref="FocusAppearance.MeasureFocusAppearanceAsync"/>.</summary>
public sealed record FocusAppearanceOptions
{
    /// <summary>How far around the element (in CSS pixels) changes count as its indicator. Default 8.</summary>
    public int Margin { get; init; } = 8;

    /// <summary>
    /// The largest difference in any color channel (0 to 255) that still counts as unchanged, to ignore rendering noise.
    /// Default 2.
    /// </summary>
    public int Tolerance { get; init; } = 2;
}

/// <summary>Measures focus indicators from screenshots.</summary>
public static class FocusAppearance
{
    // Decodes both PNGs in the page (no image library needed in .NET) and compares them pixel by pixel. A Blob is not
    // subject to the page's content security policy and does not taint the canvas.
    private const string CompareScript = """
        async ([before, after, tolerance]) => {
          const load = async (base64) => {
            const binary = atob(base64);
            const bytes = new Uint8Array(binary.length);
            for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
            const bitmap = await createImageBitmap(new Blob([bytes], { type: 'image/png' }));
            let canvas;
            if (typeof OffscreenCanvas === 'function') {
              canvas = new OffscreenCanvas(bitmap.width, bitmap.height);
            } else {
              canvas = document.createElement('canvas');
              canvas.width = bitmap.width;
              canvas.height = bitmap.height;
            }
            const context = canvas.getContext('2d');
            context.drawImage(bitmap, 0, 0);
            return context.getImageData(0, 0, bitmap.width, bitmap.height);
          };
          const [a, b] = await Promise.all([load(before), load(after)]);
          if (a.width !== b.width || a.height !== b.height) return { changed: -1, contrasting: 0 };
          const linear = c => { c /= 255; return c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4); };
          const luminance = (d, i) => 0.2126 * linear(d[i]) + 0.7152 * linear(d[i + 1]) + 0.0722 * linear(d[i + 2]);
          let changed = 0, contrasting = 0;
          for (let i = 0; i < a.data.length; i += 4) {
            if (Math.abs(a.data[i] - b.data[i]) <= tolerance && Math.abs(a.data[i + 1] - b.data[i + 1]) <= tolerance
              && Math.abs(a.data[i + 2] - b.data[i + 2]) <= tolerance) continue;
            changed++;
            const l1 = luminance(a.data, i), l2 = luminance(b.data, i);
            if ((Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05) >= 3) contrasting++;
          }
          return { changed, contrasting };
        }
        """;

    /// <summary>
    /// Screenshots the element and the area around it unfocused, focuses it as the keyboard would (so that
    /// <c>:focus-visible</c> styles apply), screenshots it again and compares the two. Afterwards focus is removed
    /// from the element. Screenshots are taken with animations stopped and the text caret hidden.
    /// </summary>
    /// <returns>The measurement, or <see langword="null"/> when the element is not on screen.</returns>
    public static Task<FocusAppearanceMeasurement?> MeasureFocusAppearanceAsync(this ILocator element, FocusAppearanceOptions? options = null) =>
        MeasureAsync(element, options, keepFocus: false);

    internal static async Task<FocusAppearanceMeasurement?> MeasureAsync(ILocator element, FocusAppearanceOptions? options, bool keepFocus)
    {
        ArgumentNullException.ThrowIfNull(element);
        options ??= new FocusAppearanceOptions();
        var page = element.Page;

        // In the middle of the viewport, so that sticky headers are unlikely to cover the indicator.
        await element.EvaluateAsync("""
            el => {
              const active = el.ownerDocument.activeElement;
              if (active && active !== el.ownerDocument.body) active.blur();
              el.scrollIntoView({ block: 'center', inline: 'center' });
            }
            """).ConfigureAwait(false);
        await NextFramesAsync(page).ConfigureAwait(false);
        var viewport = await page.EvaluateAsync<JsonElement>("() => ({ width: window.innerWidth, height: window.innerHeight })").ConfigureAwait(false);
        var (viewportWidth, viewportHeight) = (viewport.GetProperty("width").GetDouble(), viewport.GetProperty("height").GetDouble());
        bool OnScreen(LocatorBoundingBoxResult? b) =>
            b is { Width: > 0, Height: > 0 } && b.X + b.Width > 0 && b.Y + b.Height > 0 && b.X < viewportWidth && b.Y < viewportHeight;

        var unfocusedBox = await element.BoundingBoxAsync().ConfigureAwait(false);

        // Focus it first to learn where it is when focused: a skip link is off screen or clipped to 1 pixel until then.
        // A key press first puts the browser in keyboard modality, so programmatic focus matches :focus-visible also
        // where the focusVisible option is not supported.
        await page.Keyboard.PressAsync("Shift").ConfigureAwait(false);
        await FocusAsync(element, preventScroll: OnScreen(unfocusedBox)).ConfigureAwait(false);
        await NextFramesAsync(page).ConfigureAwait(false);
        var focusedBox = await element.BoundingBoxAsync().ConfigureAwait(false);
        if (!OnScreen(focusedBox))
        {
            await element.EvaluateAsync("el => el.blur()").ConfigureAwait(false);
            return null;
        }

        // Focusing may have scrolled the page; measure the unfocused box again at this scroll position.
        await element.EvaluateAsync("el => el.blur()").ConfigureAwait(false);
        await NextFramesAsync(page).ConfigureAwait(false);
        unfocusedBox = await element.BoundingBoxAsync().ConfigureAwait(false);
        var visibleUnfocused = OnScreen(unfocusedBox) && unfocusedBox!.Width >= 4 && unfocusedBox.Height >= 4;
        var boxes = visibleUnfocused ? new[] { unfocusedBox!, focusedBox! } : [focusedBox!];
        var left = Math.Max(0, Math.Floor(boxes.Min(b => b.X) - options.Margin));
        var top = Math.Max(0, Math.Floor(boxes.Min(b => b.Y) - options.Margin));
        var right = Math.Min(viewportWidth, Math.Ceiling(boxes.Max(b => b.X + b.Width) + options.Margin));
        var bottom = Math.Min(viewportHeight, Math.Ceiling(boxes.Max(b => b.Y + b.Height) + options.Margin));
        if (right - left < 1 || bottom - top < 1)
        {
            return null;
        }

        var clip = new Clip { X = (float)left, Y = (float)top, Width = (float)(right - left), Height = (float)(bottom - top) };
        var before = await ScreenshotAsync(page, clip).ConfigureAwait(false);
        await page.Keyboard.PressAsync("Shift").ConfigureAwait(false);
        var focused = await FocusAsync(element, preventScroll: true).ConfigureAwait(false);
        await NextFramesAsync(page).ConfigureAwait(false);
        var after = await ScreenshotAsync(page, clip).ConfigureAwait(false);
        if (!keepFocus)
        {
            await element.EvaluateAsync("el => el.blur()").ConfigureAwait(false);
        }

        var result = await page.EvaluateAsync<JsonElement>(CompareScript, new object[] { Convert.ToBase64String(before), Convert.ToBase64String(after), options.Tolerance }).ConfigureAwait(false);
        var changed = result.GetProperty("changed").GetInt32();

        // The required area is based on the unfocused element; one that is not visible until focused is measured focused.
        var size = visibleUnfocused ? unfocusedBox! : focusedBox!;
        return new FocusAppearanceMeasurement
        {
            Width = size.Width,
            Height = size.Height,

            // Screenshots of different sizes (a resized viewport) cannot be compared: count everything as changed.
            ChangedArea = changed < 0 ? (int)(clip.Width * clip.Height) : changed,
            ContrastingArea = result.GetProperty("contrasting").GetInt32(),
            FocusVisibleMatched = focused.GetProperty("focusVisible").GetBoolean(),
            UserAgentIndicator = focused.GetProperty("userAgent").GetBoolean(),
            HiddenUntilFocused = !visibleUnfocused,
        };
    }

    private static Task<JsonElement> FocusAsync(ILocator element, bool preventScroll) => element.EvaluateAsync<JsonElement>("""
        (el, preventScroll) => {
          el.focus({ preventScroll, focusVisible: true });
          const style = getComputedStyle(el);
          return { focusVisible: el.matches(':focus-visible'), userAgent: style.outlineStyle === 'auto' && style.outlineWidth !== '0px' };
        }
        """, preventScroll);

    /// <summary>The findings for a measurement: 2.4.7 when nothing changed, and 2.4.13 when asked for.</summary>
    internal static IEnumerable<AccessibilityFinding> Findings(FocusAppearanceMeasurement measurement, FindingTarget target, bool reportFocusAppearance)
    {
        var data = new Dictionary<string, string>
        {
            ["changedArea"] = measurement.ChangedArea.ToString(CultureInfo.InvariantCulture),
            ["contrastingArea"] = measurement.ContrastingArea.ToString(CultureInfo.InvariantCulture),
            ["requiredArea"] = measurement.RequiredArea.ToString("0", CultureInfo.InvariantCulture),
        };
        if (!measurement.IsVisible)
        {
            yield return ToolkitRules.FocusNotVisible.CreateFinding(target, data: data);
        }
        else if (reportFocusAppearance && !measurement.MeetsFocusAppearance && !measurement.UserAgentIndicator)
        {
            yield return ToolkitRules.FocusAppearance.CreateFinding(
                target,
                remediation: string.Create(CultureInfo.InvariantCulture, $"The indicator changes {measurement.ContrastingArea} CSS px² by 3:1 or more; a 2 px perimeter of this {measurement.Width:0}×{measurement.Height:0} element is {measurement.RequiredArea:0} px². {ToolkitRules.FocusAppearance.Remediation}"),
                data: data);
        }
    }

    private static Task<byte[]> ScreenshotAsync(IPage page, Clip clip) => page.ScreenshotAsync(new PageScreenshotOptions
    {
        Clip = clip,
        Scale = ScreenshotScale.Css,
        Animations = ScreenshotAnimations.Disabled,
        Caret = ScreenshotCaret.Hide,
        Type = ScreenshotType.Png,
    });

    // Lets style changes on focus (and Blazor's re-render of focus handlers) reach the screen.
    private static Task NextFramesAsync(IPage page) =>
        page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(() => setTimeout(resolve, 50))))");
}
