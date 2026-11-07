# Component testing with bUnit

`BlazorAccessibilityToolkit.ComponentTesting` audits the markup that bUnit renders. It is fast (about 1.5 ms per fixture and audit without a browser) and needs no browser unless you add one.

## What is checked, and what is not

| Check | Browserless | With `PlaywrightMarkupAuditEngine` |
| --- | --- | --- |
| Blazor `@onclick` (and other pointer handlers) on elements without role, focusability or keyboard handler | yes | yes |
| axe-core WCAG rules (names, roles, ARIA validity, structure) | fallback subset | yes |
| Color contrast | no | yes, when you supply stylesheets |
| `ValidationMessage` association, invalid controls without an error reference, broken ID references, duplicate IDs, radio grouping, fieldset legends | yes | yes |
| Keyboard behavior, focus, scripts, real layout | no | no: use the interactive host |

These assertions operate on **rendered markup only**. JavaScript interop is mocked (bUnit loose mode), and CSS is absent unless you pass it in. Use the [interactive host](interactive-testing.md) for anything that depends on events, focus or scripts.

## Fixtures

```csharp
await using var fixture = await AccessibilityFixture.For<SettingsPanel>()
    .WithParameter(p => p.Title, "Settings")
    .WithParameters(p => p.Add(c => c.OnSave, () => { }))              // bUnit's parameter builder
    .WithChildContent("<p>Extra</p>")
    .WithCascadingValue(theme)                                         // optionally named: WithCascadingValue(v, "Theme")
    .WithServices(s => s.AddSingleton<IClock>(new FakeClock()))         // includes localization, HttpClient fakes ...
    .WithAuthorization(auth => auth.SetAuthorized("ana"))
    .WithContext(ctx => ctx.JSInterop.SetupVoid("focusFirst"))          // anything else on BunitContext
    .WithOptions(new AccessibilityAuditOptions { IncludeBestPractices = true })
    .WithName("settings panel")                                        // scenario name in reports
    .BuildAsync();

var initial = await fixture.AuditAsync("initial");
fixture.Component.Find("button.expand").Click();                       // change state with bUnit
var expanded = await fixture.AuditAsync("expanded");
```

- Each fixture creates its own `BunitContext`, so fixtures share no state and can run in parallel. JS interop is in loose mode unless you change it with `WithContext`.
- Wait for asynchronous rendering with bUnit, for example `fixture.Component.WaitForElement("ul")` or `WaitForState(...)`, before you audit. The toolkit does not guess at how long to wait.
- An audit of a component that rendered nothing fails, because nothing was checked.

Already using bUnit? Audit any rendered component directly:

```csharp
var cut = Render<IconButton>(p => p.Add(c => c.Label, "Close"));
var audit = await cut.AuditAccessibilityAsync("labelled", settings);
```

## Adding a browser engine

```csharp
var engine = new PlaywrightMarkupAuditEngine(browser);    // pooled pages; dispose it with your fixture
var settings = new ComponentAuditSettings
{
    Engines = [engine],
    Document = new MarkupDocumentOptions
    {
        StylesheetFiles = [Path.Combine(AppContext.BaseDirectory, "assets", "app.css")],
        Language = "en",
    },
};
```

- When an engine is configured, the browserless fallback rules are not run, because axe-core covers them. The Razor-specific click-handler rule still runs.
- Page-level rules such as `region`, `landmark-one-main` and `page-has-heading-one` are disabled for component audits unless you set `PageLevelRulesForComponents`.
- To get stylesheets from a Razor class library into the test output, add `<Content Include="..\MyLib\wwwroot\app.css" Link="assets\app.css" CopyToOutputDirectory="PreserveNewest" />`.

## Component attribution

Every finding carries `Component`:

```text
Rendered by: Toolbar > IconButton
```

- `Method = "render-tree"`: derived from bUnit's component render tree. The path lists the components that rendered the element, from the fixture root to the innermost component.
- Child content (`RenderFragment`) is attributed to the component that *renders* it, which can differ from the component that authored it.
- `Method = "fixture-root"`: attribution fell back to the fixture root, and the audit carries a warning that says why. This happens when the HTML parser changes the tree. For example, a component that renders only `<td>` elements is valid inside a parent's `<tr>`, but parsed on its own the `<td>` is dropped.
- The toolkit never reports Razor source lines. The CLI can map component types to `.razor` files for SARIF when the file name is unique.

## Razor-specific rules

| Rule | What it catches |
| --- | --- |
| `blazor-click-handler-semantics` | `<div @onclick>` and similar: no interactive role, not focusable, or no `@onkeydown` for Enter/Space. Elements inside a native button or link and `aria-hidden` content are ignored. |
| `blazor-validation-message-association` | `ValidationMessage` text not referenced by any `aria-describedby`/`aria-errormessage`. Blazor does not create this link. |
| `blazor-invalid-without-error-reference` | `aria-invalid="true"` (set by `InputBase`) without a referenced error message. |
| `blazor-duplicate-id`, `blazor-duplicate-id-referenced` | Hard-coded ids in reusable components rendered more than once. |

See [rules](rules.md) for the full catalog.

## Regression tests for known defects

Assert that a defect is still detected, so a regression in the component or in the test setup cannot hide it:

```csharp
var audit = await fixture.AuditAsync();
audit.ShouldHaveFinding("button-name", FindingStatus.Violation);
```
