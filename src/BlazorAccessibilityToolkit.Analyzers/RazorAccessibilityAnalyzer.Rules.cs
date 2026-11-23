using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace BlazorAccessibilityToolkit.Analyzers;

/// <summary>Rules BA11Y014 and later, the per-file rules, and the analyzer's <c>.editorconfig</c> settings.</summary>
public sealed partial class RazorAccessibilityAnalyzer
{
    /// <summary>autofocus attribute.</summary>
    public static readonly DiagnosticDescriptor Autofocus = Rule("BA11Y014", "Avoid autofocus", "<{0}> has autofocus, which moves focus away from where keyboard and screen reader users expect it on load; set focus deliberately (for example with ElementReference.FocusAsync() when a dialog opens) or remove it", string.Empty, DiagnosticSeverity.Info);

    /// <summary>accesskey attribute.</summary>
    public static readonly DiagnosticDescriptor AccessKey = Rule("BA11Y015", "Avoid accesskey", "accesskey=\"{0}\" can conflict with shortcuts of browsers and assistive technologies and is rarely discoverable; remove it or offer documented, remappable shortcuts", string.Empty, DiagnosticSeverity.Info);

    /// <summary>marquee and blink.</summary>
    public static readonly DiagnosticDescriptor DistractingElement = Rule("BA11Y016", "Moving content cannot be paused", "<{0}> moves or blinks and cannot be paused, stopped or hidden; remove it", "2.2.2");

    /// <summary>video without captions.</summary>
    public static readonly DiagnosticDescriptor MediaCaptions = Rule("BA11Y017", "Video has no captions", "<video> has no <track kind=\"captions\">; add captions for the audio content (or mark the video muted when it has no audio)", "1.2.2");

    /// <summary>scope attribute misuse.</summary>
    public static readonly DiagnosticDescriptor ScopeAttribute = Rule("BA11Y018", "Invalid scope attribute", "{0}", "1.3.1");

    /// <summary>Invalid autocomplete token.</summary>
    public static readonly DiagnosticDescriptor AutocompleteValid = Rule("BA11Y019", "Invalid autocomplete value", "autocomplete=\"{0}\" is not a valid autofill value{1}; browsers and assistive technologies cannot identify the field's purpose", "1.3.5");

    /// <summary>aria-activedescendant on an element that cannot be focused.</summary>
    public static readonly DiagnosticDescriptor ActiveDescendantFocus = Rule("BA11Y020", "aria-activedescendant on an element that cannot receive focus", "<{0}> has aria-activedescendant but cannot receive focus; add tabindex=\"0\" so that keyboard focus, and the active descendant, can be followed", "2.1.1, 4.1.2");

    /// <summary>Role without its required states and properties.</summary>
    public static readonly DiagnosticDescriptor RoleRequiredAttributes = Rule("BA11Y021", "Role is missing required ARIA attributes", "role=\"{0}\" requires {1}; assistive technologies cannot report the widget's state without it", "4.1.2");

    /// <summary>Invalid value of an ARIA attribute.</summary>
    public static readonly DiagnosticDescriptor AriaAttributeValue = Rule("BA11Y022", "Invalid ARIA attribute value", "{0}=\"{1}\" is not valid; allowed: {2}", "4.1.2");

    /// <summary>Explicit role equal to the implicit role.</summary>
    public static readonly DiagnosticDescriptor RedundantRole = Rule("BA11Y023", "Redundant role", "<{0}> already has the role '{1}'; remove role=\"{1}\"", string.Empty, DiagnosticSeverity.Info);

    /// <summary>Non-interactive element in the tab sequence.</summary>
    public static readonly DiagnosticDescriptor NonInteractiveTabIndex = Rule("BA11Y024", "Non-interactive element in the tab order", "<{0} tabindex=\"{1}\"> is not interactive, so a keyboard stop on it does nothing; remove the tabindex, or give it an interactive role and keyboard handling (a scrollable region needs an accessible name)", string.Empty, DiagnosticSeverity.Info);

    /// <summary>Hover handlers without focus equivalents.</summary>
    public static readonly DiagnosticDescriptor MouseEventsWithoutFocus = Rule("BA11Y025", "Hover handler has no keyboard equivalent", "<{0}> has @{1} but no @{2}; content or behavior that appears on hover must also be available on keyboard focus", "2.1.1, 1.4.13");

    /// <summary>Interactive content nested in a button or link.</summary>
    public static readonly DiagnosticDescriptor NestedInteractive = Rule("BA11Y026", "Interactive control is nested in another control", "<{0}> is inside <{1}>; nested controls are not announced or are unreachable for screen reader users; move it outside", "4.1.2");

    /// <summary>Link text that is meaningless out of context.</summary>
    public static readonly DiagnosticDescriptor AmbiguousLinkText = Rule("BA11Y027", "Link text is ambiguous", "Link text \"{0}\" does not say where the link goes; use text that describes the destination, or add it with aria-label or visually hidden text", "2.4.4", DiagnosticSeverity.Info);

    /// <summary>lang value that is not a language tag.</summary>
    public static readonly DiagnosticDescriptor LangValid = Rule("BA11Y028", "Invalid language tag", "lang=\"{0}\" is not a valid BCP 47 language tag (for example en, en-US, fr-CA); screen readers cannot choose the right pronunciation", "3.1.1, 3.1.2");

    /// <summary>Viewport meta that prevents zoom.</summary>
    public static readonly DiagnosticDescriptor ViewportZoom = Rule("BA11Y029", "Zoom is disabled", "The viewport meta tag sets {0}, which prevents people from zooming; remove it", "1.4.4");

