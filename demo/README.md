# Demo

The demo shows what the toolkit does on real Blazor components: realistic accessibility defects next to their fixes, the tests that tell them apart, and the CI gate that keeps them from coming back.

The quickest way in is the showcase app. Every page explains one feature, renders a live broken component and its fixed version, shows the test that checks them, and has a **Run this test** button that executes that test in the app's process and displays what the toolkit reports. A **Run all** panel at the top of each page runs every test on it and sums up how each one ended. Defect cards say who the defect locks out and what a screen reader typically announces, so the problem is clear before any test runs.

The showcase has a light, a dark and a system theme (the switcher in the header; the choice is kept in a cookie, so pages render in the right theme from the first paint). The end-to-end tests audit every page in both themes.

```bash
dotnet build
pwsh demo/BlazorAccessibilityToolkit.Demo.WebApp/bin/Debug/net10.0/playwright.ps1 install chromium   # for the browser examples
dotnet run --project demo/BlazorAccessibilityToolkit.Demo.WebApp
```

Then open <http://localhost:5180>. The examples on the first two pages need no browser; the others use Playwright and report clearly when Chromium is not installed.

## The tour

| Page | Feature | Broken component | Fixed component |
| --- | --- | --- | --- |
| What the toolkit does | Overview: every feature at a glance, and the simplest test | `UnlabelledIconButton` | `IconButton` |
| Component audits | `AccessibilityFixture` without a browser and with axe-core, component attribution, auditing states, regression tests | `LegacyFormattingToolbar` | `FormattingToolbar`, `DisclosureCard` |
| Blazor-aware rules | Rules browsers cannot apply: `@onclick` without keyboard support, `ValidationMessage` not linked to its field, hard-coded ids in reused components, ungrouped radio buttons | `ClickableCard`, `LegacySignupForm`, `LegacyCartLines`, `UngroupedShippingOptions` | `DisclosureCard`, `SignupForm`, `CartLines`, `ShippingOptions` |
| Page audits | `AuditAccessibilityAsync` on pages of the running app, `ExcludeSelectors`, and documented suppressions with an owner and an expiry date (an expired one stops applying) | the legacy page | the legacy page with a suppression, a showcase page with its defects excluded |
| Keyboard scenarios | `BlazorTestHost` and `KeyboardScenario`: focus into, inside and out of a dialog; focus on the first invalid field | `FocuslessDialog`, `LegacySignupForm` | `ModalDialog`, `SignupForm` |
| Focus order | `CaptureFocusOrderAsync`, `ShouldMatch`, hidden focus targets, missing focus indicators (`ReportMissingFocusIndicator`), cycles inside modal dialogs | `AnimatedFilterPanel`, `OutlineFreeViewSwitcher` | `FilterPanel`, `ViewSwitcher`, `ModalDialog` |
| ARIA patterns | `AriaPatterns` verifiers for tabs, disclosures, modal dialogs and menu buttons, plus the browser diagnostic for stale `aria-expanded` | `NaiveTabs`, `StaleDisclosure`, `FocuslessDialog` | `Tabs`, `DisclosureCard`, `ModalDialog`, `MenuButton` |
| Live regions | `LiveRegionMonitor`: what changed, and patterns that keep messages from being announced | `ToastSaveButton` | `SaveButton` |
| Accessibility tree | ARIA snapshots compared with a reviewed template, with a readable diff | `NaiveTabs` | `Tabs` |
| CI gate and reports | An interactive gate: record a baseline, introduce a regression, try strict mode, download HTML, SARIF, JUnit and JSON reports | | |
| Manual review | The checklist in `a11y/manual`, its coverage, and its effect on the gate | | |
| Rule catalog | Every toolkit rule with its status, WCAG criteria and environment, searchable, with links to live examples | | |
| Legacy page | A page of the "existing app" with known issues tracked in the baseline | `UnlabelledIconButton`, `ClickableCard` | |

