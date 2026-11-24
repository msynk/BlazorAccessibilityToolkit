# Browser and interactive testing

Everything here uses real browsers through `BlazorAccessibilityToolkit.Playwright`. Features work on any Playwright `IPage`, whether it shows your running app, a page loaded with `SetContentAsync`, or a component in the [interactive host](#interactive-component-host).

## Browsers and sessions

```csharp
await using var browser = await AccessibilityBrowser.LaunchAsync(new AccessibilityBrowserOptions
{
    Browser = "chromium",       // default: BLAZOR_A11Y_BROWSER or chromium
    ViewportWidth = 1280,       // also: Locale, ReducedMotion (default on), Headless, Channel
    DisplayConditions = DisplayConditions.Dark,   // optional: color scheme, forced colors, contrast for every context
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

### A second rule engine: IBM Equal Access

```csharp
var audit = await page.AuditAccessibilityAsync(new AccessibilityAuditOptions { IbmEqualAccess = true });
```

Each engine finds problems the other misses. With `IbmEqualAccess`, the [IBM Equal Access accessibility checker engine](https://github.com/IBMa/equal-access) runs after axe-core in page and region audits.

- **Findings:**
  - IBM findings have the engine `ibm-equal-access` and IBM's rule ids, such as `aria_navigation_label_unique`. IBM's help pages are linked.
  - Violations count as violations, and potential violations as potential issues.
  - Recommendations count as best practices, and are reported only with `IncludeBestPractices`.
  - Checks that only a person can make are counted in a warning, not listed.
- **Rule set:** it follows `Tags`: WCAG 2.2, 2.1 or 2.0 (A and AA). With `IncludeBestPractices`, the IBM Accessibility rule set is used instead. It also covers EN 301 549 and Section 508 and includes recommendations.
- **Duplicates:** the same problem is not reported twice. When IBM finds a problem on an element that axe-core reports as a violation of the same WCAG success criterion, the axe-core finding records `confirmedBy` (for example `ibm-equal-access:img_alt_valid`) instead. A warning gives the number of such merges.
- **Settings:** `DisabledRules`, `ExcludeSelectors` and `MaxNodesPerRule` apply to IBM rule ids and findings too.
- **Version:** the engine is accessibility-checker-engine 4.0.34 (Apache-2.0), bundled in the package. It runs inside its own scope, so a page that uses the Ace code editor keeps its `window.ace`.
- **Not supported:** bUnit markup audits through the browser engine run axe-core only. The engine does not look into iframes.

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
- `ExpectFocusIndicator()` expects the focused element to look different from its unfocused self (see [focus appearance](#focus-appearance-and-obscured-focus)). `ExpectFocusNotObscured()` expects it not to be hidden behind a sticky header, banner or overlay (WCAG 2.4.11; pass `allowPartial: false` for 2.4.12). `ExpectTitle("Orders")` waits for the document title, for example after a client-side navigation.
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
| `blazor-focus-obscured` | The focused element was entirely covered by other content (sticky header, cookie banner, non-modal overlay), WCAG 2.4.11. On by default (`ReportObscuredFocus`). `FocusStop.Obscuration` also records partial covering (2.4.12). |
| `blazor-focus-indicator-not-detected` | Opt-in: the element looked the same focused and unfocused. Verify manually, because indicators drawn by other elements are not seen. |
| `blazor-focus-not-visible` | With `MeasureFocusAppearance`: no pixel in or around the element changed when it received focus (2.4.7). A violation, measured from screenshots; see [measuring focus indicators](#measuring-focus-indicators). |
| `blazor-focus-appearance` | With `MeasureFocusAppearance` and `ReportFocusAppearance`: the indicator is smaller than a 2 CSS pixel perimeter or changes contrast by less than 3:1 (2.4.13, AAA). |

A positive `tabindex` is reported, not judged. Whether an order is *logical* needs the expectation you write in `ShouldMatch`.

**WebKit** follows Safari's default settings and does not move focus to links with Tab. Write browser-aware expectations or run focus-order tests in Chromium and Firefox.

### Visual order

With `new FocusOrderOptions { ReportVisualOrderMismatch = true }`, the capture reports `blazor-focus-order-visual-mismatch` (2.4.3, 1.3.2) for a stop that leaves the visual reading order and comes back. For example, focus goes from A to C and then to B, while B is between A and C on screen, as CSS `order`, `row-reverse` or a positive `tabindex` cause. Moving up to the next column of a layout is not reported, and right-to-left documents are read from the right. Every `FocusStop` records its `Bounds` in page coordinates.

### Tab-stop overlay

```csharp
var order = await page.CaptureFocusOrderAsync(new FocusOrderOptions { ReportVisualOrderMismatch = true });
var png = await order.SaveTabStopOverlayAsync(page, "artifacts/checkout-tab-stops.png");
```

This saves a full-page screenshot with every stop outlined and numbered, connected in focus order, as Accessibility Insights' tab-stop visualization shows. Stops with findings are drawn in red. Attach the image to test results or the manual review of focus order (2.4.3).

To show it in the HTML report next to the findings, turn the capture into an audit with the image attached:

```csharp
var audit = await order.ToAuditWithTabStopsAsync("checkout", page, "artifacts/checkout-tab-stops.png");
```

Any audit can carry files: `audit.WithAttachment("trace", path, "application/zip")`. The HTML report embeds images up to `HtmlReportFormatter.MaxEmbeddedAttachmentBytes` (5 MB) and links other files.

### Focus appearance and obscured focus

Before tabbing, the capture records the unfocused style of every focusable element: outline, border, box-shadow, background, color, text decoration, font weight, transform, filter and opacity, of the element, its `::before` and `::after`, and its parent (for `:focus-within` styles). A stop counts as indicated when anything visible changed on focus. Properties without a visible effect, such as an `outline-offset` while `outline-style` is `none`, do not count. Elements focused without a recorded baseline fall back to "has an outline or box-shadow".

For each stop the toolkit also asks the browser which element is on top at nine points of the focused element (`elementFromPoint`). When none of them is the element or part of it, focus is *entirely obscured* (`blazor-focus-obscured`). When focus lands on an element outside the viewport (an off-screen skip link that stays off-screen), it is reported as `blazor-focus-hidden-target`. Smooth scrolling is given time to settle before a stop is judged.

### Measuring focus indicators

The style comparison above guesses. Screenshots measure: they show what actually changes on screen when an element receives focus.

```csharp
var order = await page.CaptureFocusOrderAsync(new FocusOrderOptions { MeasureFocusAppearance = true });       // 2.4.7
var aaa = await page.CaptureFocusOrderAsync(new FocusOrderOptions { MeasureFocusAppearance = true, ReportFocusAppearance = true }); // and 2.4.13
var save = await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).MeasureFocusAppearanceAsync();
Console.WriteLine(save);   // for example "704 px² changed, 668 px² with 3:1 contrast, 608 px² needed for 2.4.13"