    /// <summary>Timed refresh or redirect.</summary>
    public static readonly DiagnosticDescriptor MetaRefresh = Rule("BA11Y030", "Timed refresh or redirect", "<meta http-equiv=\"refresh\" content=\"{0}\"> reloads or redirects the page after a delay that users cannot control; redirect on the server, or let users choose", "2.2.1, 3.2.5");

    /// <summary>Routable page without a page title.</summary>
    public static readonly DiagnosticDescriptor PageTitleMissing = Rule("BA11Y031", "Routable page sets no title", "This page has a route but no <PageTitle>; each page needs a descriptive title, which is also what screen readers announce after client-side navigation", "2.4.2");

    /// <summary>Router without FocusOnNavigate.</summary>
    public static readonly DiagnosticDescriptor FocusOnNavigateMissing = Rule("BA11Y032", "Router does not move focus after navigation", "<Router> has no <FocusOnNavigate>; after client-side navigation, focus stays on the activated link and screen reader users are not told that the page changed. Add <FocusOnNavigate RouteData=\"@routeData\" Selector=\"h1\" /> or manage focus yourself", "2.4.3");

    /// <summary>Paste or autofill blocked.</summary>
    public static readonly DiagnosticDescriptor PasteOrAutofillBlocked = Rule("BA11Y033", "Field blocks paste or password managers", "{0}; people who cannot memorize or transcribe passwords and codes rely on pasting and password managers", "3.3.8");

    /// <summary>Drag interaction to review for a single-pointer alternative.</summary>
    public static readonly DiagnosticDescriptor DraggingAlternative = Rule("BA11Y034", "Dragging needs a single-pointer alternative", "<{0}> supports dragging ({1}); make sure the same result can be achieved without dragging, for example with buttons or a menu", "2.5.7", DiagnosticSeverity.Info);

    /// <summary>ARIA state bound to a bool that Blazor removes when false.</summary>
    public static readonly DiagnosticDescriptor AriaStateBoundToBool = Rule("BA11Y035", "ARIA state disappears when false", "{0}=\"{1}\" is bound to a bool. Blazor removes an attribute whose value is false, so the state is never reported as false. Render a string instead: {0}=\"@({2} ? \"true\" : \"false\")\"", "4.1.2");

    /// <summary>alt text that repeats the role.</summary>
    public static readonly DiagnosticDescriptor RedundantImageAlt = Rule("BA11Y036", "Image alt text contains redundant words", "alt=\"{0}\" contains '{1}'; screen readers already announce images, so describe the content or purpose instead", "1.1.1", DiagnosticSeverity.Info);

    /// <summary>role none or presentation on a focusable element.</summary>
    public static readonly DiagnosticDescriptor PresentationRoleConflict = Rule("BA11Y037", "Focusable element has role none or presentation", "<{0} role=\"{1}\"> can receive focus or has a global ARIA attribute, so browsers ignore the role; remove the role, or make the element unfocusable", "4.1.2");

    /// <summary>Fragment-only link that resolves against the base URL.</summary>
    public static readonly DiagnosticDescriptor FragmentLinkLeavesPage = Rule("BA11Y038", "In-page link leaves the page", "<a href=\"{0}\"> resolves against <base href=\"/\"> in Blazor apps, so it opens the home page instead of moving to \"{1}\" on this page; include the page's path, for example href=\"/orders#{1}\", or use the SkipLink component", "2.4.1, 2.4.4");

    private static readonly string[] StateTerms = ["none", "false", "true", "disabled", "enabled", "undefined", "null"];

