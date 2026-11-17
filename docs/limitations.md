# Limitations and tested environments

## What automation cannot tell you

- **Conformance.** Passing every automated check does not mean a page conforms to WCAG. Many criteria need human judgment: whether reading and focus order make sense, whether link and heading text is meaningful, contrast over images and gradients, reflow, motion, and error recovery. Track them with [manual review](manual-review.md).
- **Screen reader output.** Live-region monitoring, ARIA snapshots and virtual screen-reader transcripts record DOM and accessibility-tree evidence. They do not emulate a screen reader and cannot confirm what is spoken, in which order, or whether an announcement was interrupted.
- **Accessible names in bUnit markup.** Role queries, name assertions and transcripts of bUnit markup use the toolkit's own accname 1.2 implementation without CSS: only inline `display: none`/`visibility: hidden` hide content, and CSS generated content is not part of names. The browser helpers use axe-core's implementation with real styles.
- **Static heuristics.** The analyzer's `BA11Y035` only recognizes bools it can see in the same `.razor` file. Conformance drafts (OpenACR, accessibility statement) are starting points with `TODO`s, not reports.
- **Logical focus order.** The toolkit records the observed order and flags traps, stuck focus and hidden targets. Whether the order is logical is judged against the expectation you write.
- **Focus indicators.** During focus-order capture and keyboard scenarios that expect an indicator, the toolkit compares each element's focused style with its recorded unfocused style (element, pseudo-elements and parent). Indicators drawn by other elements, or by script after focus, are not seen, elements focused without a recorded style fall back to "outline or box-shadow present", and the contrast and area of the indicator are not measured.
- **Obscured focus, reflow and text spacing.** These checks use the browser's own hit testing and layout at one moment in time. Content that animates into place, sticky elements with `pointer-events: none`, and clipping that depends on fonts loading later can produce different results. Findings are potential issues.
- **Razor analyzers.** The compile-time rules read `.razor` markup, not the rendered output. Values computed at run time, attribute splatting and content rendered by child components are assumed to be correct. They complement the rendered-markup audits; they do not replace them.

## Rendering environments

| Environment | Supported |
| --- | --- |
| bUnit markup (any component) | yes. No CSS unless supplied, no JavaScript, no layout or focus. |
| Static SSR (host or app) | yes |
| Interactive Server (host or app) | yes |
| Interactive WebAssembly / Auto | end to end against the running app only, not in the isolated host |
| Prerendering | the isolated host renders without prerendering; end-to-end tests see the prerendered page first and must wait for interactivity |

## DOM features

- **iframes:** pages with frames are audited through Deque.AxeCore.Playwright, which coordinates axe-core across same-origin and cross-origin frames. Pages without frames run axe-core directly in the page (loaded once, same version, same result parsing). The toolkit's own diagnostics, focus helpers and live-region monitoring inspect the main document and same-origin frames for focus only. Cross-origin content is not inspected.
- **Shadow DOM:** axe-core and focus detection pierce open shadow roots. Toolkit diagnostics and live-region monitoring cover the main document tree only. Closed shadow roots are not visible.
- **Canvas, SVG charts and other custom-drawn UI:** only their accessible fallback (roles, names, alternative content) can be checked.
- **Virtualized lists and lazy content:** only rendered items are audited. Scroll or page through the content and audit each state you care about.
- **Very large pages:** axe-core can be slow. Audit a region (`locator.AuditAccessibilityAsync()`), exclude heavy third-party widgets, or raise `Timeout`. A timeout fails the audit; it never passes it.

## Component attribution

- It names the component that *rendered* an element, never a source line. Child content appears under the component that renders it.
- It falls back to the fixture root, with a warning, when the parsed DOM differs from the render tree. For example, `<td>` or `<tr>` elements rendered outside a table in isolation.
- The interactive host attributes findings to the fixture root only, because the browser does not expose component boundaries.

## Tested environments

| Component | Versions |
| --- | --- |
| .NET SDK and runtime | 10.0.403 SDK, 10.0.14 runtime (Windows 11; Linux in CI) |
| Microsoft.Playwright | 1.64.1: Chromium 156.0.8078.4, Firefox 157.0, WebKit 27.2 |
| axe-core | 4.14.0 (Deque.AxeCore.Playwright 4.14.0) |
| bUnit | 2.13.3 |
| Test frameworks | xUnit v3 3.2.2 (main suite), NUnit 4.6.1 and MSTest 4.5.1 (compatibility suites in `tests/Compatibility`, VSTest runner) |

The full test suite (unit, component, browser, host, demo and end-to-end tests) passes on Chromium, Firefox and WebKit. Known browser differences are handled and documented:

- WebKit skips links on Tab, as Safari does by default.
- Headless Firefox keeps focus on the last element at the end of the sequence.

## Measured performance

Measured on a developer workstation with Chromium. CI agents are typically slower.

| Scenario | Time |
| --- | --- |
| bUnit fixture and browserless audit | ~2 ms each; under 3 MB managed memory growth over 200 audits |
| bUnit fixture and axe-core audit in a pooled browser page | ~65 ms each sequentially (axe-core is loaded once per pooled page); 12 in parallel on fresh pages in ~1 to 1.5 s |
| Interactive host: render a component in a new context | a few hundred ms |

`PerformanceTests` in `tests/BlazorAccessibilityToolkit.ComponentTesting.Tests` records current numbers in `performance.txt`. Its bounds catch order-of-magnitude regressions.