await KeyboardScenario.For(page).StartFromDocument().PressTab().ExpectVisibleFocus().RunAsync();
```

For each element, the toolkit scrolls it to the middle of the viewport and focuses it once to learn where it is when focused, because a skip link is off screen or clipped to a pixel until then. It then removes focus and takes a screenshot of the element and 8 CSS pixels around it, in both positions. Next it focuses the element as the keyboard would (so `:focus-visible` styles apply), takes a second screenshot and compares the two pixel by pixel. Animations are stopped and the text caret is hidden in both screenshots. The measurement (`FocusAppearanceMeasurement`, also on `FocusStop.Appearance`) reports:

- `ChangedArea`: the CSS pixels whose color changed. When it is 0, focus is not visible (`blazor-focus-not-visible`, WCAG 2.4.7).
- `ContrastingArea`: the changed pixels whose focused and unfocused colors have a contrast ratio of at least 3:1.
- `RequiredArea`: the area of a 2 CSS pixel thick perimeter of the unfocused element, 4 × (width + height). WCAG 2.4.13 (AAA) needs `ContrastingArea` to be at least this large (`blazor-focus-appearance`).
- `HiddenUntilFocused`: the element is not visible until it receives focus, like a skip link. Its focused size is used for `RequiredArea`.
- `UserAgentIndicator`: the browser's own focus ring is drawn (`outline-style: auto`). 2.4.13 exempts indicators the user agent determines, so they are not reported under that rule.

What it does not do:

- An indicator drawn more than 8 CSS pixels away from the element is not seen. `FocusAppearanceOptions.Margin` widens the area.
- The measurement focuses each element twice more after the tab sequence is captured, so it runs `onfocus` and `onblur` handlers again. An element that stays off screen when focused is not measured; the capture reports it as `blazor-focus-hidden-target`.
- The comparison treats a pixel that is covered in both screenshots as unchanged. Combine it with `blazor-focus-obscured` for indicators hidden behind sticky content.
- 1.4.11 contrast of the indicator against adjacent colors is not measured; 2.4.13 compares each pixel's focused and unfocused colors, as the success criterion defines it.

## Adaptation: reflow, text spacing and display preferences

```csharp
var reflow = await page.AuditReflowAsync();                         // 320 x 256 CSS px, then the viewport is restored
var spacing = await page.AuditTextSpacingAsync();                   // WCAG 1.4.12 spacing applied, then removed
var dark = await page.AuditUnderAsync(DisplayConditions.Dark);      // full audit labelled "dark"
var forced = await page.AuditUnderAsync(DisplayConditions.ForcedColorsActive);   // Windows high contrast (Chromium)
await page.EmulateDisplayConditionsAsync(DisplayConditions.MoreContrast);         // for your own scenarios
var motion = await page.AuditMotionAsync();                         // prefers-reduced-motion: reduce, then restored
var orientation = await page.AuditOrientationAsync();               // portrait and landscape, then the viewport is restored
var targets = await page.AuditTargetSizeAsync();                    // 44 x 44 CSS px (2.5.5); new TargetSizeOptions { Minimum = 48 } for Material
var apca = await page.AuditApcaContrastAsync();                     // APCA lightness contrast, as information (not WCAG 2)
```

| Rule | WCAG | What it reports |
| --- | --- | --- |
| `blazor-reflow-horizontal-scroll` | 1.4.10 | At 320 CSS pixels the page scrolls horizontally, and this element is where the overflow starts. Content inside tables, grids, toolbars, `pre`, `svg`, `canvas`, video, maps and `[data-a11y-allow-horizontal-scroll]` is exempt (a warning lists it); `ReflowOptions.IncludeTwoDimensionalContent` reports it too. `AuditAtWidth = true` also runs axe-core at that width. |
| `blazor-text-spacing-clipped` | 1.4.12 | With line height 1.5, paragraph spacing 2, letter spacing 0.12 and word spacing 0.16 times the font size, the element's text overflows a container that hides overflow (or line clamping). Elements that clipped before the change are not reported. |
| `blazor-motion-not-reduced` | 2.2.2, 2.3.3 | With `prefers-reduced-motion: reduce`, an animation still moves content (transform, position, size, background position) or runs longer than five seconds, or media keeps playing for longer than five seconds. Loading indicators (`role="progressbar"`, `role="status"`, `aria-busy`, spinner and loader classes) are not reported. |
| `blazor-target-size-enhanced` | 2.5.5 | A pointer target is smaller than `TargetSizeOptions.Minimum` (44 CSS pixels) in either dimension: the AAA criterion and the iOS, Android and Flutter touch guidelines. Inline links in text are exempt, and a label that wraps a checkbox or radio button counts as its target. axe-core already checks the 24 pixel minimum (2.5.8). |
| `blazor-apca-contrast` | none (best practice) | Text whose APCA lightness contrast is below the level its font size and weight call for; see [APCA contrast](#apca-contrast). |

### APCA contrast

APCA (the Accessible Perceptual Contrast Algorithm) is the contrast method proposed for WCAG 3. It models how people perceive lightness contrast better than the WCAG 2 ratio does, especially for thin text and dark themes. It is not a WCAG 2 requirement: the WCAG 2 ratio, which axe-core's `color-contrast` checks, remains the conformance test. `AuditApcaContrastAsync` adds APCA as information next to it:

```csharp
var apca = await page.AuditApcaContrastAsync();
var content = await page.AuditApcaContrastAsync(new ApcaContrastOptions { MinimumLc = 60 });   // one level for all text
double lc = Apca.Contrast("#777777", "#ffffff");                                              // 71.1: dark text on light
int needed = Apca.RequiredLc(fontSizePx: 16, fontWeight: 400, out var tooSmall);              // 90
```

- The text and background colors come from axe-core's `color-contrast` rule, for every text element it measures, passing or failing. Text that axe-core cannot measure (on background images or gradients, or under other elements) is skipped, and a warning gives the number.
- The contrast is computed with APCA 0.0.98G-4g, as apca-w3 0.1.9 implements it. The port reproduces the published apca-w3 test values exactly. Lc is positive for dark text on a light background and negative for light text on a dark background.
- The required level comes from the font size and weight, following [APCA in a Nutshell](https://github.com/Myndex/SAPC-APCA/blob/master/documentation/APCA_in_a_Nutshell.md). The text needs the lowest of Lc 45, 60, 75 and 90 whose minimum font size it reaches: for example 36 px at weight 400 needs Lc 45, 24 px Lc 60, 18 px Lc 75, and 16 px Lc 90. Text smaller than 14 px at weight 400 (18 px at 300) is below every level, and the finding says so. `MinimumLc` sets one level for all text instead, for example 60 for content text that is not body text.
- Findings are best-practice potential issues with no WCAG reference, so the default quality gate does not fail on them unless you fail on potential issues.

**License.** APCA's reference implementation, apca-w3, is not open source. Its [license](https://github.com/Myndex/apca-w3/blob/master/LICENSE.md) permits use for predicting the contrast of web content and requires a license from Myndex Research for commercial use. The toolkit's MIT license does not cover APCA: check that your use is permitted before you rely on these results.
| `blazor-orientation-locked` | 1.3.4 | In portrait or landscape (390 x 844 CSS pixels by default), the page is rotated by 90 degrees, a "rotate your device" message appears, or less than a quarter of the text of the other orientation is shown. |

These are potential issues: they show where content overflows, which usually means lost content, but whether content is really lost or exempt needs a person. Display conditions matter because contrast, `box-shadow` focus rings and background-image icons often fail only in dark mode or with forced colors. Forced colors emulation is supported by Chromium only.

## ARIA pattern verification

```csharp
(await AriaPatterns.VerifyModalDialogAsync(page.GetByRole(AriaRole.Button, new() { Name = "Delete account" }))).ShouldPass();
(await AriaPatterns.VerifyTabsAsync(page.GetByRole(AriaRole.Tablist))).ShouldPass();
(await AriaPatterns.VerifyDisclosureAsync(page.GetByRole(AriaRole.Button, new() { Name = "Details" }))).ShouldPass();
(await AriaPatterns.VerifyMenuButtonAsync(page.GetByRole(AriaRole.Button, new() { Name = "Actions" }))).ShouldPass();
```

More verifiers cover composite widgets and form controls:

| Verifier | What it checks |
| --- | --- |
| `VerifyRadioGroupAsync(group)` | Named `radiogroup` (or `fieldset`), at most one checked radio, one tab stop, arrow keys move and check (wrapping for custom widgets), Tab leaves the group. Works with native radios (`InputRadioGroup`) and `role="radio"`. |
| `VerifyToolbarAsync(toolbar)` | `role="toolbar"`, a name when the page has several toolbars, one tab stop, arrow keys (vertical toolbars: up and down), Home and End, Tab leaves the toolbar. |
| `VerifyListboxAsync(listbox)` | Named listbox, one tab stop (focus on the listbox with `aria-activedescendant`, or roving focus on options), arrow keys, Home and End, selection follows focus or Space selects. |
| `VerifyComboboxAsync(input)` | Named combobox with `aria-expanded` and `aria-controls`; ArrowDown opens the listbox, options become active through `aria-activedescendant` while DOM focus stays in the input, Enter accepts, Escape closes. |
| `VerifySliderAsync(slider)` | Named slider (`role="slider"` or `input type="range"`) with current, minimum and maximum value; arrow keys change the value, Home and End set the limits. |
| `VerifyCheckboxAsync(control)` | Checkbox or switch: named, exposes its checked state, in the tab sequence, Space toggles both ways. |
| `VerifyTooltipAsync(trigger)` | Tooltip pattern and WCAG 1.4.13: appears on focus and hover, is referenced by `aria-describedby`, Escape dismisses it without moving focus, it stays while the pointer moves onto it and does not disappear on its own. |
| `VerifyAccordionAsync(container)` | Every header is a button inside a heading and behaves as a disclosure. |
| `VerifySortableTableAsync(table, column?)` | Sortable column headers contain a button; at most one header has `aria-sort`; Enter sorts, sets the header's `aria-sort` to ascending or descending and clears the others; Enter again reverses it; focus stays on the button; the rows are in the announced order. QuickGrid's sortable columns pass. |
| `VerifyPaginationAsync(next, content)` | The next-page control is a named button; Enter shows the next page; the change is conveyed by a live region or a focus move; focus is never lost, including on the last page. QuickGrid's `Paginator` fails the last two: it announces nothing, and its focused "next" button becomes `disabled` on the last page, which drops focus to the body. Add a `role="status"` element with the page number next to it, and keep focus on a control that stays enabled. |
| `VerifyLoadingStateAsync(page, url, regionSelector, loaded)` | For `[StreamRendering]` placeholders and content loaded in `OnInitializedAsync` (4.1.3): while loading, the region is `aria-busy="true"` or a status message (live region or progress bar) says so; once `loaded` appears, `aria-busy` is cleared and a status message reports the result. It loads the page itself, to observe the loading phase from the start. A bare "Loading…" placeholder fails. |
| `VerifyFormValidationAsync(form, submit?)` | Submitting a form that has errors with the keyboard marks each invalid field with `aria-invalid`, links each one to its error text (`aria-describedby` or `aria-errormessage`), moves focus to the first invalid field or to an error summary, and makes the errors perceivable (that focus move, or a live region that contains them). Fill the form with invalid values first. Browser constraint validation is accepted. Default `EditForm` markup fails: `ValidationMessage` is not linked to its field and focus stays on the submit button. |

Each verifier drives the widget with real keys and records every APG requirement as a pass or fail check, so one run shows all problems:

```text
The widget does not follow the Menu button pattern.
  [pass] Enter opens the menu
  [FAIL] ArrowUp on the first item wraps to the last
  [FAIL] Escape closes the menu
