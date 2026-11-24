# Blazor Accessibility Toolkit

An accessibility engineering toolkit for Razor components and Blazor apps. It audits components and pages, drives them with a real keyboard, inspects focus, names, roles and states, observes live regions, snapshots the accessibility tree, and turns the results into CI-ready reports.

The toolkit does more than run a scanner in a browser. It knows about Razor and Blazor:

- Findings name the **Razor component that rendered the element**, for example `Toolbar > IconButton`.
- It detects **Blazor `@onclick` handlers** on elements that keyboard users cannot reach or activate. Browsers cannot see these handlers.
- It flags **`ValidationMessage`** output that is not linked to its input, fields marked required only by a `*`, and personal-data fields without `autocomplete`.
- It checks **Razor files at compile time** (`BA11Y001` to `BA11Y038`), like `eslint-plugin-jsx-a11y` does for JSX, including Blazor pitfalls no other tool sees: `aria-expanded="@open"` disappears when `open` is false, routable pages without `<PageTitle>`, and `@onpaste:preventDefault` on password fields.
- It queries bUnit markup **by role and accessible name**, like Testing Library, and records **virtual screen-reader transcripts** of components and pages.
- It verifies **client-side page changes**: the title updates and focus moves to the new content (`<PageTitle>`, `<FocusOnNavigate>`).
- It renders **isolated components interactively** in a real browser, with in-process parameters.

> Automated checks find a subset of accessibility problems. They cannot prove WCAG conformance. The toolkit keeps inconclusive results and manual-review items separate, and never reports them as passed.

**See it running.** The [demo showcase](demo) puts realistic broken components next to their fixes, with the test that tells them apart and a button that runs it live. It covers every feature, has light and dark themes, and audits itself with the toolkit:

```bash
dotnet build
dotnet run --project demo/BlazorAccessibilityToolkit.Demo.WebApp   # http://localhost:5180
```

## Packages

| Package | Use it for |
| --- | --- |
| `BlazorAccessibilityToolkit` | Core model, assertions, baselines, suppressions, quality gates, manual review and reports (console, JSON, JUnit, SARIF, HTML). No dependencies. |
| `BlazorAccessibilityToolkit.ComponentTesting` | bUnit component fixtures, Razor markup rules and component attribution. Works without a browser. |
| `BlazorAccessibilityToolkit.Playwright` | axe-core audits (optionally with IBM Equal Access as a second engine), keyboard scenarios, focus order, live regions, ARIA snapshots, ARIA pattern checks, a browser engine for bUnit markup, and an in-process interactive component host. |
| `BlazorAccessibilityToolkit.Cli` | The `blazor-a11y` dotnet tool: aggregates results, compares them with a baseline, applies the failure policy and writes reports (including a Markdown job summary) with CI exit codes. `blazor-a11y fix` applies the Razor analyzer's safe fixes, and `blazor-a11y mcp` serves the toolkit to coding agents. |
| `BlazorAccessibilityToolkit.Components` | Runtime helpers for your app: a live announcer service, a skip link that works with `<base href="/">`, focus traps with focus restoration, a route announcer and visually hidden text. |
| `BlazorAccessibilityToolkit.Windows` | Axe.Windows (UI Automation) audits of the native window and controls of Blazor Hybrid apps on Windows. |
| `BlazorAccessibilityToolkit.DevTools` | A development overlay for your app: axe-core checks each page as it changes and lists the violations in a panel and the browser console, like `@axe-core/react`. |
| `BlazorAccessibilityToolkit.Analyzers` | Compile-time checks of `.razor` markup: clickable `div`s, images without `alt`, unlabeled fields, unnamed buttons, invalid ARIA, missing page titles and more (38 rules), with component-library mapping. Warnings with the Razor file and line, and fixes for findings with one safe edit. |

Requirements: .NET 10, and Playwright browsers for browser features (installed explicitly, see [Getting started](docs/getting-started.md)).

## Quick look

**Component markup with bUnit.** Fast, and runs without a browser:

```csharp
await using var fixture = await AccessibilityFixture.For<IconButton>()
    .WithParameter(p => p.Label, "Close")
    .BuildAsync();

var audit = await fixture.AuditAsync();
audit.ShouldHaveNoViolations();
```

**The same markup audited with axe-core in a browser engine.** Findings are attributed to the component that rendered the element:

```csharp
var settings = ComponentAuditSettings.Default.WithEngine(new PlaywrightMarkupAuditEngine(browser));
await using var fixture = await AccessibilityFixture.For<Toolbar>().WithSettings(settings).BuildAsync();
var finding = (await fixture.AuditAsync()).ShouldHaveFinding("button-name");
// finding.Component.ComponentPath == ["App.Toolbar", "App.IconButton"]
```

**An isolated component with real keyboard input** (Interactive Server, in-process host):

```csharp
await using var dialog = await host.RenderAsync<ModalDialog>(browser, p => p.Add(c => c.TriggerText, "Delete account"));

await dialog.Keyboard()
    .StartFromDocument()
    .PressTab().ExpectFocused("button", "Delete account").RememberFocus("trigger")
    .PressEnter().ExpectFocused("button", "Cancel")
    .ExpectFocusContainedWithin(dialog.Page.GetByRole(AriaRole.Dialog))
    .Audit("dialog open")
    .PressEscape().ExpectFocusRestored("trigger")
    .RunAsync();
```

**Pages of the running app, and the CI gate:**

```csharp
var audit = await page.AuditAccessibilityAsync();
audit.ShouldHaveNoViolations();
```

```bash
BLAZOR_A11Y_RESULTS_DIR=a11y-results dotnet test
blazor-a11y report --input a11y-results --baseline a11y-baseline.json --output a11y-report
```

## What it covers

- **Automated audits.** axe-core 4.14 with WCAG 2.0/2.1/2.2 A and AA tags, plus presets for AAA, EN 301 549, Section 508, Trusted Tester v5 and RGAA 4 (France). Best-practice rules are kept separate. You can include and exclude rules, exclude selectors, and add documented suppressions with an owner and expiry date.
- **Razor-aware diagnostics:**
  - click handlers without keyboard support,
  - unassociated validation messages,
  - invalid controls without error references,
  - broken ID references,
  - duplicate IDs from reused components,
  - `aria-expanded` and tab selection that disagree with what is visible,
  - ungrouped radio buttons and fieldsets without a legend,
  - active navigation links without `aria-current`, required fields marked only visually, personal-data fields without `autocomplete` (1.3.5),
  - an inaccessible Blazor error notification (`#blazor-error-ui`).
- **Compile-time Razor checks.** 38 analyzer rules with Razor file and line, configurable in `.editorconfig`, ignorable per element with a Razor comment, and able to check component libraries mapped to native elements (`MudIconButton:button`).
- **While you develop.** A development overlay runs axe-core in the running app after each change and lists the violations, and `blazor-a11y fix` applies the analyzer's safe fixes. Coding agents use the same checks, fixes and test results through the MCP server.
- **Queries and assertions on bUnit markup.** `FindByRole("button", "Save")`, `FindByLabelText`, `ShouldHaveAccessibleName`, `ShouldHaveAccessibleDescription`, `ShouldHaveAccessibleErrorMessage` and state assertions, on an accname 1.2 implementation.
- **Virtual screen-reader transcripts** of bUnit markup and browser pages (`heading, Settings, level 2` / `button, Bold, not pressed`), for diffable regression tests of semantics.
- **Keyboard and focus:**
  - Tab, Shift+Tab, Enter, Space, arrow keys, Home, End and Escape scenarios,
  - focus restoration and containment,
  - focus-order capture with trap, stuck and hidden-target detection,
  - focus hidden behind sticky headers or banners (WCAG 2.4.11),
  - focus indicators detected as a visible style change, including borders, backgrounds and pseudo-elements.
