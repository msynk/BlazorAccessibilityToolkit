# Changelog

## 0.1.0-preview.1 — 2026-11-04

First preview release.

### Packages

- `BlazorAccessibilityToolkit`:
  - normalized findings with statuses (violation, potential issue, inconclusive, manual review),
  - WCAG 2.2 catalog and tag mapping,
  - framework-agnostic assertions,
  - audit builder with evidence redaction, fingerprints and suppressions (reason, owner, expiry),
  - count-aware baselines and a quality gate with CI exit codes,
  - manual-review checklists,
  - reports: console, JSON (schema 1.0), JUnit, SARIF 2.1.0 and HTML,
  - per-audit result files (`BLAZOR_A11Y_RESULTS_DIR`).
- `BlazorAccessibilityToolkit.ComponentTesting`:
  - bUnit `AccessibilityFixture` and `cut.AuditAccessibilityAsync()`,
  - Razor markup rules (Blazor click handlers, `ValidationMessage` association, invalid controls without error references, duplicate IDs, broken ID references, radio and fieldset grouping),
  - browserless fallback rules,
  - component attribution from the render tree,
  - pluggable markup engines.
- `BlazorAccessibilityToolkit.Playwright`:
  - axe-core 4.14 audits of pages and regions through Deque.AxeCore.Playwright,
  - browser diagnostics (expanded and tab-selection state, ID references, validation messages, grouping),
  - accessible name, role and state inspection,
  - keyboard scenarios, focus-order capture with trap detection, live-region monitoring and analysis,
  - ARIA snapshots (inline and stored), ARIA pattern verifiers (modal dialog, disclosure, tabs, menu button),
  - a pooled browser engine for bUnit markup,
  - the in-process interactive component host (`BlazorTestHost`).
- `BlazorAccessibilityToolkit.Cli` (`blazor-a11y`): `report` (with SARIF file locations for GitHub code scanning via `--source-root` and `--sarif-fallback-uri`), `baseline`, and `manual init|validate`.

### Engines and environments

- axe-core 4.14.0, Microsoft.Playwright 1.64.1 (Chromium 156, Firefox 157, WebKit 27.2), bUnit 2.13.3, .NET 10.
- Tested in Chromium, Firefox and WebKit, with xUnit v3, NUnit and MSTest. See [limitations](docs/limitations.md).
