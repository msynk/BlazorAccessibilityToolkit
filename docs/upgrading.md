# Upgrading

## Engine and browser upgrades are deliberate

Pinned runtime dependencies are listed in `Directory.Packages.props`:

| Package | Pinned | Affects |
| --- | --- | --- |
| Deque.AxeCore.Playwright | 4.14.0 (axe-core 4.14) | which rules exist, impacts, messages, selectors |
| Microsoft.Playwright | 1.64.1 | browser versions, ARIA snapshot format, keyboard behavior |
| bunit | 2.13.3 | markup and render tree used by component audits |

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

## Fingerprints and report schema

- The fingerprint algorithm is versioned (`FindingFingerprint.Version`, currently `v1`). Baselines store the version, and loading a baseline with a different version fails with instructions to regenerate it.
- The JSON report schema is versioned (`AccessibilityReport.CurrentSchemaVersion`, currently `1.0`). Fields are only added within `1.x`. Readers reject other major versions.

## Migration notes

### 0.1.0-preview.1

First release. No migrations.
