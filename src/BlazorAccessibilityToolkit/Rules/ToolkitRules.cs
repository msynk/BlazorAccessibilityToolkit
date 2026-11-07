using System.Collections.Frozen;

namespace BlazorAccessibilityToolkit.Rules;

/// <summary>
/// Catalog of the rules implemented by the toolkit itself. They complement axe-core: they cover Razor-specific
/// patterns (Blazor event handlers, <c>ValidationMessage</c> association), interaction state, focus behavior and
/// live regions, plus a small browserless fallback set for markup-only component tests.
/// </summary>
public static class ToolkitRules
{
    /// <summary>Engine name used on toolkit findings.</summary>
    public const string EngineName = "blazor-a11y";

    /// <summary>Environment: static Razor markup without a browser.</summary>
    public const string Markup = "markup";

    /// <summary>Environment: live browser DOM.</summary>
    public const string Browser = "browser";

    private const string Apg = "https://www.w3.org/WAI/ARIA/apg/";

    /// <summary>Elements with Blazor click handlers that are not keyboard operable or expose no interactive role.</summary>
    public static AccessibilityRule ClickHandlerSemantics { get; } = new()
    {
        Id = "blazor-click-handler-semantics",
        Description = "Elements with Blazor @onclick handlers must be focusable, expose an interactive role and support keyboard activation.",
        Help = "Clickable element is not operable with a keyboard or has no interactive role",
        Remediation = "Use a native <button> (or <a href>) instead of a clickable <div>/<span>. If a custom element is unavoidable, add role=\"button\", tabindex=\"0\" and an @onkeydown handler for Enter and Space.",
        Status = FindingStatus.PotentialIssue,
        Impact = AccessibilityImpact.Serious,
        Wcag = ["2.1.1", "4.1.2"],
        HelpUrl = Apg + "patterns/button/",
        Environments = [Markup],
    };

    /// <summary>Blazor <c>ValidationMessage</c> output not referenced by the invalid control.</summary>
    public static AccessibilityRule ValidationMessageAssociation { get; } = new()
    {
        Id = "blazor-validation-message-association",
        Description = "Validation messages must be programmatically associated with the control they describe.",
        Help = "Validation message is not referenced by aria-describedby or aria-errormessage of any control",
        Remediation = "Give the message element an id and reference it from the input with aria-describedby (or aria-errormessage together with aria-invalid). Blazor's <ValidationMessage> does not create this association automatically.",
        Status = FindingStatus.PotentialIssue,
        Impact = AccessibilityImpact.Moderate,
        Wcag = ["1.3.1", "3.3.1"],
        HelpUrl = "https://www.w3.org/WAI/WCAG22/Techniques/aria/ARIA21",
        Environments = [Markup, Browser],
    };

    /// <summary>Controls marked invalid without an error description reference.</summary>
    public static AccessibilityRule InvalidWithoutErrorReference { get; } = new()
    {
        Id = "blazor-invalid-without-error-reference",
        Description = "Controls marked aria-invalid=\"true\" should reference the text that describes the error.",
        Help = "Invalid control does not reference an error message",
        Remediation = "Reference the visible error text with aria-describedby or aria-errormessage so assistive technologies announce why the value is invalid.",
        Status = FindingStatus.PotentialIssue,
        Impact = AccessibilityImpact.Moderate,
        Wcag = ["3.3.1"],
        HelpUrl = "https://www.w3.org/WAI/WCAG22/Understanding/error-identification.html",
        Environments = [Markup, Browser],
    };

    /// <summary>Duplicate id values (not referenced by ARIA or labels).</summary>
    public static AccessibilityRule DuplicateId { get; } = new()
    {
        Id = "blazor-duplicate-id",
        Description = "id attribute values should be unique. Reusable components with hard-coded ids produce duplicates when rendered more than once.",
        Help = "Duplicate id value",
        Remediation = "Generate ids per component instance (for example from a parameter or a per-instance counter) instead of hard-coding them in the .razor file.",
        Status = FindingStatus.PotentialIssue,
        Impact = AccessibilityImpact.Minor,
        IsBestPractice = true,
        HelpUrl = "https://developer.mozilla.org/docs/Web/HTML/Global_attributes/id",
        Environments = [Markup, Browser],
    };

