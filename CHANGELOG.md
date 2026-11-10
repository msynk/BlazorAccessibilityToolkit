# Changelog

## Unreleased

### Added

- `AccessibilityAudit.ExpiredSuppressions` (JSON field `expiredSuppressions`) lists suppressions that had expired. Strict quality gates now fail on this list instead of on warning text that contains "expired".

### Fixed

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