```

## Page changes in Blazor apps

Blazor's router and enhanced navigation replace page content without loading a new document, so browsers do not announce the new page. The app has to update the title (`<PageTitle>`) and move focus to the new content (`<FocusOnNavigate RouteData="routeData" Selector="h1" />`) or announce the change.

```csharp
(await NavigationChecks.VerifyNavigationAsync(page.GetByRole(AriaRole.Link, new() { Name = "Orders" }))).ShouldPass();
```

It presses Enter on the link and checks that the URL changes, the new page has a non-empty title that differs from the previous one (2.4.2) and a language (3.1.1), and that after a client-side navigation focus moved into the new content (`main` or the main heading) or a live region announced the change, instead of staying on the link or falling back to the document body (2.4.3, 4.1.3). A full document load passes the focus check, because browsers announce new documents.

## Every page of the app

`BlazorRoutes.Discover` finds the routable components (`@page`, `[Route]`) of your app by reflection, so a new page cannot be left out of the audits, as pa11y-ci does with a sitemap:

```csharp
var routes = BlazorRoutes.Discover([typeof(Program).Assembly], new RouteDiscoveryOptions
{
    Exclude = ["/admin/*", "/error"],
    ParameterValues = new Dictionary<string, string> { ["id"] = "42", ["/docs/{*path}"] = "/docs/install" },
});