    /// <summary>Duplicate id values referenced by ARIA attributes or labels (markup fallback; axe-core covers this in the browser).</summary>
    public static AccessibilityRule DuplicateIdReferenced { get; } = new()
    {
        Id = "blazor-duplicate-id-referenced",
        Description = "ids referenced by labels or ARIA attributes must be unique, otherwise the relationship points at the wrong element.",
        Help = "Referenced id is not unique",
        Remediation = "Make the id unique per component instance and update the referencing attribute.",
        Status = FindingStatus.Violation,
        Impact = AccessibilityImpact.Serious,
        Wcag = ["1.3.1", "4.1.2"],
        HelpUrl = "https://www.w3.org/WAI/WCAG22/Understanding/name-role-value.html",
        Environments = [Markup],
    };

    /// <summary>IDREF attributes pointing at ids that do not exist.</summary>
    public static AccessibilityRule IdReferenceMissing { get; } = new()
    {
        Id = "blazor-idref-missing-target",
        Description = "aria-labelledby, aria-describedby, aria-controls, aria-errormessage, aria-owns, aria-activedescendant and label[for] must reference existing ids.",
        Help = "Referenced id does not exist",
        Remediation = "Render the referenced element, or remove the reference. For isolated components, the target may live in a parent component; verify it on the composed page.",
        Status = FindingStatus.PotentialIssue,
        Impact = AccessibilityImpact.Serious,
        Wcag = ["1.3.1", "4.1.2"],
        HelpUrl = "https://www.w3.org/TR/wai-aria-1.2/#valuetype_idref",
        Environments = [Markup, Browser],
    };

    /// <summary>Radio buttons sharing a name without a group label.</summary>
    public static AccessibilityRule RadioGroupUngrouped { get; } = new()
    {
        Id = "blazor-radio-group-ungrouped",
        Description = "Related radio buttons should be grouped in a <fieldset> with a <legend>, or in an element with role=\"radiogroup\" and an accessible name.",
        Help = "Radio buttons are not grouped with a group label",
        Remediation = "Wrap the radio buttons in <fieldset><legend>Question</legend>...</fieldset> (Blazor's <InputRadioGroup> does not add one).",
        Status = FindingStatus.PotentialIssue,
        Impact = AccessibilityImpact.Moderate,
        Wcag = ["1.3.1"],
        HelpUrl = "https://www.w3.org/WAI/tutorials/forms/grouping/",
        Environments = [Markup, Browser],
    };

    /// <summary>Fieldset without a legend.</summary>
    public static AccessibilityRule FieldsetMissingLegend { get; } = new()
    {
        Id = "blazor-fieldset-missing-legend",
        Description = "A <fieldset> should have a <legend> as its first child to name the group.",
        Help = "Fieldset has no legend",
        Remediation = "Add a <legend> that describes the group, or remove the fieldset if the grouping is not meaningful.",
        Status = FindingStatus.PotentialIssue,
        Impact = AccessibilityImpact.Moderate,
        Wcag = ["1.3.1"],
        HelpUrl = "https://www.w3.org/WAI/tutorials/forms/grouping/",
        Environments = [Markup, Browser],
    };

    /// <summary>aria-expanded contradicts the visibility of the controlled element.</summary>
    public static AccessibilityRule ExpandedStateMismatch { get; } = new()
    {
        Id = "blazor-expanded-state-mismatch",
        Description = "aria-expanded must reflect whether the element referenced by aria-controls is shown.",
        Help = "aria-expanded does not match the visibility of the controlled element",
        Remediation = "Update aria-expanded in the same render that shows or hides the controlled content.",
        Status = FindingStatus.PotentialIssue,
        Impact = AccessibilityImpact.Serious,
        Wcag = ["4.1.2"],
        HelpUrl = Apg + "patterns/disclosure/",
        Environments = [Browser],
    };

    /// <summary>Tab selection state inconsistent with the visible panel.</summary>
    public static AccessibilityRule TabSelectionMismatch { get; } = new()
    {
        Id = "blazor-tab-selection-mismatch",
        Description = "In a single-select tablist exactly one tab is aria-selected, and only the selected tab's panel is shown.",
        Help = "Tab selection does not match the displayed panel",
        Remediation = "Set aria-selected=\"true\" on the active tab only, and show only the panel it controls (aria-controls).",
        Status = FindingStatus.PotentialIssue,
        Impact = AccessibilityImpact.Serious,
        Wcag = ["4.1.2", "1.3.1"],
        HelpUrl = Apg + "patterns/tabs/",
        Environments = [Browser],
    };

