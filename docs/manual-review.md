# Manual review

Automated rules cannot decide many success criteria: logical focus order, meaningful link text, reflow and zoom, contrast over images, error recovery, motion, and how screen readers actually behave. The toolkit tracks these checks explicitly, so that untested criteria never look like passes.

## Checklists

```bash
blazor-a11y manual init --name "Checkout" --scope "/checkout" --output a11y/manual/checkout.json
```

This creates the standard checklist (`ManualReviewTemplates.Standard`). Each item has an id, a title, guidance and WCAG criteria. The items cover:

- keyboard operation and character key shortcuts
- focus order, including overlays and focus return
- visible and unobscured focus
- names, label-in-name and headings
- zoom and reflow
- text spacing
- contrast in all states
- use of color and sensory characteristics
- error handling
- instructions and status messages
- content on hover or focus
- motion and timing
- pointer alternatives and target size
- representative screen reader testing

Record results by editing the JSON:

```json
{
  "id": "keyboard-operation",
  "title": "Every function is operable with the keyboard alone ...",
  "wcag": ["2.1.1", "2.1.2", "2.1.4"],
  "status": "Passed",
  "tester": "Ana Lima",
  "date": "2026-11-04",
  "notes": "Checked open, navigate and close with keyboard only.",
  "environments": ["Chrome 156 + NVDA 2026.1", "Safari 19 + VoiceOver"],
  "evidence": ["https://example.org/recordings/checkout-keyboard.mp4"]
}
```

| Status | Rule |
| --- | --- |
| `NotTested` | default |
| `RequiresReview` | flagged for review, for example after a redesign |
| `Passed` | **requires `tester` and `date`**; otherwise it is treated as `RequiresReview` |
| `Failed` | counts as a violation; describe the failure in `notes` |
| `NotApplicable` | **requires `justification`**; otherwise it is treated as `RequiresReview` |

`blazor-a11y manual validate <files>` lists incompletely recorded entries and exits with 1 when there are any.

## In reports and gates

Pass `--manual-review` to `blazor-a11y report`:

- The summary shows how many checks passed, failed, are not applicable, or are still outstanding.
- The HTML report lists every item. Items whose recorded status was not accepted are shown as "recorded as Passed, incomplete".
- Failed manual checks fail the gate by default.
- Outstanding checks are reported as notes, and fail the gate with `--fail-on-manual-review`. Use that for release branches.

In code, `ManualReviewEvaluator.ToFindings(checklist)` turns outstanding items into `ManualReviewRequired` findings and failed items into `Violation` findings, if you want them inside audits.

## Recommended practice

- Keep one checklist per feature or page in the repository, next to the tests. Review changes to it like code.
- Re-open items (`RequiresReview`) when the related component changes significantly.
- Record the assistive technology and browser versions in `environments`. Results differ between combinations.
- A keyboard scenario test and a manual check complement each other. The test prevents regressions; the review confirms the experience makes sense.
