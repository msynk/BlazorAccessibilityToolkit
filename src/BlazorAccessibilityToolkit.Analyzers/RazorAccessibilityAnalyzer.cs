using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace BlazorAccessibilityToolkit.Analyzers;

/// <summary>
/// Reports common accessibility defects in <c>.razor</c> (and <c>.cshtml</c>) markup at compile time. The Razor SDK
/// passes these files to analyzers as additional files, so diagnostics appear in the build output and the IDE error
/// list with the Razor file and line.
/// </summary>
/// <remarks>
/// <para>Rules only judge what the markup states. Values computed at run time (<c>alt="@Alt"</c>), attribute splatting
/// (<c>@attributes</c>) and content rendered by child components are assumed to be correct, so the rules stay quiet
/// rather than guess. Run the toolkit's bUnit and browser audits for the rendered result.</para>
/// <para>Ignore a finding with a Razor comment on the line before the element, <c>@* blazor-a11y-ignore BA11Y001 *@</c>
/// (all rules when no id is given), or for a whole file with <c>@* blazor-a11y-ignore-file BA11Y001 *@</c>. Turn the
/// analyzer off for files or folders with <c>blazor_a11y_analyzer.enabled = false</c> in <c>.editorconfig</c>, and change
/// severities with <c>dotnet_diagnostic.BA11Y001.severity</c>.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class RazorAccessibilityAnalyzer : DiagnosticAnalyzer
{
    private const string Category = "Accessibility";
    private const string Docs = "https://github.com/msynk/BlazorAccessibilityToolkit/blob/main/docs/analyzers.md#";

    /// <summary>Clickable element without keyboard support or interactive role.</summary>
    public static readonly DiagnosticDescriptor ClickHandlerSemantics = Rule("BA11Y001", "Clickable element is not keyboard accessible", "<{0}> has a Blazor @{1} handler, but {2}; use a <button>, or add an interactive role, tabindex=\"0\" and an @onkeydown handler", "2.1.1, 4.1.2");

    /// <summary>Image without alt.</summary>
    public static readonly DiagnosticDescriptor ImageAlt = Rule("BA11Y002", "Image has no text alternative", "{0}; add alt=\"description\" (or an accessible name), or alt=\"\" for a decorative <img>", "1.1.1");

    /// <summary>Positive tabindex.</summary>
    public static readonly DiagnosticDescriptor PositiveTabIndex = Rule("BA11Y003", "Positive tabindex", "tabindex=\"{0}\" overrides the document focus order; use tabindex=\"0\" and order the markup instead", "2.4.3");

    /// <summary>Unknown aria-* attribute.</summary>
    public static readonly DiagnosticDescriptor UnknownAriaAttribute = Rule("BA11Y004", "Unknown ARIA attribute", "'{0}' is not a WAI-ARIA attribute{1}", "4.1.2");

    /// <summary>Unknown role.</summary>
    public static readonly DiagnosticDescriptor UnknownRole = Rule("BA11Y005", "Unknown ARIA role", "role=\"{0}\" contains no valid WAI-ARIA role", "4.1.2");

    /// <summary>Focusable element hidden from assistive technologies.</summary>
    public static readonly DiagnosticDescriptor AriaHiddenFocusable = Rule("BA11Y006", "Focusable element is hidden from assistive technologies", "<{0}> can receive keyboard focus but is inside aria-hidden=\"true\" content; remove aria-hidden or make it unfocusable (tabindex=\"-1\", disabled or inert)", "4.1.2");

    /// <summary>Form field without a label.</summary>
    public static readonly DiagnosticDescriptor FormFieldLabel = Rule("BA11Y007", "Form field has no label", "<{0}> has no label: {1}", "1.3.1, 4.1.2");

    private const string LabelAdvice = "wrap it in a <label>, or give it an id referenced by <label for>, or an aria-label";

    /// <summary>Button or link without a name.</summary>
    public static readonly DiagnosticDescriptor ControlName = Rule("BA11Y008", "Button or link has no accessible name", "<{0}> has no text content and no aria-label, aria-labelledby or title, so screen readers announce it without a name", "4.1.2, 2.4.4");

    /// <summary>Empty heading.</summary>
    public static readonly DiagnosticDescriptor EmptyHeading = Rule("BA11Y009", "Heading has no content", "<{0}> has no text content", "1.3.1, 2.4.6");

    /// <summary>Document without language.</summary>
    public static readonly DiagnosticDescriptor HtmlLang = Rule("BA11Y010", "Page has no language", "<html> has no lang attribute; add lang=\"en\" (or the page's language)", "3.1.1");

    /// <summary>Frame without title.</summary>
    public static readonly DiagnosticDescriptor FrameTitle = Rule("BA11Y011", "Frame has no title", "<iframe> has no title attribute that describes its content", "4.1.2");

    /// <summary>Link used as a button.</summary>
    public static readonly DiagnosticDescriptor LinkAsButton = Rule("BA11Y012", "Link is used as a button", "<a href=\"{0}\"> has an @onclick handler and no real destination; use a <button type=\"button\"> for actions", "4.1.2");

    /// <summary>Interactive role on an element that cannot receive focus.</summary>
    public static readonly DiagnosticDescriptor InteractiveRoleFocus = Rule("BA11Y013", "Element with an interactive role is not focusable", "<{0} role=\"{1}\"> cannot receive keyboard focus; add tabindex=\"0\" (or tabindex=\"-1\" when a composite widget manages focus) or use the native element", "2.1.1");

    private static readonly HashSet<string> PointerEvents = new(StringComparer.OrdinalIgnoreCase) { "@onclick", "@onmousedown", "@onmouseup", "@onpointerdown", "@onpointerup", "@ondblclick" };

    private static readonly HashSet<string> KeyEvents = new(StringComparer.OrdinalIgnoreCase) { "@onkeydown", "@onkeyup", "@onkeypress" };

    private static readonly HashSet<string> InteractiveRoles = new(StringComparer.Ordinal)
    {
        "button", "link", "checkbox", "menuitem", "menuitemcheckbox", "menuitemradio", "option", "radio", "switch", "tab", "treeitem",
        "gridcell", "row", "combobox", "slider", "spinbutton", "textbox", "searchbox", "scrollbar", "columnheader", "rowheader",
    };

    // Roles whose elements must be focusable themselves (composite widgets manage focus of their children).
    private static readonly HashSet<string> FocusableRoles = new(StringComparer.Ordinal)
    {
        "button", "link", "checkbox", "menuitem", "menuitemcheckbox", "menuitemradio", "radio", "switch", "tab", "slider", "spinbutton", "textbox", "searchbox", "combobox", "scrollbar",
    };

    private static readonly HashSet<string> AriaAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "aria-actions", "aria-activedescendant", "aria-atomic", "aria-autocomplete", "aria-braillelabel", "aria-brailleroledescription", "aria-busy", "aria-checked",
        "aria-colcount", "aria-colindex", "aria-colindextext", "aria-colspan", "aria-controls", "aria-current", "aria-describedby", "aria-description",
        "aria-details", "aria-disabled", "aria-dropeffect", "aria-errormessage", "aria-expanded", "aria-flowto", "aria-grabbed", "aria-haspopup",
        "aria-hidden", "aria-invalid", "aria-keyshortcuts", "aria-label", "aria-labelledby", "aria-level", "aria-live", "aria-modal", "aria-multiline",
        "aria-multiselectable", "aria-orientation", "aria-owns", "aria-placeholder", "aria-posinset", "aria-pressed", "aria-readonly", "aria-relevant",
        "aria-required", "aria-roledescription", "aria-rowcount", "aria-rowindex", "aria-rowindextext", "aria-rowspan", "aria-selected", "aria-setsize",
        "aria-sort", "aria-valuemax", "aria-valuemin", "aria-valuenow", "aria-valuetext",
    };

    private static readonly HashSet<string> Roles = new(StringComparer.Ordinal)
    {
        "alert", "alertdialog", "application", "article", "banner", "blockquote", "button", "caption", "cell", "checkbox", "code", "columnheader",
        "combobox", "comment", "complementary", "contentinfo", "definition", "deletion", "dialog", "directory", "document", "emphasis", "feed", "figure", "form",
        "generic", "grid", "gridcell", "group", "heading", "img", "image", "insertion", "link", "list", "listbox", "listitem", "log", "main", "mark",
        "marquee", "math", "menu", "menubar", "menuitem", "menuitemcheckbox", "menuitemradio", "meter", "navigation", "none", "note", "option",
        "paragraph", "presentation", "progressbar", "radio", "radiogroup", "region", "row", "rowgroup", "rowheader", "scrollbar", "search", "searchbox", "sectionfooter", "sectionheader",
        "separator", "slider", "spinbutton", "status", "strong", "subscript", "suggestion", "superscript", "switch", "tab", "table", "tablist", "tabpanel", "term",
        "textbox", "time", "timer", "toolbar", "tooltip", "tree", "treegrid", "treeitem",
    };

    // Blazor's built-in form components render a labelable <input>, <select> or <textarea> and pass attributes through.
    private static readonly HashSet<string> BlazorInputs = new(StringComparer.Ordinal)
    {
        "InputText", "InputTextArea", "InputNumber", "InputDate", "InputSelect", "InputCheckbox", "InputFile",
    };

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
        ClickHandlerSemantics, ImageAlt, PositiveTabIndex, UnknownAriaAttribute, UnknownRole, AriaHiddenFocusable, FormFieldLabel,
        ControlName, EmptyHeading, HtmlLang, FrameTitle, LinkAsButton, InteractiveRoleFocus,
        Autofocus, AccessKey, DistractingElement, MediaCaptions, ScopeAttribute, AutocompleteValid, ActiveDescendantFocus,
        RoleRequiredAttributes, AriaAttributeValue, RedundantRole, NonInteractiveTabIndex, MouseEventsWithoutFocus, NestedInteractive,
        AmbiguousLinkText, LangValid, ViewportZoom, MetaRefresh, PageTitleMissing, FocusOnNavigateMissing, PasteOrAutofillBlocked,
        DraggingAlternative, AriaStateBoundToBool, RedundantImageAlt, PresentationRoleConflict, FragmentLinkLeavesPage);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start => start.RegisterAdditionalFileAction(AnalyzeFile));
    }

    private static void AnalyzeFile(AdditionalFileAnalysisContext context)
    {
        var path = context.AdditionalFile.Path;
        if (!path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var options = context.Options.AnalyzerConfigOptionsProvider.GetOptions(context.AdditionalFile);
        if (options.TryGetValue("blazor_a11y_analyzer.enabled", out var enabled) && string.Equals(enabled.Trim(), "false", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var text = context.AdditionalFile.GetText(context.CancellationToken);
        if (text is null)
        {
            return;
        }

        var settings = AnalyzerSettings.From(options);
        var markup = text.ToString();
        foreach (var (descriptor, element, span, args) in Analyze(markup, settings))
        {
            var location = Location.Create(path, span, text.Lines.GetLinePositionSpan(span));
            context.ReportDiagnostic(CreateDiagnostic(descriptor, location, FixesFor(descriptor, element, span, args, markup), args));
        }
    }

    /// <summary>Runs the rules on Razor markup. Exposed for tests.</summary>
    internal static IEnumerable<(DiagnosticDescriptor Descriptor, RazorElement? Element, TextSpan Span, object[] Args)> Analyze(string markup, AnalyzerSettings? settings = null)
    {
        settings ??= AnalyzerSettings.Default;
        var elements = RazorMarkupScanner.Scan(markup);
        foreach (var element in elements)
        {
            if (element.IsComponent && settings.ComponentMap.TryGetValue(element.Name, out var native))
            {
                element.MapTo(native);
            }
        }

        var file = new FileFacts(markup, elements);
        ResolveLabelledBy(markup, elements);

        // Labels that wrap a checkbox, radio button or button: activating the control with the keyboard dispatches a click
        // that bubbles to the label, so a click handler on the label is reachable.
        var labelsWithControls = new HashSet<RazorElement>(
            elements.Where(x => (!x.IsComponent && x.LowerName is "input" or "button") || BlazorInputs.Contains(x.Name) || x.Name is "InputRadio")
                .SelectMany(x => x.Ancestors())
                .Where(a => a.LowerName == "label" && !a.IsComponent));

        foreach (var element in elements)
        {
            foreach (var result in Check(element, labelsWithControls, settings).Concat(CheckMore(element, file, settings)))
            {
                if (!element.IsIgnored(result.Descriptor.Id))
                {
                    yield return result;
                }
            }
        }

        foreach (var result in CheckFile(file))
        {
            if (!file.IsIgnored(result.Descriptor.Id))
            {
                yield return result;
            }
        }
    }

    private static IEnumerable<(DiagnosticDescriptor Descriptor, RazorElement? Element, TextSpan Span, object[] Args)> Check(RazorElement e, HashSet<RazorElement> labelsWithControls, AnalyzerSettings settings)
    {
        var tag = NameSpan(e);
        var splatted = e.Has("@attributes");

        // Attribute validity applies to components too: they usually pass aria-* attributes to an element.
        foreach (var attribute in e.Attributes)
        {
            if (attribute.Name.StartsWith("aria-", StringComparison.OrdinalIgnoreCase) && !AriaAttributes.Contains(attribute.Name))
            {
                var suggestion = Suggest(attribute.Name);
                yield return (UnknownAriaAttribute, e, attribute.Span, [attribute.Name, suggestion is null ? string.Empty : $"; did you mean '{suggestion}'?"]);
            }
        }

        if (e.Attribute("role") is { IsDynamic: false, Value: { Length: > 0 } roleValue } roleAttribute)
        {
            var tokens = roleValue.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length > 0 && !tokens.Any(IsKnownRole))
            {
                yield return (UnknownRole, e, roleAttribute.Span, [roleValue]);
            }
        }

        if (e.Attribute("tabindex") is { IsDynamic: false } tabIndexAttribute && int.TryParse(tabIndexAttribute.Value?.Trim(), out var tabIndex) && tabIndex > 0)
        {
            yield return (PositiveTabIndex, e, tabIndexAttribute.Span, [tabIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        }

        if (e.IsComponent)
        {
            if (BlazorInputs.Contains(e.Name) && !splatted && !IsLabelled(e, settings) && !IsHidden(e))
            {
                yield return (FormFieldLabel, e, tag, [e.Name, LabelAdvice]);
            }

            yield break;
        }

        var hidden = IsHidden(e);
        var role = FirstRole(e);
        var pointer = e.Attributes.Where(a => PointerEvents.Contains(a.Name)).Select(a => a.Name.Substring(1)).ToArray();

        if (pointer.Length > 0 && !splatted && !hidden && !IsNativelyInteractive(e) && !labelsWithControls.Contains(e) && !e.Ancestors().Any(IsInteractiveAncestor)
            && !IsManagedItem(e, role))
        {
            var problems = new List<string>();
            var dynamicRole = e.Attribute("role") is { IsDynamic: true };
            if (!dynamicRole && (role is null || !InteractiveRoles.Contains(role)))
            {
                problems.Add(role is null ? "it has no interactive role" : $"its role '{role}' is not interactive");
            }

            var tabIndexValue = e.Attribute("tabindex");
            if (tabIndexValue is null)
            {
                if (!IsNativelyFocusable(e))
                {
                    problems.Add("it is not focusable (no tabindex)");
                }
            }
            else if (!tabIndexValue.IsDynamic && int.TryParse(tabIndexValue.Value?.Trim(), out var index) && index < 0)
            {
                problems.Add($"tabindex=\"{tabIndexValue.Value}\" keeps it out of the tab sequence");
            }

            if (!new[] { e }.Concat(e.Ancestors()).Any(x => x.Attributes.Any(a => KeyEvents.Contains(a.Name))))
            {
                problems.Add("no @onkeydown or @onkeyup handler supports Enter or Space");
            }

            if (problems.Count > 0)
            {
                yield return (ClickHandlerSemantics, e, tag, [e.Name, string.Join("/@", pointer), string.Join(", ", problems)]);
            }
        }
        else if (pointer.Length == 0 && !splatted && !hidden && role is not null && FocusableRoles.Contains(role) && !e.Has("tabindex") && !IsNativelyFocusable(e) && !IsNativelyInteractive(e) && !e.Has("contenteditable"))
        {
            yield return (InteractiveRoleFocus, e, tag, [e.Name, role]);
        }

        if (e.LowerName == "a" && pointer.Contains("onclick", StringComparer.OrdinalIgnoreCase) && e.Static("href") is { } href
            && (href.Trim() == "#" || href.Trim().StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)))
        {
            yield return (LinkAsButton, e, tag, [href]);
        }

        // A presentational role on a focusable element is ignored (WAI-ARIA conflict resolution), so the element keeps
        // its implicit role and still needs a name.
        var namedRole = role is "presentation" or "none" && IsFocusable(e) ? null : role;
        var alt = e.Attribute("alt");

        if (e.LowerName == "img" && !splatted && !hidden && namedRole is not ("presentation" or "none") && !HasNameAttribute(e, settings)
            && (alt is null || (!alt.IsDynamic && alt.Value is { Length: > 0 } blank && blank.Trim().Length == 0)))
        {
            // alt=" " is neither the empty alt of a decorative image nor a text alternative.
            yield return (ImageAlt, e, tag, [alt is null ? "<img> has no alt attribute" : "<img> has an alt attribute with only spaces"]);
        }
        else if (e.LowerName == "input" && string.Equals(e.Static("type")?.Trim(), "image", StringComparison.OrdinalIgnoreCase) && !splatted && !hidden && !HasNameAttribute(e, settings)
            && (alt is null || (!alt.IsDynamic && string.IsNullOrWhiteSpace(alt.Value))))
        {
            // An image button always needs a name: alt="" does not make it decorative.
            yield return (ImageAlt, e, tag, [alt is null ? "<input type=\"image\"> has no alt attribute" : "<input type=\"image\"> has an empty alt attribute"]);
        }
        else if (role == "img" && e.LowerName is not ("img" or "svg") && !splatted && !hidden && !HasNameAttribute(e, settings))
        {
            yield return (ImageAlt, e, tag, [$"<{e.Name} role=\"img\"> has no aria-label or aria-labelledby"]);
        }
        else if (e.LowerName == "area" && e.Has("href") && !splatted && !hidden && !e.Has("alt") && !HasNameAttribute(e, settings))
        {
            yield return (ImageAlt, e, tag, ["<area> has no alt attribute"]);
        }
        else if (e.LowerName == "svg" && role == "img" && !splatted && !hidden && !HasNameAttribute(e, settings) && !e.Children.Any(c => c.LowerName == "title") && !e.HasDynamicContent && !e.HasChildComponent)
        {
            yield return (ImageAlt, e, tag, ["<svg role=\"img\"> has no <title>, aria-label or aria-labelledby"]);
        }

        // The hidden attribute, inert and display: none or visibility: hidden also take an element out of the focus order.
        if (IsFocusable(e) && new[] { e }.Concat(e.Ancestors()).Any(a => string.Equals(a.Static("aria-hidden"), "true", StringComparison.OrdinalIgnoreCase))
            && !new[] { e }.Concat(e.Ancestors()).Any(a => a.Static("inert") is not null || a.Static("hidden") is not null || a.StyleHidden))
        {
            yield return (AriaHiddenFocusable, e, tag, [e.Name]);
        }

        // A disabled field with a presentational role is not a form field (the role applies, because it cannot be focused).
        if (IsLabelable(e) && !splatted && !hidden && !IsLabelled(e, settings) && namedRole is not ("presentation" or "none"))
        {
            yield return (FormFieldLabel, e, tag, [e.Name, LabelAdvice]);
        }
        else if (role is not null && FieldRoles.Contains(role) && e.LowerName is not ("input" or "select" or "textarea") && !e.IsMapped && !splatted && !hidden && !HasNameAttribute(e, settings)
            && !(NameFromContentRoles.Contains(role) && HasContentOrName(e, settings)))
        {
            // A <label> cannot name a <div role="textbox">: only aria-label or aria-labelledby (or content, for a checkbox) can.
            yield return (FormFieldLabel, e, tag, [$"{e.Name} role=\"{role}\"", NameFromContentRoles.Contains(role)
                ? "a <label> cannot name it, so give it text content, an aria-label or aria-labelledby"
                : "a <label> cannot name it, so add aria-label or aria-labelledby"]);
        }

        var isButton = (e.LowerName == "button" && namedRole is null) || namedRole == "button";
        var isLink = (e.LowerName == "a" && e.Has("href") && namedRole is null) || namedRole is "link" or "doc-backlink" or "doc-biblioref" or "doc-glossref" or "doc-noteref";
        if ((isButton || isLink) && !splatted && !hidden && !HasContentOrName(e, settings))
        {
            yield return (ControlName, e, tag, [e.Name]);
        }

        if ((e.LowerName is "h1" or "h2" or "h3" or "h4" or "h5" or "h6" || role == "heading") && !splatted && !hidden && !HasContentOrName(e, settings))
        {
            yield return (EmptyHeading, e, tag, [e.Name]);
        }

        if (e.LowerName == "html" && !splatted && (!e.Has("lang") || (e.Static("lang") is { } emptyLang && emptyLang.Trim().Length == 0)))
        {
            yield return (HtmlLang, e, tag, []);
        }

        // HasNameAttribute accepts a non-blank title. A frame removed from the tab order (tabindex="-1") or made
        // presentational is not announced as a frame, so its title does not matter.
        if (e.LowerName == "iframe" && !splatted && !hidden && !HasNameAttribute(e, settings) && role is not ("presentation" or "none")
            && !(e.Static("tabindex") is { } frameTabIndex && frameTabIndex.Trim().StartsWith("-", StringComparison.Ordinal)))
        {
            yield return (FrameTitle, e, tag, []);
        }
    }

    private static TextSpan NameSpan(RazorElement e) => new(e.Start + 1, Math.Max(1, e.Name.Length));

    private static bool HasContentOrName(RazorElement e, AnalyzerSettings settings) =>
        e.HasText || e.HasDynamicContent || e.HasChildComponent || e.HasNamedImage || HasNameAttribute(e, settings);

    private static bool HasNameAttribute(RazorElement e, AnalyzerSettings? settings = null) =>
        (e.Attribute("aria-label") is { } label && (label.IsDynamic || !string.IsNullOrWhiteSpace(label.Value)))
        || (e.Has("aria-labelledby") && !e.LabelledByEmpty)
        || (e.Attribute("title") is { } title && (title.IsDynamic || !string.IsNullOrWhiteSpace(title.Value)))
        || (e.IsMapped && settings is not null && e.Attributes.Any(a => settings.LabelAttributes.Contains(a.Name) && (a.IsDynamic || !string.IsNullOrWhiteSpace(a.Value))));

    // aria-labelledby gives no name when every element it references is empty. A reference is trusted when its target is
    // not in this file (another component may render it), except in a static HTML document, where nothing else can.
    private static void ResolveLabelledBy(string markup, IReadOnlyList<RazorElement> elements)
    {
        var byId = new Dictionary<string, List<RazorElement>>(StringComparer.Ordinal);
        foreach (var element in elements)
        {
            if (element.Static("id")?.Trim() is { Length: > 0 } id)
            {
                if (!byId.TryGetValue(id, out var list))
                {
                    byId[id] = list = [];
                }

                list.Add(element);
            }
        }

        // A static document: an <html> element and no Razor at all (no directive, expression, tag helper or component).
        var standalone = elements.Any(x => x.LowerName == "html" && !x.IsComponent)
            && ScriptOrStyle.Replace(markup, string.Empty).IndexOf('@') < 0
            && !elements.Any(x => x.IsComponent || x.IsMapped || x.Name.IndexOf(':') >= 0 || x.LowerName is "partial" or "component");
        foreach (var element in elements)
        {
            if (element.Static("aria-labelledby") is { } value)
            {
                var ids = value.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                element.LabelledByEmpty = ids.Length > 0 && ids.All(id => byId.TryGetValue(id, out var targets) ? targets.All(IsEmptyLabel) : standalone);
            }
        }

        if (elements.Any(x => x.LabelledByEmpty))
        {
            RazorMarkupScanner.UpdateNamedImages(elements);
        }
    }

    // Empty even for aria-labelledby, which also reads hidden content and the alt, title and value of the element: a
    // plain element without text, children, Razor content, naming attributes, @ref or @attributes (script may fill it).
    private static bool IsEmptyLabel(RazorElement e) =>
        !e.IsComponent && !e.IsMapped
        && e.LowerName is not ("input" or "select" or "textarea" or "img" or "area" or "svg" or "object" or "button" or "meter" or "progress" or "output")
        && !e.HasAnyText && e.Children.Count == 0 && !e.HasDynamicContent && !e.HasChildComponent
        && !e.Attributes.Any(a => a.IsDynamic || a.Name.StartsWith("@", StringComparison.Ordinal)
            || (a.Name.ToLowerInvariant() is "aria-label" or "title" or "alt" or "aria-labelledby" or "value" && !string.IsNullOrWhiteSpace(a.Value)));

    private static readonly System.Text.RegularExpressions.Regex ScriptOrStyle = new(
        "<(script|style)\\b[^>]*>.*?</\\1\\s*>",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static bool IsLabelled(RazorElement e, AnalyzerSettings settings)
    {
        if (HasNameAttribute(e, settings) || e.Has("placeholder"))
        {
            return true;
        }

        if (e.Ancestors().Any(a => a.LowerName == "label" && !a.IsComponent))
        {
            return true;
        }

        // With an id, a <label for> in this or another component may reference it, which cannot be checked here.
        return e.Has("id");
    }

    private static bool IsLabelable(RazorElement e)
    {
        if (e.LowerName is "select" or "textarea")
        {
            return true;
        }

        if (e.LowerName != "input")
        {
            return false;
        }

        var type = e.Attribute("type");
        if (type is { IsDynamic: true })
        {
            return false;
        }

        return (type?.Value ?? "text").Trim().ToLowerInvariant() is not ("hidden" or "submit" or "button" or "reset" or "image");
    }

    private static bool IsHidden(RazorElement e) =>
        new[] { e }.Concat(e.Ancestors()).Any(x => string.Equals(x.Static("aria-hidden"), "true", StringComparison.OrdinalIgnoreCase) || x.Static("hidden") is not null || x.Static("inert") is not null || x.StyleHidden);

    // Roles of form fields that need an accessible name (as in the ACT rule "Form field has non-empty accessible name").
    private static readonly HashSet<string> FieldRoles = new(StringComparer.Ordinal)
    {
        "checkbox", "combobox", "listbox", "menuitemcheckbox", "menuitemradio", "radio", "searchbox", "slider", "spinbutton", "switch", "textbox",
    };

    // Of those, the roles that take their name from their content.
    private static readonly HashSet<string> NameFromContentRoles = new(StringComparer.Ordinal) { "checkbox", "menuitemcheckbox", "menuitemradio", "radio", "switch" };

    private static bool IsFocusable(RazorElement e)
    {
        // Dynamic boolean attributes (disabled="@busy") are only rendered when true, so they count as absent.
        if (e.IsComponent || e.Static("disabled") is not null || e.Static("inert") is not null)
        {
            return false;
        }

        if (e.Attribute("tabindex") is { } tabIndex)
        {
            return tabIndex.IsDynamic || !(tabIndex.Value ?? string.Empty).Trim().StartsWith("-", StringComparison.Ordinal);
        }

        return e.LowerName switch
        {
            "a" or "area" => e.Has("href"),
            "button" or "select" or "textarea" or "summary" or "iframe" => true,
            "input" => !string.Equals(e.Static("type"), "hidden", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private static bool IsNativelyInteractive(RazorElement e) =>
        e.LowerName is "button" or "select" or "textarea" or "summary" or "details"
        || (e.LowerName is "a" or "area" && e.Has("href"))
        || (e.LowerName == "input" && !string.Equals(e.Static("type"), "hidden", StringComparison.OrdinalIgnoreCase));

    private static bool IsNativelyFocusable(RazorElement e) =>
        (e.LowerName is "audio" or "video" && e.Has("controls")) || e.LowerName == "iframe" || e.Has("contenteditable");

    // Items of a composite widget whose container manages focus and keys (aria-activedescendant or roving tabindex):
    // an option in a listbox, a gridcell in a grid ... They are activated through the container's keyboard handling.
    private static readonly HashSet<string> ManagedItemRoles = new(StringComparer.Ordinal) { "option", "gridcell", "row", "treeitem", "menuitem", "menuitemcheckbox", "menuitemradio" };

    private static bool IsManagedItem(RazorElement e, string? role) =>
        role is not null && ManagedItemRoles.Contains(role)
        && e.Ancestors().Any(a => a.Name is "ActiveDescendantGroup" or "RovingFocusGroup"
            || (!a.IsComponent && FirstRole(a) is "listbox" or "grid" or "treegrid" or "tree" or "menu" or "menubar"));

    private static bool IsInteractiveAncestor(RazorElement e) =>
        !e.IsComponent && (e.LowerName is "button" or "summary" or "select" or "option" || (e.LowerName == "a" && e.Has("href")));

    private static string? FirstRole(RazorElement e) =>
        e.Attribute("role") is { IsDynamic: false, Value: { } value } ? value.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() : null;

    private static bool IsKnownRole(string role) =>
        Roles.Contains(role) || role.StartsWith("doc-", StringComparison.Ordinal) || role.StartsWith("graphics-", StringComparison.Ordinal);

    // The closest known ARIA attribute by edit distance (typos such as aria-labeledby).
    private static string? Suggest(string name)
    {
        string? best = null;
        var bestDistance = 3;
        foreach (var candidate in AriaAttributes)
        {
            var distance = Distance(name.ToLowerInvariant(), candidate);
            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best;
    }

    private static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    private static DiagnosticDescriptor Rule(string id, string title, string message, string wcag, DiagnosticSeverity severity = DiagnosticSeverity.Warning) => new(
        id,
        title,
        message,
        Category,
        severity,
        isEnabledByDefault: true,
        description: (wcag.Length == 0 ? "Best practice." : $"WCAG {wcag}.") + " Found in Razor markup at compile time; values computed at run time are not checked.",
        helpLinkUri: Docs + id.ToLowerInvariant());
}