    /// <summary>Image without a text alternative (markup fallback for axe-core image-alt).</summary>
    public static AccessibilityRule MarkupImageAlt { get; } = new()
    {
        Id = "blazor-markup-image-alt",
        Description = "Images must have an alt attribute (empty for decorative images).",
        Help = "Image has no alt attribute",
        Remediation = "Add alt=\"description\" for informative images or alt=\"\" for decorative images.",
        Status = FindingStatus.Violation,
        Impact = AccessibilityImpact.Critical,
        Wcag = ["1.1.1"],
        HelpUrl = "https://www.w3.org/WAI/tutorials/images/",
        Environments = [Markup],
    };

    /// <summary>Buttons and links without any name source (markup fallback for axe-core button-name/link-name).</summary>
    public static AccessibilityRule MarkupControlName { get; } = new()
    {
        Id = "blazor-markup-control-name",
        Description = "Buttons and links must have an accessible name.",
        Help = "Button or link has no accessible name",
        Remediation = "Add visible text, or aria-label / aria-labelledby for icon-only controls.",
        Status = FindingStatus.Violation,
        Impact = AccessibilityImpact.Serious,
        Wcag = ["4.1.2", "2.4.4"],
        HelpUrl = "https://www.w3.org/WAI/WCAG22/Understanding/name-role-value.html",
        Environments = [Markup],
    };

    /// <summary>Form fields without a label (markup fallback for axe-core label).</summary>
    public static AccessibilityRule MarkupFormLabel { get; } = new()
    {
        Id = "blazor-markup-form-label",
        Description = "Form fields must have a label.",
        Help = "Form field has no label",
        Remediation = "Associate a <label for=\"id\"> with the field, wrap it in a <label>, or use aria-label / aria-labelledby.",
        Status = FindingStatus.Violation,
        Impact = AccessibilityImpact.Serious,
        Wcag = ["1.3.1", "4.1.2"],
        HelpUrl = "https://www.w3.org/WAI/tutorials/forms/labels/",
        Environments = [Markup],
    };

    /// <summary>Focusable content inside aria-hidden (markup fallback for axe-core aria-hidden-focus).</summary>
    public static AccessibilityRule MarkupAriaHiddenFocusable { get; } = new()
    {
        Id = "blazor-markup-aria-hidden-focusable",
        Description = "Elements hidden with aria-hidden=\"true\" must not be or contain focusable elements.",
        Help = "aria-hidden element contains focusable content",
        Remediation = "Remove aria-hidden, or make the content unfocusable (tabindex=\"-1\", disabled, or the inert attribute).",
        Status = FindingStatus.Violation,
        Impact = AccessibilityImpact.Serious,
        Wcag = ["4.1.2"],
        HelpUrl = "https://www.w3.org/TR/wai-aria-1.2/#aria-hidden",
        Environments = [Markup],
    };

    /// <summary>Unknown aria-* attribute (markup fallback for axe-core aria-valid-attr).</summary>
    public static AccessibilityRule MarkupInvalidAriaAttribute { get; } = new()
    {
        Id = "blazor-markup-invalid-aria-attribute",
        Description = "aria-* attributes must be defined by WAI-ARIA.",
        Help = "Unknown ARIA attribute",
        Remediation = "Fix the attribute name (for example aria-labelledby, not aria-labeledby).",
        Status = FindingStatus.Violation,
        Impact = AccessibilityImpact.Critical,
        Wcag = ["4.1.2"],
        HelpUrl = "https://www.w3.org/TR/wai-aria-1.2/#state_prop_def",
        Environments = [Markup],
    };

    /// <summary>Unknown role value (markup fallback for axe-core aria-roles).</summary>
    public static AccessibilityRule MarkupInvalidRole { get; } = new()
    {
        Id = "blazor-markup-invalid-role",
        Description = "role attributes must contain a valid WAI-ARIA role.",
        Help = "Unknown ARIA role",
        Remediation = "Use a role defined by WAI-ARIA, or remove the attribute and use a native element.",
        Status = FindingStatus.Violation,
        Impact = AccessibilityImpact.Critical,
        Wcag = ["4.1.2"],
        HelpUrl = "https://www.w3.org/TR/wai-aria-1.2/#role_definitions",
        Environments = [Markup],
    };

