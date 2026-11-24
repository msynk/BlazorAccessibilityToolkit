# Roadmap

We have compared the toolkit with accessibility tools in other stacks twice. [ADR 0002](adr/0002-coverage-expansion.md) and [ADR 0003](adr/0003-second-survey.md) describe the surveys and what each added. This page lists the remaining gaps, most valuable first. Each entry says what the gap is and what makes it hard.

## Planned

1. **Standards updates:**
   - EN 301 549 V4.1.1 clause mapping once V4 is cited in the Official Journal,
   - BITV (Germany) presets, once axe-core maps rules to BITV (RGAA 4 and Trusted Tester v5 presets are done),
   - axe-core 4.14.1 once Deque.AxeCore.Playwright ships it.

## Considered, not planned yet

- **Siteimprove Alfa** as a third engine. Alfa is a set of TypeScript packages built to run in Node.js, not a script for the page, so it would need a Node.js process next to the .NET tests.
- **Blazor Hybrid on macOS, iOS and Android**, whose web views (WKWebView, Android WebView) Playwright cannot automate.
- **Focus lost when `<Virtualize>` recycles the focused row.** It happens only when a pointer user scrolls the focused row out of view, and the result depends on the browser's sequential focus starting point. A check would be noisy, and the fix (keeping the focused item rendered) belongs in the component. Keyboard navigation through a virtualized list keeps the focused row in view and is not affected.
- **Real screen-reader automation** (Guidepup with NVDA or VoiceOver in CI), beyond the virtual transcripts.

## Done after the second survey

- Code fixes for the analyzer findings that have one safe edit, in the IDE and with `blazor-a11y fix` ([Razor analyzers](analyzers.md#fixes)).
- A development overlay that runs axe-core in the running app and lists the violations ([Development overlay](dev-overlay.md)).
- An MCP server that gives coding agents the analyzer, its fixes, the test results and the rule catalog ([MCP server](mcp-server.md)).
- Blazor Hybrid apps on Windows (WPF, Windows Forms, .NET MAUI): the BlazorWebView's content audited through WebView2, and the native window through Axe.Windows ([Blazor Hybrid apps](blazor-hybrid.md)).
- IBM Equal Access as an optional second rule engine next to axe-core, with findings on the same element and success criterion merged ([A second rule engine](interactive-testing.md#a-second-rule-engine-ibm-equal-access)).
- APCA contrast as information next to WCAG 2 contrast ([APCA contrast](interactive-testing.md#apca-contrast)).
- Focus indicators measured from screenshots: invisible focus (2.4.7) and focus appearance (2.4.13, AAA) ([measuring focus indicators](interactive-testing.md#measuring-focus-indicators)).
- An ACT rules implementation report for the Razor analyzer, in the EARL format the ACT Task Force collects ([ACT rules implementation report](act-rules.md)). Submitting it to the ACT implementations page is a separate step for the maintainers.

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
