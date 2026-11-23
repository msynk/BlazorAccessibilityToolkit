using Microsoft.CodeAnalysis.Text;

namespace BlazorAccessibilityToolkit.Analyzers;

/// <summary>An attribute of a scanned element.</summary>
internal sealed class RazorAttribute
{
    public RazorAttribute(string name, string? value, TextSpan span)
    {
        Name = name;
        Value = value;
        Span = span;
    }

    /// <summary>Attribute name as written (for example <c>@onclick</c> or <c>aria-label</c>).</summary>
    public string Name { get; }

    /// <summary>Value without quotes, or <c>null</c> for a boolean attribute.</summary>
    public string? Value { get; }

    public TextSpan Span { get; }

    /// <summary>True when the value is computed at run time (contains a Razor expression).</summary>
    public bool IsDynamic => Name.StartsWith("@", StringComparison.Ordinal) || (Value is not null && Value.IndexOf('@') >= 0);
}

/// <summary>An element or component tag found in Razor markup, with what is known about its content.</summary>
internal sealed class RazorElement
{
    public RazorElement(string name, int start, RazorElement? parent)
    {
        Name = name;
        Start = start;
        Parent = parent;
        LowerName = name.ToLowerInvariant();
        IsComponent = name.Length > 0 && (char.IsUpper(name[0]) || name.IndexOf('.') >= 0);
    }

    public string Name { get; }

    /// <summary>Lower-case element name, or the native element a mapped component renders.</summary>
    public string LowerName { get; private set; }

    /// <summary>True for Razor components (names starting with an upper-case letter or containing a namespace) that are not mapped to a native element.</summary>
    public bool IsComponent { get; private set; }

    /// <summary>True when the component is checked as the native element it renders (<c>blazor_a11y_analyzer.component_map</c>).</summary>
    public bool IsMapped { get; private set; }

    /// <summary>Checks this component as the native element <paramref name="element"/>.</summary>
    public void MapTo(string element)
    {
        LowerName = element.ToLowerInvariant();
        IsComponent = false;
        IsMapped = true;
    }

    public int Start { get; }

    /// <summary>Span of the start tag.</summary>
    public TextSpan StartTag { get; set; }

    public RazorElement? Parent { get; }

    public List<RazorAttribute> Attributes { get; } = [];

    /// <summary>Child elements and components, in source order.</summary>
    public List<RazorElement> Children { get; } = [];

    /// <summary>Static text content readable by assistive technologies (HTML entities not decoded).</summary>
    public System.Text.StringBuilder Text { get; } = new();

    /// <summary>Static text that assistive technologies can read (not inside aria-hidden content).</summary>
    public bool HasText { get; set; }

    /// <summary>
    /// Rendered static text (also aria-hidden text, which is still shown) whose closest lang attribute is this element's
    /// or an ancestor's: text inside a descendant with its own lang does not count.
    /// </summary>
    public bool HasLanguageText { get; set; }

    private bool? _styleHidden;

    /// <summary>Whether the static inline style hides the element (cached <see cref="RazorMarkupScanner.StyleHides"/>).</summary>
    public bool StyleHidden => _styleHidden ??= RazorMarkupScanner.StyleHides(this);

    /// <summary>Any static text in the content, hidden or not.</summary>
    public bool HasAnyText { get; set; }

    /// <summary>Set when every element that aria-labelledby references is known to be empty (or, in a static document, missing).</summary>
    public bool LabelledByEmpty { get; set; }

    /// <summary>Content rendered by a Razor expression (unknown at compile time).</summary>
    public bool HasDynamicContent { get; set; }

    /// <summary>A component in the content (it may render text).</summary>
    public bool HasChildComponent { get; set; }

    /// <summary>An image with alternative text in the content.</summary>
    public bool HasNamedImage { get; set; }

    /// <summary>True when a Razor comment on the line before the element (or for the whole file) ignores rules.</summary>
    public HashSet<string>? IgnoredRules { get; set; }

    public bool IgnoresAll { get; set; }