    /// <summary>Positive tabindex (markup fallback for axe-core tabindex).</summary>
    public static AccessibilityRule MarkupPositiveTabIndex { get; } = new()
    {
        Id = "blazor-markup-positive-tabindex",
        Description = "A positive tabindex overrides the DOM focus order and often produces an illogical order.",
        Help = "Element has a positive tabindex",
        Remediation = "Use tabindex=\"0\" and order the DOM to match the visual order. Verify the focus order manually if the positive value is intentional.",
        Status = FindingStatus.PotentialIssue,
        Impact = AccessibilityImpact.Minor,
        IsBestPractice = true,
        Wcag = ["2.4.3"],
        HelpUrl = "https://www.w3.org/WAI/WCAG22/Understanding/focus-order.html",
        Environments = [Markup],
    };

    /// <summary>Pressing Tab left focus on the same element.</summary>
    public static AccessibilityRule FocusStuck { get; } = new()
    {
        Id = "blazor-focus-stuck",
        Description = "Pressing Tab must move focus away from the current element.",
        Help = "Focus did not move when Tab was pressed (possible keyboard trap)",
        Remediation = "Check keydown handlers that call preventDefault for Tab and focus/blur handlers that move focus back.",
        Status = FindingStatus.PotentialIssue,
        Impact = AccessibilityImpact.Critical,
        Wcag = ["2.1.2"],
        HelpUrl = "https://www.w3.org/WAI/WCAG22/Understanding/no-keyboard-trap.html",
        Environments = [Browser],
    };

    /// <summary>Focus cycles inside a region that is not a modal dialog.</summary>
    public static AccessibilityRule FocusTrap { get; } = new()
    {
        Id = "blazor-focus-trap",
        Description = "Tab navigation should be able to leave a region unless the region is a modal dialog.",
        Help = "Focus cycles within part of the page that is not a modal dialog (possible keyboard trap)",
        Remediation = "Only trap focus inside modal dialogs (role=dialog with aria-modal=\"true\" or <dialog> opened with showModal()), and always provide Escape to close them.",
        Status = FindingStatus.PotentialIssue,
        Impact = AccessibilityImpact.Serious,
        Wcag = ["2.1.2"],
        HelpUrl = "https://www.w3.org/WAI/WCAG22/Understanding/no-keyboard-trap.html",
        Environments = [Browser],
    };

    /// <summary>Focus landed on an element that is not visible.</summary>
    public static AccessibilityRule FocusHiddenTarget { get; } = new()
    {
        Id = "blazor-focus-hidden-target",
        Description = "Keyboard focus should only reach elements that are visible and exposed to assistive technologies.",
        Help = "Focus moved to an element that is not visible or is aria-hidden",
        Remediation = "Remove hidden elements from the tab order (tabindex=\"-1\", inert, or do not render them) or make them visible when focused (for example skip links).",
        Status = FindingStatus.PotentialIssue,
        Impact = AccessibilityImpact.Serious,
        Wcag = ["2.4.7", "2.4.3"],
        HelpUrl = "https://www.w3.org/WAI/WCAG22/Understanding/focus-visible.html",
        Environments = [Browser],
    };

    /// <summary>No focus indicator style detected (opt-in observation).</summary>
    public static AccessibilityRule FocusIndicatorNotDetected { get; } = new()
    {
        Id = "blazor-focus-indicator-not-detected",
        Description = "Focused elements should have a visible focus indicator.",
        Help = "No outline or box-shadow was detected on the focused element",
        Remediation = "Do not remove the default outline without a replacement. Style :focus-visible with an outline that meets 3:1 contrast. Verify manually: indicators made from other properties are not detected.",
        Status = FindingStatus.PotentialIssue,
        Impact = AccessibilityImpact.Moderate,
        Wcag = ["2.4.7"],
        HelpUrl = "https://www.w3.org/WAI/WCAG22/Understanding/focus-visible.html",
        Environments = [Browser],
    };

