using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BlazorAccessibilityToolkit.Playwright.Internal;
using Microsoft.Playwright;

namespace BlazorAccessibilityToolkit.Playwright;

/// <summary>Settings for <see cref="KeyboardScenario"/>.</summary>
public sealed record KeyboardScenarioOptions
{
    /// <summary>How long expectations wait for the UI to reach the expected state (Blazor Server updates are asynchronous). Default 5 s.</summary>
    public TimeSpan ExpectTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Start a <see cref="LiveRegionMonitor"/> before the first step so live-region expectations work. Default <c>true</c>.</summary>
    public bool MonitorLiveRegions { get; init; } = true;

    /// <summary>Options for audits run by <see cref="KeyboardScenario.Audit"/>.</summary>
    public AccessibilityAuditOptions? AuditOptions { get; init; }

    /// <summary>Scenario name used in audit labels and failure messages.</summary>
    public string? Name { get; init; }
}

/// <summary>Kind of scenario step.</summary>
public enum ScenarioStepKind
{
    /// <summary>A key press or custom action.</summary>
    Action,

    /// <summary>An expectation.</summary>
    Expectation,

    /// <summary>An accessibility audit.</summary>
    Audit,
}

/// <summary>Outcome of one scenario step.</summary>
public sealed record ScenarioStepResult
{
    /// <summary>One-based step number.</summary>
    public int Number { get; init; }

    /// <summary>Step description.</summary>
    public required string Description { get; init; }

    /// <summary>Step kind.</summary>
    public ScenarioStepKind Kind { get; init; }

    /// <summary>Whether the step succeeded.</summary>
    public bool Passed { get; init; }

    /// <summary>Focused element after the step.</summary>
    public FocusSnapshot? FocusAfter { get; init; }

    /// <summary>Failure details.</summary>
    public string? Error { get; init; }

    /// <summary>Duration in milliseconds.</summary>
    public double DurationMs { get; init; }

    /// <inheritdoc />
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Number}. {(Passed ? "ok " : "FAIL")} {Description} -> focus: {FocusAfter?.ToString() ?? "?"}");
}

/// <summary>
/// A keyboard-only interaction script executed with real browser key presses. Steps are queued fluently and run by
/// <see cref="RunAsync"/>; expectations poll until they are met or time out, so no fixed delays are needed for
/// asynchronous Blazor updates. <see cref="RunAsync"/> can be called repeatedly to run steps queued since the last run.
/// </summary>
/// <example>
/// <code>
/// await KeyboardScenario.For(page)
///     .PressTab()
///     .ExpectFocused(role: "button", name: "Save")
///     .PressEnter()
///     .ExpectLiveRegionUpdate("Changes saved")
///     .RunAsync();
/// </code>
/// </example>
public sealed class KeyboardScenario
{
    private readonly List<(string Description, ScenarioStepKind Kind, Func<Task> Run)> _pending = [];
    private readonly List<ScenarioStepResult> _results = [];
    private readonly List<AccessibilityAudit> _audits = [];
    private readonly Dictionary<string, int> _remembered = new(StringComparer.Ordinal);
    private readonly Func<string, AccessibilityAuditOptions?, Task<AccessibilityAudit>> _audit;
    private LiveRegionMonitor? _monitor;
    private bool _started;

    internal KeyboardScenario(IPage page, KeyboardScenarioOptions? options, Func<string, AccessibilityAuditOptions?, Task<AccessibilityAudit>>? audit)
    {
        Page = page;
        Options = options ?? new KeyboardScenarioOptions();
        _audit = audit ?? ((state, auditOptions) => page.AuditAccessibilityAsync(auditOptions ?? Options.AuditOptions, new AuditLabel { Scenario = Options.Name, State = state }));
    }

    /// <summary>The page.</summary>
    public IPage Page { get; }

    /// <summary>Scenario settings.</summary>
    public KeyboardScenarioOptions Options { get; }

    /// <summary>Results of executed steps.</summary>
    public IReadOnlyList<ScenarioStepResult> Steps => _results;

    /// <summary>Audits executed by <see cref="Audit"/> steps.</summary>
    public IReadOnlyList<AccessibilityAudit> Audits => _audits;

    /// <summary>The live-region monitor (available after the scenario started, when enabled).</summary>
    public LiveRegionMonitor? LiveRegions => _monitor;

    /// <summary>Creates a scenario for a page.</summary>
    public static KeyboardScenario For(IPage page, KeyboardScenarioOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        return new KeyboardScenario(page, options, null);
    }

    /// <summary>Presses Tab.</summary>
    public KeyboardScenario PressTab(int times = 1) => Press("Tab", times);

    /// <summary>Presses Shift+Tab.</summary>
    public KeyboardScenario PressShiftTab(int times = 1) => Press("Shift+Tab", times);