    public RazorAttribute? Attribute(string name)
    {
        foreach (var attribute in Attributes)
        {
            if (string.Equals(attribute.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return attribute;
            }
        }

        return null;
    }

    public bool Has(string name) => Attribute(name) is not null;

    /// <summary>The attribute's static value, or <c>null</c> when absent or dynamic.</summary>
    public string? Static(string name) => Attribute(name) is { IsDynamic: false } a ? a.Value ?? string.Empty : null;

    public IEnumerable<RazorElement> Ancestors()
    {
        for (var e = Parent; e is not null; e = e.Parent)
        {
            yield return e;
        }
    }

    public bool IsIgnored(string id) => IgnoresAll || (IgnoredRules is not null && IgnoredRules.Contains(id));
}

/// <summary>
/// A tolerant scanner for Razor markup. It finds element and component start tags with their attributes and tracks,
/// for each element, whether its content contains readable text, Razor expressions, child components or named images.
/// It skips Razor comments, HTML comments, <c>@code</c>/<c>@functions</c> blocks, <c>@{ }</c> code blocks and the
/// content of <c>script</c> and <c>style</c>. It is not a full Razor parser: it never throws, and constructs it does not
/// understand are treated as dynamic content so that rules stay quiet rather than report false positives.
/// </summary>
internal static class RazorMarkupScanner
{
    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr",
    };

    private static readonly HashSet<string> CodeKeywords = new(StringComparer.Ordinal)
    {
        "if", "else", "foreach", "for", "while", "do", "switch", "using", "lock", "try", "catch", "finally", "case", "default", "break", "continue", "return",
    };

    private const string IgnorePrefix = "blazor-a11y-ignore";