    private static readonly HashSet<string> AutofillFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "name", "honorific-prefix", "given-name", "additional-name", "family-name", "honorific-suffix", "nickname", "username", "new-password",
        "current-password", "one-time-code", "organization-title", "organization", "street-address", "address-line1", "address-line2",
        "address-line3", "address-level4", "address-level3", "address-level2", "address-level1", "country", "country-name", "postal-code",
        "cc-name", "cc-given-name", "cc-additional-name", "cc-family-name", "cc-number", "cc-exp", "cc-exp-month", "cc-exp-year", "cc-csc",
        "cc-type", "transaction-currency", "transaction-amount", "language", "bday", "bday-day", "bday-month", "bday-year", "sex", "url", "photo",
    };

    private static readonly HashSet<string> AutofillContactFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "tel", "tel-country-code", "tel-national", "tel-area-code", "tel-local", "tel-local-prefix", "tel-local-suffix", "tel-extension", "email", "impp",
    };

    private static readonly Dictionary<string, string[]> RequiredAttributesByRole = new(StringComparer.Ordinal)
    {
        ["checkbox"] = ["aria-checked"],
        ["combobox"] = ["aria-expanded"],
        ["heading"] = ["aria-level"],
        ["menuitemcheckbox"] = ["aria-checked"],
        ["menuitemradio"] = ["aria-checked"],
        ["meter"] = ["aria-valuenow"],
        ["radio"] = ["aria-checked"],
        ["scrollbar"] = ["aria-controls", "aria-valuenow"],
        ["slider"] = ["aria-valuenow"],
        ["switch"] = ["aria-checked"],
    };

    private static readonly string[] Boolean = ["true", "false"];
    private static readonly string[] BooleanOrUndefined = ["true", "false", "undefined"];
    private static readonly string[] Tristate = ["true", "false", "mixed"];

    // Token values (ARIA 1.2). Integer and number values are checked separately.
    private static readonly Dictionary<string, string[]> AriaTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        ["aria-atomic"] = Boolean,
        ["aria-busy"] = Boolean,
        ["aria-disabled"] = Boolean,
        ["aria-modal"] = Boolean,
        ["aria-multiline"] = Boolean,
        ["aria-multiselectable"] = Boolean,
        ["aria-readonly"] = Boolean,
        ["aria-required"] = Boolean,
        ["aria-hidden"] = BooleanOrUndefined,
        ["aria-expanded"] = BooleanOrUndefined,
        ["aria-selected"] = BooleanOrUndefined,
        ["aria-grabbed"] = BooleanOrUndefined,
        ["aria-checked"] = ["true", "false", "mixed", "undefined"],
        ["aria-pressed"] = ["true", "false", "mixed", "undefined"],
        ["aria-autocomplete"] = ["inline", "list", "both", "none"],
        ["aria-current"] = ["page", "step", "location", "date", "time", "true", "false"],
        ["aria-haspopup"] = ["false", "true", "menu", "listbox", "tree", "grid", "dialog"],
        ["aria-invalid"] = ["grammar", "false", "spelling", "true"],
        ["aria-live"] = ["assertive", "off", "polite"],
        ["aria-orientation"] = ["horizontal", "vertical", "undefined"],
        ["aria-sort"] = ["ascending", "descending", "none", "other"],
    };

    private static readonly HashSet<string> TokenListAttributes = new(StringComparer.OrdinalIgnoreCase) { "aria-relevant", "aria-dropeffect" };

    private static readonly Dictionary<string, string[]> AriaTokenLists = new(StringComparer.OrdinalIgnoreCase)
    {
        ["aria-relevant"] = ["additions", "all", "removals", "text"],
        ["aria-dropeffect"] = ["copy", "execute", "link", "move", "none", "popup"],
    };

    private static readonly HashSet<string> IntegerAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "aria-colcount", "aria-colindex", "aria-colspan", "aria-level", "aria-posinset", "aria-rowcount", "aria-rowindex", "aria-rowspan", "aria-setsize",
    };

    private static readonly HashSet<string> NumberAttributes = new(StringComparer.OrdinalIgnoreCase) { "aria-valuemax", "aria-valuemin", "aria-valuenow" };

    // ARIA states where "absent" means something different from "false" (not expandable, not a toggle, no state).
    private static readonly HashSet<string> StatesWhereFalseMatters = new(StringComparer.OrdinalIgnoreCase) { "aria-expanded", "aria-pressed", "aria-checked", "aria-selected" };

    // Roles whose children are presentational: interactive content inside them is not exposed.
    private static readonly HashSet<string> ControlRoles = new(StringComparer.Ordinal) { "button", "link", "checkbox", "radio", "switch", "tab", "menuitem", "menuitemcheckbox", "menuitemradio", "option", "treeitem" };

    private static readonly string[] AmbiguousLinkTexts =
    [
        "click here", "here", "click", "click this", "link", "a link", "this link", "more", "read more", "learn more", "more info", "more information", "details", "this",
    ];

    private static readonly string[] RedundantAltWords = ["image", "picture", "photo", "photograph", "graphic", "icon"];

    private static IEnumerable<(DiagnosticDescriptor Descriptor, RazorElement? Element, TextSpan Span, object[] Args)> CheckMore(RazorElement e, FileFacts file, AnalyzerSettings settings)
    {
        var tag = NameSpan(e);
        var splatted = e.Has("@attributes");

        // Attribute values apply to components as well: they usually pass them to an element.
        foreach (var attribute in e.Attributes)
        {
            if (attribute.IsDynamic || attribute.Value is null || !attribute.Name.StartsWith("aria-", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (InvalidAriaValue(attribute.Name, attribute.Value) is { } allowed)
            {
                yield return (AriaAttributeValue, e, attribute.Span, [attribute.Name.ToLowerInvariant(), attribute.Value, allowed]);
            }
        }

        // A value computed at run time (@onpaste:preventDefault="@block") may be false, so only static values count.
        if (e.Attribute("@onpaste:preventDefault") is { } pasteBlocked && (pasteBlocked.Value is null || string.Equals(pasteBlocked.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase)))
        {
            yield return (PasteOrAutofillBlocked, e, pasteBlocked.Span, [$"<{e.Name}> prevents pasting with @onpaste:preventDefault"]);
        }
        else if (e.Static("onpaste") is { } onpaste && onpaste.IndexOf("return false", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            yield return (PasteOrAutofillBlocked, e, e.Attribute("onpaste")!.Span, [$"<{e.Name}> prevents pasting with onpaste=\"{onpaste}\""]);
        }

        if (e.IsComponent)
        {
            yield break;
        }

        var hidden = IsHidden(e);
        var role = FirstRole(e);

        if (e.Attribute("autofocus") is { } autofocus && !(autofocus.IsDynamic && autofocus.Value is not null))
        {
            yield return (Autofocus, e, autofocus.Span, [e.Name]);
        }

        if (e.Attribute("accesskey") is { } accessKey)
        {
            yield return (AccessKey, e, accessKey.Span, [accessKey.Value ?? string.Empty]);
        }

        if (e.LowerName is "marquee" or "blink")
        {
            yield return (DistractingElement, e, tag, [e.Name]);
        }

        if (e.LowerName == "video" && !splatted && !hidden && !e.Has("muted") && !e.HasDynamicContent && !e.HasChildComponent
            && !e.Children.Any(c => c.LowerName == "track" && (c.Attribute("kind") is { IsDynamic: true } || string.Equals(c.Static("kind")?.Trim(), "captions", StringComparison.OrdinalIgnoreCase))))
        {
            yield return (MediaCaptions, e, tag, []);
        }

        if (e.Attribute("scope") is { IsDynamic: false } scope)
        {
            if (e.LowerName != "th")
            {
                yield return (ScopeAttribute, e, scope.Span, [$"The scope attribute is only valid on <th>, not on <{e.Name}>; use <th scope=\"col\"> or <th scope=\"row\"> for header cells"]);
            }
            else if ((scope.Value ?? string.Empty).Trim().ToLowerInvariant() is not ("row" or "col" or "rowgroup" or "colgroup"))
            {
                yield return (ScopeAttribute, e, scope.Span, [$"scope=\"{scope.Value}\" is not valid; use row, col, rowgroup or colgroup"]);
            }
        }

        // A disabled or hidden field is never filled in, so its autocomplete value does not matter.
        if (e.LowerName is "input" or "select" or "textarea" && e.Attribute("autocomplete") is { IsDynamic: false, Value: { } autocomplete } autocompleteAttribute
            && !hidden && e.Static("disabled") is null && !string.Equals(e.Static("aria-disabled")?.Trim(), "true", StringComparison.OrdinalIgnoreCase)
            && InvalidAutocomplete(autocomplete) is { } autocompleteProblem)
        {
            yield return (AutocompleteValid, e, autocompleteAttribute.Span, [autocomplete, autocompleteProblem]);
        }

        if (e.Has("aria-activedescendant") && !splatted && !hidden && !e.Has("tabindex") && !IsFocusable(e) && !IsNativelyFocusable(e))
        {
            yield return (ActiveDescendantFocus, e, tag, [e.Name]);
        }

        if (role is not null && !splatted && !hidden && RequiredAttributes(e, role) is { Length: > 0 } required && !ProvidesStateNatively(e, role))
        {
            var missing = required.Where(a => !e.Has(a)).ToArray();
            if (missing.Length > 0)
            {
                yield return (RoleRequiredAttributes, e, tag, [role, string.Join(" and ", missing)]);
            }
        }

        if (role is not null && e.Attribute("role") is { IsDynamic: false } roleAttribute && ImplicitRole(e) == role && !IsDeliberatelyRedundant(e, role))
        {
            yield return (RedundantRole, e, roleAttribute.Span, [e.Name, role]);
        }

        if (e.Attribute("tabindex") is { IsDynamic: false } tabIndexAttribute && int.TryParse(tabIndexAttribute.Value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var tabIndex) && tabIndex >= 0
            && !splatted && !hidden && !IsNativelyInteractive(e) && !IsNativelyFocusable(e) && !e.Has("contenteditable")
            && (role is null || !(InteractiveRoles.Contains(role) || role is "tabpanel" or "listbox" or "grid" or "tree" or "treegrid" or "menu" or "menubar" or "radiogroup" or "tablist" or "toolbar" or "dialog" or "alertdialog" or "application"))
            && !e.Attributes.Any(a => PointerEvents.Contains(a.Name) || KeyEvents.Contains(a.Name))
            && !HasNameAttribute(e, settings) && !e.Has("aria-activedescendant") && e.Attribute("role") is not { IsDynamic: true })
        {
            yield return (NonInteractiveTabIndex, e, tabIndexAttribute.Span, [e.Name, tabIndexAttribute.Value!.Trim()]);
        }

        if (!splatted && !hidden)
        {
            foreach (var (hover, focus) in new[] { ("@onmouseover", "@onfocus"), ("@onmouseenter", "@onfocus"), ("@onmouseout", "@onblur"), ("@onmouseleave", "@onblur") })
            {
                var focusAlternatives = focus == "@onfocus" ? new[] { "@onfocus", "@onfocusin" } : ["@onblur", "@onfocusout"];
                if (e.Has(hover) && !focusAlternatives.Any(e.Has) && !e.Ancestors().Any(a => focusAlternatives.Any(a.Has)))
                {
                    yield return (MouseEventsWithoutFocus, e, e.Attribute(hover)!.Span, [e.Name, hover.Substring(1), focus.Substring(1)]);
                    break;
                }
            }
        }

        if (!hidden && (IsNativelyInteractive(e) || IsFocusable(e)) && e.LowerName is not ("details" or "iframe")
            && e.Ancestors().FirstOrDefault(a => !a.IsComponent && IsControlContainer(a)) is { } control
            && !(e.LowerName == "summary" && control.LowerName == "details"))
        {
            yield return (NestedInteractive, e, tag, [e.Name, control.Name]);
        }

        if (e.LowerName == "a" && e.Has("href") && !splatted && !hidden && !e.HasDynamicContent && !e.HasChildComponent && !e.HasNamedImage && !HasNameAttribute(e, settings))
        {
            var text = NormalizeText(e.Text.ToString());
            var comparable = text.TrimEnd('.', '!', '…', ':', ' ').ToLowerInvariant();
            if (comparable.EndsWith("...", StringComparison.Ordinal))
            {
                comparable = comparable.Substring(0, comparable.Length - 3).TrimEnd();
            }

            if (AmbiguousLinkTexts.Contains(comparable))
            {
                yield return (AmbiguousLinkText, e, tag, [text]);
            }
        }

        // lang="" means "unknown language" and is valid; lang=" " is not. On other elements than <html>, a tag only matters
        // when the element has content in that language (not only children with their own lang).
        // On <html>, BA11Y010 already reports a lang of only spaces.
        if (e.Attribute("lang") is { IsDynamic: false, Value: { } lang } langAttribute && lang.Length > 0 && !IsValidLanguageTag(lang)
            && (e.LowerName == "html" ? lang.Trim().Length > 0 : HasOwnLanguageContent(e)))
        {
            yield return (LangValid, e, langAttribute.Span, [lang]);
        }

        if (e.LowerName == "meta")
        {
            var name = e.Static("name")?.Trim();
            var content = e.Static("content");
            if (string.Equals(name, "viewport", StringComparison.OrdinalIgnoreCase) && content is not null && ViewportProblem(content) is { } viewportProblem)
            {
                yield return (ViewportZoom, e, tag, [viewportProblem]);
            }

            // Browsers act on the first refresh <meta> with a valid delay only. "More than 20 hours" is the WCAG 2.2.1 exception.
            if (content is not null && IsRefresh(e) && RefreshDelay(content) is > 0 and <= 72000
                && file.Elements.First(IsRefresh) == e)
            {
                yield return (MetaRefresh, e, tag, [content]);
            }
        }

        if (e.LowerName == "input" && string.Equals(e.Static("type")?.Trim(), "password", StringComparison.OrdinalIgnoreCase)
            && string.Equals(e.Static("autocomplete")?.Trim(), "off", StringComparison.OrdinalIgnoreCase))
        {
            yield return (PasteOrAutofillBlocked, e, e.Attribute("autocomplete")!.Span, ["<input type=\"password\" autocomplete=\"off\"> asks password managers not to fill the field; use autocomplete=\"current-password\" or \"new-password\""]);
        }

        if (!hidden)
        {
            var drag = new List<string>();
            if (string.Equals(e.Static("draggable")?.Trim(), "true", StringComparison.OrdinalIgnoreCase))
            {
                drag.Add("draggable=\"true\"");
            }

            drag.AddRange(e.Attributes.Where(a => a.Name is "@ondragstart" or "@ondrop" or "@ondrag").Select(a => a.Name));
            if (drag.Count > 0)
            {
                yield return (DraggingAlternative, e, tag, [e.Name, string.Join(", ", drag)]);
            }
        }

        foreach (var attribute in e.Attributes.Where(a => StatesWhereFalseMatters.Contains(a.Name) && a.Value is not null))
        {
            if (file.IsBoolExpression(attribute.Value!) is { } expression)
            {
                yield return (AriaStateBoundToBool, e, attribute.Span, [attribute.Name.ToLowerInvariant(), attribute.Value!, expression]);
            }
        }

        // A handler that cancels the navigation (@onclick:preventDefault) makes the link work once the page is interactive.
        if (e.LowerName == "a" && e.Static("href") is { Length: > 1 } fragment && fragment.StartsWith("#", StringComparison.Ordinal) && fragment.IndexOf(' ') < 0
            && !e.Has("@onclick:preventDefault"))
        {
            yield return (FragmentLinkLeavesPage, e, e.Attribute("href")!.Span, [fragment, fragment.Substring(1)]);
        }

        if (e.LowerName == "img" && e.Static("alt") is { } alt && !hidden)
        {
            var words = Regex.Split(alt.ToLowerInvariant(), "[^a-z]+");
            if (RedundantAltWords.FirstOrDefault(words.Contains) is { } redundant)
            {
                yield return (RedundantImageAlt, e, e.Attribute("alt")!.Span, [alt, redundant]);
            }
        }

        if (role is "none" or "presentation" && !splatted && (IsFocusable(e) || e.Attributes.Any(a => a.Name is "aria-describedby" or "aria-label" or "aria-labelledby" or "aria-live" or "aria-controls" or "aria-owns")))
        {
            yield return (PresentationRoleConflict, e, tag, [e.Name, role]);
        }
    }

    private static IEnumerable<(DiagnosticDescriptor Descriptor, RazorElement? Element, TextSpan Span, object[] Args)> CheckFile(FileFacts file)
    {
        if (file.PageDirective is { } page && !file.Elements.Any(e => e.Name is "PageTitle" || (e.LowerName == "title" && !e.IsComponent)))
        {
            yield return (PageTitleMissing, null, page, []);
        }

        if (file.Elements.FirstOrDefault(e => e.Name is "Router" or "Microsoft.AspNetCore.Components.Routing.Router") is { } router
            && !file.Elements.Any(e => e.Name is "FocusOnNavigate" or "Microsoft.AspNetCore.Components.Routing.FocusOnNavigate" or "NavigationFocus" or "BlazorAccessibilityToolkit.Components.NavigationFocus"))
        {
            yield return (FocusOnNavigateMissing, router, NameSpan(router), []);
        }
    }

    // A control whose children are presentational: a button, a link with href, or an explicit control role.
    private static bool IsControlContainer(RazorElement e) =>
        (e.LowerName == "button" && FirstRole(e) is null)
        || (e.LowerName == "a" && e.Has("href") && FirstRole(e) is null)
        || (FirstRole(e) is { } role && ControlRoles.Contains(role));

    // Native elements that provide the state a role requires (an <input type="checkbox" role="switch"> is checked natively).
    // The required states and properties of a role (WAI-ARIA 1.2). A focusable separator is a splitter, which needs a value.
    private static string[] RequiredAttributes(RazorElement e, string role) =>
        role == "separator" && e.Static("tabindex") is { } separatorTabIndex && !separatorTabIndex.Trim().StartsWith("-", StringComparison.Ordinal) && IsFocusable(e) ? ["aria-valuenow"]
        : RequiredAttributesByRole.TryGetValue(role, out var required) ? required : [];

    // Native elements that expose the state themselves: a checkbox input is checked or not, a heading has a level. An
    // <input role="combobox"> does not say whether its popup is expanded; a <select> does.
    private static bool ProvidesStateNatively(RazorElement e, string role) => role == "combobox"
        ? e.LowerName == "select"
        : e.LowerName is "input" or "select" or "meter" or "progress" or "option" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6";

    private static string? ImplicitRole(RazorElement e)
    {
        if (e.LowerName == "input" && e.Attribute("type") is { IsDynamic: true })
        {
            return null;
        }

        var type = (e.Static("type") ?? "text").Trim().ToLowerInvariant();
        return e.LowerName switch
        {
            "button" => "button",
            "a" or "area" when e.Has("href") => "link",
            "nav" => "navigation",
            "main" => "main",
            "aside" when HasNameAttribute(e) || !e.Ancestors().Any(a => !a.IsComponent && a.LowerName is "article" or "aside" or "nav" or "section") => "complementary",
            "article" => "article",
            "ul" or "ol" => "list",
            "li" => "listitem",
            "table" => "table",
            "dialog" => "dialog",
            "progress" => "progressbar",
            "hr" => "separator",
            "textarea" => "textbox",
            "h1" or "h2" or "h3" or "h4" or "h5" or "h6" => "heading",
            "img" when e.Static("alt") is { Length: > 0 } => "img",
            "input" when type == "checkbox" => "checkbox",
            "input" when type == "radio" => "radio",
            "input" when type == "range" => "slider",
            "input" when type is "button" or "submit" or "reset" => "button",
            "input" when type is "text" or "email" or "tel" or "url" && !e.Has("list") => "textbox",
            "input" when type == "search" && !e.Has("list") => "searchbox",
            "input" when type == "number" => "spinbutton",
            _ => null,
        };
    }

    // role="list" on <ul>/<ol> restores list semantics that Safari drops for unstyled lists; role="navigation" on <nav>
    // is common and harmless.
    private static bool IsDeliberatelyRedundant(RazorElement e, string role) =>
        (role == "list" && e.LowerName is "ul" or "ol") || (role == "navigation" && e.LowerName == "nav");

    private static string? InvalidAriaValue(string name, string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (AriaTokens.TryGetValue(name, out var tokens))
        {
            return tokens.Contains(trimmed.ToLowerInvariant()) ? null : string.Join(", ", tokens);
        }

        if (TokenListAttributes.Contains(name) && AriaTokenLists.TryGetValue(name, out var list))
        {
            return trimmed.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).All(t => list.Contains(t.ToLowerInvariant())) ? null : "a space-separated list of " + string.Join(", ", list);
        }

        if (IntegerAttributes.Contains(name))
        {
            return int.TryParse(trimmed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _) ? null : "an integer";
        }

        if (NumberAttributes.Contains(name))
        {
            return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out _) ? null : "a number";
        }

        return null;
    }

    // Returns why the value is invalid (appended to the message), or null when valid.
    private static string? InvalidAutocomplete(string value)
    {
        var tokens = value.Trim().ToLowerInvariant().Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).ToList();
        if (tokens.Count == 0)
        {
            return null;
        }

        if (tokens.Count == 1 && (tokens[0] is "on" or "off" || StateTerms.Contains(tokens[0])))
        {
            return null;
        }

        if (tokens[tokens.Count - 1] == "webauthn")
        {
            tokens.RemoveAt(tokens.Count - 1);
            if (tokens.Count == 0)
            {
                return " (webauthn must follow a field name)";
            }
        }

        var field = tokens[tokens.Count - 1];
        var index = 0;
        if (tokens[index].StartsWith("section-", StringComparison.Ordinal))
        {
            index++;
        }

        if (index < tokens.Count - 1 && tokens[index] is "shipping" or "billing")
        {
            index++;
        }

        var hasContactHint = false;
        if (index < tokens.Count - 1 && tokens[index] is "home" or "work" or "mobile" or "fax" or "pager")
        {
            hasContactHint = true;
            index++;
        }

        if (index != tokens.Count - 1)
        {
            return " (unexpected token order)";
        }

        if (AutofillContactFields.Contains(field))
        {
            return null;
        }

        if (AutofillFields.Contains(field))
        {
            return hasContactHint ? $" ('{tokens[index - 1]}' only applies to contact fields such as tel and email)" : null;
        }

        return $" ('{field}' is not an autofill field name)";
    }

    private static string? ViewportProblem(string content)
    {
        foreach (var part in content.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split(['='], 2);
            if (pair.Length != 2)
            {
                continue;
            }

            var key = pair[0].Trim().ToLowerInvariant();
            var value = pair[1].Trim().ToLowerInvariant();
            if (key == "user-scalable" && Math.Abs(ViewportNumber(value, no: 0)) < 1)
            {
                // Browsers read words other than yes (such as true) as no.
                return value is "no" || LeadingNumber.IsMatch(value) ? $"user-scalable={value}" : $"user-scalable={value} (read as no; write user-scalable=yes or leave it out)";
            }

            if (key == "maximum-scale" && ViewportNumber(value, no: 0.1) is var max && max >= 0 && max < 2)
            {
                return $"maximum-scale={value}";
            }
        }

        return null;
    }

    // Values are read as browsers read them (CSS Device Adaptation): yes is 1, no is 0 (user-scalable) or 0.1,
    // device-width and device-height are 10, a number is read up to the first character that is not part of it, and
    // anything else counts as 0. So user-scalable allows zooming when its value is at least 1 (or at most -1), and a
    // negative maximum-scale is ignored.
    private static double ViewportNumber(string value, double no)
    {
        switch (value)
        {
            case "yes":
                return 1;
            case "no":
                return no;
            case "device-width" or "device-height":
                return 10;
        }

        var match = LeadingNumber.Match(value);
        return match.Success && double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : 0;
    }

    private static readonly Regex LeadingNumber = new("^[+-]?(?:[0-9]+(?:[.][0-9]*)?|[.][0-9]+)(?:e[+-]?[0-9]+)?", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>
    /// A language tag whose primary subtag is registered (as the ACT rules require; assistive technologies only use that
    /// part) and whose other subtags are well-formed: one to eight letters or digits. Tags without a language, such as
    /// private use (x-...) and grandfathered (i-...) tags, tell assistive technologies nothing they can use.
    /// </summary>
    private static bool IsValidLanguageTag(string tag)
    {
        var subtags = tag.Trim().Split('-');
        return subtags.All(s => s.Length is > 0 and <= 8 && s.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9'))
            && subtags[0].Length is 2 or 3
            && LanguageSubtags.IsPrimaryLanguage(subtags[0]);
    }

    // Rendered text (also aria-hidden text, which is still read on screen), a named image or dynamic content whose
    // closest lang attribute is this element's.
    private static bool HasOwnLanguageContent(RazorElement e)
    {
        if (e.HasDynamicContent || e.HasChildComponent)
        {
            return true;
        }

        if (e.HasLanguageText)
        {
            return true;
        }

        // An image with alt text that is not inside a descendant with its own lang.
        var stack = new Stack<RazorElement>(e.Children);
        while (stack.Count > 0)
        {
            var child = stack.Pop();
            if (child.Has("lang"))
            {
                continue;
            }

            if (child.LowerName == "img" && !child.IsComponent && !string.IsNullOrWhiteSpace(child.Attribute("alt")?.Value) && child.Static("hidden") is null && !child.StyleHidden)
            {
                return true;
            }

            foreach (var grandchild in child.Children)
            {
                stack.Push(grandchild);
            }
        }

        return false;
    }

    // The delay as the HTML specification reads a refresh <meta>: leading digits (a fraction is ignored) followed by the
    // end, a separator or white space. Anything else ("+5", "0: url") makes browsers ignore the element.
    private static double? RefreshDelay(string content)
    {
        var match = RefreshContent.Match(content);
        return match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) ? seconds : null;
    }

    private static readonly Regex RefreshContent = new("^[\\t\\n\\f\\r ]*([0-9]+)[0-9.]*(?:$|[;,\\t\\n\\f\\r ])", RegexOptions.CultureInvariant);

    private static bool IsRefresh(RazorElement e) =>
        e.LowerName == "meta" && string.Equals(e.Static("http-equiv")?.Trim(), "refresh", StringComparison.OrdinalIgnoreCase) && e.Static("content") is { } content && RefreshDelay(content) is not null;

    private static string NormalizeText(string text) =>
        Regex.Replace(WebUtility.HtmlDecode(text), "\\s+", " ").Trim();

    /// <summary>Facts about the whole file: the route directive, bool members declared in code, and file-level ignores.</summary>
    internal sealed class FileFacts
    {
        private static readonly Regex PageDirectivePattern = new("^[ \\t]*@(?:page\\b|attribute\\s*\\[\\s*(?:Microsoft\\.AspNetCore\\.Components\\.)?Route\\b)", RegexOptions.Multiline | RegexOptions.CultureInvariant);
        private static readonly Regex BoolDeclaration = new("\\bbool\\??\\s+([A-Za-z_][A-Za-z0-9_]*)\\s*(?:[;={,){]|=>)", RegexOptions.CultureInvariant);
        private static readonly Regex FileIgnore = new("@\\*\\s*blazor-a11y-ignore-file\\b([^*]*)\\*@", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex MemberChain = new("^[A-Za-z_][A-Za-z0-9_]*(?:\\.[A-Za-z_][A-Za-z0-9_]*)*$", RegexOptions.CultureInvariant);
        private readonly HashSet<string> _boolMembers;
        private readonly HashSet<string> _ignored = new(StringComparer.OrdinalIgnoreCase);
        private readonly bool _ignoresAll;

        public FileFacts(string text, IReadOnlyList<RazorElement> elements)
        {
            Elements = elements;
            if (PageDirectivePattern.Match(text) is { Success: true } page)
            {
                var start = page.Index + page.Value.IndexOf('@');
                PageDirective = TextSpan.FromBounds(start, page.Index + page.Length);
            }

            _boolMembers = new HashSet<string>(BoolDeclaration.Matches(text).Cast<Match>().Select(m => m.Groups[1].Value), StringComparer.Ordinal);
            foreach (Match match in FileIgnore.Matches(text))
            {
                var ids = match.Groups[1].Value.Split([' ', ',', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Where(t => t.StartsWith("BA11Y", StringComparison.OrdinalIgnoreCase)).ToArray();
                _ignoresAll |= ids.Length == 0;
                _ignored.UnionWith(ids);
            }
        }

        public IReadOnlyList<RazorElement> Elements { get; }

        /// <summary>Span of the <c>@page</c> (or <c>@attribute [Route]</c>) directive, if the file is routable.</summary>
        public TextSpan? PageDirective { get; }

        public bool IsIgnored(string id) => _ignoresAll || _ignored.Contains(id);

        /// <summary>
        /// The C# expression when an attribute value is a Razor expression that is evidently a bool: a negation, a
        /// comparison or logical operation, a literal, or a member declared as <c>bool</c> in this file. Otherwise null.
        /// </summary>
        // The index of the parenthesis that closes the one at index 0, or -1.
        private static int ClosingParenthesis(string text)
        {
            var depth = 0;
            for (var i = 0; i < text.Length; i++)
            {
                depth += text[i] == '(' ? 1 : text[i] == ')' ? -1 : 0;
                if (depth == 0)
                {
                    return i;
                }
            }

            return -1;
        }

        public string? IsBoolExpression(string value)
        {
            var trimmed = value.Trim();
            if (!trimmed.StartsWith("@", StringComparison.Ordinal))
            {
                return null;
            }

            var expression = trimmed.Substring(1).Trim();
            if (expression.StartsWith("(", StringComparison.Ordinal) && ClosingParenthesis(expression) == expression.Length - 1)
            {
                expression = expression.Substring(1, expression.Length - 2).Trim();
            }
            else if (expression.StartsWith("(", StringComparison.Ordinal))
            {
                // "@(a) && (b)" renders "@(a)" and then the text " && (b)": not an expression.
                return null;
            }

            if (expression.Length == 0 || expression.IndexOf('"') >= 0 || expression.IndexOf('?') >= 0 || expression.IndexOf('$') >= 0
                || expression.IndexOf("ToString", StringComparison.Ordinal) >= 0 || expression.IndexOf('@') >= 0)
            {
                return null;
            }

            if (expression is "true" or "false")
            {
                return expression;
            }

            if (expression.StartsWith("!", StringComparison.Ordinal) && !expression.StartsWith("!=", StringComparison.Ordinal))
            {
                return expression;
            }

            foreach (var op in new[] { "==", "!=", "&&", "||", ">=", "<=", " is " })
            {
                if (expression.IndexOf(op, StringComparison.Ordinal) >= 0)
                {
                    return expression;
                }
            }

            if (MemberChain.IsMatch(expression))
            {
                var last = expression.Substring(expression.LastIndexOf('.') + 1);
                return _boolMembers.Contains(last) ? expression : null;
            }

            return null;
        }
    }
}

/// <summary>
/// Analyzer settings read from <c>.editorconfig</c>:
/// <c>blazor_a11y_analyzer.component_map = MudButton:button, MudIconButton:button, MudImage:img</c> checks components as
/// the native element they render, and <c>blazor_a11y_analyzer.component_label_attributes = Label, AriaLabel</c> lists the
/// component parameters that give a mapped component its accessible name.
/// </summary>
internal sealed class AnalyzerSettings
{
    public const string ComponentMapKey = "blazor_a11y_analyzer.component_map";
    public const string LabelAttributesKey = "blazor_a11y_analyzer.component_label_attributes";

    private static readonly string[] DefaultLabelAttributes = ["Label", "AriaLabel", "Title", "Alt", "AlternateText"];

    public static AnalyzerSettings Default { get; } = new(new Dictionary<string, string>(StringComparer.Ordinal), DefaultLabelAttributes);

    public AnalyzerSettings(IReadOnlyDictionary<string, string> componentMap, IEnumerable<string> labelAttributes)
    {
        ComponentMap = componentMap;
        LabelAttributes = new HashSet<string>(labelAttributes, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Component name to native element.</summary>
    public IReadOnlyDictionary<string, string> ComponentMap { get; }

    /// <summary>Attributes (component parameters) that name a mapped component.</summary>
    public HashSet<string> LabelAttributes { get; }

    public static AnalyzerSettings From(AnalyzerConfigOptions options)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (options.TryGetValue(ComponentMapKey, out var mapValue))
        {
            foreach (var entry in mapValue.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries))
            {
                var pair = entry.Split([':', '='], 2);
                if (pair.Length == 2 && pair[0].Trim().Length > 0 && pair[1].Trim().Length > 0)
                {
                    map[pair[0].Trim()] = pair[1].Trim().ToLowerInvariant();
                }
            }
        }

        var labels = options.TryGetValue(LabelAttributesKey, out var labelValue)
            ? labelValue.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim())
            : DefaultLabelAttributes;
        return map.Count == 0 && !options.TryGetValue(LabelAttributesKey, out _) ? Default : new AnalyzerSettings(map, labels);
    }

    /// <summary>Settings for tests.</summary>
    public static AnalyzerSettings Create(string componentMap, string? labelAttributes = null)
    {
        var values = new Dictionary<string, string> { [ComponentMapKey] = componentMap };
        if (labelAttributes is not null)
        {
            values[LabelAttributesKey] = labelAttributes;
        }

        return From(new DictionaryOptions(values));
    }

    /// <summary>Settings from the analyzer options of an <c>.editorconfig</c> section (the <c>blazor-a11y fix</c> command).</summary>
    public static AnalyzerSettings From(IReadOnlyDictionary<string, string> values) => From(new DictionaryOptions(values));

    private sealed class DictionaryOptions(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            var found = values.TryGetValue(key, out var v);
            value = v ?? string.Empty;
            return found;
        }
    }
}
