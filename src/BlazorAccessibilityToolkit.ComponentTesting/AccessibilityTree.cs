using System.Globalization;
using System.Text;
using AngleSharp.Dom;
using BlazorAccessibilityToolkit.ComponentTesting.Internal;
using Bunit;
using Microsoft.AspNetCore.Components;

namespace BlazorAccessibilityToolkit.ComponentTesting;

/// <summary>A node of the accessibility tree computed from bUnit markup: a role with its name, description and states, or static text.</summary>
public sealed record AccessibilityTreeNode
{
    /// <summary>The role, or <c>"text"</c> for static text.</summary>
    public required string Role { get; init; }

    /// <summary>Accessible name, or the text of a text node.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Accessible description.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>States that are set, without the <c>aria-</c> prefix (for example <c>level</c>, <c>checked</c>, <c>expanded</c>).</summary>
    public IReadOnlyDictionary<string, string> States { get; init; } = new Dictionary<string, string>();

    /// <summary>Child nodes. Controls whose content forms their name (buttons, links, headings ...) have none.</summary>
    public IReadOnlyList<AccessibilityTreeNode> Children { get; init; } = [];

    /// <summary>The element the node was computed from (<c>null</c> for text).</summary>
    public IElement? Element { get; init; }
}

/// <summary>
/// The accessibility tree of bUnit markup and a virtual screen-reader transcript of it: one line per item that a screen
/// reader in browse mode reads, in reading order, such as <c>heading, Settings, level 2</c> or <c>button, Bold, pressed</c>.
/// </summary>
/// <remarks>
/// The transcript is a readable, diffable record of roles, names and states. It is <b>not</b> what any particular screen
/// reader says: NVDA, JAWS, VoiceOver and TalkBack word, order and abbreviate announcements differently, and there is no
/// CSS, so content hidden by stylesheets is included. Use it to catch regressions in semantics, and test with real
/// assistive technologies for conformance.
/// </remarks>
public static class AccessibilityTree
{
    private static readonly string[] TranscriptStates = ["level", "checked", "pressed", "expanded", "selected", "current", "haspopup", "disabled", "readonly", "required", "invalid", "busy", "valuetext", "valuenow"];

    /// <summary>Computes the accessibility tree of the component's current markup.</summary>
    public static IReadOnlyList<AccessibilityTreeNode> GetAccessibilityTree<TComponent>(this IRenderedComponent<TComponent> cut)
        where TComponent : IComponent
    {
        var computation = AccessibleQueries.Computation(cut);
        var nodes = new List<AccessibilityTreeNode>();
        foreach (var node in cut.Nodes)
        {
            nodes.AddRange(Build(node, computation));
        }

        return Merge(nodes);
    }

    /// <summary>Computes the virtual screen-reader transcript of the component's current markup (lines separated by <c>\n</c>).</summary>
    public static string GetScreenReaderTranscript<TComponent>(this IRenderedComponent<TComponent> cut)
        where TComponent : IComponent => ToTranscript(cut.GetAccessibilityTree());

    /// <summary>Formats tree nodes as a transcript (lines separated by <c>\n</c>) with <see cref="ScreenReaderTranscript"/>.</summary>
    public static string ToTranscript(IEnumerable<AccessibilityTreeNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        return ScreenReaderTranscript.Format(nodes.Select(ToNode));
    }

    /// <summary>
    /// Asserts that the transcript equals <paramref name="expected"/>. Lines are compared after trimming, blank lines are
    /// ignored, and the failure message shows a line diff.
    /// </summary>
    public static void ShouldMatchTranscript<TComponent>(this IRenderedComponent<TComponent> cut, string expected)
        where TComponent : IComponent => ScreenReaderTranscript.ShouldMatch(cut.GetScreenReaderTranscript(), expected);

    /// <summary>Converts a node (and its children) to the element-independent <see cref="AccessibilityNode"/>.</summary>
    public static AccessibilityNode ToNode(AccessibilityTreeNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return new AccessibilityNode { Role = node.Role, Name = node.Name, Description = node.Description, States = node.States, Children = node.Children.Select(ToNode).ToArray() };
    }

    private static IEnumerable<AccessibilityTreeNode> Build(INode node, AccessibilityComputation computation)
    {
        if (node.NodeType == NodeType.Text)
        {
            var text = AccessibilityComputation.Normalize(node.TextContent);
            if (text.Length > 0)
            {
                yield return new AccessibilityTreeNode { Role = "text", Name = text };
            }

            yield break;
        }

        if (node is not IElement element || AccessibilityComputation.IsHidden(element))
        {
            yield break;
        }

        var role = computation.Role(element);

        // A label that wraps its control is read as part of the control ("checkbox, Email me updates, checked"), not twice.
        if (element.LocalName == "label" && element.QuerySelector("input:not([type=hidden]), select, textarea, meter, progress, output") is not null)
        {
            foreach (var child in element.ChildNodes.SelectMany(c => Build(c, computation)).Where(n => n.Role != "text"))
            {
                yield return child;
            }

            yield break;
        }

        if (role is null or "none" or "generic")
        {
            foreach (var child in element.ChildNodes.SelectMany(c => Build(c, computation)))
            {
                yield return child;
            }

            // Block-level elements end a run of text.
            if (element.LocalName is "br" or "div" or "p" or "li" or "section" or "header" or "footer")
            {
                yield return new AccessibilityTreeNode { Role = "break" };
            }

            yield break;
        }

        var states = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var state in TranscriptStates)
        {
            if (computation.State(element, state, role) is { } value)
            {
                states[state] = value;
            }
        }

        var children = ScreenReaderTranscript.AtomicRoles.Contains(role)
            ? []
            : Merge(element.ChildNodes.SelectMany(c => Build(c, computation)).ToList());
        yield return new AccessibilityTreeNode
        {
            Role = role,
            Name = computation.Name(element),
            Description = computation.Description(element),
            States = states,
            Children = children,
            Element = element,
        };
    }

    // Joins adjacent text nodes and drops the internal break markers.
    private static List<AccessibilityTreeNode> Merge(List<AccessibilityTreeNode> nodes)
    {
        var result = new List<AccessibilityTreeNode>();
        var text = new StringBuilder();
        void Flush()
        {
            if (text.Length > 0)
            {
                result.Add(new AccessibilityTreeNode { Role = "text", Name = AccessibilityComputation.Normalize(text.ToString()) });
                text.Clear();
            }
        }

        foreach (var node in nodes)
        {
            if (node.Role == "text")
            {
                text.Append(' ').Append(node.Name);
            }
            else
            {
                Flush();
                if (node.Role != "break")
                {
                    result.Add(node);
                }
            }
        }

        Flush();
        return result;
    }
}
