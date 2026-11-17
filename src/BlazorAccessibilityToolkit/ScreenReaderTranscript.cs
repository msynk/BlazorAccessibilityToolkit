using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace BlazorAccessibilityToolkit;

/// <summary>A node of an accessibility tree: a role with its name, description and states, or static text (role <c>text</c>).</summary>
public sealed record AccessibilityNode
{
    /// <summary>The role, or <c>"text"</c> for static text.</summary>
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    /// <summary>Accessible name, or the text of a text node.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>Accessible description.</summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>States that are set, without the <c>aria-</c> prefix (for example <c>level</c>, <c>checked</c>, <c>expanded</c>).</summary>
    [JsonPropertyName("states")]
    public IReadOnlyDictionary<string, string> States { get; init; } = new Dictionary<string, string>();

    /// <summary>Child nodes. Controls whose content is their name (buttons, links, headings ...) have none.</summary>
    [JsonPropertyName("children")]
    public IReadOnlyList<AccessibilityNode> Children { get; init; } = [];
}

/// <summary>
/// Formats an accessibility tree as a virtual screen-reader transcript: one line per item that a screen reader reads in
/// browse mode, in reading order, as <c>role, name, states, description</c>, for example <c>heading, Settings, level 2</c>
/// or <c>button, Bold, pressed</c>, with static text on lines of its own.
/// </summary>
/// <remarks>
/// A transcript is a readable, diffable record of roles, names and states, for catching regressions. It is <b>not</b>
/// what a particular screen reader says: NVDA, JAWS, VoiceOver and TalkBack word, order and abbreviate announcements
/// differently. Test with real assistive technologies for conformance.
/// </remarks>
public static class ScreenReaderTranscript
{
    /// <summary>Roles whose content is their name: read as one item, without children.</summary>
    public static IReadOnlySet<string> AtomicRoles { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "button", "link", "checkbox", "radio", "switch", "img", "textbox", "searchbox", "combobox", "slider", "spinbutton", "progressbar", "meter",
        "scrollbar", "separator", "option", "tab", "menuitem", "menuitemcheckbox", "menuitemradio", "heading", "columnheader", "rowheader",
    };

    // Roles with children whose name comes from their content.
    private static readonly HashSet<string> NameFromContentRoles = new(StringComparer.Ordinal)
    {
        "treeitem", "tooltip", "comment", "suggestion", "sectionheader", "sectionfooter", "marquee",
    };

    // Roles that are not announced; only their content is read.
    private static readonly HashSet<string> TransparentRoles = new(StringComparer.Ordinal)
    {
        "paragraph", "listitem", "row", "cell", "gridcell", "rowgroup", "strong", "emphasis", "code", "term", "definition", "time", "caption",
        "deletion", "insertion", "mark", "subscript", "superscript", "generic", "none", "presentation",
    };

