using System.Text.Json.Serialization;
using Microsoft.Playwright;

namespace BlazorAccessibilityToolkit.Playwright;

public static partial class AriaPatterns
{
    private const string InvalidFieldsScript = """
        form => {
          const t = window.__blazorA11y;
          const fields = Array.from(form.querySelectorAll('input, select, textarea, [role=textbox], [role=combobox], [role=listbox], [role=checkbox], [role=radiogroup], [role=spinbutton]'))
            .filter(el => { const v = (el.getAttribute('aria-invalid') || '').trim().toLowerCase(); return v && v !== 'false'; });
          const text = el => (el.textContent || '').replace(/\s+/g, ' ').trim();
          const referenced = el => ['aria-describedby', 'aria-errormessage']
            .flatMap(a => (el.getAttribute(a) || '').split(/\s+/).filter(Boolean))
            .map(id => el.ownerDocument.getElementById(id))
            .filter(target => target && text(target).length > 0)
            .map(text);
          const active = t.activeElementDeep();
          const native = !form.checkValidity || form.checkValidity() ? null : Array.from(form.querySelectorAll(':invalid')).filter(e => e.matches('input, select, textarea')).map(e => t.selectorFor(e));
          const liveText = Array.from(document.querySelectorAll('[role=alert], [role=status], [aria-live=polite], [aria-live=assertive]'))
            .filter(r => t.isVisible(r)).map(text).filter(Boolean).join(' | ');
          return {
            fields: fields.map(el => ({ selector: t.selectorFor(el), name: t.inspectElement(el).name, errors: referenced(el) })),
            focusedInvalid: !!active && fields.includes(active),
            focusedText: active && active !== document.body ? text(active).slice(0, 500) : '',
            focus: active && active !== document.body ? t.selectorFor(active) : null,
            nativeInvalid: native,
            liveText: liveText.slice(0, 2000),
          };
        }
        """;

