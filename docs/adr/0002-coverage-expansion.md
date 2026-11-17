# ADR 0002: Coverage expansion after the cross-stack survey

- Status: accepted
- Date: 2026-11-17

## Context

We compared the toolkit with accessibility tooling in other stacks:

- engines and wrappers: axe-core 4.14, IBM Equal Access, Siteimprove Alfa, pa11y, Lighthouse;
- linters: `eslint-plugin-jsx-a11y`, `eslint-plugin-vuejs-accessibility`, `@angular-eslint/template`, axe Linter;
- semi-automated tools: Accessibility Insights, axe DevTools guided tests, Storybook's a11y addon;
- assertions: Testing Library role queries, Playwright accessibility assertions, Guidepup's virtual screen reader;
- native platforms: Android ATF and Compose, iOS `performAccessibilityAudit`, Flutter guidelines, Axe.Windows;
- standards: WCAG 2.2, EN 301 549 and the European Accessibility Act, VPAT/ACR.

Five gaps mattered most for Blazor teams:

1. There was no compile-time check of Razor markup. Every other major UI stack has one; .NET has none.
2. WCAG 2.2 and adaptation criteria that axe-core does not test were not covered: focus not obscured (2.4.11), reflow (1.4.10), text spacing (1.4.12), content on hover or focus (1.4.13), and user preferences such as forced colors.
3. Focus indicators were only detected from outlines and box-shadows.
4. Composite widgets beyond dialogs, disclosures, tabs and menu buttons had no APG verifier, and client-side page changes were not checked.
5. Reports did not connect findings and manual reviews to success criteria, which conformance reports (VPAT/ACR, EN 301 549 statements under the EAA) need.

## Decisions

### Razor analyzer reads additional files

The analyzer reads `.razor` files, which the Razor SDK passes to analyzers as additional files. It does not analyze the C# that the Razor source generator produces:

- Diagnostics point at the Razor file and line without mapping generated code.
- The analyzer does not depend on the generator's output, which changes between SDK versions.

The scanner is tolerant rather than a full Razor parser. Anything it cannot judge statically counts as present and correct: dynamic values, `@attributes` splatting, content from child components and expressions. The analyzer complements rendered-markup audits; it never replaces them.

It targets Roslyn 4.8, so it loads in every .NET 8 and later SDK. It ships as a separate development-dependency package, because it belongs in component libraries, not test projects.

### Heuristics stay potential issues

These checks report `PotentialIssue`, consistent with ADR 0001:

- reflow, text spacing, obscured focus;
- input purpose, required markers, NavLink `aria-current`;
- the Blazor error UI.

They measure layout or naming conventions at one moment in time. Whether content is really lost, or a field really collects personal data, needs a person.

### Focus indicators are a visible style difference

Before keyboard navigation, the toolkit records the unfocused computed style of every focusable element: the element itself, its pseudo-elements and its parent. A focus indicator is any visible difference after focus. Properties without a visible effect are normalized away, for example an outline offset while there is no outline.

This replaces the outline/box-shadow heuristic wherever a baseline exists, and keeps that heuristic as the fallback.

Screenshot diffs, as used by some tools for 2.4.13, were rejected: they depend on fonts, antialiasing and timing, and would make results browser-specific.

### Coverage never claims support from automation

`WcagCoverage` marks a criterion "Supports" only when a recorded manual review passed it (with tester and date) and no violation maps to it. Passing automated rules never establishes support, because rules cover a fraction of each criterion.

EN 301 549 clauses are derived from the WCAG number (clause 9 of V3.2.1 for WCAG 2.1 A and AA). WCAG 2.2-only criteria are shown without a clause until V4 is cited.

## Consequences

- The `report` format list grows by `markdown`, and the HTML report gains a coverage section. The JSON schema is unchanged.
- The toolkit script version is 9. Pages that already contain version 8 get the new helpers on the next audit.
- New browser findings can appear in existing suites (for example `blazor-input-purpose-missing`). They are potential issues, so default assertions and gates do not fail on them; strict modes and baselines show them as new.

Deferred items are listed in [the roadmap](../roadmap.md).