    public static List<RazorElement> Scan(string text)
    {
        var elements = new List<RazorElement>();
        var stack = new List<RazorElement>();

        // Open Razor code blocks (the "{ }" of @if, @foreach ...), each with the element depth at which it was opened.
        // Directly inside a code block, content is C#, not text; inside an element opened in the block, it is markup.
        var codeBlocks = new List<int>();
        HashSet<string>? fileIgnores = null;
        var fileIgnoresAll = false;
        HashSet<string>? pendingIgnores = null;
        var pendingIgnoresAll = false;
        var pendingLineEnd = -1;
        var i = 0;
        var n = text.Length;

        while (i < n)
        {
            var c = text[i];
            if (c == '@')
            {
                if (At(text, i, "@*"))
                {
                    var end = text.IndexOf("*@", i + 2, StringComparison.Ordinal);
                    var comment = text.Substring(i + 2, (end < 0 ? n : end) - i - 2).Trim();
                    i = end < 0 ? n : end + 2;
                    if (comment.StartsWith(IgnorePrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        var rest = comment.Substring(IgnorePrefix.Length);
                        var wholeFile = rest.StartsWith("-file", StringComparison.OrdinalIgnoreCase);
                        if (wholeFile)
                        {
                            rest = rest.Substring("-file".Length);
                        }

                        var ids = new HashSet<string>(rest.Split([' ', ',', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Where(t => t.StartsWith("BA11Y", StringComparison.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);
                        if (wholeFile)
                        {
                            fileIgnoresAll |= ids.Count == 0;
                            fileIgnores ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            fileIgnores.UnionWith(ids);
                        }
                        else
                        {
                            pendingIgnores = ids;
                            pendingIgnoresAll = ids.Count == 0;
                            pendingLineEnd = LineEnd(text, i);
                        }
                    }

                    continue;
                }

                if (At(text, i, "@@"))
                {
                    MarkText(stack, "@");
                    i += 2;
                    continue;
                }

                // An e-mail address: Razor treats "name@host" as text.
                if (i > 0 && char.IsLetterOrDigit(text[i - 1]))
                {
                    MarkText(stack, "@");
                    i++;
                    continue;
                }

                var word = ReadIdentifier(text, i + 1);
                if (word is "code" or "functions")
                {
                    var brace = text.IndexOf('{', i);
                    i = brace < 0 ? n : SkipBalanced(text, brace, '{', '}');
                    continue;
                }

                if (i + 1 < n && text[i + 1] == '{')
                {
                    i = SkipBalanced(text, i + 1, '{', '}');
                    continue;
                }

                if (i + 1 < n && text[i + 1] == '(')
                {
                    MarkDynamic(stack);
                    i = SkipBalanced(text, i + 1, '(', ')');
                    continue;
                }

                if (word.Length == 0)
                {
                    MarkText(stack, "@");
                    i++;
                    continue;
                }

                i += 1 + word.Length;
                if (word == "await")
                {
                    // @await SomethingAsync(): the expression after "await" renders content.
                    i = SkipWhitespace(text, i);
                    word = ReadIdentifier(text, i);
                    i += word.Length;
                }
                else if (CodeKeywords.Contains(word) || word is "page" or "layout" or "inject" or "inherits" or "implements" or "namespace" or "attribute" or "rendermode" or "typeparam" or "preservewhitespace" or "model" or "section")
                {
                    // Control flow or a directive: skip its parenthesized part and the rest of a directive line.
                    i = SkipWhitespace(text, i);
                    if (i < n && text[i] == '(')
                    {
                        i = SkipBalanced(text, i, '(', ')');
                    }
                    else if (!CodeKeywords.Contains(word) || word == "using")
                    {
                        i = LineEnd(text, i);
                        continue;
                    }

                    var brace = SkipWhitespace(text, i);
                    if (CodeKeywords.Contains(word) && brace < n && text[brace] == '{')
                    {
                        codeBlocks.Add(stack.Count);
                        i = brace + 1;
                    }

                    continue;
                }

                // An implicit expression such as @Model.Name, @item?.Title or @Format(value)[0].
                MarkDynamic(stack);
                while (i < n)
                {
                    if (text[i] == '(')
                    {
                        i = SkipBalanced(text, i, '(', ')');
                    }
                    else if (text[i] == '[')
                    {
                        i = SkipBalanced(text, i, '[', ']');
                    }
                    else if ((text[i] == '.' || (text[i] == '?' && i + 1 < n && text[i + 1] == '.')) && i + 2 < n && char.IsLetter(text[i + (text[i] == '?' ? 2 : 1)]))
                    {
                        i += text[i] == '?' ? 2 : 1;
                        i += ReadIdentifier(text, i).Length;
                    }
                    else
                    {
                        break;
                    }
                }

                continue;
            }

            if (c == '<')
            {
                if (At(text, i, "<!--"))
                {
                    var end = text.IndexOf("-->", i + 4, StringComparison.Ordinal);
                    i = end < 0 ? n : end + 3;
                    continue;
                }

                if (i + 1 < n && text[i + 1] == '/')
                {
                    var name = ReadTagName(text, i + 2);
                    var close = text.IndexOf('>', i);
                    i = close < 0 ? n : close + 1;
                    for (var k = stack.Count - 1; k >= 0; k--)
                    {
                        if (string.Equals(stack[k].Name, name, StringComparison.Ordinal))
                        {
                            stack.RemoveRange(k, stack.Count - k);
                            codeBlocks.RemoveAll(depth => depth > stack.Count);
                            break;
                        }
                    }

                    continue;
                }

                if (i + 1 < n && (char.IsLetter(text[i + 1]) || text[i + 1] == '_'))
                {
                    var name = ReadTagName(text, i + 1);
                    var parent = stack.Count > 0 ? stack[stack.Count - 1] : null;
                    var element = new RazorElement(name, i, parent);
                    var j = ReadAttributes(text, i + 1 + name.Length, element, out var selfClosing);
                    element.StartTag = TextSpan.FromBounds(i, j);
                    if (pendingIgnores is not null && pendingLineEnd >= 0 && i <= NextLineEnd(text, pendingLineEnd))
                    {
                        element.IgnoredRules = pendingIgnores;
                        element.IgnoresAll = pendingIgnoresAll;
                        pendingIgnores = null;
                    }

                    if (fileIgnores is not null)
                    {
                        element.IgnoredRules = element.IgnoredRules is null ? fileIgnores : new HashSet<string>(element.IgnoredRules.Concat(fileIgnores), StringComparer.OrdinalIgnoreCase);
                    }

                    element.IgnoresAll |= fileIgnoresAll;
                    elements.Add(element);
                    parent?.Children.Add(element);
                    if (element.IsComponent)
                    {
                        foreach (var open in stack)
                        {
                            open.HasChildComponent = true;
                        }
                    }
                    else if (element.LowerName == "img" && IsNamedImage(element) && !IsHiddenAttribute(element))
                    {
                        MarkUntilHidden(stack, e => e.HasNamedImage = true);
                    }

                    i = j;
                    if (!selfClosing && !(VoidElements.Contains(name) && !element.IsComponent))
                    {
                        if (element.LowerName is "script" or "style" && !element.IsComponent)
                        {
                            var end = text.IndexOf("</" + name, i, StringComparison.OrdinalIgnoreCase);
                            i = end < 0 ? n : end;
                            continue;
                        }

                        stack.Add(element);
                    }

                    continue;
                }

                MarkText(stack, "<");
                i++;
                continue;
            }

            // C# directly inside a code block: track its braces, it is not text.
            if (codeBlocks.Count > 0 && codeBlocks[codeBlocks.Count - 1] == stack.Count)
            {
                while (i < n && text[i] != '<' && text[i] != '@')
                {
                    var ch = text[i];
                    if (ch == '"' || ch == '\'')
                    {
                        var close = text.IndexOf(ch, i + 1);
                        i = close < 0 ? n : close + 1;
                        continue;
                    }

                    if (ch == '{')
                    {
                        codeBlocks.Add(stack.Count);
                    }
                    else if (ch == '}')
                    {
                        codeBlocks.RemoveAt(codeBlocks.Count - 1);
                        if (codeBlocks.Count == 0 || codeBlocks[codeBlocks.Count - 1] != stack.Count)
                        {
                            i++;
                            break;
                        }
                    }

                    i++;
                }

                continue;
            }

            // Text up to the next tag or Razor transition.
            var next = i;
            while (next < n && text[next] != '<' && text[next] != '@')
            {
                next++;
            }

            var segment = text.Substring(i, next - i);
            if (IsReadableText(segment))
            {
                MarkText(stack, segment);
            }

            i = next;
        }

        return elements;
    }

    private static bool IsHiddenAttribute(RazorElement element) =>
        string.Equals(element.Static("aria-hidden"), "true", StringComparison.OrdinalIgnoreCase) || element.Static("hidden") is not null || element.StyleHidden;

    /// <summary>
    /// Sets <see cref="RazorElement.HasNamedImage"/> again after <see cref="RazorElement.LabelledByEmpty"/> is known: an
    /// image named only by aria-labelledby that references empty elements does not name its container.
    /// </summary>
    internal static void UpdateNamedImages(IReadOnlyList<RazorElement> elements)
    {
        foreach (var element in elements)
        {
            element.HasNamedImage = false;
        }

        foreach (var image in elements)
        {
            // As in the first pass: mapped components are not counted as images here.
            if (image.LowerName == "img" && !image.IsComponent && !image.IsMapped && IsNamedImage(image) && !IsHiddenAttribute(image) && !image.Ancestors().Any(IsHiddenAttribute))
            {
                foreach (var ancestor in image.Ancestors())
                {
                    ancestor.HasNamedImage = true;
                }
            }
        }
    }

    // An image that gives its container a name: alt text, aria-label, aria-labelledby or title, unless a presentational
    // role removes it (which a focusable image ignores).
    private static bool IsNamedImage(RazorElement image)
    {
        var named = !string.IsNullOrWhiteSpace(image.Attribute("alt")?.Value)
            || (image.Attribute("aria-label") is { } label && (label.IsDynamic || !string.IsNullOrWhiteSpace(label.Value)))
            || (image.Has("aria-labelledby") && !image.LabelledByEmpty)
            || (image.Attribute("title") is { } title && (title.IsDynamic || !string.IsNullOrWhiteSpace(title.Value)));
        var role = image.Static("role")?.Trim().Split(' ')[0].ToLowerInvariant();
        return named && !(role is "presentation" or "none" && image.Attribute("tabindex") is null);
    }

    /// <summary>
    /// Whether the element's static inline style removes it from rendering (<c>display: none</c>) or hides it
    /// (<c>visibility: hidden</c> or <c>collapse</c>). Style sheets are not read.
    /// </summary>
    internal static bool StyleHides(RazorElement element)
    {
        if (element.Static("style") is not { Length: > 0 } style)
        {
            return false;
        }

        foreach (var declaration in style.Split(';'))
        {
            var colon = declaration.IndexOf(':');
            if (colon < 0)
            {
                continue;
            }

            var property = declaration.Substring(0, colon).Trim().ToLowerInvariant();
            var value = declaration.Substring(colon + 1).Replace("!important", string.Empty).Trim().ToLowerInvariant();
            if ((property == "display" && value == "none") || (property == "visibility" && value is "hidden" or "collapse"))
            {
                return true;
            }
        }

        return false;
    }

    // Text inside an aria-hidden element is not part of the accessible name of its ancestors.
    private static void MarkUntilHidden(List<RazorElement> stack, Action<RazorElement> mark)
    {
        for (var k = 0; k < stack.Count; k++)
        {
            if (IsHiddenAttribute(stack[k]))
            {
                return;
            }
        }

        foreach (var element in stack)
        {
            mark(element);
        }
    }

    private static void MarkText(List<RazorElement> stack, string text)
    {
        foreach (var element in stack)
        {
            element.HasAnyText = true;
        }

        MarkUntilHidden(stack, e =>
        {
            e.HasText = true;
            e.Text.Append(text);
        });

        // The language of rendered text (aria-hidden text is still shown on screen) comes from the closest element with a
        // lang attribute, so the text belongs to that element and the ones in between, not to its other ancestors.
        if (!stack.Any(e => e.Static("hidden") is not null || e.StyleHidden))
        {
            for (var k = stack.Count - 1; k >= 0; k--)
            {
                stack[k].HasLanguageText = true;
                if (stack[k].Has("lang"))
                {
                    break;
                }
            }
        }
    }

    private static void MarkDynamic(List<RazorElement> stack)
    {
        foreach (var element in stack)
        {
            element.HasDynamicContent = true;
        }
    }

    // Plain text that is not just C# punctuation left between Razor code constructs ("}", "{", "} else {", ";").
    private static bool IsReadableText(string text)
    {
        var stripped = text.Replace("{", " ").Replace("}", " ").Replace(";", " ");
        foreach (var token in stripped.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (token != "else")
            {
                return true;
            }
        }

        return false;
    }

    private static int ReadAttributes(string text, int i, RazorElement element, out bool selfClosing)
    {
        var n = text.Length;
        selfClosing = false;
        while (i < n)
        {
            i = SkipWhitespace(text, i);
            if (i >= n)
            {
                break;
            }

            if (text[i] == '>')
            {
                return i + 1;
            }

            if (text[i] == '/' && i + 1 < n && text[i + 1] == '>')
            {
                selfClosing = true;
                return i + 2;
            }

            if (At(text, i, "@*"))
            {
                var end = text.IndexOf("*@", i + 2, StringComparison.Ordinal);
                i = end < 0 ? n : end + 2;
                continue;
            }

            var start = i;
            if (text[i] == '@' && i + 1 < n && text[i + 1] == '(')
            {
                // A Razor expression in attribute position.
                i = SkipBalanced(text, i + 1, '(', ')');
                element.Attributes.Add(new RazorAttribute("@attributes", null, TextSpan.FromBounds(start, i)));
                continue;
            }

            while (i < n && !char.IsWhiteSpace(text[i]) && text[i] != '=' && text[i] != '>' && !(text[i] == '/' && i + 1 < n && text[i + 1] == '>'))
            {
                i++;
            }

            if (i == start)
            {
                i++;
                continue;
            }

            var name = text.Substring(start, i - start);
            var afterName = SkipWhitespace(text, i);
            string? value = null;
            if (afterName < n && text[afterName] == '=')
            {
                i = SkipWhitespace(text, afterName + 1);
                if (i < n && (text[i] == '"' || text[i] == '\''))
                {
                    var quote = text[i];
                    var valueStart = i + 1;
                    i = valueStart;
                    var depth = 0;
                    while (i < n)
                    {
                        var ch = text[i];
                        if (ch == '(')
                        {
                            depth++;
                        }
                        else if (ch == ')' && depth > 0)
                        {
                            depth--;
                        }
                        else if (ch == quote && depth == 0)
                        {
                            break;
                        }
                        else if (ch == '"' && depth > 0)
                        {
                            // A C# string inside a Razor expression, for example @onclick="@(() => Select("x"))".
                            var close = text.IndexOf('"', i + 1);
                            i = close < 0 ? n : close;
                        }

                        i++;
                    }

                    value = text.Substring(valueStart, Math.Min(i, n) - valueStart);
                    i = Math.Min(i + 1, n);
                }
                else
                {
                    var valueStart = i;
                    if (i < n && text[i] == '@' && i + 1 < n && text[i + 1] == '(')
                    {
                        i = SkipBalanced(text, i + 1, '(', ')');
                    }
                    else
                    {
                        while (i < n && !char.IsWhiteSpace(text[i]) && text[i] != '>' && !(text[i] == '/' && i + 1 < n && text[i + 1] == '>'))
                        {
                            i++;
                        }
                    }

                    value = text.Substring(valueStart, i - valueStart);
                }
            }

            element.Attributes.Add(new RazorAttribute(name, value, TextSpan.FromBounds(start, i)));
        }

        return n;
    }

    private static int SkipBalanced(string text, int open, char openChar, char closeChar)
    {
        var depth = 0;
        var i = open;
        var n = text.Length;
        while (i < n)
        {
            var c = text[i];
            if (c == '"' || c == '\'')
            {
                // C# string or character literal (verbatim and interpolated strings are close enough for skipping).
                var j = i + 1;
                while (j < n && text[j] != c && text[j] != '\n')
                {
                    if (text[j] == '\\')
                    {
                        j++;
                    }

                    j++;
                }

                i = j + 1;
                continue;
            }

            if (c == '/' && i + 1 < n && text[i + 1] == '/')
            {
                i = LineEnd(text, i);
                continue;
            }

            if (c == '/' && i + 1 < n && text[i + 1] == '*')
            {
                var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? n : end + 2;
                continue;
            }

            if (c == openChar)
            {
                depth++;
            }
            else if (c == closeChar)
            {
                depth--;
                if (depth == 0)
                {
                    return i + 1;
                }
            }

            i++;
        }

        return n;
    }

    private static string ReadIdentifier(string text, int i)
    {
        var start = i;
        while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
        {
            i++;
        }

        return text.Substring(start, i - start);
    }

    private static string ReadTagName(string text, int i)
    {
        var start = i;
        while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '-' or '_' or '.' or ':'))
        {
            i++;
        }

        return text.Substring(start, i - start);
    }

    private static int SkipWhitespace(string text, int i)
    {
        while (i < text.Length && char.IsWhiteSpace(text[i]))
        {
            i++;
        }

        return i;
    }

    private static int LineEnd(string text, int i)
    {
        var end = text.IndexOf('\n', i);
        return end < 0 ? text.Length : end;
    }

    private static int NextLineEnd(string text, int lineEnd) => lineEnd >= text.Length ? text.Length : LineEnd(text, lineEnd + 1);

    private static bool At(string text, int i, string value) => string.CompareOrdinal(text, i, value, 0, value.Length) == 0;
}
