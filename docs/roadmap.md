# Roadmap

We have compared the toolkit with accessibility tools in other stacks twice. [ADR 0002](adr/0002-coverage-expansion.md) and [ADR 0003](adr/0003-second-survey.md) describe the surveys and what each added. This page lists the remaining gaps, most valuable first. Each entry says what the gap is and what makes it hard.

## Planned

1. **Runtime helpers package.** Components and services that make the accessible path the easy one, as Angular CDK and React Aria do:
   - an `ILiveAnnouncer` service with a `<LiveAnnouncer />` host (polite and assertive, repeated messages announced again),
   - `<SkipLink>`, `<VisuallyHidden>` and `<FocusTrap RestoreFocus>`,
   - a `<RouteAnnouncer />` for enhanced navigation in static SSR,
   - roving-tabindex and active-descendant helpers.
   - **What makes it hard:** focus management needs a JavaScript module served as a static web asset of a Razor class library, which the isolated test host does not serve yet. The helpers must also work in every render mode, and stay small.
2. **Render-mode and lifecycle checks** that only apply to Blazor:
   - focus lost when prerendered content becomes interactive,
   - enhanced navigation that neither moves focus nor announces the page,
   - `[StreamRendering]` placeholders without `aria-busy`,
   - `<Virtualize>` rows without `aria-setsize`/`aria-posinset`, and focus lost when rows are recycled,
   - QuickGrid sort state and page changes that are not announced,
   - the .NET 10 reconnect modal.
   - **What makes it hard:** each needs a scenario that drives the render mode, not a static check.
3. **Mobile-derived heuristics:**
   - adjacent controls with the same accessible name but different targets,
   - role words repeated in names ("Submit button"),
   - nested clickables with identical bounds,
   - an opt-in 44/48 CSS pixel target profile, stricter than WCAG 2.5.8.
   - They follow Android ATF, iOS `performAccessibilityAudit` and Flutter's guidelines.
4. **Tab-stop visualization.** A numbered tab-stop overlay screenshot in the HTML report, and a check that visual order follows DOM order, as Accessibility Insights FastPass does.
5. **Consistent help (3.2.6).** Compare where help mechanisms sit across pages discovered with `BlazorRoutes`.
6. **Standards updates:**
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
- conformance report export.

See [ADR 0003](adr/0003-second-survey.md).