## Projects

| Project | What it is |
| --- | --- |
| `BlazorAccessibilityToolkit.Demo.Components` | The components under test. `Accessible/` holds correct patterns; `Defects/` holds intentional, realistic mistakes. Each defect's header comment names the rule that detects it and its fixed counterpart. |
| `BlazorAccessibilityToolkit.Demo.WebApp` | The showcase (Blazor Web App, Interactive Server). The live tests are in `Lab/Examples`; pages display that code as it runs, read from embedded resources, with only the lines that hand results to the page (`env.Show(...)`) left out. Browser examples share one Playwright browser and one in-process `BlazorTestHost`, started on first use. |
| `BlazorAccessibilityToolkit.Demo.ComponentTests` | The same checks as an xUnit suite: bUnit audits (browserless and with axe-core), the interactive host, keyboard scenarios, focus order, live regions, ARIA patterns, an ARIA snapshot (`__a11y_snapshots__`), and regression tests that keep every intentional defect detected. |
| `BlazorAccessibilityToolkit.Demo.EndToEndTests` | The showcase on Kestrel through `WebApplicationFactory`: every page audited with axe-core in the light and the dark theme (intentional defects, marked `data-known-defect`, are excluded), the theme switcher, the collapsed navigation on narrow screens, "Run all", skip link, focus order, navigation, keyboard flows, the live test runner, the not-found and error pages, and the legacy page's known issues. `ShowcaseExampleTests` runs every live example and checks that it ends as its page says. |
| `a11y-baseline.json` | The committed baseline: the known findings of the intentional defects, as produced by the two test suites. |
| `a11y/manual/modal-dialog.json` | A manual-review checklist with example recorded results (passed, not applicable with justification, requires review, not tested). |

## Run the tests and the gate

From the repository root:

```bash
dotnet build
pwsh demo/BlazorAccessibilityToolkit.Demo.ComponentTests/bin/Debug/net10.0/playwright.ps1 install chromium

# Tests write one result file per audit when BLAZOR_A11Y_RESULTS_DIR is set. Start from an empty directory:
# results of earlier runs would be counted again and exceed the baseline.
rm -rf a11y-results                                 # PowerShell: Remove-Item -Recurse -Force a11y-results -ErrorAction Ignore
export BLAZOR_A11Y_RESULTS_DIR=$PWD/a11y-results    # PowerShell: $env:BLAZOR_A11Y_RESULTS_DIR = "$PWD/a11y-results"
dotnet test demo/BlazorAccessibilityToolkit.Demo.ComponentTests
dotnet test demo/BlazorAccessibilityToolkit.Demo.EndToEndTests

# CI gate: passes because the known defects are in the baseline; any new finding fails it.
dotnet run --project src/BlazorAccessibilityToolkit.Cli -- report \
  --input a11y-results --baseline demo/a11y-baseline.json \
  --manual-review demo/a11y/manual/modal-dialog.json \
  --output a11y-report --source-root demo
```

Open `a11y-report/accessibility-report.html` to explore the results.

Try these:

- Add `--strict`. The gate fails because the baseline still lists known issues.
- Remove the `aria-label` from `Accessible/IconButton.razor`. `Icon_button_has_an_accessible_name` fails, and the gate reports a `[new]` finding.
- Run with `BLAZOR_A11Y_BROWSER=firefox` or `webkit` (install that browser with `playwright.ps1` first).
- After changing a defect, regenerate the baseline: `dotnet run --project src/BlazorAccessibilityToolkit.Cli -- baseline --input a11y-results --baseline demo/a11y-baseline.json`.

Notes:

- The launch profile runs the showcase in the `Development` environment, which serves the component library's static files from the build output, and clears `BLAZOR_A11Y_RESULTS_DIR` so that live runs started from the pages never add files to a results directory.
- The end-to-end tests run the live examples too, so their audits are part of the test results and of the baseline.
