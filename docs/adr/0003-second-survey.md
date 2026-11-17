# ADR 0003: Second cross-stack survey and the gaps it closed

- Status: accepted
- Date: 2026-11-17

## Context

After [ADR 0002](0002-coverage-expansion.md), we surveyed accessibility tooling across stacks again, looking for capabilities the toolkit still lacked beyond its roadmap. The survey covered:

- **Linters:** `eslint-plugin-jsx-a11y`, `eslint-plugin-vuejs-accessibility`, `@angular-eslint/template`, and the Svelte compiler's accessibility warnings.
- **Assertions and queries:** Testing Library `*ByRole` with all its options, and jest-dom accessibility matchers (`toHaveAccessibleName`, `toHaveAccessibleDescription`, `toHaveAccessibleErrorMessage`, `toHaveRole`, `toBeInvalid` ...). We also looked at Playwright's locator assertions and ARIA snapshots up to 1.65, and React Native Testing Library.
- **Engines and runners:** axe-core 4.12 to 4.14.1, pa11y-ci, Lighthouse, IBM Equal Access, Siteimprove Alfa, Accessibility Insights FastPass, axe DevTools guided tests, Guidepup's virtual screen reader, Sa11y and Editoria11y.
- **Native platforms:** Android Accessibility Test Framework and Compose, iOS `performAccessibilityAudit`, Flutter's `meetsGuideline` and Axe.Windows.
- **Runtime libraries:** Angular CDK a11y (LiveAnnouncer, FocusTrap, FocusMonitor, ListKeyManager), React Aria (FocusScope, VisuallyHidden, live announcer), the Next.js and SvelteKit route announcers, and BlazorRamp.
- **Standards and regulation:**
  - WCAG 2.2, also published as ISO/IEC 40500:2025.
  - WCAG 3, still a working draft.
  - ARIA 1.3, a working draft that adds the roles `suggestion`, `comment`, `sectionheader` and `sectionfooter`, and the attribute `aria-actions`.
  - EN 301 549 V4.1.1, published September 2026 but not yet cited in the Official Journal.
  - The European Accessibility Act, applicable since June 2025.
  - The ADA Title II deadlines, moved to April 2027 and 2028.
  - VPAT 2.5 and OpenACR.

## Gaps found

Ranked by value to Blazor teams. "Done" means this change closed the gap.

| # | Gap | Found in | Status |
| --- | --- | --- | --- |
| 1 | Statically checkable markup rules missing from the analyzer: autofocus, accesskey, distracting elements, media captions, `scope`, autocomplete tokens, `aria-activedescendant` focusability, required ARIA attributes, ARIA value types, redundant roles, non-interactive tab stops, hover without focus, nested interactive controls, ambiguous link text, BCP 47 `lang`, viewport zoom, meta refresh, redundant alt words, presentational roles on focusable elements, `input type="image"`, `area` and `svg role="img"` alternatives | jsx-a11y, vue, angular-eslint, Svelte, axe | Done: `BA11Y014` to `BA11Y037` |
| 2 | Blazor-only static checks: a routable page without `<PageTitle>`, a `<Router>` without `<FocusOnNavigate>`, `@onpaste:preventDefault`, and ARIA states bound to a bool (Blazor drops attributes whose value is `false`, so `aria-expanded="@open"` never says "collapsed") | none (Blazor-specific) | Done: `BA11Y031`, `BA11Y032`, `BA11Y033`, `BA11Y035` |
| 3 | Component-library mapping in the analyzer (`MudButton` → `button`) | jsx-a11y `settings.components`, axe Linter | Done: `blazor_a11y_analyzer.component_map` |
| 4 | ARIA 1.3 roles and attributes unknown to the analyzer and markup rules | ARIA 1.3, axe 4.13 | Done |
| 5 | Role and name queries and accessible name, description, error message and state assertions for bUnit | Testing Library, jest-dom | Done: `FindByRole`, `FindByLabelText`, `ShouldHaveAccessibleName` ..., on a C# accname 1.2 implementation |
| 6 | Virtual screen-reader transcripts | Guidepup virtual screen reader | Done: for bUnit markup and in the browser, in one format |
| 7 | Verification of form error handling after a failed submit (linked errors, focus, announcement), the most common Blazor form defect | jest-dom `toHaveAccessibleErrorMessage`, GOV.UK error summary | Done: `AriaPatterns.VerifyFormValidationAsync` |
| 8 | WCAG 2.2 heuristics: accessible authentication (3.3.8), dragging (2.5.7), redundant entry (3.3.7) | WCAG 2.2, Sa11y | Done, in bUnit, browser and analyzer checks, and consistent help (3.2.6) across pages with `AuditConsistentHelpAsync`. |
| 9 | Reduced motion (2.2.2, 2.3.3) and orientation (1.3.4) | iOS audits, Playwright media emulation | Done: `AuditMotionAsync`, `AuditOrientationAsync` |
| 10 | Route discovery to audit every page | pa11y-ci sitemap mode | Done: `BlazorRoutes.Discover`, `AuditRoutesAsync` |
| 11 | Conformance documentation export | OpenACR, VPAT 2.5, EAA | Done: `openacr` and `statement` report formats (drafts) |
| 12 | Runtime helpers (live announcer, skip link, focus trap, visually hidden, route announcer) | Angular CDK, React Aria, Next.js | Done: new package `BlazorAccessibilityToolkit.Components`, including `RovingFocusGroup`, `ActiveDescendantGroup`, `NavigationFocus` and a route announcer for static rendering. |
| 12a | `<a href="#id">` resolves against `<base href="/">` and opens the home page: skip links break on every page but the home page until the app is interactive | none (Blazor-specific, found while building `SkipLink`) | Done: `BA11Y038`, `blazor-fragment-link-leaves-page`, and the demo's skip link fixed |
| 13 | Render-mode checks: focus lost when prerendered content becomes interactive, enhanced navigation without focus or announcement, streaming placeholders without `aria-busy`, `Virtualize` rows without `aria-setsize`/`aria-posinset`, QuickGrid sort and paging announcements, the .NET 10 reconnect modal | none (Blazor-specific) | Reconnect modal (`blazor-reconnect-ui-not-accessible`), `Virtualize` set sizes (`blazor-virtualized-set-size`) and focus lost when a prerendered page becomes interactive (`VerifyFocusSurvivesInteractivityAsync`, fixed by `NavigationFocus`) done; QuickGrid sorting and paging (`VerifySortableTableAsync`, `VerifyPaginationAsync`) and streaming placeholders (`VerifyLoadingStateAsync`) done. Focus lost when `Virtualize` recycles a row scrolled away by the pointer was considered and not planned (see the roadmap). |
| 14 | Mobile-derived heuristics: duplicate names, role words in names, a stricter 44/48 pixel target profile | Android ATF, iOS, Flutter | Done: `blazor-ambiguous-link-name`, `blazor-role-in-name`, `AuditTargetSizeAsync`, `blazor-duplicate-clickable-bounds`. |
| 15 | Visual tab-stop overlay and visual-order versus DOM-order checks | Accessibility Insights | Done: `ReportVisualOrderMismatch`, `SaveTabStopOverlayAsync`, and audit attachments embedded in the HTML report. |
| 16 | EN 301 549 V4.1.1 clause mapping and RGAA tags | axe 4.11 to 4.14 | Waiting for the Official Journal citation |
| 17 | axe-core 4.14.1 (fixes false positives in target-size and color-contrast) | axe-core | Waiting for Deque.AxeCore.Playwright 4.14.1 |
| 18 | A second engine, ACT rules report, APCA, native hosts, an MCP server | IBM, Alfa, W3C ACT | Considered, unchanged from ADR 0002 |

