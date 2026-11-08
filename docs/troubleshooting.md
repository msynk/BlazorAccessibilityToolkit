# Troubleshooting

## Browsers

**`BrowserNotAvailableException: The 'chromium' browser is not installed`.** Install the browser explicitly with the script generated in your test output:

```bash
pwsh bin/Debug/net10.0/playwright.ps1 install chromium
```

On Linux CI, add `--with-deps` to also install system libraries. The browser revision is tied to the Microsoft.Playwright version (1.64.1 uses Chromium 156, Firefox 157 and WebKit 27.2). After upgrading Playwright, install again.

**Watching a test.** Set `BLAZOR_A11Y_HEADED=1`. You can also use Playwright tracing on the session's `Context`.

## Interactive component host

**`_framework/blazor.web.js is not available (HTTP 404)`.** The test project must:

1. use `<Project Sdk="Microsoft.NET.Sdk.Web">`,
2. be an executable (`<OutputType>Exe</OutputType>`; the default for xUnit v3, and add it for NUnit and MSTest),
3. contain at least one `.razor` file, or set `<RequiresAspNetWebAssets>true</RequiresAspNetWebAssets>`.

**`HostedComponentException: ... did not finish rendering`.** The message includes server log errors and browser console errors. Typical causes:

- the component throws during initialization,
- a required service is not registered in `BlazorTestHostOptions.ConfigureServices`,
- a JavaScript module path is wrong.

**Styles or JS modules from a Razor class library are missing.** Reference the library from the test project, so its `_content/<Library>/...` static web assets are served, and add stylesheet URLs to `BlazorTestHostOptions.Stylesheets`.

## Flaky tests

**Key presses do nothing in an end-to-end test.** The page was prerendered and the circuit had not connected yet, so the presses were lost. Wait for an interactivity signal; the demo app's layout exposes `data-interactive="true"`. Do not wait with fixed delays.

**Expectations time out sporadically on slow CI agents.** Increase `KeyboardScenarioOptions.ExpectTimeout`, `LiveRegionMonitor.DefaultTimeout` or `PatternOptions.Timeout`. Expectations poll, so a larger timeout only costs time when something is really wrong.

**Focus order differs between browsers.**

- WebKit does not move focus to links with Tab (Safari's default setting).
- Headless Firefox keeps focus on the last element instead of leaving the page. The toolkit recognizes this as the end of the sequence.

Write browser-aware expectations, as the demo tests do.

**"Process exited", "The pipe has been ended" or "Target page, context or browser has been closed" under heavy parallel load.** The browser process crashed. We observed this with WebKit on Windows when several test assemblies each run many contexts at once. Affected audits are reported as failed, and the next session relaunches the browser (`AccessibilityBrowser.Relaunches`). Limit parallelism: run test assemblies one at a time with `dotnet test -m:1`, lower xUnit's `maxParallelThreads`, or share fewer concurrent contexts.

**Color contrast results differ between bUnit markup and the real app.** Markup audits only see the stylesheets you pass in `MarkupDocumentOptions`. Audit the real page, or the interactive host with the app stylesheets, for authoritative contrast results.

**Audit fails with "matched N elements".** Region audits need a locator that matches exactly one element.

## Results and CI

**`No audit results were found`, exit code 2.** `BLAZOR_A11Y_RESULTS_DIR` was not set while the tests ran, the tests did not run, or `--input` points elsewhere. The variable is read once per test process.

**Every finding looks "new" against the baseline.** The baseline was created with a different fingerprint version, or scenario and state names changed. Fingerprints include the component type or URL path, the scenario and the state. Regenerate the baseline after renaming tests.

**`Could not locate the snapshot directory`.** Deterministic CI builds map source paths, so the caller path does not exist. The toolkit also searches upward from the test output for the mapped path. Otherwise set `AriaSnapshotOptions.Directory`, set the `BLAZOR_A11Y_SNAPSHOT_DIR` environment variable, or set `<DeterministicSourcePaths>false</DeterministicSourcePaths>` in test projects.

**A snapshot was "created" and the test failed.** This is intended. Review the new `.aria.yml`, commit it, and run the test again. On CI, missing snapshots are never written.
