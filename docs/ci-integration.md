# CI integration

## How results flow

1. Tests run audits as usual. Assertions fail the tests that check them.
2. When `BLAZOR_A11Y_RESULTS_DIR` is set, **every** audit (component, page or focus order) is also written to that directory as `<name>-<id>.a11y.json`. Each audit is its own file, so parallel tests and processes never share state.
3. `blazor-a11y report` aggregates the files, compares them with a baseline, applies the failure policy, writes reports and sets the exit code.

The gate is separate from test assertions. Tests can assert strictly on the components a team owns, while the gate enforces a policy across everything, for example "no new serious violations".

## The CLI

```bash
blazor-a11y report \
  --input a11y-results \                  # repeatable; directories are searched recursively
  --baseline a11y-baseline.json \
  --manual-review a11y/manual/checkout.json \
  --format console --format json --format junit --format sarif --format html \   # default: all
  --output a11y-report \
  --fail-on serious \                     # none | minor (default) | moderate | serious | critical
  --fail-on-potential --fail-on-inconclusive --fail-on-manual-review \
  --strict \
  --source-root src                       # map component types to .razor files for SARIF
```

| Exit code | Meaning |
| --- | --- |
| 0 | gate passed |
| 1 | accessibility findings, failed manual checks, or strict-mode problems exceed the policy |
| 2 | audits failed or were incomplete, result files were unreadable, or **no results were found** (use `--allow-empty` if that is expected) |
| 3 | invalid command line |

Execution problems take precedence: a run where some audits failed exits with 2 even when there are also findings.

Other commands:

```bash
blazor-a11y baseline --input a11y-results --baseline a11y-baseline.json   # refuses incomplete results unless --allow-failed
blazor-a11y manual init --name "Checkout" --scope "/checkout" --output a11y/manual/checkout.json
blazor-a11y manual validate a11y/manual/*.json                            # exit 1 when entries are incompletely recorded
```

## Baselines

A baseline is a committed list of known findings (fingerprint, rule, component, scenario, selector, count). Its JSON is sorted and contains no timestamps, so diffs are easy to review.

- **Adopting the toolkit on an existing app:** run the tests, then `blazor-a11y baseline ...`, and commit the file. From then on, only new findings fail.
- **New problems are never hidden.** Matching is by fingerprint and count. A known rule failing on another element, component, scenario or state is new, and so is an additional occurrence.
- **Fixing problems:** `report` lists resolved entries. Regenerate the baseline to lock the improvement in. `--strict` fails while resolved entries remain, and also ignores the baseline for failing.
- When some audits failed, resolved entries are flagged as unreliable, because a finding that disappears may simply not have been checked.

## Suppressions vs. baselines

| | Suppression | Baseline |
| --- | --- | --- |
| Lives in | test code (`AccessibilityAuditOptions.Suppressions`) | committed JSON file |
| Scope | one rule, optionally filtered by selector, component or scenario | exact findings |
| Requires | reason; owner and expiry recommended | — |
| Use for | accepted exceptions (third-party widget, decorative case) | debt you plan to fix |

## Report formats

- **console:** summary plus the first findings of each audit.
- **json:** the full `AccessibilityReport`, schema version `1.0` (see below).
- **junit:** one `testsuite` per audit and one `testcase` per rule.
  - Violations are failures.
  - Inconclusive, potential and suppressed results are `skipped` with a reason that says "not a pass".
  - Audits that failed to execute are reported as errors.
- **sarif:** SARIF 2.1.0 (validated against the OASIS 2.1.0 JSON schema) with rules, levels by impact, logical locations (component path or URL), `partialFingerprints`, `baselineState`, suppressions, and tool execution notifications for failed audits.
  - With `--source-root`, component findings get a file location when exactly one `.razor` file has the component's name. Paths are relative to the working directory when the source root is inside it, so run the CLI from the repository root.
  - With `--sarif-fallback-uri <file>`, findings without a component file (page audits) are anchored to that file, typically the end-to-end test that produced them.
  - GitHub code scanning only displays results that have a file location and a region. File locations therefore carry a file-level region (line 1), and every result has `properties.locationPrecision` set to `file` or `logical`. The line is an anchor, not the source line of the finding.
- **html:** a self-contained report for local investigation.

The JSON schema is versioned. Readers accept `1.x`, and fields are only added within a major version. Top-level shape:

```json
{
  "schemaVersion": "1.0",
  "tool": { "name": "...", "version": "..." },
  "generatedAt": "...",
  "summary": { "audits": 0, "violations": 0, "violationsByWcag": {}, "manualReview": {} },
  "audits": [
    { "name": "...", "status": "Completed|Failed|Skipped", "context": {}, "engines": [], "findings": [], "passedRules": [], "errors": [], "warnings": [] }
  ],
  "manualReviews": [],
  "baseline": { "new": [], "existing": [], "resolved": [], "resolvedIsReliable": true }
}
```

## GitHub Actions

The repository's own workflow (`.github/workflows/ci.yml`) is a complete example. The accessibility part:

```yaml
    env:
      BLAZOR_A11Y_RESULTS_DIR: ${{ github.workspace }}/a11y-results
      BLAZOR_A11Y_BROWSER: chromium
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { global-json-file: global.json }
      - run: dotnet build -c Release
      - run: pwsh tests/MyApp.A11yTests/bin/Release/net10.0/playwright.ps1 install --with-deps chromium
      - run: dotnet test -c Release --no-build
      - run: dotnet tool install --global BlazorAccessibilityToolkit.Cli --version 0.1.0-preview.1
      - run: >
          blazor-a11y report --input a11y-results --baseline a11y-baseline.json --output a11y-report
          --source-root src --sarif-fallback-uri tests/MyApp.A11yTests/PageTests.cs
      - uses: actions/upload-artifact@v4
        if: always()
        with: { name: a11y-report, path: a11y-report }
      - uses: github/codeql-action/upload-sarif@v3   # needs permissions: security-events: write
        if: always()
        with: { sarif_file: a11y-report/accessibility.sarif, category: accessibility }
```

## Azure Pipelines

```yaml
variables:
  BLAZOR_A11Y_RESULTS_DIR: $(Build.SourcesDirectory)/a11y-results
steps:
  - task: UseDotNet@2
    inputs: { useGlobalJson: true }
  - script: dotnet build -c Release
  - pwsh: tests/MyApp.A11yTests/bin/Release/net10.0/playwright.ps1 install --with-deps chromium
  - script: dotnet test -c Release --no-build
  - script: |
      dotnet tool install --global BlazorAccessibilityToolkit.Cli --version 0.1.0-preview.1
      blazor-a11y report --input a11y-results --baseline a11y-baseline.json --output a11y-report --format console --format junit --format html
  - task: PublishTestResults@2
    condition: always()
    inputs: { testResultsFormat: JUnit, testResultsFiles: a11y-report/accessibility-junit.xml, testRunTitle: Accessibility }
  - publish: a11y-report
    condition: always()
    artifact: a11y-report
```

## Determinism

- Reports sort audits and findings deterministically. Baselines contain no timestamps. Fingerprints ignore ports and generated ids.
- Reduced motion is emulated by default, and audits run against a fixed viewport and locale.
- Keep the browser and toolkit versions pinned, so the engine version does not change underneath a baseline.