    /// <summary>Formats the nodes as a transcript (lines separated by <c>\n</c>).</summary>
    public static string Format(IEnumerable<AccessibilityNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var lines = new List<string>();
        foreach (var node in nodes)
        {
            Write(node, lines);
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Compares two transcripts line by line (trimmed, ignoring blank lines). Returns <c>null</c> when they match, or a
    /// message with a line diff (<c>-</c> expected, <c>+</c> actual) and the actual transcript.
    /// </summary>
    public static string? Compare(string expected, string actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        var expectedLines = Lines(expected);
        var actualLines = Lines(actual);
        if (expectedLines.SequenceEqual(actualLines, StringComparer.Ordinal))
        {
            return null;
        }

        var sb = new StringBuilder("The screen-reader transcript does not match.").AppendLine().AppendLine("(- expected, + actual)");
        int i = 0, j = 0;
        while (i < expectedLines.Length || j < actualLines.Length)
        {
            if (i < expectedLines.Length && j < actualLines.Length && expectedLines[i] == actualLines[j])
            {
                sb.Append("  ").AppendLine(expectedLines[i]);
                i++;
                j++;
            }
            else if (j < actualLines.Length && (i >= expectedLines.Length || !expectedLines.Skip(i).Contains(actualLines[j])))
            {
                sb.Append("+ ").AppendLine(actualLines[j++]);
            }
            else
            {
                sb.Append("- ").AppendLine(expectedLines[i++]);
            }
        }

        return sb.AppendLine().AppendLine("Actual transcript:").Append(actual).ToString();
    }

    /// <summary>Throws <see cref="AccessibilityAssertionException"/> with a line diff when the transcripts differ.</summary>
    public static void ShouldMatch(string actual, string expected)
    {
        if (Compare(expected, actual) is { } message)
        {
            throw new AccessibilityAssertionException(message);
        }
    }

    private static string[] Lines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();

    private static void Write(AccessibilityNode node, List<string> lines)
    {
        if (node.Role == "text")
        {
            if (node.Name.Length > 0)
            {
                lines.Add(node.Name);
            }

            return;
        }

        if (TransparentRoles.Contains(node.Role))
        {
            foreach (var child in node.Children)
            {
                Write(child, lines);
            }

            return;
        }

        // Every role that is not transparent is announced: landmarks, containers, controls and named-by-content items.
        var parts = new List<string> { node.Role };
        if (node.Name.Length > 0)
        {
            parts.Add(node.Name);
        }

        parts.AddRange(DescribeStates(node));
        if (node.Role is "list" or "listbox" or "menu" or "tablist" or "radiogroup" or "tree")
        {
            var items = node.Children.Count(c => c.Role is "listitem" or "option" or "menuitem" or "menuitemcheckbox" or "menuitemradio" or "tab" or "radio" or "treeitem");
            parts.Add(items == 1 ? "1 item" : $"{items.ToString(CultureInfo.InvariantCulture)} items");
        }

        if (node.Description.Length > 0)
        {
            parts.Add(node.Description);
        }

        lines.Add(string.Join(", ", parts));

        // A role named from its content (a tree item, a tooltip ...) already said its text in the name.
        var namedByContent = NameFromContentRoles.Contains(node.Role) && node.Name.Length > 0;
        foreach (var child in node.Children)
        {
            if (namedByContent && child.Role == "text")
            {
                continue;
            }

            Write(child, lines);
        }
    }

    private static IEnumerable<string> DescribeStates(AccessibilityNode node)
    {
        var s = node.States;
        if (s.TryGetValue("level", out var level) && node.Role == "heading")
        {
            yield return "level " + level;
        }

        if (s.TryGetValue("checked", out var isChecked) && node.Role is "checkbox" or "radio" or "switch" or "menuitemcheckbox" or "menuitemradio")
        {
            yield return isChecked switch { "true" => node.Role == "switch" ? "on" : "checked", "mixed" => "partially checked", _ => node.Role == "switch" ? "off" : "not checked" };
        }

        if (s.TryGetValue("pressed", out var pressed))
        {
            yield return pressed switch { "true" => "pressed", "mixed" => "partially pressed", _ => "not pressed" };
        }

        if (s.TryGetValue("expanded", out var expanded) && expanded is "true" or "false")
        {
            yield return expanded == "true" ? "expanded" : "collapsed";
        }

        if (s.TryGetValue("selected", out var selected) && selected == "true")
        {
            yield return "selected";
        }

        if (s.TryGetValue("current", out var current) && current is { Length: > 0 } && current != "false")
        {
            yield return current == "true" ? "current" : "current " + current;
        }

        if (s.TryGetValue("haspopup", out var popup) && popup is { Length: > 0 } && popup != "false")
        {
            yield return "has popup " + (popup == "true" ? "menu" : popup);
        }

        if (s.TryGetValue("valuetext", out var valueText) && valueText.Length > 0)
        {
            yield return valueText;
        }
        else if (s.TryGetValue("valuenow", out var valueNow) && node.Role is "slider" or "spinbutton" or "progressbar" or "meter" or "scrollbar")
        {
            yield return valueNow;
        }

        foreach (var flag in new[] { "disabled", "readonly", "required", "busy" })
        {
            if (s.TryGetValue(flag, out var value) && value == "true")
            {
                yield return flag == "readonly" ? "read only" : flag;
            }
        }

        if (s.TryGetValue("invalid", out var invalid) && invalid is { Length: > 0 } && invalid != "false")
        {
            yield return "invalid";
        }
    }
}
