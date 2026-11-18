# Roadmap

We have compared the toolkit with accessibility tools in other stacks twice. [ADR 0002](adr/0002-coverage-expansion.md) and [ADR 0003](adr/0003-second-survey.md) describe the surveys and what each added. This page lists the remaining gaps, most valuable first. Each entry says what the gap is and what makes it hard.

## Planned

1. **Standards updates:**
   - EN 301 549 V4.1.1 clause mapping once V4 is cited in the Official Journal,
   - BITV (Germany) presets, once axe-core maps rules to BITV (RGAA 4 and Trusted Tester v5 presets are done),
   - axe-core 4.14.1 once Deque.AxeCore.Playwright ships it.

## Considered, not planned yet

- **A second rule engine** (IBM Equal Access or Siteimprove Alfa) next to axe-core. Results would need deduplication, and a second engine doubles the maintenance of the engine contract.
- **ACT rules implementation report.** Run the toolkit's own rules against the W3C ACT test cases and publish an EARL report.
- **Screenshot-based checks** for focus appearance (2.4.13, AAA) and indicator contrast. They depend on fonts and rendering, so they are hard to make deterministic across browsers.
- **APCA contrast** as information next to WCAG 2 contrast. axe-core does not support it, and APCA is not part of WCAG 2.
- **Blazor Hybrid and native hosts** (MAUI, WPF, WinForms) through Axe.Windows or Appium.
- **Focus lost when `<Virtualize>` recycles the focused row.** It happens only when a pointer user scrolls the focused row out of view, and the result depends on the browser's sequential focus starting point. A check would be noisy, and the fix (keeping the focused item rendered) belongs in the component. Keyboard navigation through a virtualized list keeps the focused row in view and is not affected.
- **Real screen-reader automation** (Guidepup with NVDA or VoiceOver in CI), beyond the virtual transcripts.
- **An MCP server or overlay component** that exposes findings to agents or shows them in the running app during development.

## Done after the second survey

- Code fixes for the analyzer findings that have one safe edit, in the IDE and with `blazor-a11y fix` ([Razor analyzers](analyzers.md#fixes)).

## Done in the second survey

These items were planned before and are now available:

- virtual screen-reader transcripts,
- bUnit role queries and accessible-name assertions,
- component mapping in the analyzer,
- the WCAG 2.2 heuristics for dragging, accessible authentication and redundant entry,
- route discovery,
- conformance report export,
- runtime helpers (live announcer, skip link, focus trap, route announcer, visually hidden),
- consistent help (3.2.6), duplicate link names, role words in names, a 44 pixel target profile and the reconnect modal check,
- roving focus groups, a route announcer for static rendering, `<Virtualize>` set sizes and duplicate clickable bounds,
- `ActiveDescendantGroup`, type-to-search, `NavigationFocus` with the prerendering focus check, and the tab-stop overlay screenshot,
- audit attachments in the HTML report, sortable table and pagination verifiers (QuickGrid), and the loading-state verifier for streaming rendering.

See [ADR 0003](adr/0003-second-survey.md).
