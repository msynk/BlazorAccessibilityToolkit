# Changelog

## Unreleased

### Added

- **Blazor Hybrid apps on Windows.** `BlazorHybridApp.LaunchAsync(exe)` starts a WPF, Windows Forms or .NET MAUI app with WebView2 remote debugging turned on. It waits for the BlazorWebView and connects Playwright to it, so the app's `Page` works with every audit, keyboard scenario and focus check. Each run gets its own WebView2 user data folder. `ConnectAsync(endpoint)` connects to an app that is already running. A Windows Forms test app in `tests/Hybrid` is audited on a new `windows-latest` CI job. The native window around the web view is not covered. See [Blazor Hybrid apps](docs/blazor-hybrid.md).
- **IBM Equal Access as a second rule engine** next to axe-core. Turn it on with `AccessibilityAuditOptions.IbmEqualAccess`.
  - The bundled accessibility-checker-engine 4.0.34 (Apache-2.0) runs after axe-core in page and region audits.
  - Its rule set follows the audit's WCAG version, or uses the IBM Accessibility rule set with `IncludeBestPractices`.
  - Its findings use IBM's rule ids and help pages; for example, IBM reports navigation landmarks without unique labels as a 1.3.1 violation.
  - A problem both engines find on the same element and success criterion is reported once, by axe-core, with `confirmedBy` naming the IBM rule. A gate never fails twice for it.
  - `DisabledRules`, `ExcludeSelectors` and `MaxNodesPerRule` apply to it, and it leaves a page's own `window.ace` (the Ace code editor) alone.
  - The demo's pages pass both engines. See [A second rule engine](docs/interactive-testing.md#a-second-rule-engine-ibm-equal-access).