    /// <summary>Presses Enter.</summary>
    public KeyboardScenario PressEnter() => Press("Enter");

    /// <summary>Presses Space.</summary>
    public KeyboardScenario PressSpace() => Press(" ");

    /// <summary>Presses Escape.</summary>
    public KeyboardScenario PressEscape() => Press("Escape");

    /// <summary>Presses ArrowDown.</summary>
    public KeyboardScenario PressArrowDown(int times = 1) => Press("ArrowDown", times);

    /// <summary>Presses ArrowUp.</summary>
    public KeyboardScenario PressArrowUp(int times = 1) => Press("ArrowUp", times);

    /// <summary>Presses ArrowRight.</summary>
    public KeyboardScenario PressArrowRight(int times = 1) => Press("ArrowRight", times);

    /// <summary>Presses ArrowLeft.</summary>
    public KeyboardScenario PressArrowLeft(int times = 1) => Press("ArrowLeft", times);

    /// <summary>Presses Home.</summary>
    public KeyboardScenario PressHome() => Press("Home");

    /// <summary>Presses End.</summary>
    public KeyboardScenario PressEnd() => Press("End");

    /// <summary>Presses a key or chord using Playwright key names (for example <c>Control+a</c>, <c>PageDown</c>).</summary>
    public KeyboardScenario Press(string key, int times = 1)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        var label = key == " " ? "Space" : key;
        for (var i = 0; i < times; i++)
        {
            Enqueue($"press {label}", ScenarioStepKind.Action, () => Page.Keyboard.PressAsync(key));
        }