var result = await page.AuditRoutesAsync(baseAddress, routes, options,
    beforeAudit: (p, route) => p.Locator("main[data-interactive=true]").WaitForAsync());
result.ShouldHaveNoViolations();     // lists every failing route
Assert.Empty(result.Skipped);         // routes whose required parameters have no value
```

Consistent help (WCAG 3.2.6) is checked across the same pages: help links and buttons (names matching help, contact, support, FAQ or chat), `mailto:` and `tel:` links must keep the same relative order and landmark on every page that has them:

```csharp
var help = await page.AuditConsistentHelpAsync(baseAddress, routes.Where(r => r.Url is not null).Select(r => r.Url!),
    new ConsistentHelpOptions { Selector = "#chat-launcher", BeforeAudit = p => p.Locator("main[data-interactive=true]").WaitForAsync() });
help.ShouldHaveNoViolations(AccessibilityExpectation.Strict);   // blazor-help-inconsistent is a potential issue
```

Optional (`{tab?}`) and catch-all (`{*path}`) parameters are left out when they have no value; routes with a required parameter that has no value are returned in `Skipped` instead of being dropped silently. Each audit is labelled with its route template.

## Virtual screen-reader transcripts

`GetScreenReaderTranscriptAsync()` (page or locator) and `ShouldMatchTranscriptAsync()` read the accessibility tree computed in the browser, one item per line, in the same format as the [bUnit transcript](component-testing.md#virtual-screen-reader-transcripts):

```csharp
await page.Locator("form").ShouldMatchTranscriptAsync("""
    form, Sign up
    textbox, Email, required, invalid, Enter your email address
    button, Create account
    """);
```

Unlike [ARIA snapshots](#accessibility-tree-snapshots), transcripts keep "false" states (`not pressed`, `collapsed`, `not checked`), `aria-current`, required fields and descriptions. They are a regression record of semantics, not what a particular screen reader says; test with real assistive technologies for conformance.

## Focus while a prerendered page becomes interactive

```csharp
// Hold back blazor.web.js briefly, so that there is time to tab into the prerendered page.
await page.RouteAsync("**/_framework/blazor.web.js*", async route => { await Task.Delay(500); await route.ContinueAsync(); });

var result = await NavigationChecks.VerifyFocusSurvivesInteractivityAsync(
    page,
    baseUrl + "orders",
    p => p.GetByRole(AriaRole.Link, new() { Name = "Account" }),
    async p => await p.Locator("[data-interactive=true]").CountAsync() > 0);   // your layout's interactivity marker
result.ShouldPass();
```

It focuses the target while the page is prerendered, waits until the page is interactive, and checks that focus is still on that element (or on its replacement with the same role and name). With Blazor's `<FocusOnNavigate>`, focus moves to the heading, because Blazor replaces the prerendered DOM and the router's first interactive render moves focus. The [`NavigationFocus`](runtime-components.md#navigationfocus) component fixes this.

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
