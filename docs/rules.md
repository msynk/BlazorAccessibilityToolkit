# Rules and findings

## Finding statuses

| Status | Source | Fails `ShouldHaveNoViolations()` by default | Fails the CI gate by default |
| --- | --- | --- | --- |
| `Violation` | axe-core violations and confident toolkit rules | yes | yes |
| `PotentialIssue` | toolkit heuristics that need context | no (`AccessibilityExpectation.Strict` or `FailOnPotentialIssues`) | no (`--fail-on-potential`) |
| `Inconclusive` | axe-core "incomplete" (needs review) | no (`Strict`, `ShouldBeConclusive()`) | no (`--fail-on-inconclusive`) |
| `ManualReviewRequired` | manual checklists | no | no (`--fail-on-manual-review`) |

None of the non-violation statuses is ever reported as passed. Impacts follow axe-core: minor, moderate, serious, critical. A finding without an impact always fails a minimum-impact threshold, which is the conservative choice.

Each finding contains:

- the rule id and engine,
- WCAG success criteria, mapped from axe-core tags against the WCAG 2.2 catalog,
- tags, and whether the rule is a best practice,
- description, help, remediation and help URL,
- the selector (with frame and shadow path), role, accessible name and HTML evidence (redacted, bounded),
- the engine checks and their data,
- the component attribution,
- a fingerprint,
- the suppression, when one applies.

## Rule selection

```csharp
new AccessibilityAuditOptions
{
    Tags = AccessibilityStandard.Wcag22AA,        // Wcag20A, Wcag20AA, Wcag21A, Wcag21AA, Wcag22AA (default)
    IncludeBestPractices = true,                  // tagged best-practice; flagged IsBestPractice
    IncludeExperimental = false,
    EnabledRules = ["target-size"],               // in addition to the tags
    DisabledRules = ["color-contrast"],
    ExcludeSelectors = ["#third-party-widget"],
    ToolkitDiagnostics = true,
    PageLevelRulesForComponents = false,          // region, landmark-one-main, page-has-heading-one ... off for components
    Timeout = TimeSpan.FromSeconds(60),
    MaxEvidenceLength = 1000, MaxNodesPerRule = 50, RedactFormValues = true,   // 0 or less: no limit
}
```

Unknown tags or rule ids fail the audit. They are never silently ignored.

## Suppressions

```csharp
Suppressions =
[
    new Suppression
    {
        RuleId = "color-contrast",
        Selector = "*brand-badge*",          // optional; * wildcards
        Component = "BrandBadge",            // optional; full or simple type name anywhere in the component path
        Scenario = "checkout*",              // optional
        Reason = "Brand colors under review, design ticket 812",
        Owner = "design-system team",
        Expires = new DateOnly(2027, 3, 31),
    },
]
```

- A suppression without a reason is ignored, with a warning.
- An expired suppression stops applying, produces a warning and is listed in `AccessibilityAudit.ExpiredSuppressions`. Strict CI mode fails on it.
- Suppressed findings stay in every report, shown as suppressed with their reason (SARIF marks them as external suppressions). They never count toward gates.

## Toolkit rules

Engine `blazor-a11y`. Environments: *markup* means bUnit markup without a browser, *browser* means the live DOM.

| Rule | Status | WCAG | Environment | Checks |
| --- | --- | --- | --- | --- |
| `blazor-click-handler-semantics` | Potential | 2.1.1, 4.1.2 | markup | Blazor pointer handlers on elements without an interactive role, `tabindex`, or key handler |
| `blazor-validation-message-association` | Potential | 1.3.1, 3.3.1 | markup, browser | `.validation-message` not referenced by `aria-describedby`/`aria-errormessage` |
| `blazor-invalid-without-error-reference` | Potential | 3.3.1 | markup, browser | `aria-invalid` without a referenced error message |
| `blazor-duplicate-id` | Potential (best practice) | — | markup, browser | duplicate ids that nothing references |
| `blazor-duplicate-id-referenced` | Violation | 1.3.1, 4.1.2 | markup | duplicate ids that labels or ARIA reference (axe `duplicate-id-aria` covers this in the browser) |
| `blazor-idref-missing-target` | Potential | 1.3.1, 4.1.2 | markup, browser | IDREF attributes and `label[for]` pointing at missing ids |
| `blazor-radio-group-ungrouped` | Potential | 1.3.1 | markup, browser | related radio buttons without a named fieldset or radiogroup |
| `blazor-fieldset-missing-legend` | Potential | 1.3.1 | markup, browser | fieldset without a legend or name |
| `blazor-expanded-state-mismatch` | Potential | 4.1.2 | browser | `aria-expanded` contradicts the visibility of the `aria-controls` target |
| `blazor-tab-selection-mismatch` | Potential | 4.1.2, 1.3.1 | browser | not exactly one selected tab, or panel visibility contradicts selection |
| `blazor-markup-image-alt`, `-control-name`, `-form-label`, `-aria-hidden-focusable`, `-invalid-aria-attribute`, `-invalid-role`, `-positive-tabindex` | mostly Violation | various | markup (fallback only) | conservative browserless equivalents of axe rules, only when no browser engine runs |
| `blazor-focus-*` | Potential | 2.1.2, 2.4.3, 2.4.7 | browser | see [focus order](interactive-testing.md#focus-order) |
| `blazor-live-region-*` | Potential | 4.1.3 | browser | see [live regions](interactive-testing.md#live-regions) |

The full metadata (descriptions and remediation) is in `ToolkitRules.All`.

## Fingerprints

Fingerprints identify a finding across runs. They are built from the engine, rule, status class, component type or URL *path*, scenario, state and a normalized selector. GUIDs, numbers with four or more digits, long hex ids and Blazor CSS-isolation attributes are removed from the selector.

They exclude evidence, messages, host, port and timestamps, so changing a port or a generated id does not create a "new" finding. Two different elements whose selectors differ only in generated numbers share a fingerprint. Baselines therefore count occurrences, and an extra one is still reported as new.
