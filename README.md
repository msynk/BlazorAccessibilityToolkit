# Blazor Accessibility Toolkit

An accessibility engineering toolkit for Razor components and Blazor apps. It audits components and pages, drives them with a real keyboard, inspects focus, names, roles and states, observes live regions, snapshots the accessibility tree, and turns the results into CI-ready reports.

The toolkit does more than run a scanner in a browser. It knows about Razor and Blazor:

- Findings name the **Razor component that rendered the element**, for example `Toolbar > IconButton`.
- It detects **Blazor `@onclick` handlers** on elements that keyboard users cannot reach or activate. Browsers cannot see these handlers.
- It flags **`ValidationMessage`** output that is not linked to its input.
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
| `BlazorAccessibilityToolkit.Playwright` | axe-core audits, keyboard scenarios, focus order, live regions, ARIA snapshots, ARIA pattern checks, a browser engine for bUnit markup, and an in-process interactive component host. |
| `BlazorAccessibilityToolkit.Cli` | The `blazor-a11y` dotnet tool: aggregates results, compares them with a baseline, applies the failure policy and writes reports with CI exit codes. |

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

- **Automated audits.** axe-core 4.14 with WCAG 2.0/2.1/2.2 A and AA tags. Best-practice rules are kept separate. You can include and exclude rules, exclude selectors, and add documented suppressions with an owner and expiry date.
- **Razor-aware diagnostics:**
  - click handlers without keyboard support,
  - unassociated validation messages,
  - invalid controls without error references,
  - broken ID references,
  - duplicate IDs from reused components,
  - `aria-expanded` and tab selection that disagree with what is visible,
  - ungrouped radio buttons and fieldsets without a legend.
- **Keyboard and focus:**
  - Tab, Shift+Tab, Enter, Space, arrow keys, Home, End and Escape scenarios,
  - focus restoration and containment,
  - focus-order capture with trap, stuck and hidden-target detection,
  - focus-indicator observations.
- **ARIA patterns.** Checks against the WAI-ARIA Authoring Practices for modal dialogs, disclosures, tabs and menu buttons.
- **Live regions.** DOM evidence of `status`, `alert` and `aria-live` updates, including repeated messages, plus analysis of patterns that commonly prevent announcements.
- **Accessibility-tree snapshots.** Playwright ARIA snapshots with partial and regex templates, readable diffs and an explicit update workflow.
- **Manual review.** Checklists for criteria that need human judgment. A "passed" status is only accepted with a tester and a date.
- **CI:**
  - versioned JSON, JUnit, SARIF (mapped to `.razor` files where unambiguous) and HTML reports,
  - count-aware baselines that never hide new findings,
  - strict mode,
  - nonzero exit codes for failures and for audits that did not run.

## Documentation

- [Getting started](docs/getting-started.md)
- [Component testing with bUnit](docs/component-testing.md)
- [Browser and interactive testing](docs/interactive-testing.md): pages, the component host, keyboard, focus, live regions, patterns and snapshots
- [Rules and findings](docs/rules.md)
- [CI integration](docs/ci-integration.md): results, the CLI, baselines, suppressions, GitHub Actions and Azure Pipelines
- [Manual review](docs/manual-review.md)
- [Extending the toolkit](docs/extending.md): custom rules, engines and fixtures
- [Troubleshooting](docs/troubleshooting.md)
- [Limitations and tested environments](docs/limitations.md)
- [Upgrading](docs/upgrading.md)
- [Architecture decision record](docs/adr/0001-architecture.md)

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
