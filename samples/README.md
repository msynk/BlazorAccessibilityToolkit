# Samples

| Project | What it shows |
| --- | --- |
| `SampleComponents` | Razor class library. **Accessible patterns:** `IconButton`, `ModalDialog` (focus management via a JS module), `Tabs` (roving tabindex, arrow keys, Home and End), `SignupForm` (errors associated with fields, success status), `SaveButton` (live region), `DisclosureCard`. **Intentional defects:** `UnlabelledIconButton` (missing accessible name) and `ClickableCard` (clickable `<div>`). |
| `SampleApp` | Blazor Web App (Interactive Server) using the components. It includes a skip link that moves focus explicitly (Blazor's router does not), a readiness signal for tests (`data-interactive`), and a `/legacy` page with known defects. |
| `SampleApp.ComponentTests` | bUnit markup audits (browserless and with axe-core), regression tests for the defects, interactive host tests (dialog, tabs, live region, form), an ARIA snapshot (`__a11y_snapshots__`), and ARIA pattern use. |
| `SampleApp.EndToEndTests` | The real app on Kestrel through `WebApplicationFactory`: page audits, skip link, focus order, keyboard flows across components, and known issues tracked in the baseline. |
| `a11y-baseline.json` | Committed baseline: the three known findings from the intentional defects. |
| `a11y/manual/modal-dialog.json` | Manual-review checklist with example recorded results (passed, not applicable with justification, requires review, not tested). |

## Run

```bash
dotnet build
pwsh samples/SampleApp.ComponentTests/bin/Debug/net10.0/playwright.ps1 install chromium

# Tests write one result file per audit when BLAZOR_A11Y_RESULTS_DIR is set.
export BLAZOR_A11Y_RESULTS_DIR=$PWD/a11y-results   # PowerShell: $env:BLAZOR_A11Y_RESULTS_DIR = "$PWD/a11y-results"
dotnet test samples/SampleApp.ComponentTests
dotnet test samples/SampleApp.EndToEndTests

# CI gate: passes because the known defects are in the baseline; any new finding fails it.
dotnet run --project src/BlazorAccessibilityToolkit.Cli -- report \
  --input a11y-results --baseline samples/a11y-baseline.json \
  --manual-review samples/a11y/manual/modal-dialog.json \
  --output a11y-report --source-root samples
```

Open `a11y-report/accessibility-report.html` to explore the results.

Try these:

- Add `--strict`. The gate fails because the baseline still lists known issues.
- Remove the `aria-label` from `IconButton.razor`. `Icon_button_has_an_accessible_name` fails, and the gate reports a `[new]` finding.
- Run with `BLAZOR_A11Y_BROWSER=firefox` or `webkit`.

Run the sample app itself with `dotnet run --project samples/SampleApp`.
