# Demo

| Project | What it shows |
| --- | --- |
| `BlazorAccessibilityToolkit.Demo.Components` | Razor class library. **Accessible patterns:** `IconButton`, `ModalDialog` (focus moved in, kept in and returned to the trigger, via a JS module), `Tabs` (roving tabindex, arrow keys, Home and End that do not scroll the page), `SignupForm` (required fields indicated, errors associated with fields, focus on the first invalid field, success status), `SaveButton` (progress, success and failure in a live region), `DisclosureCard`. **Intentional defects:** `UnlabelledIconButton` (missing accessible name) and `ClickableCard` (clickable `<div>`). |
| `BlazorAccessibilityToolkit.Demo.WebApp` | Blazor Web App (Interactive Server) using the components. It includes a skip link that moves focus explicitly (Blazor's router does not), a readiness signal for tests (`data-interactive`), "Page not found" and error pages, an error notification with a real dismiss button, and a `/legacy` page with known defects. |
| `BlazorAccessibilityToolkit.Demo.ComponentTests` | bUnit markup audits (browserless and with axe-core), regression tests for the defects, interactive host tests (dialog, tabs, disclosure, live regions, form), ARIA pattern verification (`AriaPatterns`), and an ARIA snapshot (`__a11y_snapshots__`). |
| `BlazorAccessibilityToolkit.Demo.EndToEndTests` | The real app on Kestrel through `WebApplicationFactory`: page audits, skip link, focus order, keyboard flows across components, the not-found and error pages, and known issues tracked in the baseline. |
| `a11y-baseline.json` | Committed baseline: the three known findings from the intentional defects. |
| `a11y/manual/modal-dialog.json` | Manual-review checklist with example recorded results (passed, not applicable with justification, requires review, not tested). |

## Run

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
- Remove the `aria-label` from `IconButton.razor`. `Icon_button_has_an_accessible_name` fails, and the gate reports a `[new]` finding.
- Run with `BLAZOR_A11Y_BROWSER=firefox` or `webkit` (install that browser with `playwright.ps1` first).

Run the demo app itself with `dotnet run --project demo/BlazorAccessibilityToolkit.Demo.WebApp` and open <http://localhost:5180>. The launch profile sets the `Development` environment, which serves the component library's static files from the build output.
