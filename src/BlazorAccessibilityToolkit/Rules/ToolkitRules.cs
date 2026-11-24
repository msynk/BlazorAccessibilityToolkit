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

    /// <summary>The current navigation link is styled as active but does not expose aria-current.</summary>
    public static AccessibilityRule NavLinkCurrent { get; } = new()
    {
        Id = "blazor-navlink-missing-aria-current",
        Description = "A navigation link that is visually marked as the current page (with the \"active\" class that Blazor's NavLink and most CSS frameworks use) should expose aria-current=\"page\".",
        Help = "Current navigation link is only indicated visually",
        Remediation = "Add aria-current=\"page\" to the active link. Blazor's NavLink in .NET 10 sets it automatically; links that toggle the class by hand, older NavLink versions and some component libraries do not.",
        Status = FindingStatus.PotentialIssue,
        Impact = AccessibilityImpact.Moderate,
        Wcag = ["1.3.1"],
        HelpUrl = "https://www.w3.org/TR/wai-aria-1.2/#aria-current",
        Environments = [Markup, Browser],
    };

    /// <summary>Fields that collect common personal data without an autocomplete token.</summary>
    public static AccessibilityRule InputPurposeMissing { get; } = new()
    {
        Id = "blazor-input-purpose-missing",
        Description = "Fields that collect information about the user (name, email, phone, address, user name, password ...) should identify their purpose with the autocomplete attribute.",
        Help = "Personal-data field has no autocomplete attribute",
        Remediation = "Add the matching autocomplete token, for example autocomplete=\"email\", \"tel\", \"given-name\", \"postal-code\", \"username\" or \"current-password\". Blazor's InputText and InputDate pass the attribute through.",
        Status = FindingStatus.PotentialIssue,
        Impact = AccessibilityImpact.Moderate,
        Wcag = ["1.3.5"],
        HelpUrl = "https://www.w3.org/WAI/WCAG22/Understanding/identify-input-purpose.html",
        Environments = [Markup, Browser],
    };

    /// <summary>Label marks a field as required, but the control does not expose it.</summary>
    public static AccessibilityRule RequiredNotExposed { get; } = new()
    {
        Id = "blazor-required-not-exposed",
        Description = "A field whose label marks it as required (for example with \"*\") should expose the requirement with the required attribute or aria-required=\"true\".",
        Help = "Field is marked as required only visually",
        Remediation = "Add the required attribute (or aria-required=\"true\") to the control. Blazor's [Required] data annotation does not add either; set it on InputText and friends yourself.",
        Status = FindingStatus.PotentialIssue,
        Impact = AccessibilityImpact.Moderate,
        Wcag = ["1.3.1", "3.3.2"],
        HelpUrl = "https://www.w3.org/WAI/tutorials/forms/instructions/#required",
        Environments = [Markup, Browser],
    };

    /// <summary>The Blazor error notification (#blazor-error-ui) is not announced or cannot be dismissed with the keyboard.</summary>
    public static AccessibilityRule ErrorUiNotAccessible { get; } = new()
    {
        Id = "blazor-error-ui-not-accessible",
        Description = "The Blazor error notification (#blazor-error-ui) should be announced when it appears and its dismiss control should be operable with the keyboard.",
        Help = "Blazor error notification is not accessible",
        Remediation = "Make #blazor-error-ui a live region (role=\"alert\") and use a <button type=\"button\" aria-label=\"Dismiss\"> instead of the template's <span class=\"dismiss\">, keeping the class so blazor.web.js still wires it up.",
        Status = FindingStatus.PotentialIssue,
        Impact = AccessibilityImpact.Serious,
        Wcag = ["2.1.1", "4.1.2", "4.1.3"],
        HelpUrl = "https://learn.microsoft.com/aspnet/core/blazor/fundamentals/handle-errors#detailed-errors-during-development",
        Environments = [Browser],
    };

    /// <summary>A field blocks pasting or password managers.</summary>
    public static AccessibilityRule PasteBlocked { get; } = new()
    {
        Id = "blazor-paste-blocked",
        Description = "Fields must not prevent pasting, and password fields must not turn password managers away. People who cannot memorize or transcribe passwords and codes rely on both (accessible authentication).",
        Help = "Field blocks paste or password managers",
        Remediation = "Remove @onpaste:preventDefault (or the script that cancels paste events), and use autocomplete=\"current-password\", \"new-password\" or \"one-time-code\" instead of autocomplete=\"off\".",
        Status = FindingStatus.PotentialIssue,
        Impact = AccessibilityImpact.Serious,
        Wcag = ["3.3.8"],
        HelpUrl = "https://www.w3.org/WAI/WCAG22/Understanding/accessible-authentication-minimum.html",
        Environments = [Markup, Browser],
    };

    /// <summary>Drag-and-drop without a reported single-pointer alternative.</summary>
    public static AccessibilityRule DraggingAlternative { get; } = new()
    {
        Id = "blazor-dragging-alternative",
        Description = "Functionality that uses dragging must also be available with a single pointer without dragging (for example buttons to move an item up or down, or a menu), unless dragging is essential.",
        Help = "Draggable content: verify that a single-pointer alternative exists",
        Remediation = "Offer the same result without dragging: move up/down buttons, a 'Move to' menu, or click-to-select then click-to-place. Keyboard support alone does not satisfy WCAG 2.5.7.",
        Status = FindingStatus.PotentialIssue,
        Impact = AccessibilityImpact.Moderate,
        Wcag = ["2.5.7"],
        HelpUrl = "https://www.w3.org/WAI/WCAG22/Understanding/dragging-movements.html",
        Environments = [Markup, Browser],
    };

    /// <summary>A form asks again for information the user already entered.</summary>
    public static AccessibilityRule RedundantEntry { get; } = new()
    {
        Id = "blazor-redundant-entry",
        Description = "Information the user already entered in the same process should be filled in automatically or be selectable, unless re-entering it is essential (for example a new password for security).",
        Help = "Field asks the user to enter the same information again",
        Remediation = "Remove the confirmation field, or fill it in automatically. To catch typos in an email address, show it back for review instead of asking for it twice.",
        Status = FindingStatus.PotentialIssue,
        Impact = AccessibilityImpact.Moderate,
        Wcag = ["3.3.7"],
        HelpUrl = "https://www.w3.org/WAI/WCAG22/Understanding/redundant-entry.html",
        Environments = [Markup, Browser],
    };

    /// <summary>Keyboard focus leaves the visual reading order.</summary>
    public static AccessibilityRule FocusOrderVisualMismatch { get; } = new()
    {
        Id = "blazor-focus-order-visual-mismatch",
        Description = "Keyboard focus should follow the visual reading order. A stop that jumps ahead and then comes back confuses keyboard users and people who use a screen magnifier.",
        Help = "Focus order does not follow the visual order",
        Remediation = "Make the DOM order match the visual order: avoid positive tabindex, and CSS order, flex-direction: row-reverse or absolute positioning that moves controls away from their place in the markup.",
        Status = FindingStatus.PotentialIssue,
        Impact = AccessibilityImpact.Moderate,
        Wcag = ["2.4.3", "1.3.2"],
        HelpUrl = "https://www.w3.org/WAI/WCAG22/Understanding/focus-order.html",
        Environments = [Browser],
    };

    /// <summary>A virtualized list renders a subset of its items without telling assistive technologies the real size.</summary>
    public static AccessibilityRule VirtualizedSetSize { get; } = new()
    {
        Id = "blazor-virtualized-set-size",
        Description = "A list rendered with <Virtualize> only contains the visible items. Each item should expose its position and the total with aria-posinset and aria-setsize, otherwise screen readers announce \"list, 21 items\" for a list of 1000.",
        Help = "Virtualized items do not expose their position and the set size",
        Remediation = "Add aria-setsize=\"@total\" and aria-posinset=\"@(index + 1)\" to each item in the Virtualize ItemContent (and the matching role, such as listitem or row, if the item is not a native list item or table row).",
        Status = FindingStatus.PotentialIssue,
        Impact = AccessibilityImpact.Moderate,
        Wcag = ["1.3.1"],
        HelpUrl = "https://www.w3.org/TR/wai-aria-1.2/#aria-setsize",
        Environments = [Browser],
    };

    /// <summary>Two interactive elements occupy the same area.</summary>
    public static AccessibilityRule DuplicateClickableBounds { get; } = new()
    {
        Id = "blazor-duplicate-clickable-bounds",
        Description = "An interactive element nested in another one with the same size and position is two targets in one place: screen readers announce both, and pointer users cannot tell which one they activate.",
        Help = "Two interactive elements share the same area",
        Remediation = "Keep one interactive element: remove the click handler or tabindex from the wrapper, or make the inner element non-interactive.",
        Status = FindingStatus.PotentialIssue,
        Impact = AccessibilityImpact.Minor,
        IsBestPractice = true,
        HelpUrl = "https://developer.android.com/guide/topics/ui/accessibility/testing#duplicate-clickable-bounds",
        Environments = [Browser],
    };

    /// <summary>Help mechanisms appear in a different place or order across pages.</summary>
    public static AccessibilityRule HelpInconsistent { get; } = new()
    {
        Id = "blazor-help-inconsistent",
        Description = "Help mechanisms that are repeated on several pages (contact details, a contact or help link, a chat, a FAQ) must appear in the same relative order and region on each page, so that people who need help can find it again.",
        Help = "Help mechanism moved relative to other pages",
        Remediation = "Render help links and contact details from the shared layout (header or footer) in a fixed order, instead of per page.",
        Status = FindingStatus.PotentialIssue,
        Impact = AccessibilityImpact.Moderate,
        Wcag = ["3.2.6"],
        HelpUrl = "https://www.w3.org/WAI/WCAG22/Understanding/consistent-help.html",
        Environments = [Browser],
    };

    /// <summary>Links with the same accessible name lead to different destinations.</summary>
    public static AccessibilityRule AmbiguousLinkName { get; } = new()
    {
        Id = "blazor-ambiguous-link-name",
        Description = "Links with the same accessible name should lead to the same destination. Screen reader users often list links by name, out of context, and cannot tell several \"Read more\" or \"Edit\" links apart.",
        Help = "Links with the same name go to different places",
        Remediation = "Make each name unique, for example \"Edit order 42\", by adding visually hidden text or aria-label that includes the visible text.",
        Status = FindingStatus.PotentialIssue,
        Impact = AccessibilityImpact.Moderate,
        Wcag = ["2.4.4", "2.4.9"],
        HelpUrl = "https://www.w3.org/WAI/WCAG22/Understanding/link-purpose-in-context.html",
        Environments = [Browser],
    };

    /// <summary>The accessible name repeats the role.</summary>
    public static AccessibilityRule RoleInName { get; } = new()
    {
        Id = "blazor-role-in-name",
        Description = "Accessible names should not repeat the role, which screen readers already announce (\"Submit button, button\").",
        Help = "Accessible name repeats the role",
        Remediation = "Remove the role word from the name: \"Submit\" instead of \"Submit button\", \"Order history\" instead of \"Link to order history\".",
        Status = FindingStatus.PotentialIssue,
        Impact = AccessibilityImpact.Minor,
        IsBestPractice = true,
        HelpUrl = "https://www.w3.org/WAI/ARIA/apg/practices/names-and-descriptions/",
        Environments = [Browser],
    };

    /// <summary>The Blazor reconnect modal is not named or does not announce its status.</summary>
    public static AccessibilityRule ReconnectUiNotAccessible { get; } = new()
    {
        Id = "blazor-reconnect-ui-not-accessible",
        Description = "The Blazor reconnect modal (#components-reconnect-modal) should be a named dialog whose status messages (reconnecting, failed, retry) are announced.",
        Help = "Blazor reconnect modal is not accessible",
        Remediation = "Give the dialog an accessible name (aria-labelledby the heading or first message) and put the status messages in a role=\"status\" element, so that changes are announced while the dialog is open.",
        Status = FindingStatus.PotentialIssue,
        Impact = AccessibilityImpact.Serious,
        Wcag = ["4.1.2", "4.1.3"],
        HelpUrl = "https://learn.microsoft.com/aspnet/core/blazor/fundamentals/signalr#reflect-the-server-side-connection-state-in-the-ui",
        Environments = [Browser],
    };

    /// <summary>A pointer target is smaller than 44 by 44 CSS pixels.</summary>
    public static AccessibilityRule TargetSizeEnhanced { get; } = new()
    {
        Id = "blazor-target-size-enhanced",
        Description = "Pointer targets should be at least 44 by 44 CSS pixels (WCAG 2.5.5, AAA; the size Apple and Android guidelines recommend for touch), except inline links in text.",
        Help = "Target is smaller than 44 by 44 CSS pixels",
        Remediation = "Increase the clickable area with padding or min-width/min-height, or enlarge the hit area with a pseudo-element.",
        Status = FindingStatus.PotentialIssue,
        Impact = AccessibilityImpact.Moderate,
        Wcag = ["2.5.5"],
        HelpUrl = "https://www.w3.org/WAI/WCAG22/Understanding/target-size-enhanced.html",
        Environments = [Browser],
    };

    /// <summary>Text below the APCA lightness contrast its size and weight call for (information, not WCAG 2).</summary>
    public static AccessibilityRule ApcaContrast { get; } = new()
    {
        Id = "blazor-apca-contrast",
        Description = "Text should have the APCA lightness contrast (Lc) that its font size and weight call for. APCA is a candidate contrast method for WCAG 3, not a WCAG 2 requirement; the WCAG 2 contrast ratio (axe-core's color-contrast) remains the conformance check.",
        Help = "Text contrast is below the APCA guidance for its size and weight",
        Remediation = "Darken dark text or lighten light text (or the background), or make the text larger or bolder. APCA is more demanding than WCAG 2 for thin, small text and for dark backgrounds.",
        Status = FindingStatus.PotentialIssue,
        Impact = AccessibilityImpact.Minor,
        IsBestPractice = true,
        HelpUrl = "https://github.com/Myndex/SAPC-APCA/blob/master/documentation/APCA_in_a_Nutshell.md",
        Environments = [Browser],
    };

    /// <summary>An in-page link that navigates to another page because of the base URL.</summary>
    public static AccessibilityRule FragmentLinkLeavesPage { get; } = new()
    {
        Id = "blazor-fragment-link-leaves-page",
        Description = "In-page links (skip links, tables of contents, \"back to top\") must move to the target on the same page. In Blazor apps, <base href=\"/\"> makes href=\"#id\" resolve to the home page.",
        Help = "In-page link opens another page",
        Remediation = "Include the current path in the link (href=\"/orders#details\"), or use BlazorAccessibilityToolkit.Components' <SkipLink>, which also moves focus to the target. A click handler with @onclick:preventDefault only helps once the page is interactive, not while it is prerendered or statically rendered.",
        Status = FindingStatus.PotentialIssue,
        Impact = AccessibilityImpact.Serious,
        Wcag = ["2.4.1"],
        HelpUrl = "https://learn.microsoft.com/aspnet/core/blazor/fundamentals/routing#hashed-routing-to-named-elements",
        Environments = [Browser],
    };

    /// <summary>Animation keeps running while the user asks for reduced motion.</summary>
    public static AccessibilityRule MotionNotReduced { get; } = new()
    {
        Id = "blazor-motion-not-reduced",
        Description = "Moving, blinking or auto-playing content that lasts more than five seconds must be pausable, and animation triggered by interaction should stop when the user prefers reduced motion (prefers-reduced-motion: reduce).",
        Help = "Animation keeps running with reduced motion requested",
        Remediation = "Wrap animations in @media (prefers-reduced-motion: no-preference), or stop them under prefers-reduced-motion: reduce. Give auto-playing video and carousels a pause button.",
        Status = FindingStatus.PotentialIssue,
        Impact = AccessibilityImpact.Serious,
        Wcag = ["2.2.2", "2.3.3"],
        HelpUrl = "https://www.w3.org/WAI/WCAG22/Techniques/css/C39",
        Environments = [Browser],
    };

    /// <summary>Content is only usable in one display orientation.</summary>
    public static AccessibilityRule OrientationLocked { get; } = new()
    {
        Id = "blazor-orientation-locked",
        Description = "Content must not restrict its view and operation to a single display orientation (portrait or landscape) unless the orientation is essential.",
        Help = "Content is restricted to one orientation",
        Remediation = "Remove orientation-specific rules that hide or rotate the page (for example a \"rotate your device\" overlay under @media (orientation: portrait)); use responsive layouts for both orientations.",
        Status = FindingStatus.PotentialIssue,
        Impact = AccessibilityImpact.Serious,
        Wcag = ["1.3.4"],
        HelpUrl = "https://www.w3.org/WAI/WCAG22/Understanding/orientation.html",
        Environments = [Browser],
    };

    /// <summary>The focused element is entirely hidden behind other content.</summary>
    public static AccessibilityRule FocusObscured { get; } = new()
    {
        Id = "blazor-focus-obscured",
        Description = "A focused element must not be entirely hidden by author-created content such as sticky headers, footers, cookie banners or non-modal dialogs.",
        Help = "Focused element is entirely covered by other content",
        Remediation = "Add scroll-padding-top/bottom matching sticky headers and footers, or close and move overlays out of the way when focus moves underneath them.",
        Status = FindingStatus.PotentialIssue,
        Impact = AccessibilityImpact.Serious,
        Wcag = ["2.4.11"],
        HelpUrl = "https://www.w3.org/WAI/WCAG22/Understanding/focus-not-obscured-minimum.html",
        Environments = [Browser],
    };

    /// <summary>Content needs horizontal scrolling at a width of 320 CSS pixels.</summary>
    public static AccessibilityRule ReflowHorizontalScroll { get; } = new()
    {
        Id = "blazor-reflow-horizontal-scroll",
        Description = "Content should reflow at a width of 320 CSS pixels (1280 pixels at 400% zoom) without scrolling in two dimensions, except for parts that need a two-dimensional layout (data tables, maps, diagrams, toolbars).",
        Help = "Content is wider than the viewport at 320 CSS pixels",
        Remediation = "Use responsive layouts (flex-wrap, grid with minmax, max-width: 100%), let long words wrap (overflow-wrap: anywhere) and put intentionally wide content such as tables in its own scroll container.",
        Status = FindingStatus.PotentialIssue,
        Impact = AccessibilityImpact.Serious,
        Wcag = ["1.4.10"],
        HelpUrl = "https://www.w3.org/WAI/WCAG22/Understanding/reflow.html",
        Environments = [Browser],
    };

    /// <summary>Text is clipped when the WCAG text-spacing values are applied.</summary>
    public static AccessibilityRule TextSpacingClipped { get; } = new()
    {
        Id = "blazor-text-spacing-clipped",
        Description = "No content may be lost when users increase line height to 1.5, paragraph spacing to 2, letter spacing to 0.12 and word spacing to 0.16 times the font size.",
        Help = "Text is clipped when text spacing is increased",
        Remediation = "Avoid fixed heights and widths on elements that contain text; use min-height, padding and wrapping instead of overflow: hidden with a fixed size.",
        Status = FindingStatus.PotentialIssue,
        Impact = AccessibilityImpact.Serious,
        Wcag = ["1.4.12"],
        HelpUrl = "https://www.w3.org/WAI/WCAG22/Understanding/text-spacing.html",
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

    /// <summary>Nothing on screen changed when the element received keyboard focus (measured from screenshots).</summary>
    public static AccessibilityRule FocusNotVisible { get; } = new()
    {
        Id = "blazor-focus-not-visible",
        Description = "Keyboard focus must be visible: the focused element, or the area around it, must look different from its unfocused state.",
        Help = "No pixel near the element changed when it received keyboard focus",
        Remediation = "Do not remove the browser's focus outline (outline: none) without a replacement. Style :focus-visible, for example with outline: 2px solid currentColor; outline-offset: 2px.",
        Status = FindingStatus.Violation,
        Impact = AccessibilityImpact.Serious,
        Wcag = ["2.4.7"],
        HelpUrl = "https://www.w3.org/WAI/WCAG22/Understanding/focus-visible.html",
        Environments = [Browser],
    };

    /// <summary>The focus indicator is smaller than a 2 CSS pixel perimeter or does not change contrast by 3:1 (AAA).</summary>
    public static AccessibilityRule FocusAppearance { get; } = new()
    {
        Id = "blazor-focus-appearance",
        Description = "A focus indicator should be at least as large as a 2 CSS pixel thick perimeter of the unfocused element, and its pixels should change by a contrast of at least 3:1 between the unfocused and focused states.",
        Help = "The focus indicator is too small or too faint for WCAG 2.4.13",
        Remediation = "Use a solid outline of at least 2 CSS pixels around the element in a color with 3:1 contrast against both the element and its background, for example outline: 2px solid; outline-offset: 2px. Thin, dotted, light or partial indicators fall short.",
        Status = FindingStatus.Violation,
        Impact = AccessibilityImpact.Moderate,
        Wcag = ["2.4.13"],
        HelpUrl = "https://www.w3.org/WAI/WCAG22/Understanding/focus-appearance.html",
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
        NavLinkCurrent, InputPurposeMissing, RequiredNotExposed, ErrorUiNotAccessible, PasteBlocked, DraggingAlternative, RedundantEntry, FragmentLinkLeavesPage, AmbiguousLinkName, RoleInName, ReconnectUiNotAccessible, HelpInconsistent, VirtualizedSetSize, DuplicateClickableBounds,
        MarkupImageAlt, MarkupControlName, MarkupFormLabel, MarkupAriaHiddenFocusable, MarkupInvalidAriaAttribute, MarkupInvalidRole, MarkupPositiveTabIndex,
        FocusStuck, FocusTrap, FocusHiddenTarget, FocusIndicatorNotDetected, FocusNotVisible, FocusAppearance, FocusRepeated, FocusObscured, FocusOrderVisualMismatch,
        ReflowHorizontalScroll, TextSpacingClipped, MotionNotReduced, OrientationLocked, TargetSizeEnhanced, ApcaContrast,
        LiveRegionInsertedWithContent, LiveRegionHiddenUpdate, LiveRegionPolitenessConflict,
    ];

    private static readonly FrozenDictionary<string, AccessibilityRule> ById = All.ToFrozenDictionary(r => r.Id, StringComparer.Ordinal);

    /// <summary>Looks up a toolkit rule.</summary>
    public static AccessibilityRule? Find(string id) => ById.GetValueOrDefault(id);
}