        return this;
    }

    /// <summary>Types text with key presses.</summary>
    public KeyboardScenario Type(string text) => Enqueue($"type \"{text}\"", ScenarioStepKind.Action, () => Page.Keyboard.TypeAsync(text));

    /// <summary>Moves focus to an element programmatically (setup only; it is not a keyboard interaction).</summary>
    public KeyboardScenario FocusOn(ILocator locator) => Enqueue($"focus {locator}", ScenarioStepKind.Action, () => locator.FocusAsync());

    /// <summary>
    /// Resets keyboard focus so the next Tab reaches the first element of the tab order (or, with
    /// <paramref name="reverse"/>, so the next Shift+Tab reaches the last one), like a freshly loaded page.
    /// </summary>
    public KeyboardScenario StartFromDocument(bool reverse = false) => Enqueue(reverse ? "start from end of document" : "start from document", ScenarioStepKind.Action, async () =>
    {
        await ToolkitScript.EnsureAsync(Page).ConfigureAwait(false);
        await Page.EvaluateAsync("r => window.__blazorA11y.blurActive(r)", reverse).ConfigureAwait(false);
    });

    /// <summary>Runs a custom step.</summary>
    public KeyboardScenario Do(string description, Func<IPage, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return Enqueue(description, ScenarioStepKind.Action, () => action(Page));
    }

    /// <summary>Expects the focused element to have the given role and/or accessible name (exact match after whitespace normalization).</summary>
    public KeyboardScenario ExpectFocused(string? role = null, string? name = null)
    {
        var description = $"expect focus on {role ?? "*"}{(name is null ? string.Empty : $" \"{name}\"")}";
        return Enqueue(description, ScenarioStepKind.Expectation, () => WaitForFocusAsync(new { role, name = name is null ? null : new { text = name, exact = true } }, description));
    }

    /// <summary>Expects the focused element to have the given role and an accessible name matching <paramref name="name"/>.</summary>
    public KeyboardScenario ExpectFocused(string? role, Regex name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var description = $"expect focus on {role ?? "*"} /{name}/";
        var flags = name.Options.HasFlag(RegexOptions.IgnoreCase) ? "i" : string.Empty;
        return Enqueue(description, ScenarioStepKind.Expectation, () => WaitForFocusAsync(new { role, name = new { pattern = name.ToString(), flags } }, description));
    }

    /// <summary>Expects <paramref name="locator"/> to be the focused element.</summary>
    public KeyboardScenario ExpectFocused(ILocator locator)
    {
        ArgumentNullException.ThrowIfNull(locator);
        var description = $"expect focus on {locator}";
        return Enqueue(description, ScenarioStepKind.Expectation, () => PollAsync(
            async () => await locator.CountAsync().ConfigureAwait(false) == 1 && await IsFocusedAsync(locator).ConfigureAwait(false),
            description));
    }

    /// <summary>Expects focus to be inside <paramref name="container"/>.</summary>
    public KeyboardScenario ExpectFocusWithin(ILocator container)
    {
        ArgumentNullException.ThrowIfNull(container);
        var description = $"expect focus within {container}";
        return Enqueue(description, ScenarioStepKind.Expectation, () => PollAsync(() => ContainsFocusAsync(container), description));
    }

    /// <summary>Expects focus to be outside <paramref name="container"/> (for example after a dialog closed).</summary>
    public KeyboardScenario ExpectFocusNotWithin(ILocator container)
    {
        ArgumentNullException.ThrowIfNull(container);
        var description = $"expect focus outside {container}";
        return Enqueue(description, ScenarioStepKind.Expectation, () => PollAsync(async () => !await ContainsFocusAsync(container).ConfigureAwait(false), description));
    }

    /// <summary>Remembers the currently focused element under <paramref name="key"/> (for example the button that opens a dialog).</summary>
    public KeyboardScenario RememberFocus(string key) => Enqueue($"remember focus as '{key}'", ScenarioStepKind.Action, async () =>
    {
        var focus = await Page.GetFocusAsync().ConfigureAwait(false);
        _remembered[key] = focus.Element?.ElementKey ?? throw new AccessibilityAssertionException($"Cannot remember focus as '{key}': nothing is focused.");
    });

    /// <summary>Expects focus to be back on the element remembered with <see cref="RememberFocus"/> (focus restoration).</summary>
    public KeyboardScenario ExpectFocusRestored(string key)
    {
        var description = $"expect focus restored to '{key}'";
        return Enqueue(description, ScenarioStepKind.Expectation, () =>
        {
            if (!_remembered.TryGetValue(key, out var elementKey))
            {
                throw new InvalidOperationException($"No focus was remembered as '{key}'. Call RememberFocus(\"{key}\") first.");
            }

            return PollAsync(async () => (await Page.GetFocusAsync().ConfigureAwait(false)).Element?.ElementKey == elementKey, description);
        });
    }

    /// <summary>
    /// Presses Tab and Shift+Tab repeatedly and expects focus to stay inside <paramref name="container"/> (modal dialog focus
    /// containment). Focus must already be inside the container. After each key press, focus has up to
    /// <see cref="KeyboardScenarioOptions.ExpectTimeout"/> to return to the container, so traps that move focus back
    /// asynchronously (for example from a Blazor Server event handler) pass.
    /// </summary>
    public KeyboardScenario ExpectFocusContainedWithin(ILocator container, int tabPresses = 0)
    {
        ArgumentNullException.ThrowIfNull(container);
        var description = $"expect focus to stay within {container} while tabbing";
        return Enqueue(description, ScenarioStepKind.Expectation, async () =>
        {
            var presses = tabPresses > 0 ? tabPresses : Math.Max(3, await container.EvaluateAsync<int>("el => window.__blazorA11y.tabbables(window.__blazorA11y.selectorFor(el)).length").ConfigureAwait(false) + 2);
            foreach (var key in new[] { "Tab", "Shift+Tab" })
            {
                for (var i = 0; i < presses; i++)
                {
                    await Page.Keyboard.PressAsync(key).ConfigureAwait(false);
                    if (!await WaitUntilAsync(() => ContainsFocusAsync(container)).ConfigureAwait(false))
                    {
                        var focus = await Page.GetFocusAsync().ConfigureAwait(false);
                        throw new AccessibilityAssertionException($"Focus escaped {container} after {i + 1} x {key} and did not return within {Options.ExpectTimeout.TotalSeconds:0.#} s: now on {focus}.");
                    }
                }
            }
        });
    }

    /// <summary>Expects an ARIA or native state on <paramref name="locator"/> (for example <c>expanded</c> = <c>true</c>); <c>null</c> expects the state to be absent.</summary>
    public KeyboardScenario ExpectState(ILocator locator, string state, string? value)
    {
        ArgumentNullException.ThrowIfNull(locator);
        var key = state.StartsWith("aria-", StringComparison.Ordinal) ? state[5..] : state;
        var description = $"expect {key}={value ?? "(absent)"} on {locator}";
        return Enqueue(description, ScenarioStepKind.Expectation, () => PollAsync(
            async () => string.Equals((await locator.GetAccessibilityInfoAsync().ConfigureAwait(false)).State(key), value, StringComparison.Ordinal),
            description));
    }

    /// <summary>
    /// Expects a live-region update containing <paramref name="text"/>. This is DOM evidence of the update, not proof of
    /// screen-reader speech.
    /// </summary>
    public KeyboardScenario ExpectLiveRegionUpdate(string text, TextMatch match = TextMatch.Contains, string? politeness = null) =>
        Enqueue($"expect live-region update \"{text}\"", ScenarioStepKind.Expectation, async () =>
        {
            var monitor = _monitor ?? throw new InvalidOperationException("Live-region monitoring is disabled for this scenario (KeyboardScenarioOptions.MonitorLiveRegions).");
            await monitor.ExpectUpdateAsync(text, match, politeness, Options.ExpectTimeout).ConfigureAwait(false);
        });

    /// <summary>Runs an accessibility audit of the current state. By default the scenario fails when it reports violations.</summary>
    public KeyboardScenario Audit(string state, AccessibilityAuditOptions? options = null, AccessibilityExpectation? expectation = null, bool assertNoViolations = true) =>
        Enqueue($"audit '{state}'", ScenarioStepKind.Audit, async () =>
        {
            var audit = await _audit(state, options).ConfigureAwait(false);
            _audits.Add(audit);
            if (assertNoViolations)
            {
                audit.ShouldHaveNoViolations(expectation);
            }
        });

    /// <summary>Runs all queued steps. Throws <see cref="AccessibilityAssertionException"/> with a transcript when a step fails.</summary>
    public async Task<IReadOnlyList<ScenarioStepResult>> RunAsync()
    {
        if (!_started)
        {
            _started = true;
            await ToolkitScript.EnsureAsync(Page).ConfigureAwait(false);
            if (Options.MonitorLiveRegions)
            {
                _monitor = await LiveRegionMonitor.StartAsync(Page).ConfigureAwait(false);
            }
        }

        var steps = _pending.ToArray();
        _pending.Clear();
        foreach (var (description, kind, run) in steps)
        {
            var stopwatch = Stopwatch.StartNew();
            Exception? failure = null;
            try
            {
                await run().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is AccessibilityAssertionException or PlaywrightException or TimeoutException or InvalidOperationException)
            {
                failure = ex;
            }

            FocusSnapshot? focus = null;
            try
            {
                focus = await Page.GetFocusAsync().ConfigureAwait(false);
            }
            catch (PlaywrightException)
            {
                // The page may have navigated or closed.
            }

            _results.Add(new ScenarioStepResult
            {
                Number = _results.Count + 1,
                Description = description,
                Kind = kind,
                Passed = failure is null,
                FocusAfter = focus,
                Error = failure?.Message,
                DurationMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 1),
            });

            if (failure is not null)
            {
                throw new AccessibilityAssertionException(Transcript(failure), failure)
                {
                    Findings = failure is AccessibilityAssertionException a ? a.Findings : [],
                };
            }
        }

        return _results;
    }

    private KeyboardScenario Enqueue(string description, ScenarioStepKind kind, Func<Task> run)
    {
        _pending.Add((description, kind, run));
        return this;
    }

    private async Task WaitForFocusAsync(object arg, string description)
    {
        try
        {
            await Page.WaitForFunctionAsync(
                "a => window.__blazorA11y && window.__blazorA11y.focusMatches(a)",
                arg,
                new PageWaitForFunctionOptions { Timeout = (float)Options.ExpectTimeout.TotalMilliseconds, PollingInterval = 25 }).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            var focus = await Page.GetFocusAsync().ConfigureAwait(false);
            throw new AccessibilityAssertionException($"Failed to {description} within {Options.ExpectTimeout.TotalSeconds:0.#} s; focus is on {focus}.");
        }
    }

    private async Task PollAsync(Func<Task<bool>> condition, string description)
    {
        if (!await WaitUntilAsync(condition).ConfigureAwait(false))
        {
            var focus = await Page.GetFocusAsync().ConfigureAwait(false);
            throw new AccessibilityAssertionException($"Failed to {description} within {Options.ExpectTimeout.TotalSeconds:0.#} s; focus is on {focus}.");
        }
    }

    // Polls until the condition holds (true) or ExpectTimeout elapses (false).
    private async Task<bool> WaitUntilAsync(Func<Task<bool>> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            if (await condition().ConfigureAwait(false))
            {
                return true;
            }

            if (stopwatch.Elapsed >= Options.ExpectTimeout)
            {
                return false;
            }

            await Task.Delay(25).ConfigureAwait(false);
        }
    }

    private static async Task<bool> IsFocusedAsync(ILocator locator)
    {
        await ToolkitScript.EnsureAsync(locator.Page).ConfigureAwait(false);
        return await locator.EvaluateAsync<bool>("el => window.__blazorA11y.activeElementDeep() === el").ConfigureAwait(false);
    }

    private static async Task<bool> ContainsFocusAsync(ILocator container)
    {
        await ToolkitScript.EnsureAsync(container.Page).ConfigureAwait(false);
        if (await container.CountAsync().ConfigureAwait(false) != 1)
        {
            return false;
        }

        return await container.EvaluateAsync<bool>("el => { const a = window.__blazorA11y.activeElementDeep(); return !!a && (el === a || el.contains(a)); }").ConfigureAwait(false);
    }

    private string Transcript(Exception failure)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"Keyboard scenario{(Options.Name is null ? string.Empty : $" '{Options.Name}'")} failed at step {_results.Count}: {failure.Message}");
        sb.AppendLine().AppendLine().AppendLine("Steps:");
        foreach (var step in _results)
        {
            sb.Append("  ").AppendLine(step.ToString());
        }

        return sb.ToString().TrimEnd();
    }
}