    /// <summary>Same element reached twice within one Tab cycle.</summary>
    public static AccessibilityRule FocusRepeated { get; } = new()
    {
        Id = "blazor-focus-repeated",
        Description = "Each focusable element should be reached once per Tab cycle.",
        Help = "Element was focused more than once in a single Tab cycle",
        Remediation = "Check for focus handlers that move focus programmatically and for duplicated focusable wrappers.",
        Status = FindingStatus.PotentialIssue,
        Impact = AccessibilityImpact.Moderate,
        Wcag = ["2.4.3"],
        HelpUrl = "https://www.w3.org/WAI/WCAG22/Understanding/focus-order.html",
        Environments = [Browser],
    };

    /// <summary>Live region inserted together with its content.</summary>
    public static AccessibilityRule LiveRegionInsertedWithContent { get; } = new()
    {
        Id = "blazor-live-region-inserted-with-content",
        Description = "Live regions should exist (empty) before content is placed in them; many screen readers do not announce a region that is added with its content.",
        Help = "Live region was added to the DOM already containing text",
        Remediation = "Render the live region container unconditionally (for example an empty <div role=\"status\">) and only change its text. role=\"alert\" is announced more reliably on insertion but should follow the same pattern.",
        Status = FindingStatus.PotentialIssue,
        Impact = AccessibilityImpact.Moderate,
        Wcag = ["4.1.3"],
        HelpUrl = "https://www.w3.org/WAI/WCAG22/Techniques/aria/ARIA22",
        Environments = [Browser],
    };

    /// <summary>Live region updated while hidden.</summary>
    public static AccessibilityRule LiveRegionHiddenUpdate { get; } = new()
    {
        Id = "blazor-live-region-hidden-update",
        Description = "Updates to hidden live regions (display:none, visibility:hidden or aria-hidden) are not announced.",
        Help = "Live region content changed while the region was hidden",
        Remediation = "Keep the live region rendered and exposed; use a visually-hidden CSS class instead of display:none if it should not be visible.",
        Status = FindingStatus.PotentialIssue,
        Impact = AccessibilityImpact.Moderate,
        Wcag = ["4.1.3"],
        HelpUrl = "https://www.w3.org/WAI/WCAG22/Understanding/status-messages.html",
        Environments = [Browser],
    };

    /// <summary>Contradictory live region role and aria-live.</summary>
    public static AccessibilityRule LiveRegionPolitenessConflict { get; } = new()
    {
        Id = "blazor-live-region-politeness-conflict",
        Description = "aria-live should not contradict the implicit politeness of role=\"alert\" (assertive) or role=\"status\" (polite).",
        Help = "aria-live value contradicts the live region role",
        Remediation = "Remove the conflicting aria-live attribute, or pick the role that matches the intended urgency.",
        Status = FindingStatus.PotentialIssue,
        Impact = AccessibilityImpact.Minor,
        Wcag = ["4.1.3"],
        HelpUrl = "https://www.w3.org/TR/wai-aria-1.2/#aria-live",
        Environments = [Browser],
    };

    /// <summary>All toolkit rules.</summary>
    public static IReadOnlyList<AccessibilityRule> All { get; } =
    [
        ClickHandlerSemantics, ValidationMessageAssociation, InvalidWithoutErrorReference, DuplicateId, DuplicateIdReferenced,
        IdReferenceMissing, RadioGroupUngrouped, FieldsetMissingLegend, ExpandedStateMismatch, TabSelectionMismatch,
        MarkupImageAlt, MarkupControlName, MarkupFormLabel, MarkupAriaHiddenFocusable, MarkupInvalidAriaAttribute, MarkupInvalidRole, MarkupPositiveTabIndex,
        FocusStuck, FocusTrap, FocusHiddenTarget, FocusIndicatorNotDetected, FocusRepeated,
        LiveRegionInsertedWithContent, LiveRegionHiddenUpdate, LiveRegionPolitenessConflict,
    ];

    private static readonly FrozenDictionary<string, AccessibilityRule> ById = All.ToFrozenDictionary(r => r.Id, StringComparer.Ordinal);

    /// <summary>Looks up a toolkit rule.</summary>
    public static AccessibilityRule? Find(string id) => ById.GetValueOrDefault(id);
}
