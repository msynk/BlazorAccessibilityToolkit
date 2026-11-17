# Roadmap

We have compared the toolkit with accessibility tools in other stacks twice. [ADR 0002](adr/0002-coverage-expansion.md) and [ADR 0003](adr/0003-second-survey.md) describe the surveys and what each added. This page lists the remaining gaps, most valuable first. Each entry says what the gap is and what makes it hard.

## Planned

1. **More runtime helpers.** `BlazorAccessibilityToolkit.Components` now has the live announcer, skip link, focus trap, route announcer and visually hidden text. Still missing:
   - roving-tabindex and active-descendant keyboard helpers for composite widgets (Angular CDK's `ListKeyManager`),
   - a route announcer for enhanced navigation in static SSR, which has no circuit to run the component.
   - **What makes it hard:** the keyboard helpers need a small, render-mode independent API. The static SSR announcer needs a script that listens to `enhancedload`.
2. **Render-mode and lifecycle checks** that only apply to Blazor:
   - focus lost when prerendered content becomes interactive,
   - enhanced navigation that neither moves focus nor announces the page,
   - `[StreamRendering]` placeholders without `aria-busy`,
   - `<Virtualize>` rows without `aria-setsize`/`aria-posinset`, and focus lost when rows are recycled,
   - QuickGrid sort state and page changes that are not announced.
   - The .NET 10 reconnect modal is checked (`blazor-reconnect-ui-not-accessible`).
   - **What makes it hard:** each needs a scenario that drives the render mode, not a static check.
3. **Nested clickables with identical bounds**, as Android ATF's `DuplicateClickableBoundsCheck` reports. Duplicate names, role words in names and a 44/48 pixel target profile are done.
4. **Tab-stop visualization.** A numbered tab-stop overlay screenshot in the HTML report, and a check that visual order follows DOM order, as Accessibility Insights FastPass does.
5. **Standards updates:**
   - EN 301 549 V4.1.1 clause mapping once V4 is cited in the Official Journal,
   - RGAA and BITV tag presets,
   - axe-core 4.14.1 once Deque.AxeCore.Playwright ships it.

## Considered, not planned yet

- **A second rule engine** (IBM Equal Access or Siteimprove Alfa) next to axe-core. Results would need deduplication, and a second engine doubles the maintenance of the engine contract.
- **ACT rules implementation report.** Run the toolkit's own rules against the W3C ACT test cases and publish an EARL report.
- **Screenshot-based checks** for focus appearance (2.4.13, AAA) and indicator contrast. They depend on fonts and rendering, so they are hard to make deterministic across browsers.
- **APCA contrast** as information next to WCAG 2 contrast. axe-core does not support it, and APCA is not part of WCAG 2.
- **Blazor Hybrid and native hosts** (MAUI, WPF, WinForms) through Axe.Windows or Appium.
- **Real screen-reader automation** (Guidepup with NVDA or VoiceOver in CI), beyond the virtual transcripts.
- **An MCP server or overlay component** that exposes findings to agents or shows them in the running app during development.
- **Roslyn code fixes** for analyzer findings that have one safe fix (for example `aria-labeledby` to `aria-labelledby`).

## Done in the second survey

These items were planned before and are now available:

- virtual screen-reader transcripts,
- bUnit role queries and accessible-name assertions,
- component mapping in the analyzer,
- the WCAG 2.2 heuristics for dragging, accessible authentication and redundant entry,
- route discovery,
- conformance report export,
- runtime helpers (live announcer, skip link, focus trap, route announcer, visually hidden),
- consistent help (3.2.6), duplicate link names, role words in names, a 44 pixel target profile and the reconnect modal check.

See [ADR 0003](adr/0003-second-survey.md).