- **Adaptation.** Reflow at 320 CSS pixels (1.4.10), text spacing (1.4.12), orientation (1.3.4), animation under reduced motion (2.2.2, 2.3.3), and audits in dark mode, forced colors (Windows high contrast) and increased contrast.
- **WCAG 2.2 heuristics.** Paste and password managers blocked on credential fields (3.3.8), draggable content without an alternative (2.5.7), confirmation fields (3.3.7) and obscured focus (2.4.11).
- **Every page.** Route discovery by reflection (`BlazorRoutes.Discover`) audits every `@page` of the app and lists the routes it could not open.
- **Page changes.** Client-side navigation updates the title and moves focus or announces the new page.
- **ARIA patterns.** Checks against the WAI-ARIA Authoring Practices for modal dialogs, disclosures, tabs, menu buttons, radio groups, toolbars, listboxes, comboboxes, sliders, checkboxes and switches, tooltips (with WCAG 1.4.13) and accordions, plus form validation after a failed submit (errors marked, linked, focused and announced).
- **Live regions.** DOM evidence of `status`, `alert` and `aria-live` updates, including repeated messages, plus analysis of patterns that commonly prevent announcements.
- **Accessibility-tree snapshots.** Playwright ARIA snapshots with partial and regex templates, readable diffs and an explicit update workflow.
- **Manual review.** Checklists for criteria that need human judgment, including a template with every WCAG 2.2 A and AA criterion. A "passed" status is only accepted with a tester and a date.
- **Conformance evidence.** A per-criterion WCAG 2.2 coverage table with EN 301 549 clauses that combines automated findings and manual review, exported on request as an OpenACR (VPAT 2.5) draft and an EAA accessibility statement draft. Automation alone never marks a criterion as supported.
- **CI:**
  - versioned JSON, JUnit, SARIF (mapped to `.razor` files where unambiguous), HTML and Markdown reports, and GitHub job summaries,
  - count-aware baselines that never hide new findings,
  - strict mode,
  - nonzero exit codes for failures and for audits that did not run.

## Documentation

- [Getting started](docs/getting-started.md)
- [Component testing with bUnit](docs/component-testing.md)
- [Browser and interactive testing](docs/interactive-testing.md): pages, the component host, keyboard, focus, live regions, patterns and snapshots
- [Rules and findings](docs/rules.md)
- [Razor analyzers](docs/analyzers.md): compile-time checks of `.razor` files
- [ACT rules implementation report](docs/act-rules.md): the analyzer against the W3C ACT Rules test cases
- [Runtime components](docs/runtime-components.md): live announcer, skip link, focus trap, route announcer
- [Development overlay](docs/dev-overlay.md): axe-core findings in the running app while you work
- [MCP server](docs/mcp-server.md): checks, fixes and test results for coding agents
- [Blazor Hybrid apps](docs/blazor-hybrid.md): audits of WPF, Windows Forms and MAUI apps through WebView2
- [CI integration](docs/ci-integration.md): results, the CLI, baselines, suppressions, GitHub Actions and Azure Pipelines
- [Manual review](docs/manual-review.md)
- [Extending the toolkit](docs/extending.md): custom rules, engines and fixtures
- [Troubleshooting](docs/troubleshooting.md)
- [Limitations and tested environments](docs/limitations.md)
- [Upgrading](docs/upgrading.md)
- Architecture decision records: [architecture](docs/adr/0001-architecture.md), [coverage expansion](docs/adr/0002-coverage-expansion.md), [second survey](docs/adr/0003-second-survey.md)
- [Roadmap](docs/roadmap.md): remaining gaps from the cross-stack comparison

## Demo

[`demo/`](demo) contains:

- a component library with accessible patterns (`Accessible/`) and intentional, realistic defects (`Defects/`), each paired with its fix,
- a showcase web app that explains every feature next to live components and runs the toolkit live: bUnit audits in process, browser features through Playwright and the in-process component host,
- component tests (bUnit and the interactive host) with regression tests for every defect,
- end-to-end tests of the showcase (`WebApplicationFactory` on Kestrel with Playwright),
- a committed baseline and a manual-review checklist for the CI gate.

See [demo/README.md](demo/README.md).

## License

MIT, see [LICENSE](LICENSE).

The Playwright package depends on Deque.AxeCore.Playwright (MIT) and Deque.AxeCore.Commons, which bundles axe-core under the Mozilla Public License 2.0.
