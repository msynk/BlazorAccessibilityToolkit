# Upgrading

## Engine and browser upgrades are deliberate

Pinned runtime dependencies are listed in `Directory.Packages.props`:

| Package | Pinned | Affects |
| --- | --- | --- |
| Deque.AxeCore.Playwright | 4.14.0 (axe-core 4.14) | which rules exist, impacts, messages, selectors |
| Deque.AxeCore.Commons | 4.14.0 | the axe-core script the development overlay serves; keep it at the Deque.AxeCore.Playwright version |
| accessibility-checker-engine (IBM Equal Access) | 4.0.34, vendored as `src/BlazorAccessibilityToolkit.Playwright/Scripts/EqualAccess/ace.js` | the second engine's rules and messages (`IbmEqualAccess`); see the README next to the file, and run `IbmEqualAccessTests` and the demo's `with_the_ibm_engine` tests in every browser |
| Microsoft.Playwright | 1.64.1 | browser versions, ARIA snapshot format, keyboard behavior |
| ModelContextProtocol.Core | 2.4.0 | the MCP protocol versions `blazor-a11y mcp` speaks; run `McpServerTests` after upgrading |
| Axe.Windows | 2.4.2 | the rules and messages of the native window audits (`BlazorAccessibilityToolkit.Windows`); run `NativeAuditTests` on Windows |
| Microsoft.AspNetCore.Components.WebView.WindowsForms | 10.0.120 | the Blazor Hybrid test app only (`tests/Hybrid`); run `BlazorHybridAppTests` on Windows |
| bunit | 2.13.3 | markup and render tree used by component audits |
| Microsoft.CodeAnalysis.CSharp | 4.8.0 | the oldest compiler that loads the Razor analyzer (.NET 8 SDK and later); raise it only when an analyzer API needs it |

Procedure for upgrading axe-core or Playwright:

1. Update the version in `Directory.Packages.props`, then install the new browsers (`playwright.ps1 install`).
2. Run the engine contract test in each browser:

   ```bash
   BLAZOR_A11Y_BROWSER=chromium dotnet test tests/BlazorAccessibilityToolkit.Playwright.Tests --filter EngineContract
   ```

   It pins the axe-core major.minor version (`EngineContractTests.PinnedAxeMajorMinor`) and the normalized findings for a fixed document. Review the diff, update the golden files with `BLAZOR_A11Y_UPDATE_SNAPSHOTS=1`, and change the pinned version.
3. Re-run the ARIA snapshot tests. Review `.received.aria.yml` diffs and accept them with `BLAZOR_A11Y_UPDATE_SNAPSHOTS=1`.
4. Run the full suite in Chromium, Firefox and WebKit.
5. Regenerate baselines when rule output changed. New rules can add findings, which will show up as new.
6. Record the changes in `CHANGELOG.md`: new or removed rules and changed impacts.

## W3C ACT Rules test cases and the language subtag registry

The analyzer's ACT rules implementation report ([act-rules.md](act-rules.md)) runs on a snapshot of the W3C test cases, and `BA11Y028` uses a copy of the IANA Language Subtag Registry. Refresh them deliberately:

```bash
BLAZOR_A11Y_UPDATE_ACT_TESTCASES=1 dotnet test tests/BlazorAccessibilityToolkit.Analyzers.Tests --filter Update_the_test_case_snapshot
BLAZOR_A11Y_UPDATE_LANGUAGE_SUBTAGS=1 dotnet test tests/BlazorAccessibilityToolkit.Analyzers.Tests --filter Update_the_language_subtags
BLAZOR_A11Y_UPDATE_SNAPSHOTS=1 dotnet test tests/BlazorAccessibilityToolkit.Analyzers.Tests --filter ActRulesTests
```

The first command downloads only new test cases and waits between requests, because the W3C server limits them. Review the changed report: a new difference from the expected outcome is either an analyzer bug to fix or needs an explanation in `ActRuleImplementation.KnownDifferences`.

## Fingerprints and report schema

- The fingerprint algorithm is versioned (`FindingFingerprint.Version`, currently `v1`). Baselines store the version, and loading a baseline with a different version fails with instructions to regenerate it.
- The JSON report schema is versioned (`AccessibilityReport.CurrentSchemaVersion`, currently `1.0`). Fields are only added within `1.x`. Readers reject other major versions.

## Migration notes

### 0.1.0-preview.1

First release. No migrations.
