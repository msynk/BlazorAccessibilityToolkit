# ADR 0001: Architecture of the Blazor Accessibility Toolkit

- Status: accepted
- Date: 2026-11-04

## Context

The repository started empty (README and license only), so there were no existing test frameworks, conventions or CI to integrate with. The toolkit has to:

- audit rendered Razor components and complete Blazor pages,
- test keyboard behavior, focus, accessible names and states, and live regions in real browsers,
- attribute findings to Razor components where that can be done reliably,
- integrate with existing .NET test suites and CI without claiming WCAG conformance.

Constraints found while prototyping:

- A component renderer such as bUnit has no layout, focus, CSS or JavaScript. It cannot test keyboard behavior. It does expose Blazor event handlers (`blazor:onclick` attributes) and the component render tree, which a browser cannot see.
- A browser cannot see which Razor component produced an element, or which elements have Blazor event handlers. Blazor uses event delegation, so there are no DOM listeners to inspect.
- In .NET 10, `_framework/blazor.web.js` is a static web asset provided by the `Microsoft.AspNetCore.App.Internal.Assets` pack. The Web SDK adds it only for executable Web SDK projects that contain `.razor` files or set `RequiresAspNetWebAssets=true`. NuGet does not import package props during restore, so a package cannot switch this on for its consumers.
- Headless Firefox keeps focus on the last element when Tab is pressed past it. WebKit, like Safari's default settings, leaves links out of the Tab sequence.

## Decisions

### Supported environments

- .NET 10 (LTS). Earlier versions are out of support or close to it, and the in-process host depends on .NET 10 static web asset behavior.
- Blazor Web Apps. Supported render modes are static SSR, Interactive Server (in the isolated host and end to end), and WebAssembly or Auto when tested end to end against a running app.
- Browsers: Chromium, Firefox and WebKit through Microsoft.Playwright 1.64.1. The whole test suite runs on all three.

### Engines: reuse axe-core, add Razor-specific rules only

- axe-core runs through **Deque.AxeCore.Playwright 4.14.0**, maintained by Deque. It is version-pinned (major.minor follows axe-core) and handles iframes through `runPartial`. Accessible names and roles for diagnostics come from axe-core's own implementation (`axe.commons`), not from a home-grown one.
- On pages without frames, the toolkit loads the bundled axe-core once per page and calls `axe.run` directly; the raw result is parsed with Deque's own `AxeResult`, so findings are identical to the Deque path. This avoids re-evaluating the ~600 KB script on every audit (about 3 times faster for repeated audits). Pages with frames always use Deque's cross-frame run. If a page already contains a different axe-core version, the pinned one replaces it.
- The toolkit adds rules only where they bring Razor- or interaction-specific value:
  - Blazor click handlers without keyboard support or role, which only bUnit can see.
  - `ValidationMessage` output that is not associated with its control.
  - `aria-expanded` and tab-selection state that contradicts what is visible.
  - Broken ID references.
  - Duplicate IDs from reused components.
  - Focus traps, stuck focus and hidden focus targets.
  - Live-region patterns that commonly prevent announcements.
- Heuristics are reported as `PotentialIssue`. axe "incomplete" results are reported as `Inconclusive`. Neither fails tests by default, and neither is ever reported as passed.
- A small browserless fallback rule set exists only for bUnit audits that run without a browser engine. These rules are skipped when a browser engine runs, so they never duplicate axe-core.
- Engine upgrades are explicit. A golden-file contract test pins the axe-core version and its normalized output for a fixed document in every browser.

### Packages

| Package | Depends on | Why it is separate |
| --- | --- | --- |
| `BlazorAccessibilityToolkit` | nothing | Findings, options, WCAG catalog, assertions, baselines, suppressions, quality gate, manual review, report writers. Used by every other package and by the CLI without pulling in browsers or bUnit. |
| `BlazorAccessibilityToolkit.ComponentTesting` | core, bUnit | Component fixtures, Razor markup rules, render-tree attribution. Keeps browser integration optional: it talks to browsers only through the `IMarkupAuditEngine` abstraction in core. |
| `BlazorAccessibilityToolkit.Playwright` | core, Microsoft.Playwright, Deque.AxeCore.Playwright, ASP.NET Core shared framework | Page and region audits, the markup engine for bUnit, keyboard scenarios, focus order, live regions, ARIA snapshots, APG pattern verifiers and the in-process interactive component host. |
| `BlazorAccessibilityToolkit.Cli` (dotnet tool `blazor-a11y`) | core | Aggregation, baselines, gates and report formats for CI, with documented exit codes. |

There are no test-framework adapter packages. Assertions throw `AccessibilityAssertionException`, which xUnit, NUnit and MSTest all report as failures, so an adapter would add packages without adding value.

### Component attribution

bUnit's component tree is walked through the framework's `Renderer.GetCurrentRenderTreeFrames`. It is read-only and suppresses `BL0006`. Each element frame, and each element inside raw markup frames, is mapped in document order to the path of components that rendered it, and the walk is checked against bUnit's parsed DOM by count and tag sequence.

If they disagree (for example a lone `<td>` dropped by the HTML parser), or the render tree is unavailable, findings are attributed to the fixture root and the audit gets a warning. The browser engine maps findings back through ordinal attributes that are moved into JavaScript expandos before auditing, so evidence stays clean.

Attribution names the component that *rendered* an element. The toolkit never claims a Razor source line.

### Interactive component host

`BlazorTestHost` runs Kestrel in-process on `127.0.0.1` with a random port. It exposes two host pages: `/_a11y/interactive/{id}` (Interactive Server without prerendering) and `/_a11y/static/{id}` (static SSR). These render registered components through `DynamicComponent`.

Parameters, render fragments, callbacks and cascading values are kept in an in-process registry, so nothing is serialized. Each fixture uses its own browser context. The host checks at startup whether `blazor.web.js` is served and reports a precise fix if it is not.

### Failure semantics

- Engine errors, timeouts, scopes that match nothing or more than one element, empty component renders, unknown tags or rules, and closed pages all make the audit `Failed`. Assertions on a failed or skipped audit throw.
- The CLI exits with 2 for failed audits, unreadable inputs or zero results, with 1 for accessibility failures, and with 3 for usage errors.

### Baselines and fingerprints

A fingerprint is the SHA-256 of a version, engine, rule, status class, component or URL path, scenario, state, and a normalized selector. GUIDs, long numbers and Blazor CSS scope attributes are removed. Evidence, host, port and timestamps are excluded.

Baselines are count-aware, so an extra occurrence is reported as new. Baselines are not written from incomplete results. Strict mode also fails on stale entries, expired suppressions and skipped audits.

### Results flow

Each audit is written as its own JSON file when `BLAZOR_A11Y_RESULTS_DIR` is set. This needs no shared state and is safe for parallel processes. The CLI aggregates these files into reports.

## Consequences

- Users choose per test between fast browserless markup audits, markup audits in a browser, interactive isolated components, and end-to-end pages.
- Interactive isolated tests require a Web SDK executable test project, plus `RequiresAspNetWebAssets` when the project has no `.razor` files.
- Isolated WebAssembly rendering is out of scope; WebAssembly behavior is tested end to end.
- Upgrading Microsoft.Playwright or Deque.AxeCore.Playwright is a deliberate change that updates golden files and snapshots.

## Implementation sequence

1. Core model, assertions and reporting.
2. axe-core and browser diagnostics.
3. Keyboard, focus, live regions and snapshots.
4. Interactive host.
5. bUnit fixtures and attribution.
6. CLI.
7. Samples.
8. Engine contract, patterns and performance tests.
9. Documentation and CI.