- **APCA contrast, as information next to WCAG 2.** APCA is the contrast method proposed for WCAG 3.
  - `Apca.Contrast(text, background)` computes the lightness contrast (Lc) with APCA 0.0.98G-4g. It is a port of apca-w3 0.1.9 that reproduces its published test values exactly.
  - `Apca.RequiredLc(size, weight)` gives the level that the APCA guidance asks for at a font size and weight.
  - `page.AuditApcaContrastAsync()` reports text below that level as `blazor-apca-contrast`, a best-practice potential issue with no WCAG reference. It reuses the colors that axe-core's `color-contrast` rule measures for every text element.
  - APCA's license differs from the toolkit's: it requires a license from Myndex Research for commercial use. See [APCA contrast](docs/interactive-testing.md#apca-contrast).
- **Focus indicators measured from screenshots** (WCAG 2.4.7 and 2.4.13). The toolkit screenshots an element unfocused, focuses it as the keyboard would and screenshots it again. It then counts the pixels that changed and those whose color changed by a contrast of at least 3:1. The images are decoded in the browser, so there is no new dependency.
  - `locator.MeasureFocusAppearanceAsync()` returns a `FocusAppearanceMeasurement` with `ChangedArea`, `ContrastingArea`, `RequiredArea` (a 2 CSS pixel perimeter) and `UserAgentIndicator`.
  - `FocusOrderOptions.MeasureFocusAppearance` measures every tab stop (`FocusStop.Appearance`). It reports `blazor-focus-not-visible` (2.4.7, a violation) when nothing changes on screen, and with `ReportFocusAppearance` also `blazor-focus-appearance` (2.4.13, AAA). The browser's own focus ring is exempt from 2.4.13, as the success criterion says.
  - `KeyboardScenario.ExpectVisibleFocus(focusAppearance)` asserts the same for the focused element.
  - Tested in Chromium, Firefox and WebKit. See [Measuring focus indicators](docs/interactive-testing.md#measuring-focus-indicators).
- **ACT rules implementation report** for the Razor analyzer. It runs 15 analyzer rules on the test cases of 18 W3C ACT Rules (285 pages, snapshot in `tests/BlazorAccessibilityToolkit.Analyzers.Tests/ActRules`). The results are published as [docs/act-rules.md](docs/act-rules.md) and as an EARL report in the ACT Task Force's format (`docs/act-rules/razor-analyzer.earl.json`).
  - 16 of the 18 rules are consistent. One finds 10 of its 11 failed examples (the 11th is reported by another analyzer rule), and one raises a false alarm on a page that relies on a script. Each remaining difference is explained in the report.
  - A test fails when an outcome changes, until the report is regenerated.
  - The first run found 27 false alarms and 31 missed failures; the analyzer fixes are listed under Fixed.
- **MCP server for coding agents:** `blazor-a11y mcp` runs a Model Context Protocol server over standard input and output, built on the official C# SDK (`ModelContextProtocol.Core`). It has five tools:
  - `check_razor` runs the Razor analyzer on files, folders or a markup snippet;
  - `fix_razor` applies the safe fixes;
  - `get_results` reads the test results, with the gate verdict, baseline comparison, filters and paging;
  - `explain_rule` explains analyzer, toolkit and axe-core rules and WCAG success criteria;
  - `list_rules` lists the rules.

  Tools return JSON with output schemas and are annotated as read-only, except `fix_razor`. The server only uses files inside its root folders (the working directory, or `--root`). The CLI package is now also an MCP server package on NuGet.org (`.mcp/server.json`). See [MCP server](docs/mcp-server.md).
- **Development overlay** in the new `BlazorAccessibilityToolkit.DevTools` package, like `@axe-core/react` and `vue-axe`. `<AccessibilityDevOverlay />` in `App.razor` and `app.MapAccessibilityDevTools()` run axe-core in the running app whenever the page has not changed for a second. A button shows the number of failing elements and opens a keyboard-accessible panel. The panel lists the violations by rule, with impact, WCAG criteria and help links, and can scroll to and outline each element. New violations are also logged to the browser console. The overlay renders only in the Development environment and stays off under browser automation, so it never runs at the same time as a test's audit. See [Development overlay](docs/dev-overlay.md).
- **Code fixes for the Razor analyzer.** Findings of `BA11Y003`, `BA11Y004`, `BA11Y023`, `BA11Y028`, `BA11Y029`, `BA11Y033`, `BA11Y035` and `BA11Y038` that have one safe edit carry it. Examples: `aria-labeledby` to `aria-labelledby`, `<button role="button">` to `<button>`, `en_US` to `en-US`, `aria-expanded="@open"` to `"@(open ? "true" : "false")"`, and `href="#list"` to `href="/orders#list"`. The fixes are offered in the IDE, including "fix all", where the IDE supports code fixes in additional files. They also run from the command line with the new **`blazor-a11y fix`** command, with `--rule` and `--dry-run`, because `dotnet format` only fixes C# files. A fix is skipped when the attribute changed after it was analyzed. The command follows the analyzer's `.editorconfig` settings and keeps the file's encoding and byte order mark. It skips files that would not be written back byte for byte, and does not follow symbolic links or junctions. See [Razor analyzers](docs/analyzers.md#fixes).

The first group of entries closes the gaps found by the second cross-stack survey ([ADR 0003](docs/adr/0003-second-survey.md)).

- **25 more analyzer rules, `BA11Y014` to `BA11Y038`.**
  - Rules modeled on jsx-a11y, vue and angular-eslint:
    - autofocus, accesskey, `<marquee>`/`<blink>`, video without captions, invalid `scope`,
    - invalid `autocomplete` tokens, `aria-activedescendant` on unfocusable elements,
    - roles without their required ARIA attributes, invalid ARIA attribute values, redundant roles,
    - non-interactive elements in the tab order, hover handlers without focus equivalents, nested interactive controls,
    - ambiguous link text, invalid BCP 47 `lang`, viewport tags that disable zoom, timed meta refresh,
    - redundant alt words, and presentational roles on focusable elements.
  - Blazor-only rules:
    - a routable page without `<PageTitle>` (`BA11Y031`),
    - a `<Router>` without `<FocusOnNavigate>` (`BA11Y032`),
    - paste blocked with `@onpaste:preventDefault` or password managers turned away (`BA11Y033`),
    - drag interactions to review (`BA11Y034`),
    - **ARIA states bound to a bool** (`BA11Y035`). Blazor drops attributes whose value is `false`, so `aria-expanded="@open"` never says "collapsed".
  - `BA11Y002` now also covers `<input type="image">`, `<area>` and `<svg role="img">`, and `BA11Y010` covers an empty `lang`.
  - Informational rules report suggestions, not warnings.
- **Component mapping in the analyzer:** `blazor_a11y_analyzer.component_map = MudIconButton:button, MudImage:img, ...` checks library components as the native element they render. `blazor_a11y_analyzer.component_label_attributes` names the parameters that label them.
- **ARIA 1.3:** the roles `comment`, `suggestion`, `sectionheader` and `sectionfooter`, and the attribute `aria-actions`, are known to the analyzer and the markup rules.
- **Role queries and accessible-name assertions for bUnit**, modeled on Testing Library and jest-dom:
  - Queries: `FindByRole` (filters for name, description and states such as checked, pressed, expanded, selected, disabled, busy, level and current, plus hidden elements and fallback roles), `FindAllByRole`, `QueryByRole`, `FindByLabelText` and `DescribeRoles`.
  - Helpers: `GetAccessibleName`, `GetAccessibleDescription`, `GetAccessibleErrorMessage`, `GetRole` and `GetAccessibleState`.
  - Assertions: `ShouldHaveAccessibleName`, `ShouldHaveAccessibleDescription`, `ShouldHaveAccessibleErrorMessage`, `ShouldHaveRole`, `ShouldBeInvalid`, `ShouldBeRequired`, `ShouldBeDisabled`, `ShouldBeChecked`, `ShouldBePressed` and `ShouldBeExpanded`.
  - It is built on a C# implementation of WAI-ARIA and HTML-AAM roles and accname 1.2.
- **Virtual screen-reader transcripts:**
  - For bUnit markup: `cut.GetAccessibilityTree()`, `cut.GetScreenReaderTranscript()` and `cut.ShouldMatchTranscript()`.
  - In the browser: `GetAccessibilityTreeAsync()`, `GetScreenReaderTranscriptAsync()` and `ShouldMatchTranscriptAsync()`.
  - Both use one format, `ScreenReaderTranscript` in the core package. Lines look like `heading, Settings, level 2` or `button, Bold, not pressed`, and include the states and descriptions that ARIA snapshots omit.
- **`AriaPatterns.VerifyFormValidationAsync`**: after a keyboard submit with errors, it checks that invalid fields are marked, linked to their error text, focused or summarized, and announced. Default `EditForm` markup fails it, and the demo's `SignupForm` passes.
- **WCAG 2.2 heuristics** in bUnit and browser audits:
  - `blazor-paste-blocked` (3.3.8): `@onpaste:preventDefault` seen by bUnit, or a cancelled paste on credential fields in the browser, and `autocomplete="off"` on passwords.
  - `blazor-dragging-alternative` (2.5.7).
  - `blazor-redundant-entry` (3.3.7): confirmation fields such as `ConfirmEmail`.
- **Adaptation audits:**
  - `page.AuditMotionAsync()` (`blazor-motion-not-reduced`, 2.2.2 and 2.3.3) reports animations and media that keep running with `prefers-reduced-motion: reduce`.
  - `page.AuditOrientationAsync()` (`blazor-orientation-locked`, 1.3.4) reports pages rotated, hidden or replaced by a "rotate your device" message in one orientation.
- **Route discovery:** `BlazorRoutes.Discover(assemblies, options)` finds every `@page` and `[Route]` component, with parameter values and exclusions. `page.AuditRoutesAsync(baseAddress, routes)` audits each page and lists routes it skipped, rather than dropping them silently.
- **Conformance drafts:** the `openacr` report format writes an OpenACR YAML file (catalog `2.5-edition-wcag-2.2-en`, the VPAT 2.5 WCAG edition). The `statement` format writes an accessibility statement draft as required by the European Accessibility Act. The CLI adds `--product`, `--product-version`, `--organization`, `--contact`, `--contact-email` and `--level`. Both formats are written only on request and never claim support without a recorded manual review.
- **Runtime components** (new package `BlazorAccessibilityToolkit.Components`, see [docs/runtime-components.md](docs/runtime-components.md)):
  - `ILiveAnnouncer` with a `<LiveAnnouncer />` host. Its regions exist before the first message, repeated messages are announced again, messages are cleared after a while, and `AnnounceAsync` reports when no host is rendered.
  - `<SkipLink>`: it renders the current path with the fragment, so it works with `<base href="/">` before and after the page becomes interactive, and it moves focus to the target.
  - `<FocusTrap>` with an initial focus, nesting and focus restoration, and `<VisuallyHidden>`.
  - `<RouteAnnouncer>` for client-side navigation: router navigation when interactive, enhanced navigation (`enhancedload`) when statically rendered.
  - `<RovingFocusGroup>`: one tab stop, arrow keys, Home and End, optional type-to-search, and optional selection that follows focus, for toolbars, tabs, radio groups, listboxes and menus.
  - `<ActiveDescendantGroup>`: `aria-activedescendant` navigation for listboxes and comboboxes that keep focus on the input, with Enter to activate and type-to-search.
  - `<NavigationFocus>`: a replacement for `<FocusOnNavigate>`. When a prerendered page becomes interactive, Blazor replaces the prerendered DOM and `FocusOnNavigate` moves focus to the heading, so a keyboard user who already tabbed into the page loses their place. `NavigationFocus` restores focus to the same element instead.
- **Audit attachments:** `AccessibilityAudit.Attachments` and `WithAttachment(name, path, contentType, description)`. The HTML report embeds image attachments (with their description as `alt` text) and links other files. `focusOrder.ToAuditWithTabStopsAsync(name, page, path)` attaches the tab-stop overlay.
- **Standards:** `AccessibilityStandard.Rgaa` (RGAA 4, France) and `AccessibilityStandard.TrustedTester` (Trusted Tester v5, the Section 508 test process) select the rules that axe-core 4.14 maps to them. Findings carry the RGAA criteria and Trusted Tester test ids as tags.
- **Loading states:** `AriaPatterns.VerifyLoadingStateAsync` checks `[StreamRendering]` placeholders and other loading content. While loading, the region must be `aria-busy` or a status message must say so; afterwards `aria-busy` is cleared and the result is announced.
- **Tables and pagination:** `AriaPatterns.VerifySortableTableAsync` (`aria-sort`, keyboard sorting, focus, row order; QuickGrid passes) and `AriaPatterns.VerifyPaginationAsync`. The latter shows that QuickGrid's `Paginator` neither announces page changes nor keeps focus on the last page, where its focused "next" button becomes disabled.
- **Focus during prerendering:** `NavigationChecks.VerifyFocusSurvivesInteractivityAsync` checks that focus a user places in a prerendered page survives the page becoming interactive.
- **Tab-stop overlay:** `focusOrder.SaveTabStopOverlayAsync(page, path)` saves a screenshot with every tab stop numbered and connected, problems in red.
- **Visual focus order:** `FocusOrderOptions.ReportVisualOrderMismatch` reports `blazor-focus-order-visual-mismatch` (2.4.3, 1.3.2) for a stop that leaves the visual reading order, without flagging column layouts. `FocusStop.Bounds` and `FocusSnapshot.Bounds` record where each stop is.
- **Render-mode checks:** `blazor-virtualized-set-size` reports `<Virtualize>` items that do not expose `aria-posinset` and `aria-setsize`, so screen readers count only the rendered items. `blazor-duplicate-clickable-bounds` (best practice) reports nested interactive elements with the same box.
- **Consistent help (3.2.6):** `page.AuditConsistentHelpAsync(baseAddress, paths)` (`blazor-help-inconsistent`) compares the order and landmark of help links, `mailto:` and `tel:` links across pages.
- **Heuristics from mobile platforms and Blazor's runtime:**
  - `blazor-ambiguous-link-name` (2.4.4): links with the same name but different destinations.
  - `blazor-role-in-name` (best practice): "Submit button", "Link to ...".
  - `blazor-reconnect-ui-not-accessible`: the .NET reconnect dialog without a name or a live region.
  - `page.AuditTargetSizeAsync()` (`blazor-target-size-enhanced`, 2.5.5): targets smaller than 44 CSS pixels, an opt-in profile stricter than axe-core's 24 pixel check.
- **In-page links that leave the page:** analyzer rule `BA11Y038` and browser rule `blazor-fragment-link-leaves-page`. With Blazor's `<base href="/">`, `<a href="#main">` opens the home page from every other page.
- **Razor analyzers** (new package `BlazorAccessibilityToolkit.Analyzers`): compile-time checks of `.razor` and `.cshtml` markup with the Razor file and line, `BA11Y001` to `BA11Y013`: clickable elements without keyboard support, images without `alt`, positive `tabindex`, unknown ARIA attributes (with typo suggestions) and roles, focusable content inside `aria-hidden`, unlabeled fields (including Blazor's `Input*` components), unnamed buttons and links, empty headings, `<html>` without `lang`, untitled frames, links used as buttons, and interactive roles that cannot receive focus. Configurable with `.editorconfig` (`blazor_a11y_analyzer.enabled`, severities) and ignorable per element or file with `@* blazor-a11y-ignore ... *@`. See [docs/analyzers.md](docs/analyzers.md).
- **New diagnostics** (browser and browserless bUnit audits):
  - `blazor-navlink-missing-aria-current`: an active navigation link without `aria-current`.
  - `blazor-input-purpose-missing` (WCAG 1.3.5): fields named or typed like personal data without `autocomplete`, with the suggested token.
  - `blazor-required-not-exposed`: a label marks the field as required, but the control is not `required` or `aria-required`.
  - `blazor-error-ui-not-accessible` (browser): `#blazor-error-ui` is not a live region or its dismiss control is not keyboard accessible.
- **Focus:**
  - `blazor-focus-obscured` (WCAG 2.4.11): focus-order capture reports stops that are entirely covered by sticky headers, banners or overlays (`FocusOrderOptions.ReportObscuredFocus`, on by default). `FocusSnapshot.Obscuration`, `FocusSnapshot.ObscuredBy` and `FocusStop.Obscuration` record full and partial covering; focus outside the viewport is reported as `blazor-focus-hidden-target`.
  - Focus indicators are now detected as a visible style change between the unfocused and focused element (outline, border, box-shadow, background, color, text decoration, transform, filter, opacity; including `::before`, `::after` and the parent for `:focus-within`), instead of only outlines and box-shadows.
  - `KeyboardScenario.ExpectFocusIndicator()`, `ExpectFocusNotObscured()` and `ExpectTitle()`.
- **Adaptation audits:** `page.AuditReflowAsync()` (`blazor-reflow-horizontal-scroll`, WCAG 1.4.10, with the two-dimensional content exception), `page.AuditTextSpacingAsync()` (`blazor-text-spacing-clipped`, WCAG 1.4.12), `page.AuditUnderAsync(DisplayConditions)` and `EmulateDisplayConditionsAsync` for dark mode, forced colors and increased contrast, and `AccessibilityBrowserOptions.DisplayConditions` for every context.
- **ARIA pattern verifiers:** `VerifyRadioGroupAsync`, `VerifyToolbarAsync`, `VerifyListboxAsync`, `VerifyComboboxAsync`, `VerifySliderAsync`, `VerifyCheckboxAsync` (checkboxes and switches), `VerifyTooltipAsync` (including WCAG 1.4.13 dismissible, hoverable and persistent) and `VerifyAccordionAsync`.
- `NavigationChecks.VerifyNavigationAsync`: after a client-side navigation, the title changes, the language is set, and focus moves to the new content or the change is announced.
- **Standards:** `AccessibilityStandard.Wcag22A`, `Wcag22AAA`, `En301549` and `Section508` presets, and `WcagReference.En301549Clause`.
- **Reporting:** `WcagCoverage` rolls findings and manual checks up to WCAG 2.2 criteria (Does not support, Supports (manually verified), Not applicable, Needs review, Not evaluated) with EN 301 549 clauses. Automated results alone never mark a criterion as supported. The HTML report shows the table; the new `markdown` format (`MarkdownReportFormatter`) shows it with totals, baseline changes and failing audits for pull requests.
- **Manual review:** `ManualReviewTemplates.Wcag22AA` with one item per WCAG 2.2 A and AA criterion; the standard template now also covers accessible authentication, redundant entry, consistent help and text alternatives.
- **CLI:** `report --step-summary` appends the Markdown report to the GitHub Actions job summary, `manual init --template standard|wcag22aa`, and `blazor-a11y rules` lists the toolkit's rules.
- `AccessibilityAudit.ExpiredSuppressions` (JSON field `expiredSuppressions`) lists suppressions that had expired. Strict quality gates now fail on this list instead of on warning text that contains "expired".

### Fixed

- Analyzer rules checked against the W3C ACT Rules test cases ([ACT rules implementation report](docs/act-rules.md)). Fewer false alarms:
  - Content hidden with an inline `style` (`display: none`, `visibility: hidden`) is treated as hidden, like the `hidden` attribute (`BA11Y002`, `BA11Y006`, `BA11Y007`, `BA11Y008`, `BA11Y011`, `BA11Y019`, `BA11Y021`).
  - A link or button is named by an image with `aria-label`, `aria-labelledby` or `title`, not only `alt` (`BA11Y008`).
  - `BA11Y019` skips disabled fields. `BA11Y011` skips frames with `tabindex="-1"` or a presentational role, and `BA11Y007` a disabled field with one.
  - `BA11Y028` checks the primary language subtag against the IANA Language Subtag Registry, which is what assistive technologies use, so `en-US-GB` is accepted. On elements other than `<html>` it reports only a `lang` that applies to some content.
  - `BA11Y029` ignores a negative `maximum-scale`. `BA11Y030` reports only the first refresh tag, as browsers use only that one.
- More failures found:
  - `BA11Y002`: `alt=" "`, an image button with `alt=""`, and elements with `role="img"` but no name.
  - `BA11Y007`: elements with a form field role (`textbox`, `combobox`, `slider` ...) and no `aria-label` or `aria-labelledby`.
  - `BA11Y008`: `role="none"` on a focusable button or link, which browsers ignore, and DPUB link roles such as `doc-biblioref`.
  - `BA11Y009`: empty `role="heading"`, and headings whose only content is an image with `role="presentation"`.
  - `BA11Y011`: a blank `title`.
  - `BA11Y021`: `role="heading"` without `aria-level`, which WAI-ARIA 1.2 requires; the documentation said the opposite. Also a focusable `separator` without `aria-valuenow`, and `<input role="combobox">` without `aria-expanded`.
  - `BA11Y028`: `eng` (English is `en`), `i-lux`, a `lang` of only spaces, and a `lang` that applies to aria-hidden text.
  - `BA11Y029`: `maximum-scale=yes`, `user-scalable=0.5` and values that are not numbers, read as browsers read them.
  - `BA11Y030`: a delay of exactly 20 hours. Refresh tags whose content browsers cannot read (`+5`, `0: url`) are skipped.
  - `aria-labelledby` that references only empty elements in the same file no longer counts as a name.
- These changes bring new warnings to some existing markup, all of which assistive technologies do get wrong:
  - an unnamed `<ul role="listbox">` (a common autocomplete popup), the ARIA 1.1 combobox wrapper `<div role="combobox"><input aria-label="City"></div>`, and `<label>Name <div role="textbox"></div></label>` (`BA11Y007`);
  - `<div role="heading">` without `aria-level` (`BA11Y021`);
  - `user-scalable=true`, which browsers read as `no` (`BA11Y029`).
- `BA11Y035` no longer reads `aria-expanded="@(a) && (b)"` as one bool expression. Razor renders it as the value of `a` followed by the text ` && (b)`.
- `BA11Y023` no longer calls `role="complementary"` redundant on an unnamed `<aside>` inside `<article>`, `<aside>`, `<nav>` or `<section>`. Such an `<aside>` is generic without the role.
- `blazor-click-handler-semantics` and `BA11Y001` no longer report clickable items of a composite widget whose container manages focus and keys, such as options of an `aria-activedescendant` listbox (found by the demo's analyzer dogfooding).
- `AriaPatterns.VerifyToolbarAsync` accepts a toolbar that is the last tab stop of the page when Tab leaves focus on the same control, as headless Firefox does at the end of the tab order.
- Verifier fixes from a code review:
  - `AuditConsistentHelpAsync` compares a help link repeated in the header and the footer occurrence by occurrence.
  - `VerifyLoadingStateAsync` no longer counts a status that is there before, during and after the load (a cart count) as announcing it.
  - `VerifySortableTableAsync` needs at least two rows to judge the order, and accepts ordinal as well as culture-specific sorting.
  - `ReportVisualOrderMismatch` no longer reports focus that reads down one column and moves to the next.
  - `VerifyFocusSurvivesInteractivityAsync` tells apart two elements with the same name.
  - The default help-link pattern of the in-page helper used a backspace instead of a word boundary.
- Runtime component fixes from the same review:
  - `RovingFocusGroup`, `ActiveDescendantGroup` and `FocusTrap` release their JavaScript handles when they are disposed while the handle is still being created.
  - `ActiveDescendantGroup` clears `aria-activedescendant` when it is disposed.
  - A newer `LiveAnnouncer` host replaces the previous one instead of throwing when a layout change renders both.
  - `RouteAnnouncer` no longer expands `$` in page titles, and no longer waits forever in background tabs.
- Live regions: when one text node changed several times before the observer callback ran (as when Blazor renders "Saving…" and an immediate failure in quick succession), every change was recorded with the final text, so the intermediate update was lost and `ExpectLiveRegionUpdate("Saving")` failed intermittently. Each change now keeps its own value.
- `AriaPatterns.VerifyTooltipAsync` moves the pointer onto the tooltip in one step. Intermediate steps raced the page's hide timers, so the hoverable check could fail intermittently in Firefox.
- `AriaPatterns.VerifyFormValidationAsync` waits for focus and error links that follow the invalid markers in a later render, as Blazor's `OnAfterRenderAsync` focus calls do (seen in WebKit).
- Browser diagnostics: `blazor-expanded-state-mismatch` skips triggers that are not rendered. A menu button hidden on wide screens, whose navigation is always shown there, is no longer reported.
- Live regions: `ExpectUpdateAsync` no longer matches text that only remains in a region (for example earlier `log` entries), removals, or updates of hidden regions. Atomic and inserted regions still match on their whole text.
- Live regions: monitoring after a navigation now works. The observer no longer depends on `document.documentElement` existing when the init script runs, does not report server-rendered regions as inserted, and expectations made before the navigation no longer hide updates on the new page.
- Live regions and browser diagnostics: empty but rendered elements (an empty status region, an expanded listbox that is still loading, an empty selected tab panel) are no longer treated as hidden.
- `MaxNodesPerRule` of zero or less means no limit. Before, it dropped every axe-core node and the audit passed.
- `AriaPatterns.VerifyTabsAsync`: tabs that cannot be focused count as outside the tab sequence, so the roving-tabindex check no longer passes a non-focusable selected tab or fails a non-focusable unselected one.
- `KeyboardScenario.ExpectFocusContainedWithin` and `AriaPatterns.VerifyModalDialogAsync` wait for focus to return after each key press, so focus traps that move focus back asynchronously (for example on the Blazor Server circuit) pass.
- Razor click-handler rule: `tabindex="-1"` no longer counts as keyboard focusable, and `@onclick` on `<label>`, `<option>`, `<video>` and `<audio>` is now checked (a label that wraps a checkbox, radio button or button is still accepted).
- bUnit selectors use unique ancestor ids as anchors (`#billing > div > button`) instead of positional selectors.
- Stored ARIA snapshots resolve deterministic source paths (`/_/...`, `/_1/...`) on Windows build agents.
- Browser audits no longer report filtered-out best-practice toolkit rules (such as `blazor-duplicate-id`) as passed.
- The audit builder keeps different problems of one rule on the same element (for example two broken id references), and `Build()` can be called more than once without duplicating warnings.
- Hosted generic components report their type without versioned generic arguments, matching bUnit audits and keeping fingerprints stable across versions.
- The JUnit report declares `encoding="utf-8"` when formatted to a string, instead of `utf-16`.

### Demo

- New **Runtime components** page: a cart button that announces through `ILiveAnnouncer`, a `RovingFocusGroup` toolbar, an `ActiveDescendantGroup` combobox and a `FocusTrap` drawer, each verified live with the matching ARIA pattern. The layout hosts the app's `LiveAnnouncer`.
- Demo buttons without a class get the theme's colors. In WebKit's dark mode they kept the browser's light gray background with white text (contrast 1.81:1).
- The sticky navigation sidebar is sticky only on viewports tall enough for the whole menu. Before, its last links could receive focus without ever becoming visible (found by the focus-order test after the navigation grew); a test guards the limit.
- The demo uses `<NavigationFocus>` instead of `<FocusOnNavigate>`. Focus a keyboard user places while a page is prerendered is no longer moved to the heading when the page becomes interactive; new tests cover this and the focus move after client-side navigation.
- The demo's skip link renders the current path, so it no longer points at the home page before the page becomes interactive (found by the new `BA11Y038`), and its test runs on a page other than the home page too.
- The demo projects run the Razor analyzers at build time (`demo/Directory.Build.targets`). They are switched off only for the intentional defects in `Defects/`, and a test checks that every accessible component and showcase page is free of analyzer findings.
- New tests:
  - the form validation pattern on `SignupForm` (passes) and `LegacySignupForm` (fails),
  - an end-to-end audit of every route discovered by reflection.
- The committed baseline includes the new `blazor-input-purpose-missing` finding of the intentional `LegacySignupForm` defect. A test verifies that the Razor analyzers report the `ClickableCard` and `UnlabelledIconButton` defects and nothing in the accessible components.
- The showcase has a light, a dark and a system theme. Every color is a `light-dark()` token in `app.css` and `components.css` (the components still render in the light theme by default, which is what the component tests audit), the theme switcher is a radio group in the header, and the choice is kept in a cookie so the server renders pages in that theme without a flash. The end-to-end tests audit every page in both themes and test the switcher with the keyboard.
- The home page shows every feature at a glance, with links to live examples, the toolkit in numbers, and an example of a failing test's output.
- A **Run all** panel on each page runs every live test on it and lists how each one ended. Defect cards say who the defect locks out and what a screen reader typically announces.
- New live examples: page audits of the running app, with `ExcludeSelectors` and documented suppressions (one that applies and one that has expired); `AriaPatterns.VerifyModalDialogAsync` on `FocuslessDialog` and `ModalDialog`; missing focus indicators with `ReportMissingFocusIndicator`, on the new `OutlineFreeViewSwitcher` defect and its fix `ViewSwitcher`. Results show suppressions, expired suppressions and stops without a focus indicator.
- The rule catalog can be searched and filtered, and links each rule to its live example. Every page links to the previous and next page of the tour, and the navigation collapses behind a menu button on narrow screens.
- The demo web app is now a showcase of the toolkit. Each page explains one feature (component audits, Blazor-aware rules, keyboard scenarios, focus order, ARIA patterns, live regions, accessibility-tree snapshots, the CI gate, manual review and the rule catalog), renders a broken component next to its fix, shows the test that checks them, and runs that test live with a **Run this test** button. The CI gate page simulates baselines, regressions and strict mode, and serves the reports in every format.
- The component library is split into `Accessible/` and `Defects/` (namespaces `BlazorAccessibilityToolkit.Demo.Components.Accessible` and `.Defects`), with new pairs: `FormattingToolbar` / `LegacyFormattingToolbar`, `FilterPanel` / `AnimatedFilterPanel`, `MenuButton`, `ShippingOptions` / `UngroupedShippingOptions`, `QuantityField` and `CartLines` / `HardcodedQuantityField` and `LegacyCartLines`, and the defects `FocuslessDialog`, `NaiveTabs`, `StaleDisclosure`, `ToastSaveButton` and `LegacySignupForm`.
- The demo tests cover the new components, keep every intentional defect detected, audit every showcase page (excluding the marked defects) and run every live example. The `Settings` and `Signup` sample pages are replaced by the showcase pages, and the baseline is regenerated.
- The `samples` folder is now `demo`, and its projects are renamed: `SampleComponents` to `BlazorAccessibilityToolkit.Demo.Components`, `SampleApp` to `BlazorAccessibilityToolkit.Demo.WebApp`, `SampleApp.ComponentTests` to `BlazorAccessibilityToolkit.Demo.ComponentTests` and `SampleApp.EndToEndTests` to `BlazorAccessibilityToolkit.Demo.EndToEndTests`.
- `dotnet run --project demo/BlazorAccessibilityToolkit.Demo.WebApp` works: a launch profile sets the `Development` environment. Before, the app ran as `Production`, `blazor.web.js` and the component files returned errors, and the app never became interactive.
- The demo app has the `/error` page its exception handler pointed to, a "Page not found" page (status 404) for unknown URLs, and the Blazor error notification with a real dismiss button.
- `ModalDialog`: Tab brings focus back into the dialog after it was lost (for example by clicking the backdrop), backdrop clicks no longer take focus out of the dialog, the dialog can hold focus itself, and it has no empty description without content.
- `Tabs`: Arrow keys, Home and End no longer scroll the page, and modified keys (such as Alt+ArrowLeft) are left to the browser.
- `SignupForm`: required fields are indicated, a field is never marked invalid without its error text, focus moves to the first invalid field after a failed submit, and the success status is cleared on a later failure.
- `SaveButton`: a failed save is announced instead of leaving "Saving…" and a disabled button behind, and a repeated result is announced again.
- `IconButton` no longer sets `title`, so its accessible name comes only from `Label`, as documented.
- The demo tests verify the tabs, modal dialog and disclosure with `AriaPatterns`, assert roles and states in the markup tests, and cover the not-found and error pages. The end-to-end fixture no longer creates an extra `WebApplicationFactory`, and both fixtures dispose cleanly when initialization fails.

## 0.1.0-preview.1 — 2026-11-04

First preview release.

### Packages

- `BlazorAccessibilityToolkit`:
  - normalized findings with statuses (violation, potential issue, inconclusive, manual review),
  - WCAG 2.2 catalog and tag mapping,
  - framework-agnostic assertions,
  - audit builder with evidence redaction, fingerprints and suppressions (reason, owner, expiry),
  - count-aware baselines and a quality gate with CI exit codes,
  - manual-review checklists,
  - reports: console, JSON (schema 1.0), JUnit, SARIF 2.1.0 and HTML,
  - per-audit result files (`BLAZOR_A11Y_RESULTS_DIR`).
- `BlazorAccessibilityToolkit.ComponentTesting`:
  - bUnit `AccessibilityFixture` and `cut.AuditAccessibilityAsync()`,
  - Razor markup rules (Blazor click handlers, `ValidationMessage` association, invalid controls without error references, duplicate IDs, broken ID references, radio and fieldset grouping),
  - browserless fallback rules,
  - component attribution from the render tree,
  - pluggable markup engines.
- `BlazorAccessibilityToolkit.Playwright`:
  - axe-core 4.14 audits of pages and regions through Deque.AxeCore.Playwright,
  - browser diagnostics (expanded and tab-selection state, ID references, validation messages, grouping),
  - accessible name, role and state inspection,
  - keyboard scenarios, focus-order capture with trap detection, live-region monitoring and analysis,
  - ARIA snapshots (inline and stored), ARIA pattern verifiers (modal dialog, disclosure, tabs, menu button),
  - a pooled browser engine for bUnit markup,
  - the in-process interactive component host (`BlazorTestHost`).
- `BlazorAccessibilityToolkit.Cli` (`blazor-a11y`): `report` (with SARIF file locations for GitHub code scanning via `--source-root` and `--sarif-fallback-uri`), `baseline`, and `manual init|validate`.

### Engines and environments

- axe-core 4.14.0, Microsoft.Playwright 1.64.1 (Chromium 156, Firefox 157, WebKit 27.2), bUnit 2.13.3, .NET 10.
- Tested in Chromium, Firefox and WebKit, with xUnit v3, NUnit and MSTest. See [limitations](docs/limitations.md).
