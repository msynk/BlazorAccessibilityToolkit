# Extending the toolkit

## Custom page-level checks

Use `AccessibilityAuditBuilder` so that custom findings get the same treatment as built-in ones: evidence redaction and truncation, fingerprints, suppressions, deterministic ordering, failure semantics and results persistence.

```csharp
public static async Task<AccessibilityAudit> AuditHeadingLevelsAsync(IPage page)
{
    var rule = new AccessibilityRule
    {
        Id = "acme-heading-skip",
        Description = "Heading levels should not skip (h2 followed by h4).",
        Help = "Heading level skipped",
        Remediation = "Use the next heading level, or style the heading instead of changing its level.",
        Status = FindingStatus.PotentialIssue,
        Impact = AccessibilityImpact.Moderate,
        Wcag = ["1.3.1"],
        HelpUrl = "https://www.w3.org/WAI/tutorials/page-structure/headings/",
        Environments = ["browser"],
    };

    var builder = new AccessibilityAuditBuilder("headings", new AccessibilityContext { Kind = AuditTargetKind.Page, Url = page.Url });
    builder.AddEngine("acme-rules", "1.0");
    try
    {
        var levels = await page.Locator("h1,h2,h3,h4,h5,h6").EvaluateAllAsync<int[]>("els => els.map(e => +e.tagName[1])");
        for (var i = 1; i < levels.Length; i++)
        {
            if (levels[i] > levels[i - 1] + 1)
            {
                builder.AddFinding(rule.CreateFinding(new FindingTarget { Selector = $"h{levels[i]}:nth-of-type(1)" }));
            }
        }

        if (!builder.HasErrors)
        {
            builder.AddPassedRule(rule.Id);
        }
    }
    catch (PlaywrightException ex)
    {
        builder.AddError("acme-rules", ex);   // the audit becomes Failed; it can never pass silently
    }

    return builder.Build();
}
```

Guidelines:

- Report confident failures as `Violation`, and heuristics as `PotentialIssue`.
- Prefix rule ids with your organization's name so they cannot collide with axe-core or toolkit rules.
- Do not reimplement axe-core rules. Add rules for patterns axe-core cannot see.
- Record an error whenever the check could not run. Never return an empty, passing audit in that case.
- Persist custom audits for CI with `new AccessibilityResultStore(dir).Save(audit)`, or read the `BLAZOR_A11Y_RESULTS_DIR` environment variable yourself.

## Custom markup engines

Implement `IMarkupAuditEngine` to audit bUnit markup with another engine, for example a different browser automation library or an in-house service you control.

- Each element in `MarkupAuditRequest.BodyHtml` carries a `data-a11y-ord` attribute. Report it as `FindingTarget.ElementOrdinal`, and the toolkit attributes findings to Razor components.
- Remove the attribute before you produce evidence.
- Return `ExecutionError`s rather than throwing, unless the failure is a programming error.

```csharp
public sealed class MyEngine : IMarkupAuditEngine
{
    public string Name => "my-engine";

    public async Task<MarkupEngineResult> AuditAsync(MarkupAuditRequest request, CancellationToken cancellationToken = default)
    {
        // ... run your engine on request.BodyHtml with request.Options ...
        return new MarkupEngineResult { Findings = findings, Engines = [new EngineInfo { Name = "my-engine", Version = "2.1" }] };
    }
}

var settings = ComponentAuditSettings.Default.WithEngine(new MyEngine());
```

## Reusable fixtures and scenarios

Wrap repeated setup in your own helpers. Fixtures are plain objects and compose naturally:

```csharp
public static class A11yFixtures
{
    public static AccessibilityFixtureBuilder<T> ForShopComponent<T>(this A11yEnvironment env) where T : IComponent =>
        AccessibilityFixture.For<T>()
            .WithSettings(env.Markup)
            .WithServices(s => s.AddScoped<ICartService, FakeCartService>())
            .WithCascadingValue(new ShopTheme("light"));
}

public static class DialogScenarios
{
    // Reusable keyboard scenario for every dialog in the design system.
    public static KeyboardScenario OpenAndDismissWithEscape(this KeyboardScenario scenario, ILocator trigger, ILocator dialog) => scenario
        .FocusOn(trigger).RememberFocus("trigger")
        .PressEnter().ExpectFocusWithin(dialog).ExpectFocusContainedWithin(dialog)
        .Audit("open")
        .PressEscape().ExpectFocusRestored("trigger");
}
```

For standard widgets, prefer the built-in `AriaPatterns` verifiers. They encode the WAI-ARIA Authoring Practices keyboard models.

## Custom report formats

Implement `IReportFormatter` (`Name`, `DefaultFileName`, `Write(report, writer)`). Pass it an `AccessibilityReport` created with `AccessibilityReport.Create(audits, checklists, baseline)`, or loaded with `AccessibilityReport.Load(path)`.
