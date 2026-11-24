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
    Tags = AccessibilityStandard.Wcag22AA,        // Wcag20A/AA, Wcag21A/AA, Wcag22A, Wcag22AA (default), Wcag22AAA, En301549, Section508, Rgaa, TrustedTester
    IncludeBestPractices = true,                  // tagged best-practice; flagged IsBestPractice
    IncludeExperimental = false,
    EnabledRules = ["target-size"],               // in addition to the tags
    DisabledRules = ["color-contrast"],
    ExcludeSelectors = ["#third-party-widget"],
    ToolkitDiagnostics = true,
    IbmEqualAccess = false,                       // also run the IBM Equal Access engine (browser audits)
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
| `blazor-expanded-state-mismatch` | Potential | 4.1.2 | browser | `aria-expanded` contradicts the visibility of the `aria-controls` target (triggers that are not rendered are skipped) |
| `blazor-tab-selection-mismatch` | Potential | 4.1.2, 1.3.1 | browser | not exactly one selected tab, or panel visibility contradicts selection |
| `blazor-navlink-missing-aria-current` | Potential | 1.3.1 | markup, browser | a link with the `active` class inside a `nav` without `aria-current` (.NET 10's `NavLink` sets it; hand-written and library links often do not) |
| `blazor-input-purpose-missing` | Potential | 1.3.5 | markup, browser | fields named or typed like personal data (`Model.Email`, `PhoneNumber`, `FirstName`, `PostalCode`, `type="password"` ...) without `autocomplete`; the finding suggests the token |
| `blazor-required-not-exposed` | Potential | 1.3.1, 3.3.2 | markup, browser | a label marks the field as required (`*`, `(required)`) but the control has neither `required` nor `aria-required="true"` (`[Required]` adds neither) |
| `blazor-error-ui-not-accessible` | Potential | 2.1.1, 4.1.2, 4.1.3 | browser | `#blazor-error-ui` is not a live region, or its `.dismiss` control is a `span` that keyboard users cannot reach (the project template's markup) |
| `blazor-reflow-horizontal-scroll` | Potential | 1.4.10 | browser | see [adaptation](interactive-testing.md#adaptation-reflow-text-spacing-and-display-preferences) |
| `blazor-text-spacing-clipped` | Potential | 1.4.12 | browser | see [adaptation](interactive-testing.md#adaptation-reflow-text-spacing-and-display-preferences) |
| `blazor-motion-not-reduced` | Potential | 2.2.2, 2.3.3 | browser | animations that keep moving content, or run longer than five seconds, while `prefers-reduced-motion: reduce` is emulated, and media that keeps playing; see [adaptation](interactive-testing.md#adaptation-reflow-text-spacing-and-display-preferences) |
| `blazor-orientation-locked` | Potential | 1.3.4 | browser | one orientation rotates the page, shows a "rotate your device" message or hides most of the text |
| `blazor-paste-blocked` | Potential | 3.3.8 | markup, browser | `@onpaste:preventDefault` (bUnit sees it as an attribute; the browser check dispatches an empty paste event to credential fields and reports a cancelled one; this runs the page's paste handlers, and disabling the rule skips it), or a password field with `autocomplete="off"` |
| `blazor-dragging-alternative` | Potential | 2.5.7 | markup, browser | `draggable="true"` content (one finding per container): check that a single-pointer alternative exists |
| `blazor-fragment-link-leaves-page` | Potential | 2.4.1 | browser | an `<a href="#id">` that resolves to another document because of `<base href>` (Blazor's default `<base href="/">` sends it to the home page). A click handler that cancels navigation fixes it only once the page is interactive |
| `blazor-virtualized-set-size` | Potential | 1.3.1 | browser | items rendered by `<Virtualize>` (between its `aria-hidden` spacers, when items are left out) without `aria-posinset` and `aria-setsize` |
| `blazor-duplicate-clickable-bounds` | Potential (best practice) | — | browser | an interactive element nested in another one with the same size and position (Android ATF's `DuplicateClickableBoundsCheck`) |
| `blazor-help-inconsistent` | Potential | 3.2.6 | browser | `page.AuditConsistentHelpAsync(baseAddress, paths)`: a help mechanism repeated across pages appears in a different order or landmark |
| `blazor-ambiguous-link-name` | Potential | 2.4.4, 2.4.9 | browser | links with the same accessible name that lead to different destinations ("Read more", "Edit"); one finding per name |
| `blazor-role-in-name` | Potential (best practice) | — | browser | button names ending in "button", link names starting with "Link to" or ending in "link" |
| `blazor-reconnect-ui-not-accessible` | Potential | 4.1.2, 4.1.3 | browser | `#components-reconnect-modal` (the .NET reconnect dialog) has no accessible name, or its status messages are not in a live region |
| `blazor-apca-contrast` | Potential (best practice) | none | browser | `page.AuditApcaContrastAsync()`: text below the APCA lightness contrast its size and weight call for; information next to WCAG 2, see [APCA contrast](interactive-testing.md#apca-contrast) |
| `blazor-target-size-enhanced` | Potential | 2.5.5 | browser | `page.AuditTargetSizeAsync()`: pointer targets smaller than 44 by 44 CSS pixels (configurable, for example 48 for Material), except inline links; axe-core covers the 24 pixel minimum of 2.5.8 |
| `blazor-redundant-entry` | Potential | 3.3.7 | markup, browser | a second field for information already asked for (`ConfirmEmail`, `EmailConfirmation`, `repeat-phone` ...); repeated passwords are exempt |
| `blazor-markup-image-alt`, `-control-name`, `-form-label`, `-aria-hidden-focusable`, `-invalid-aria-attribute`, `-invalid-role`, `-positive-tabindex` | mostly Violation | various | markup (fallback only) | conservative browserless equivalents of axe rules, only when no browser engine runs |
| `blazor-focus-*` | Potential | 2.1.2, 2.4.3, 2.4.7, 2.4.11, 1.3.2 | browser | see [focus order](interactive-testing.md#focus-order), including `blazor-focus-order-visual-mismatch` |
| `blazor-focus-not-visible`, `blazor-focus-appearance` | Violation | 2.4.7, 2.4.13 (AAA) | browser | opt-in screenshot measurement of focus indicators; see [measuring focus indicators](interactive-testing.md#measuring-focus-indicators) |
| `blazor-live-region-*` | Potential | 4.1.3 | browser | see [live regions](interactive-testing.md#live-regions) |

The full metadata (descriptions and remediation) is in `ToolkitRules.All`; `blazor-a11y rules` prints it. Compile-time checks of `.razor` files (`BA11Y001` to `BA11Y038`) are described in [Razor analyzers](analyzers.md).

## Fingerprints

Fingerprints identify a finding across runs. They are built from the engine, rule, status class, component type or URL *path*, scenario, state and a normalized selector. GUIDs, numbers with four or more digits, long hex ids and Blazor CSS-isolation attributes are removed from the selector.

They exclude evidence, messages, host, port and timestamps, so changing a port or a generated id does not create a "new" finding. Two different elements whose selectors differ only in generated numbers share a fingerprint. Baselines therefore count occurrences, and an extra one is still reported as new.
