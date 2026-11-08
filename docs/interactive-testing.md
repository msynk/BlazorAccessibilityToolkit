# Browser and interactive testing

Everything here uses real browsers through `BlazorAccessibilityToolkit.Playwright`. Features work on any Playwright `IPage`, whether it shows your running app, a page loaded with `SetContentAsync`, or a component in the [interactive host](#interactive-component-host).

## Browsers and sessions

```csharp
await using var browser = await AccessibilityBrowser.LaunchAsync(new AccessibilityBrowserOptions
{
    Browser = "chromium",       // default: BLAZOR_A11Y_BROWSER or chromium
    ViewportWidth = 1280,       // also: Locale, ReducedMotion (default on), Headless, Channel
});
await using var session = await browser.NewSessionAsync();   // a fresh, isolated browser context per test
await session.Page.GotoAsync(url);
```

`LaunchAsync` throws `BrowserNotAvailableException` with installation instructions when the browser is missing.

## Auditing pages and regions

```csharp
var audit = await page.AuditAccessibilityAsync(
    new AccessibilityAuditOptions { Tags = AccessibilityStandard.Wcag22AA, IncludeBestPractices = true },
    new AuditLabel { Scenario = "checkout", State = "payment step" });

var region = await page.GetByRole(AriaRole.Navigation).AuditAccessibilityAsync();   // the locator must match exactly one element
```

- axe-core and the toolkit's browser diagnostics run together. Audit again after each interaction you care about (dialog open, menu expanded, form submitted).
- Audits never pass silently. They fail with an execution error when the engine fails or times out (`AccessibilityAuditOptions.Timeout`, default 60 s), when the scope matches zero or several elements, when tags or rules are unknown, or when the page is closed.
- Interactive Server pages are prerendered, and key presses sent before the circuit connects are lost. Expose readiness (the demo app's layout sets `data-interactive="true"` from `RendererInfo.IsInteractive`) and wait for it instead of sleeping.

## Inspecting accessible names, roles and states

```csharp
var info = await page.GetByLabel("Email").GetAccessibilityInfoAsync();
// info.Role == "textbox", info.Name == "Email", info.Description == "Enter your email address.", info.State("invalid") == "true"

await button.ShouldHaveRoleAsync("button");
await button.ShouldHaveAccessibleNameAsync("Delete invoice 42");
await button.ShouldHaveStateAsync("expanded", "false");    // null asserts that the state is absent
var focus = await page.GetFocusAsync();                    // pierces shadow roots and same-origin iframes
```

Names and roles are computed with axe-core's implementation of the accessible name algorithm. They are not read from the platform accessibility tree.

## Keyboard scenarios

```csharp
await KeyboardScenario.For(page, new KeyboardScenarioOptions { Name = "settings", ExpectTimeout = TimeSpan.FromSeconds(5) })
    .StartFromDocument()                         // like a fresh page load; StartFromDocument(reverse: true) for Shift+Tab
    .PressTab()
    .ExpectFocused("tab", "Profile")
    .PressArrowRight().ExpectFocused("tab", "Notifications")
    .PressEnd().PressHome()
    .ExpectState(page.GetByRole(AriaRole.Tab, new() { Name = "Profile" }), "selected", "true")
    .FocusOn(page.GetByRole(AriaRole.Button, new() { Name = "Delete account" }))
    .RememberFocus("trigger")
    .PressEnter()
    .ExpectFocusWithin(page.GetByRole(AriaRole.Dialog))
    .ExpectFocusContainedWithin(page.GetByRole(AriaRole.Dialog))    // Tab and Shift+Tab stay inside
    .Audit("dialog open")                                           // fails the scenario on violations by default
    .PressEscape()
    .ExpectFocusRestored("trigger")
    .ExpectLiveRegionUpdate("Account deleted.")
    .RunAsync();
```

- Keys are real Playwright key presses: `PressTab/ShiftTab/Enter/Space/Escape/Arrow*/Home/End`, `Press("Control+a")`, `Type("text")`.
- Expectations poll until the condition holds or the timeout expires, so Blazor Server round trips need no fixed delays.
- When a step fails, the exception includes a transcript of every step and where focus was after it.
- `ExpectFocused(role, name)` compares the name exactly after normalizing whitespace. Use the `Regex` overload for partial names.
- `FocusOn` sets up a starting point programmatically. It is not a keyboard interaction.

## Focus order

```csharp
var order = await page.CaptureFocusOrderAsync(new FocusOrderOptions { ReportMissingFocusIndicator = true });
order.ShouldMatch("link Skip to main content", "link Home", "button Save changes").ShouldNotTrapFocus();
Console.WriteLine(order.Describe());
```

The capture presses Tab until focus leaves the page, wraps, cycles or gets stuck. `Stops` is what was observed. `Findings` holds heuristics, all reported as potential issues:

| Rule | Meaning |
| --- | --- |
| `blazor-focus-trap` | Focus cycles within a subset of the page that is not a modal dialog. Cycling inside `aria-modal="true"` or `showModal()` dialogs is expected and not reported. |
| `blazor-focus-stuck` | Tab did not move focus. In headless Firefox, which keeps focus on the last element, this is recognized as the end of the sequence. |
| `blazor-focus-hidden-target` | Focus landed on an invisible or `aria-hidden` element. |
| `blazor-focus-repeated` | One element was reached twice in a single cycle. |
| `blazor-focus-indicator-not-detected` | Opt-in: no outline or box-shadow while focused. Verify manually, because other styles may indicate focus. |

A positive `tabindex` is reported, not judged. Whether an order is *logical* needs the expectation you write in `ShouldMatch`.

**WebKit** follows Safari's default settings and does not move focus to links with Tab. Write browser-aware expectations or run focus-order tests in Chromium and Firefox.

## ARIA pattern verification

```csharp
(await AriaPatterns.VerifyModalDialogAsync(page.GetByRole(AriaRole.Button, new() { Name = "Delete account" }))).ShouldPass();
(await AriaPatterns.VerifyTabsAsync(page.GetByRole(AriaRole.Tablist))).ShouldPass();
(await AriaPatterns.VerifyDisclosureAsync(page.GetByRole(AriaRole.Button, new() { Name = "Details" }))).ShouldPass();
(await AriaPatterns.VerifyMenuButtonAsync(page.GetByRole(AriaRole.Button, new() { Name = "Actions" }))).ShouldPass();
```

Each verifier drives the widget with real keys and records every APG requirement as a pass or fail check, so one run shows all problems:

```text
The widget does not follow the Menu button pattern.
  [pass] Enter opens the menu
  [FAIL] ArrowUp on the first item wraps to the last
  [FAIL] Escape closes the menu
```

## Live regions

```csharp
var monitor = await LiveRegionMonitor.StartAsync(page);       // start before the interaction
await page.GetByRole(AriaRole.Button, new() { Name = "Search" }).ClickAsync();
await monitor.ExpectUpdateAsync(new Regex(@"^\d+ results found$"), politeness: "polite");
await monitor.ExpectUpdateAsync("Saved", TextMatch.Exact);    // each call consumes one update, so repeated messages are asserted one by one
await monitor.ExpectNoUpdateAsync(TimeSpan.FromMilliseconds(500));
var findings = await monitor.AnalyzeAsync();
```

- Each change record contains the region role, effective politeness, `aria-atomic`, `aria-relevant`, `aria-busy`, region text, added and removed text, whether the region existed before, and whether it was hidden. Empty regions that are rendered are not hidden.
- `ExpectUpdateAsync` matches the text an update adds. For regions that are inserted, or atomic (`aria-atomic="true"`, and `status` and `alert` by default), it also matches the whole region text after the update, because those are presented as a whole. Removals and updates of hidden regions never match, and text that merely stays in a region (for example earlier entries of a `log`) does not match later updates.
- Monitoring survives navigations. On a new page it starts once the document has been parsed, so server-rendered regions are not reported as inserted; changes made while the page is still loading (for example by streaming rendering) are not recorded.
- `AnalyzeAsync` reports patterns that commonly prevent announcements: regions inserted together with their content, updates to hidden regions, and `aria-live` values that contradict `role="alert"` or `role="status"`.

**Limitation:** this is DOM evidence. It does not emulate a screen reader and does not prove that anything was spoken. Speech depends on the assistive technology, browser, timing and user settings. Track real screen reader testing in the [manual review](manual-review.md).

## Accessibility-tree snapshots

```csharp
await page.Locator("nav").ShouldMatchAriaSnapshotAsync("""
    - navigation "Main":
      - list:
        - listitem:
          - link "Home"
    """);                                                     // inline partial template

await component.ShouldMatchStoredAriaSnapshotAsync("tabs-initial");   // stored in __a11y_snapshots__ next to the test file
```

- Snapshots use Playwright's ARIA snapshots: roles, accessible names, states and hierarchy, with no generated ids or styling. Stored files are templates. You can delete irrelevant parts and use regular expressions such as `- status: /\d+ results/`.
- A missing snapshot is written and the test fails, so the new file gets reviewed. On CI (`CI=true`) it is not written.
- A mismatch writes `name.received.aria.yml` and shows a diff. Run with `BLAZOR_A11Y_UPDATE_SNAPSHOTS=1` to accept changes. Updating overwrites manual template edits.
- Snapshots catch semantic regressions. They do not prove accessibility. Their format depends on Playwright, so upgrade it deliberately.

## Interactive component host

`BlazorTestHost` renders a single component in a real browser with Interactive Server, without needing an app:

```csharp
var options = new BlazorTestHostOptions { Culture = new CultureInfo("de-DE") };
options.Stylesheets.Add("_content/MyComponents/MyComponents.bundle.scp.css");
options.ConfigureServices = services => services.AddScoped<ICartService, FakeCartService>();
await using var host = await BlazorTestHost.StartAsync(options);

await using var component = await host.RenderAsync<CartSummary>(browser, p => p
    .Add(c => c.Items, items)                              // any object, render fragment or EventCallback: passed in-process
    .Add(c => c.OnCheckout, EventCallback.Factory.Create(this, () => checkedOut = true))
    .AddCascadingValue(user)
    .AddChildContent("<p>Free shipping</p>"));

await component.AuditAsync("initial");                     // scoped to the component; findings attributed to CartSummary
await component.InvokeAsync(c => c.Refresh());             // runs on the circuit's dispatcher
await component.Keyboard()...RunAsync();
await component.CaptureFocusOrderAsync();
```

- **Requirements:** the test project must use `Microsoft.NET.Sdk.Web`, must be an executable (`<OutputType>Exe</OutputType>`; the default for xUnit v3, and it works for NUnit and MSTest under VSTest), and must either contain a `.razor` file or set `<RequiresAspNetWebAssets>true</RequiresAspNetWebAssets>`. Without these, the host reports exactly what is missing. Static rendering (`HostedRenderMode.Static`) works without them.
- **Isolation:** each `RenderAsync(browser, ...)` uses a new browser context. Services are shared by everything rendered by one host, so register per-test state as scoped (one scope per circuit), or start another host.
- **Errors:** rendering failures throw `HostedComponentException`, including server log errors and browser console errors.
- **Not supported in isolation:** Interactive WebAssembly. Test it end to end.

## End-to-end tests of the app

With .NET 10, `WebApplicationFactory` can run your real app on Kestrel:

```csharp
var factory = new WebApplicationFactory<Program>();
factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));   // a free port chosen by the OS
factory.StartServer();
await session.Page.GotoAsync(new Uri(factory.ClientOptions.BaseAddress, "/settings").ToString());
```

See `demo/BlazorAccessibilityToolkit.Demo.EndToEndTests`.
