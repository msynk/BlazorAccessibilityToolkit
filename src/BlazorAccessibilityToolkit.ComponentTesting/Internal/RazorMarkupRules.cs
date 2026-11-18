using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using BlazorAccessibilityToolkit.Rules;

namespace BlazorAccessibilityToolkit.ComponentTesting.Internal;

/// <summary>A rule result on a bUnit element.</summary>
internal sealed record MarkupRuleResult(AccessibilityRule Rule, IElement Element, string? Message = null, IReadOnlyDictionary<string, string>? Data = null, FindingStatus? Status = null, AccessibilityImpact? Impact = null);

/// <summary>
/// Rules evaluated on bUnit's rendered markup (AngleSharp DOM). The Razor-specific rule uses bUnit's
/// <c>blazor:on*</c> attributes, which reveal Blazor event handlers that a browser cannot see. The other rules are a
/// conservative browserless fallback, used only when no browser engine is configured.
/// </summary>
internal static partial class RazorMarkupRules
{
    // Elements whose keyboard activation fires the click handler. Not label, option, video or audio: a click handler
    // on those is not reachable with the keyboard (see ForwardsKeyboardActivation for labels wrapping a control).
    private static readonly FrozenSet<string> NativeInteractive = FrozenSet.ToFrozenSet(["button", "select", "textarea", "summary", "details"], StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> InteractiveRoles = FrozenSet.ToFrozenSet(
    [
        "button", "link", "checkbox", "menuitem", "menuitemcheckbox", "menuitemradio", "option", "radio", "switch", "tab", "treeitem",
        "gridcell", "row", "combobox", "slider", "spinbutton", "textbox", "searchbox", "scrollbar", "columnheader", "rowheader",
    ], StringComparer.Ordinal);

    private static readonly FrozenSet<string> PointerEvents = FrozenSet.ToFrozenSet(["blazor:onclick", "blazor:onmousedown", "blazor:onmouseup", "blazor:onpointerdown", "blazor:onpointerup", "blazor:ondblclick"], StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> KeyEvents = FrozenSet.ToFrozenSet(["blazor:onkeydown", "blazor:onkeyup", "blazor:onkeypress"], StringComparer.OrdinalIgnoreCase);

    private static readonly string[] IdRefAttributes = ["aria-labelledby", "aria-describedby", "aria-controls", "aria-owns", "aria-errormessage", "aria-activedescendant", "aria-details", "aria-flowto"];

    private static readonly FrozenSet<string> AriaAttributes = FrozenSet.ToFrozenSet(
    [
        "aria-actions", "aria-activedescendant", "aria-atomic", "aria-autocomplete", "aria-braillelabel", "aria-brailleroledescription", "aria-busy", "aria-checked",
        "aria-colcount", "aria-colindex", "aria-colindextext", "aria-colspan", "aria-controls", "aria-current", "aria-describedby", "aria-description",
        "aria-details", "aria-disabled", "aria-dropeffect", "aria-errormessage", "aria-expanded", "aria-flowto", "aria-grabbed", "aria-haspopup",
        "aria-hidden", "aria-invalid", "aria-keyshortcuts", "aria-label", "aria-labelledby", "aria-level", "aria-live", "aria-modal", "aria-multiline",
        "aria-multiselectable", "aria-orientation", "aria-owns", "aria-placeholder", "aria-posinset", "aria-pressed", "aria-readonly", "aria-relevant",
        "aria-required", "aria-roledescription", "aria-rowcount", "aria-rowindex", "aria-rowindextext", "aria-rowspan", "aria-selected", "aria-setsize",
        "aria-sort", "aria-valuemax", "aria-valuemin", "aria-valuenow", "aria-valuetext",
    ], StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> Roles = FrozenSet.ToFrozenSet(
    [
        "alert", "alertdialog", "application", "article", "banner", "blockquote", "button", "caption", "cell", "checkbox", "code", "columnheader",
        "combobox", "comment", "complementary", "contentinfo", "definition", "deletion", "dialog", "directory", "document", "emphasis", "feed", "figure", "form",
        "generic", "grid", "gridcell", "group", "heading", "img", "image", "insertion", "link", "list", "listbox", "listitem", "log", "main", "mark",
        "marquee", "math", "menu", "menubar", "menuitem", "menuitemcheckbox", "menuitemradio", "meter", "navigation", "none", "note", "option",
        "paragraph", "presentation", "progressbar", "radio", "radiogroup", "region", "row", "rowgroup", "rowheader", "scrollbar", "search", "searchbox", "sectionfooter", "sectionheader",
        "separator", "slider", "spinbutton", "status", "strong", "subscript", "suggestion", "superscript", "switch", "tab", "table", "tablist", "tabpanel", "term",
        "textbox", "time", "timer", "toolbar", "tooltip", "tree", "treegrid", "treeitem",
    ], StringComparer.Ordinal);

    /// <summary>Runs the rules. <paramref name="browserless"/> enables the fallback rules.</summary>
    public static IReadOnlyList<MarkupRuleResult> Run(IReadOnlyList<IElement> elements, bool browserless, AccessibilityAuditOptions options)
    {
        var excluded = elements.Where(e => options.ExcludeSelectors.Any(s => SafeMatches(e, s))).ToHashSet();
        var scope = elements.Where(e => !excluded.Contains(e) && !e.Ancestors<IElement>().Any(excluded.Contains)).ToArray();
        var results = new List<MarkupRuleResult>();
        ClickHandlers(scope, results);
        PastePrevented(scope, results);
        if (browserless)
        {
            var byId = elements.Where(e => !string.IsNullOrEmpty(e.Id)).GroupBy(e => e.Id!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
            var referenced = ReferencedIds(elements);
            Ids(scope, byId, referenced, results);
            ValidationMessages(scope, elements, byId, results);
            Groups(scope, byId, results);
            NamesAndLabels(scope, elements, byId, results);
            Aria(scope, results);
            Forms(scope, elements, byId, results);
            Wcag22(scope, results);
        }

        var disabled = options.DisabledRules.ToHashSet(StringComparer.Ordinal);
        return results.Where(r => !disabled.Contains(r.Rule.Id) && (!r.Rule.IsBestPractice || options.IncludeBestPractices)).ToArray();
    }

    /// <summary>Rules evaluated in the given mode (reported as passed when they found nothing).</summary>
    public static IEnumerable<AccessibilityRule> Evaluated(bool browserless, AccessibilityAuditOptions options)
    {
        var rules = new List<AccessibilityRule> { ToolkitRules.ClickHandlerSemantics, ToolkitRules.PasteBlocked };
        if (browserless)
        {
            rules.AddRange(
            [
                ToolkitRules.DuplicateId, ToolkitRules.DuplicateIdReferenced, ToolkitRules.IdReferenceMissing, ToolkitRules.ValidationMessageAssociation,
                ToolkitRules.InvalidWithoutErrorReference, ToolkitRules.RadioGroupUngrouped, ToolkitRules.FieldsetMissingLegend, ToolkitRules.MarkupImageAlt,
                ToolkitRules.MarkupControlName, ToolkitRules.MarkupFormLabel, ToolkitRules.MarkupAriaHiddenFocusable, ToolkitRules.MarkupInvalidAriaAttribute,
                ToolkitRules.MarkupInvalidRole, ToolkitRules.MarkupPositiveTabIndex, ToolkitRules.NavLinkCurrent, ToolkitRules.InputPurposeMissing,
                ToolkitRules.RequiredNotExposed, ToolkitRules.DraggingAlternative, ToolkitRules.RedundantEntry,
            ]);
        }

        var disabled = options.DisabledRules.ToHashSet(StringComparer.Ordinal);
        return rules.Where(r => !disabled.Contains(r.Id) && (!r.IsBestPractice || options.IncludeBestPractices));
    }

    private static void ClickHandlers(IEnumerable<IElement> scope, List<MarkupRuleResult> results)
    {
        foreach (var element in scope)
        {
            var pointer = element.Attributes.Where(a => PointerEvents.Contains(a.Name)).Select(a => a.Name["blazor:".Length..]).ToArray();
            if (pointer.Length == 0 || IsHiddenFromAT(element) || IsNativelyInteractive(element) || ForwardsKeyboardActivation(element) || element.Ancestors<IElement>().Any(IsInteractiveAncestor) || IsManagedItem(element))
            {
                continue;
            }

            var role = FirstRole(element);
            var problems = new List<string>();
            if (role is null || !InteractiveRoles.Contains(role))
            {
                problems.Add(role is null ? "it has no interactive role" : $"its role '{role}' is not interactive");
            }

            var tabIndex = element.GetAttribute("tabindex");
            if (tabIndex is not null && int.TryParse(tabIndex.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
            {
                if (index < 0)
                {
                    problems.Add($"tabindex=\"{tabIndex}\" keeps it out of the keyboard tab sequence");
                }
            }
            else if (!IsNativelyFocusable(element))
            {
                // Browsers ignore a tabindex that is not an integer.
                problems.Add(tabIndex is null ? "it is not focusable (no tabindex)" : $"it is not focusable (tabindex=\"{tabIndex}\" is not an integer)");
            }

            var hasKeyHandler = new[] { element }.Concat(element.Ancestors<IElement>()).Any(e => e.Attributes.Any(a => KeyEvents.Contains(a.Name)));
            if (!hasKeyHandler)
            {
                problems.Add("no @onkeydown/@onkeyup handler supports Enter or Space");
            }

            if (problems.Count == 0)
            {
                continue;
            }

            var message = string.Create(CultureInfo.InvariantCulture, $"<{element.LocalName}> has a Blazor @{string.Join("/@", pointer)} handler, but {string.Join(", ", problems)}. ") + ToolkitRules.ClickHandlerSemantics.Remediation;
            results.Add(new MarkupRuleResult(ToolkitRules.ClickHandlerSemantics, element, message, new Dictionary<string, string>
            {
                ["events"] = string.Join(" ", pointer),
                ["role"] = role ?? string.Empty,
                ["tabindex"] = tabIndex ?? string.Empty,
                ["keyboardHandler"] = hasKeyHandler.ToString(),
            }));
        }
    }

    /// <summary>True for results that come from Blazor event handler attributes (turned off with ComponentAuditSettings.RazorRules).</summary>
    public static bool IsRazorSpecific(MarkupRuleResult result) =>
        result.Rule == ToolkitRules.ClickHandlerSemantics || (result.Rule == ToolkitRules.PasteBlocked && result.Data is { } data && data.TryGetValue("reason", out var reason) && reason == "paste");

    // Razor-specific: @onpaste:preventDefault is invisible in the browser's DOM, but bUnit renders it as an attribute.
    private static void PastePrevented(IEnumerable<IElement> scope, List<MarkupRuleResult> results)
    {
        foreach (var element in scope.Where(e => e.Attributes.Any(a => string.Equals(a.Name, "blazor:onpaste:preventdefault", StringComparison.OrdinalIgnoreCase))))
        {
            var credential = IsCredentialField(element);
            results.Add(new MarkupRuleResult(
                ToolkitRules.PasteBlocked,
                element,
                $"<{element.LocalName}> cancels paste with @onpaste:preventDefault. Remove it: people who cannot memorize or transcribe {(credential ? "passwords and codes" : "text")} rely on pasting.",
                new Dictionary<string, string> { ["reason"] = "paste", ["credential"] = credential.ToString() },
                Impact: credential ? null : AccessibilityImpact.Moderate));
        }
    }

    // WCAG 2.2 heuristics: password managers turned away, draggable content and repeated entry.
    private static void Wcag22(IReadOnlyCollection<IElement> scope, List<MarkupRuleResult> results)
    {
        foreach (var element in scope.Where(e => !IsHiddenFromAT(e)))
        {
            if (element.LocalName == "input" && Is(element, "type", "password") && Is(element, "autocomplete", "off"))
            {
                results.Add(new MarkupRuleResult(
                    ToolkitRules.PasteBlocked,
                    element,
                    "<input type=\"password\" autocomplete=\"off\"> asks password managers not to fill the field. Use autocomplete=\"current-password\" or \"new-password\".",
                    new Dictionary<string, string> { ["reason"] = "autocomplete-off", ["credential"] = "True" }));
            }

            if (RedundantEntryPurpose(element) is { } repeated)
            {
                results.Add(new MarkupRuleResult(ToolkitRules.RedundantEntry, element, Data: new Dictionary<string, string> { ["purpose"] = repeated }));
            }
        }

        // One finding per container of draggable items, not one per item.
        foreach (var group in scope.Where(e => Is(e, "draggable", "true") && !IsHiddenFromAT(e)).GroupBy(e => e.ParentElement))
        {
            var items = group.ToArray();
            results.Add(new MarkupRuleResult(
                ToolkitRules.DraggingAlternative,
                items[0],
                Data: new Dictionary<string, string> { ["draggable"] = items.Length.ToString(CultureInfo.InvariantCulture) }));
        }
    }

    private static bool IsCredentialField(IElement element) =>
        Is(element, "type", "password")
        || (element.GetAttribute("autocomplete") ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(t => t is "username" or "current-password" or "new-password" or "one-time-code");

    // "ConfirmEmail", "Model.EmailConfirmation", "repeat-email": a second field for information already asked for.
    // Repeated passwords are exempt (re-entering a new password is accepted for security).
    private static string? RedundantEntryPurpose(IElement element)
    {
        if (element.LocalName != "input" || NonTextInputTypes.Contains(element.GetAttribute("type") ?? "text") || Is(element, "type", "password"))
        {
            return null;
        }

        foreach (var value in new[] { element.GetAttribute("name"), element.Id })
        {
            var last = (value ?? string.Empty).Split(['.', '[', ']'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;
            var token = new string(last.ToLowerInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());
            if (RedundantEntry().Match(token) is { Success: true } match)
            {
                return match.Groups["purpose"].Value;
            }
        }

        return null;
    }

    [GeneratedRegex("^(?:(?:confirm|repeat|verify|retype|reenter)(?<purpose>e?mail|emailaddress|phone|phonenumber|username)|(?<purpose>e?mail|emailaddress|phone|phonenumber|username)(?:confirm|confirmation|again|repeat|verify|verification))$", RegexOptions.CultureInvariant)]
    private static partial Regex RedundantEntry();

    private static void Ids(IEnumerable<IElement> scope, Dictionary<string, IElement[]> byId, HashSet<string> referenced, List<MarkupRuleResult> results)
    {
        var scopeSet = scope.ToHashSet();
        foreach (var (id, elements) in byId.Where(kv => kv.Value.Length > 1))
        {
            foreach (var element in elements.Skip(1).Where(scopeSet.Contains))
            {
                var data = new Dictionary<string, string> { ["id"] = id, ["count"] = elements.Length.ToString(CultureInfo.InvariantCulture) };
                results.Add(referenced.Contains(id)
                    ? new MarkupRuleResult(ToolkitRules.DuplicateIdReferenced, element, Data: data)
                    : new MarkupRuleResult(ToolkitRules.DuplicateId, element, Data: data));
            }
        }

        foreach (var element in scope)
        {
            foreach (var attribute in IdRefAttributes)
            {
                var value = element.GetAttribute(attribute);
                if (value is null)
                {
                    continue;
                }

                var missing = Tokens(value).Where(id => !byId.ContainsKey(id)).ToArray();
                if (missing.Length > 0)
                {
                    results.Add(new MarkupRuleResult(ToolkitRules.IdReferenceMissing, element, $"{attribute} references missing id(s): {string.Join(", ", missing)}. {ToolkitRules.IdReferenceMissing.Remediation}", new Dictionary<string, string> { ["attribute"] = attribute, ["missing"] = string.Join(" ", missing) }));
                }
            }

            if (element.LocalName == "label" && element.GetAttribute("for") is { Length: > 0 } target && !byId.ContainsKey(target))
            {
                results.Add(new MarkupRuleResult(ToolkitRules.IdReferenceMissing, element, $"label for=\"{target}\" does not match any element id. {ToolkitRules.IdReferenceMissing.Remediation}", new Dictionary<string, string> { ["attribute"] = "for", ["missing"] = target }));
            }
        }
    }

    private static void ValidationMessages(IEnumerable<IElement> scope, IEnumerable<IElement> all, Dictionary<string, IElement[]> byId, List<MarkupRuleResult> results)
    {
        var described = all
            .SelectMany(e => Tokens(e.GetAttribute("aria-describedby")).Concat(Tokens(e.GetAttribute("aria-errormessage"))))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var element in scope)
        {
            if (element.ClassList.Contains("validation-message") && !string.IsNullOrWhiteSpace(element.TextContent) && (string.IsNullOrEmpty(element.Id) || !described.Contains(element.Id)))
            {
                results.Add(new MarkupRuleResult(ToolkitRules.ValidationMessageAssociation, element, Data: new Dictionary<string, string> { ["text"] = Normalize(element.TextContent) }));
            }

            var invalid = element.GetAttribute("aria-invalid");
            if (invalid is not null && invalid != "false" && invalid.Length > 0)
            {
                var refs = Tokens(element.GetAttribute("aria-describedby")).Concat(Tokens(element.GetAttribute("aria-errormessage"))).Where(byId.ContainsKey);
                if (!refs.Any())
                {
                    results.Add(new MarkupRuleResult(ToolkitRules.InvalidWithoutErrorReference, element, Data: new Dictionary<string, string> { ["ariaInvalid"] = invalid }));
                }
            }
        }
    }

    private static void Groups(IReadOnlyCollection<IElement> scope, Dictionary<string, IElement[]> byId, List<MarkupRuleResult> results)
    {
        foreach (var group in scope.Where(e => e.LocalName == "input" && Is(e, "type", "radio") && !string.IsNullOrEmpty(e.GetAttribute("name"))).GroupBy(e => e.GetAttribute("name")!, StringComparer.Ordinal))
        {
            var radios = group.ToArray();
            if (radios.Length < 2)
            {
                continue;
            }

            var container = radios[0].Ancestors<IElement>().FirstOrDefault(a => a.LocalName == "fieldset" || Is(a, "role", "radiogroup") || Is(a, "role", "group"));
            var labelled = container is not null && radios.All(r => container.Contains(r)) && (container.LocalName == "fieldset"
                ? container.Children.Any(c => c.LocalName == "legend" && !string.IsNullOrWhiteSpace(c.TextContent)) || HasAriaName(container, byId)
                : HasAriaName(container, byId));
            if (!labelled)
            {
                results.Add(new MarkupRuleResult(ToolkitRules.RadioGroupUngrouped, radios[0], Data: new Dictionary<string, string> { ["name"] = group.Key, ["count"] = radios.Length.ToString(CultureInfo.InvariantCulture) }));
            }
        }

        foreach (var fieldset in scope.Where(e => e.LocalName == "fieldset"))
        {
            if (!fieldset.Children.Any(c => c.LocalName == "legend" && !string.IsNullOrWhiteSpace(c.TextContent)) && !HasAriaName(fieldset, byId))
            {
                results.Add(new MarkupRuleResult(ToolkitRules.FieldsetMissingLegend, fieldset));
            }
        }
    }

    private static void NamesAndLabels(IEnumerable<IElement> scope, IReadOnlyList<IElement> all, Dictionary<string, IElement[]> byId, List<MarkupRuleResult> results)
    {
        foreach (var element in scope.Where(e => !IsHiddenFromAT(e)))
        {
            var role = FirstRole(element);
            if (element.LocalName == "img" && !element.HasAttribute("alt") && role is not ("presentation" or "none") && !HasAriaName(element, byId) && !element.HasAttribute("title"))
            {
                results.Add(new MarkupRuleResult(ToolkitRules.MarkupImageAlt, element));
            }

            var isButton = (element.LocalName == "button" && role is null) || role == "button";
            var isLink = (element.LocalName == "a" && element.HasAttribute("href") && role is null) || role == "link";
            if ((isButton || isLink) && !HasAnyNameSource(element, byId))
            {
                results.Add(new MarkupRuleResult(ToolkitRules.MarkupControlName, element));
            }

            if (IsLabelable(element) && !HasLabel(element, all, byId))
            {
                // A control with an id may be labelled by a parent component's <label for>; that cannot be confirmed in isolation.
                var external = !string.IsNullOrEmpty(element.Id);
                results.Add(new MarkupRuleResult(
                    ToolkitRules.MarkupFormLabel,
                    element,
                    external ? $"No label was found in the component for #{element.Id}. If a parent component renders <label for=\"{element.Id}\">, verify it on the composed page." : null,
                    Status: external ? FindingStatus.PotentialIssue : null));
            }

            if (element.GetAttribute("tabindex") is { } tabindex && int.TryParse(tabindex, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0)
            {
                results.Add(new MarkupRuleResult(ToolkitRules.MarkupPositiveTabIndex, element, Data: new Dictionary<string, string> { ["tabindex"] = tabindex }));
            }
        }

        foreach (var hidden in scope.Where(e => Is(e, "aria-hidden", "true")))
        {
            var focusable = new[] { hidden }.Concat(hidden.QuerySelectorAll("*")).FirstOrDefault(IsFocusable);
            if (focusable is not null)
            {
                results.Add(new MarkupRuleResult(ToolkitRules.MarkupAriaHiddenFocusable, hidden, $"<{focusable.LocalName}> inside aria-hidden content can receive focus. {ToolkitRules.MarkupAriaHiddenFocusable.Remediation}"));
            }
        }
    }

    private static void Aria(IEnumerable<IElement> scope, List<MarkupRuleResult> results)
    {
        foreach (var element in scope)
        {
            foreach (var attribute in element.Attributes.Where(a => a.Name.StartsWith("aria-", StringComparison.OrdinalIgnoreCase) && !AriaAttributes.Contains(a.Name)))
            {
                results.Add(new MarkupRuleResult(ToolkitRules.MarkupInvalidAriaAttribute, element, $"'{attribute.Name}' is not a WAI-ARIA attribute. {ToolkitRules.MarkupInvalidAriaAttribute.Remediation}", new Dictionary<string, string> { ["attribute"] = attribute.Name }));
            }

            if (element.GetAttribute("role") is { } roleValue)
            {
                var tokens = Tokens(roleValue).ToArray();
                if (tokens.Length > 0 && !tokens.Any(IsKnownRole))
                {
                    results.Add(new MarkupRuleResult(ToolkitRules.MarkupInvalidRole, element, $"role=\"{roleValue}\" contains no valid WAI-ARIA role. {ToolkitRules.MarkupInvalidRole.Remediation}", new Dictionary<string, string> { ["role"] = roleValue }));
                }
            }
        }
    }

    private static readonly (Regex Pattern, string Purpose)[] PurposeByName =
    [
        (new Regex("^(e?mail|emailaddress|useremail)$", RegexOptions.CultureInvariant), "email"),
        (new Regex("^(phone|phonenumber|telephone|telephonenumber|tel|mobile|mobilephone|mobilenumber|cellphone|cellphonenumber)$", RegexOptions.CultureInvariant), "tel"),
        (new Regex("^(firstname|givenname|forename)$", RegexOptions.CultureInvariant), "given-name"),
        (new Regex("^(lastname|familyname|surname)$", RegexOptions.CultureInvariant), "family-name"),
        (new Regex("^(fullname|yourname)$", RegexOptions.CultureInvariant), "name"),
        (new Regex("^(username|userid|login|loginname)$", RegexOptions.CultureInvariant), "username"),
        (new Regex("^(password|currentpassword)$", RegexOptions.CultureInvariant), "current-password"),
        (new Regex("^(newpassword|confirmpassword|passwordconfirmation|repeatpassword)$", RegexOptions.CultureInvariant), "new-password"),
        (new Regex("^(zip|zipcode|postalcode|postcode)$", RegexOptions.CultureInvariant), "postal-code"),
        (new Regex("^(street|streetaddress|address|address1|addressline1)$", RegexOptions.CultureInvariant), "address-line1"),
        (new Regex("^(address2|addressline2)$", RegexOptions.CultureInvariant), "address-line2"),
        (new Regex("^(birthdate|dateofbirth|dob|bday|birthday)$", RegexOptions.CultureInvariant), "bday"),
    ];

    private static readonly FrozenSet<string> NonTextInputTypes = FrozenSet.ToFrozenSet(["hidden", "submit", "button", "reset", "image", "checkbox", "radio", "file", "range", "color"], StringComparer.OrdinalIgnoreCase);

    // Mirrors the browser diagnostics in toolkit.js: NavLink aria-current, input purpose and visually-only required fields.
    private static void Forms(IEnumerable<IElement> scope, IReadOnlyList<IElement> all, Dictionary<string, IElement[]> byId, List<MarkupRuleResult> results)
    {
        foreach (var element in scope.Where(e => !IsHiddenFromAT(e)))
        {
            if (element.LocalName == "a" && element.HasAttribute("href") && element.ClassList.Contains("active")
                && element.Ancestors<IElement>().Any(a => a.LocalName == "nav" || Is(a, "role", "navigation"))
                && element.GetAttribute("aria-current") is null or "false")
            {
                results.Add(new MarkupRuleResult(ToolkitRules.NavLinkCurrent, element, Data: new Dictionary<string, string> { ["href"] = element.GetAttribute("href") ?? string.Empty }));
            }

            if (!element.HasAttribute("autocomplete") && PersonalDataPurpose(element) is { } purpose)
            {
                results.Add(new MarkupRuleResult(
                    ToolkitRules.InputPurposeMissing,
                    element,
                    $"This field appears to collect \"{purpose}\" but has no autocomplete attribute. Add autocomplete=\"{purpose}\" (or the token that matches its purpose).",
                    new Dictionary<string, string> { ["suggested"] = purpose }));
            }

            if (IsRequirable(element) && !element.HasAttribute("required") && !Is(element, "aria-required", "true")
                && LabelTexts(element, all, byId).FirstOrDefault(IsRequiredMarker) is { } label)
            {
                results.Add(new MarkupRuleResult(ToolkitRules.RequiredNotExposed, element, Data: new Dictionary<string, string> { ["label"] = label }));
            }
        }
    }

    private static string? PersonalDataPurpose(IElement element)
    {
        if (element.LocalName is not ("input" or "textarea" or "select"))
        {
            return null;
        }

        var type = element.LocalName == "input" ? (element.GetAttribute("type") ?? "text").ToLowerInvariant() : element.LocalName;
        if (NonTextInputTypes.Contains(type))
        {
            return null;
        }

        // Blazor forms name fields after the bound model path (for example "Model.Email").
        foreach (var value in new[] { element.GetAttribute("name"), element.Id })
        {
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            var last = value.Split(['.', '[', ']'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;
            var token = new string(last.ToLowerInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());
            foreach (var (pattern, purpose) in PurposeByName)
            {
                if (pattern.IsMatch(token))
                {
                    return purpose;
                }
            }
        }

        return type switch
        {
            "email" => "email",
            "tel" => "tel",
            "password" => "current-password",
            _ => null,
        };
    }

    private static bool IsRequirable(IElement element) =>
        element.LocalName is "select" or "textarea"
        || (element.LocalName == "input" && (element.GetAttribute("type") ?? "text").ToLowerInvariant() is not ("hidden" or "submit" or "button" or "reset" or "image" or "range" or "color"));

    private static IEnumerable<string> LabelTexts(IElement element, IReadOnlyList<IElement> all, Dictionary<string, IElement[]> byId)
    {
        foreach (var label in element.Ancestors<IElement>().Where(a => a.LocalName == "label"))
        {
            yield return Normalize(label.TextContent);
        }

        if (!string.IsNullOrEmpty(element.Id))
        {
            foreach (var label in all.Where(l => l.LocalName == "label" && l.GetAttribute("for") == element.Id))
            {
                yield return Normalize(label.TextContent);
            }
        }

        foreach (var id in Tokens(element.GetAttribute("aria-labelledby")))
        {
            if (byId.TryGetValue(id, out var targets))
            {
                yield return Normalize(targets[0].TextContent);
            }
        }
    }

    // "Email *", "* Email", "Email (required)" or "Email - required".
    private static bool IsRequiredMarker(string text) => RequiredMarker().IsMatch(text);

    [GeneratedRegex(@"^\*|\*\s*:?$|\(\s*required\s*\)|[:\-–]\s*required$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex RequiredMarker();

    private static bool IsKnownRole(string role) => Roles.Contains(role) || role.StartsWith("doc-", StringComparison.Ordinal) || role.StartsWith("graphics-", StringComparison.Ordinal);

    private static bool HasLabel(IElement element, IReadOnlyList<IElement> all, Dictionary<string, IElement[]> byId)
    {
        if (HasAriaName(element, byId) || !string.IsNullOrWhiteSpace(element.GetAttribute("title")) || !string.IsNullOrWhiteSpace(element.GetAttribute("placeholder")))
        {
            return true;
        }

        if (element.Ancestors<IElement>().Any(a => a.LocalName == "label" && !string.IsNullOrWhiteSpace(VisibleText(a))))
        {
            return true;
        }

        return !string.IsNullOrEmpty(element.Id) && all.Any(l => l.LocalName == "label" && l.GetAttribute("for") == element.Id && !string.IsNullOrWhiteSpace(VisibleText(l)));
    }

    private static bool HasAnyNameSource(IElement element, Dictionary<string, IElement[]> byId) =>
        HasAriaName(element, byId)
        || !string.IsNullOrWhiteSpace(element.GetAttribute("title"))
        || !string.IsNullOrWhiteSpace(VisibleText(element))
        || element.QuerySelectorAll("img[alt], area[alt]").Any(i => !string.IsNullOrWhiteSpace(i.GetAttribute("alt")) && !IsHiddenFromAT(i))
        || element.QuerySelectorAll("svg title, [aria-label], [aria-labelledby]").Any(i => !IsHiddenFromAT(i));

    private static bool HasAriaName(IElement element, Dictionary<string, IElement[]> byId) =>
        !string.IsNullOrWhiteSpace(element.GetAttribute("aria-label"))
        || Tokens(element.GetAttribute("aria-labelledby")).Any(id => byId.TryGetValue(id, out var targets) && targets.Any(t => !string.IsNullOrWhiteSpace(t.TextContent) || HasAriaName(t, byId)));

    private static string VisibleText(IElement element)
    {
        var sb = new StringBuilder();
        void Append(INode node)
        {
            foreach (var child in node.ChildNodes)
            {
                if (child is IElement e)
                {
                    if (IsHiddenFromAT(e) || e.LocalName is "script" or "style" or "template")
                    {
                        continue;
                    }

                    if (e.LocalName is "input" && (Is(e, "type", "submit") || Is(e, "type", "button") || Is(e, "type", "reset")))
                    {
                        sb.Append(e.GetAttribute("value"));
                    }

                    Append(e);
                }
                else if (child.NodeType == NodeType.Text)
                {
                    sb.Append(child.TextContent);
                }
            }
        }

        Append(element);
        return Normalize(sb.ToString());
    }

    private static bool IsLabelable(IElement element)
    {
        if (element.LocalName is "select" or "textarea")
        {
            return !element.HasAttribute("hidden");
        }

        if (element.LocalName != "input")
        {
            return false;
        }

        var type = (element.GetAttribute("type") ?? "text").ToLowerInvariant();
        return type is not ("hidden" or "submit" or "button" or "reset" or "image");
    }

    private static bool IsFocusable(IElement element)
    {
        if (element.HasAttribute("disabled") || element.HasAttribute("inert"))
        {
            return false;
        }

        if (element.GetAttribute("tabindex") is { } tabindex)
        {
            return !tabindex.Trim().StartsWith('-');
        }

        return element.LocalName switch
        {
            "a" or "area" => element.HasAttribute("href"),
            "button" or "select" or "textarea" or "summary" or "iframe" => true,
            "input" => !Is(element, "type", "hidden"),
            "audio" or "video" => element.HasAttribute("controls"),
            _ => Is(element, "contenteditable", "true") || Is(element, "contenteditable", string.Empty),
        };
    }

    private static bool IsNativelyInteractive(IElement element) =>
        NativeInteractive.Contains(element.LocalName)
        || (element.LocalName is "a" or "area" && element.HasAttribute("href"))
        || (element.LocalName == "input" && !Is(element, "type", "hidden"));

    // Focusable without a tabindex, among elements that are not natively interactive.
    private static bool IsNativelyFocusable(IElement element) =>
        (element.LocalName is "audio" or "video" && element.HasAttribute("controls"))
        || element.LocalName == "iframe"
        || Is(element, "contenteditable", "true") || Is(element, "contenteditable", string.Empty);

    // A label that wraps a checkbox, radio button or button: activating that control with the keyboard dispatches a
    // click that bubbles to the label, so the label's click handler runs.
    private static bool ForwardsKeyboardActivation(IElement element) =>
        element.LocalName == "label"
        && element.QuerySelectorAll("input, button").Any(c => c.LocalName == "button"
            || (c.GetAttribute("type")?.ToLowerInvariant() is "checkbox" or "radio" or "submit" or "reset" or "button" or "image"));

    // An item of a composite widget whose container manages focus and keys (aria-activedescendant or roving tabindex),
    // such as an option in a listbox: it is activated through the container's keyboard handling.
    private static bool IsManagedItem(IElement element) =>
        FirstRole(element) is "option" or "gridcell" or "row" or "treeitem" or "menuitem" or "menuitemcheckbox" or "menuitemradio"
        && element.Ancestors<IElement>().Any(a => FirstRole(a) is "listbox" or "grid" or "treegrid" or "tree" or "menu" or "menubar");

    // Content inside these elements is activated through the ancestor (keyboard included).
    private static bool IsInteractiveAncestor(IElement element) =>
        element.LocalName is "button" or "summary" or "select" or "option" || (element.LocalName is "a" && element.HasAttribute("href"));

    private static bool IsHiddenFromAT(IElement element) =>
        new[] { element }.Concat(element.Ancestors<IElement>()).Any(e => Is(e, "aria-hidden", "true") || e.HasAttribute("hidden") || e.HasAttribute("inert"));

    private static string? FirstRole(IElement element) => Tokens(element.GetAttribute("role")).FirstOrDefault();

    private static bool Is(IElement element, string attribute, string value) =>
        string.Equals(element.GetAttribute(attribute), value, StringComparison.OrdinalIgnoreCase);

    private static HashSet<string> ReferencedIds(IEnumerable<IElement> elements)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in elements)
        {
            foreach (var attribute in IdRefAttributes)
            {
                ids.UnionWith(Tokens(element.GetAttribute(attribute)));
            }

            if (element.LocalName == "label" && element.GetAttribute("for") is { Length: > 0 } target)
            {
                ids.Add(target);
            }
        }

        return ids;
    }

    private static IEnumerable<string> Tokens(string? value) =>
        string.IsNullOrWhiteSpace(value) ? [] : value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static bool SafeMatches(IElement element, string selector)
    {
        try
        {
            return element.Matches(selector);
        }
        catch (Exception ex) when (ex is DomException or ArgumentException)
        {
            return false;
        }
    }

    internal static string Normalize(string? text) => Whitespace().Replace(text ?? string.Empty, " ").Trim();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