    /// <summary>
    /// Form validation (WCAG 3.3.1, 3.3.3, 4.1.2, 4.1.3): submitting the form with the keyboard while it has errors marks
    /// each invalid field with <c>aria-invalid</c>, links each field to its error text with <c>aria-describedby</c> or
    /// <c>aria-errormessage</c>, moves focus to the first invalid field or to an error summary, and makes the errors
    /// perceivable to screen reader users (by that focus move or a live region).
    /// </summary>
    /// <param name="form">The form (or the container of the fields). Fill it with invalid values before calling.</param>
    /// <param name="submit">The submit control. Default: the first submit button in <paramref name="form"/>.</param>
    /// <param name="options">Timeouts.</param>
    /// <remarks>
    /// Blazor's <c>InputBase</c> components set <c>aria-invalid</c>, but <c>ValidationMessage</c> is not linked to its
    /// field and nothing moves focus after a failed submit, so default <c>EditForm</c> markup fails the second and third
    /// checks. Browser constraint validation (<c>required</c> without <c>novalidate</c>) is accepted when the browser
    /// blocks submission and focuses the first invalid field.
    /// </remarks>
    public static async Task<PatternVerification> VerifyFormValidationAsync(ILocator form, ILocator? submit = null, PatternOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(form);
        options ??= new PatternOptions();
        var page = form.Page;
        var checks = new List<PatternCheck>();
        await Internal.ToolkitScript.EnsureAsync(page).ConfigureAwait(false);

        submit ??= form.Locator("button[type=submit], input[type=submit], input[type=image], button:not([type])").First;
        if (await submit.CountAsync().ConfigureAwait(false) == 0)
        {
            checks.Add(new("The form has a submit button", false, "pass the submit control explicitly"));
            return Result("Form validation", checks);
        }

        var submitInfo = await submit.GetAccessibilityInfoAsync().ConfigureAwait(false);
        checks.Add(new("Submit control is a named button", submitInfo.Role == "button" && submitInfo.Name.Length > 0, submitInfo.ToString()));

        await submit.FocusAsync().ConfigureAwait(false);
        await page.Keyboard.PressAsync("Enter").ConfigureAwait(false);

        FormState? state = null;
        var reported = await WaitAsync(async () =>
        {
            state = Internal.ToolkitScript.Deserialize<FormState>(await form.EvaluateAsync<System.Text.Json.JsonElement>(InvalidFieldsScript).ConfigureAwait(false)) ?? new FormState();
            return state.Fields.Count > 0 || state.NativeInvalid is { Count: > 0 };
        }, options).ConfigureAwait(false);

        if (!reported || state is null)
        {
            checks.Add(new("Submitting with errors marks the invalid fields (aria-invalid)", false, "no field in the form has aria-invalid=\"true\" and the browser reported no invalid field; fill the form with invalid values before verifying"));
            return Result("Form validation", checks);
        }

        // Error links and the focus move often follow the invalid markers in a later render (for example a Blazor
        // OnAfterRenderAsync that calls FocusAsync), so wait for the settled state before judging it.
        if (!Settled(state))
        {
            await WaitAsync(async () =>
            {
                state = Internal.ToolkitScript.Deserialize<FormState>(await form.EvaluateAsync<System.Text.Json.JsonElement>(InvalidFieldsScript).ConfigureAwait(false)) ?? state;
                return Settled(state);
            }, options).ConfigureAwait(false);
        }

        if (state.Fields.Count == 0 && state.NativeInvalid is { Count: > 0 } native)
        {
            // Browser constraint validation: the browser blocks submission, focuses and describes the first invalid field.
            var focusedNative = state.Focus is not null && native.Contains(state.Focus);
            checks.Add(new("Invalid fields are reported (browser constraint validation)", true, string.Join(", ", native)));
            checks.Add(new("Focus moves to the first invalid field", focusedNative, $"focus: {state.Focus ?? "(document)"}"));
            return Result("Form validation", checks);
        }

        checks.Add(new("Submitting with errors marks the invalid fields (aria-invalid)", true, string.Join(", ", state.Fields.Select(f => $"\"{f.Name}\""))));

        var unlinked = state.Fields.Where(f => f.Errors.Count == 0).ToArray();
        checks.Add(new(
            "Each invalid field references its error text (aria-describedby or aria-errormessage)",
            unlinked.Length == 0,
            unlinked.Length == 0 ? null : "not linked: " + string.Join(", ", unlinked.Select(f => $"\"{f.Name}\" ({f.Selector})"))));

        // Focus must not stay on the submit button: move it to the first invalid field or to an error summary.
        var errorTexts = state.Fields.SelectMany(f => f.Errors).Where(e => e.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        var focusOnSummary = !state.FocusedInvalid && state.FocusedText.Length > 0 && errorTexts.Any(e => state.FocusedText.Contains(e, StringComparison.Ordinal));
        checks.Add(new(
            "Focus moves to the first invalid field or to an error summary",
            state.FocusedInvalid || focusOnSummary,
            $"focus: {state.Focus ?? "(document)"}"));

        var announced = state.FocusedInvalid || focusOnSummary || (state.LiveText.Length > 0 && (errorTexts.Length == 0 || errorTexts.Any(e => state.LiveText.Contains(e, StringComparison.Ordinal))));
        checks.Add(new(
            "Errors are conveyed to screen reader users (focus moves to them, or a live region contains them)",
            announced,
            state.LiveText.Length > 0 ? $"live regions: {state.LiveText}" : "no live region contains the errors"));

        return Result("Form validation", checks);
    }

    // True when every check that can still change over time passes: native validation focused a field, or every invalid
    // field is linked to its error and focus is on an invalid field or on an element that shows an error.
    private static bool Settled(FormState state)
    {
        if (state.Fields.Count == 0)
        {
            return state.NativeInvalid is { Count: > 0 } native && state.Focus is not null && native.Contains(state.Focus);
        }

        var errors = state.Fields.SelectMany(f => f.Errors).Where(e => e.Length > 0).ToArray();
        var focused = state.FocusedInvalid || (state.FocusedText.Length > 0 && errors.Any(e => state.FocusedText.Contains(e, StringComparison.Ordinal)));
        return focused && state.Fields.All(f => f.Errors.Count > 0);
    }

    private sealed class FormState
    {
        [JsonPropertyName("fields")]
        public List<InvalidField> Fields { get; set; } = [];

        [JsonPropertyName("focusedInvalid")]
        public bool FocusedInvalid { get; set; }

        [JsonPropertyName("focusedText")]
        public string FocusedText { get; set; } = string.Empty;

        [JsonPropertyName("focus")]
        public string? Focus { get; set; }

        [JsonPropertyName("nativeInvalid")]
        public List<string>? NativeInvalid { get; set; }

        [JsonPropertyName("liveText")]
        public string LiveText { get; set; } = string.Empty;
    }

    private sealed class InvalidField
    {
        [JsonPropertyName("selector")]
        public string Selector { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("errors")]
        public List<string> Errors { get; set; } = [];
    }
}
