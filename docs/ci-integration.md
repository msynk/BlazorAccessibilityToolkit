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
  --format console --format json --format junit --format sarif --format html --format markdown \   # default: all
  --output a11y-report \
  --fail-on serious \                     # none | minor (default) | moderate | serious | critical
  --fail-on-potential --fail-on-inconclusive --fail-on-manual-review \
  --strict \
  --source-root src \                     # map component types to .razor files for SARIF
  --step-summary                          # GitHub Actions: append the Markdown report to the job summary
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
blazor-a11y manual init --name "Site" --template wcag22aa --output a11y/manual/wcag.json   # one item per WCAG 2.2 A/AA criterion
blazor-a11y manual validate a11y/manual/*.json                            # exit 1 when entries are incompletely recorded
blazor-a11y rules                                                         # the toolkit's own rules with status, impact and WCAG
blazor-a11y report --input a11y-results --format openacr,statement --product "Contoso Shop"   # conformance drafts, see below
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
- **html:** a self-contained report for local investigation, including the WCAG coverage table.
- **markdown:** GitHub-flavored Markdown for pull request comments and job summaries: totals, baseline changes, violations by rule, failing audits (collapsed) and the WCAG coverage table.

## WCAG and EN 301 549 coverage

The HTML and Markdown reports roll every finding and manual check up to the WCAG 2.2 A and AA success criteria (`WcagCoverage.Create(report, level)` in code):

| Result | Meaning |
| --- | --- |
| Does not support | an unsuppressed violation maps to the criterion, or a manual check of it failed |
| Supports (manually verified) | every manual check of the criterion passed (with tester and date) or is not applicable, and no violation maps to it |
| Not applicable | every manual check was recorded as not applicable, with a justification |
| Needs review | potential issues, inconclusive results or outstanding manual checks |
| Not evaluated | nothing in the report covers the criterion |

Automated results alone never produce "Supports": axe-core passing a rule does not show that a criterion is met. Each row also shows the EN 301 549 clause (V3.2.1, clause 9 incorporates WCAG 2.1 A and AA; criteria added in WCAG 2.2 show "—"). Combined with the `wcag22aa` manual template, the table is a starting point for an accessibility conformance report (VPAT/ACR) or an EAA accessibility statement; it is not a conformance claim.

To select engine rules by standard, use `AccessibilityStandard.En301549` (axe-core's EN 301 549 tags), `Section508`, `Wcag22A`, `Wcag22AA` (default) or `Wcag22AAA`.

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

### Conformance report drafts (OpenACR, accessibility statement)

Two formats are written only when you ask for them:

```bash
blazor-a11y report --input a11y-results --manual-review a11y/manual/wcag22aa.json --output a11y-report \
  --format openacr,statement --product "Contoso Shop" --product-version 3.1 --organization Contoso \
  --contact "Accessibility team" --contact-email a11y@contoso.example --level AA
```

- `openacr` writes `accessibility-conformance-report.openacr.yaml` in the [OpenACR](https://github.com/GSA/openacr) format of the US General Services Administration, catalog `2.5-edition-wcag-2.2-en` (the VPAT 2.5 WCAG edition), for web content. Each criterion's adherence follows the coverage table: `supports` only after a recorded, passed manual review; `does-not-support` when a violation or failed manual check maps to it; `not-applicable` when every manual check says so; otherwise `not-evaluated`, with a note on the evidence that exists.
- `statement` writes `accessibility-statement.md`, a draft accessibility statement with the sections of the EU model statement and the information the European Accessibility Act asks service providers to publish: compliance status, non-accessible content (the failing criteria with their EN 301 549 clauses), preparation of the statement, feedback and contact, and the enforcement procedure.

Both are **drafts**. They never claim full conformance, leave `TODO` placeholders for what automation cannot know, and must be completed and reviewed by an accessibility specialist before they are published. In code, use `OpenAcrReportFormatter` and `AccessibilityStatementFormatter` with a `ConformanceReportInfo`.

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
          --source-root src --sarif-fallback-uri tests/MyApp.A11yTests/PageTests.cs --step-summary
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