## Decisions

### The analyzer reports more, but at the right severity

Rules that are often deliberate are reported as suggestions (`DiagnosticSeverity.Info`), not warnings. These are autofocus, accesskey, redundant roles, non-interactive tab stops, ambiguous link text, redundant alt words and dragging. Rules with a clear defect are warnings. Two deliberate patterns are accepted: `<ul role="list">`, which restores list semantics that Safari drops, and `<nav role="navigation">`.

`BA11Y035` (ARIA state bound to a bool) only reports what it can prove from the file. That covers negations, comparisons, logical expressions and literals, plus members declared as `bool` in the same `.razor` file. The analyzer reads markup, not the compilation, as decided in ADR 0002. Rendered-markup audits cannot see this defect, because the attribute is simply absent.

Mapped components are checked as the native element they render. Their accessible name may also come from configurable parameters (`Label`, `AriaLabel`, `Title`, `Alt` ...).

### One accessible name implementation per environment

bUnit markup has no browser, so `ComponentTesting` contains a C# implementation of WAI-ARIA 1.2 roles, HTML-AAM implicit roles and accname 1.2:

- aria-labelledby, including hidden referenced content;
- embedded control values;
- aria-label;
- native labels, legends, captions and alt text;
- name from content for the roles that allow it;
- title and placeholder.

It works without CSS, so the documentation states what that means: inline styles only, and no generated content. In the browser, the toolkit keeps using axe-core's implementation. Both feed one transcript format, `ScreenReaderTranscript` in the core package, so a bUnit transcript and a browser transcript of the same markup are comparable.

### Transcripts are labelled as virtual

A transcript lists `role, name, states, description` per item. It keeps "false" states (`not pressed`, `collapsed`) that ARIA snapshots omit. It is documented, in the API remarks and in the docs, as a regression record and not as screen reader output.

### Browser checks may dispatch a synthetic paste event

Blazor implements `@onpaste:preventDefault` in its event delegation, which leaves no trace in the DOM. The browser check therefore dispatches an empty, cancelable paste event, and only to credential fields: password, `username`, `current-password`, `new-password` and `one-time-code`. It reports the field when the event is cancelled, and restores the field value. The event does run the page's paste handlers, so disabling the rule (`DisabledRules`) skips the probe entirely. bUnit sees the flag directly as a `blazor:onpaste:preventDefault` attribute, so the component rule needs no event.

### Conformance exports are drafts by construction

OpenACR adherence follows `WcagCoverage`:

- `supports` only after a passed manual review;
- `does-not-support` when there is a violation or a failed manual check;
- `not-applicable` when every manual check says so;
- `not-evaluated` otherwise, with notes on the evidence.

The accessibility statement never states full compliance. Legal and contact sections are `TODO` placeholders. Both formats are written only on request, so CI artifacts do not suddenly contain documents that look official.

## Consequences

- The toolkit script version is 10.
- New potential issues can appear in existing suites: `blazor-paste-blocked`, `blazor-dragging-alternative` and `blazor-redundant-entry` in page and component audits. Default assertions and gates do not fail on potential issues.
- Projects that use the analyzer get the new warnings on their next build. `BA11Y031` and `BA11Y032` apply to every routable page and router. Use `.editorconfig` severities or `@* blazor-a11y-ignore-file *@` where a project manages titles or focus differently.
- The remaining gaps are listed in the [roadmap](../roadmap.md).
