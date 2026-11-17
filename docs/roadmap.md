# Roadmap

We compared the toolkit with accessibility tools in other stacks. [ADR 0002](adr/0002-coverage-expansion.md) describes the survey and what has been added. This page lists the remaining gaps, most valuable first. Each entry says what the gap is and what makes it hard.

## Planned

1. **Virtual screen-reader transcripts.** Generate the phrases a screen reader would speak (for example `button, Save, pressed`) from the accessibility tree, as Guidepup's virtual screen reader does. Add live-region announcements to the transcript and snapshot it. This would extend, not replace, ARIA snapshots.
   - **What makes it hard:** real screen readers differ, so a transcript must not be presented as proof of what is spoken.
2. **bUnit role queries and accessible-name assertions.** `FindByRole(role, name, level, expanded, ...)`, `ShouldHaveAccessibleName` and `ShouldHaveAccessibleDescription` on bUnit markup, using an accname 1.2 implementation. They would mirror Testing Library and the Playwright assertions.
3. **Component mapping in the analyzer.** An `.editorconfig` map from component libraries to native elements, so that `<FluentButton>` or `<MudIconButton>` are checked like `<button>`. jsx-a11y and axe Linter provide the same kind of map.
4. **Remaining WCAG 2.2 heuristics:**
   - Dragging (2.5.7): flag drag handlers and `draggable` elements for review.
   - Accessible authentication (3.3.8): check that paste works and `autocomplete` is set on password and one-time-code fields.
   - Redundant entry (3.3.7): find fields asked twice across steps of a form.
   - Consistent help (3.2.6): compare where help links sit across pages.
5. **Route discovery.** Enumerate `@page` routes by reflection to audit every page of an app, similar to pa11y-ci's sitemap mode.
6. **Conformance report export.** Export `WcagCoverage` and checklists as an OpenACR (YAML) or VPAT 2.5 draft, and as an EAA accessibility statement draft. Add EN 301 549 V4 clause mapping once V4 is cited in the Official Journal.

## Considered, not planned yet

- **A second rule engine** (IBM Equal Access or Siteimprove Alfa) next to axe-core. Results would need deduplication, and a second engine doubles the maintenance of the engine contract.
- **ACT rules implementation report.** Run the toolkit's own rules against the W3C ACT test cases and publish an EARL report.
- **Screenshot-based checks** for focus appearance (2.4.13, AAA) and indicator contrast. They depend on fonts and rendering, so they are hard to make deterministic across browsers.
- **APCA contrast** as information next to WCAG 2 contrast. axe-core does not support it, and APCA is not part of WCAG 2.
- **Blazor Hybrid and native hosts** (MAUI, WPF, WinForms) through Axe.Windows or Appium.
- **An MCP server or overlay component** that exposes findings to agents or shows them in the running app during development.
