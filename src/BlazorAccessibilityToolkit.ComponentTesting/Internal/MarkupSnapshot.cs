using System.Reflection;
using AngleSharp.Dom;
using AngleSharp.Html;
using AngleSharp.Html.Parser;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using BlazorAccessibilityToolkit.Engines;

// The render tree types are used read-only to attribute elements to components. If they change, Capture() falls
// back to root-only attribution with a warning instead of failing the audit.
#pragma warning disable BL0006

namespace BlazorAccessibilityToolkit.ComponentTesting.Internal;

/// <summary>
/// A snapshot of a rendered component: its elements in document order, the markup sent to browser engines (with
/// ordinal attributes, without bUnit's <c>blazor:*</c> attributes), and the component that rendered each element.
/// </summary>
internal sealed class MarkupSnapshot
{
    private static readonly MethodInfo? GetFrames = typeof(Renderer).GetMethod(
        "GetCurrentRenderTreeFrames",
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
        [typeof(int)]);

    private MarkupSnapshot(IReadOnlyList<IElement> elements, string browserMarkup, IReadOnlyList<string[]>? componentPaths, string? attributionWarning, bool hasContent)
    {
        Elements = elements;
        BrowserMarkup = browserMarkup;
        ComponentPaths = componentPaths;
        AttributionWarning = attributionWarning;
        HasContent = hasContent;
    }

    /// <summary>Elements in document order; the index is the element ordinal.</summary>
    public IReadOnlyList<IElement> Elements { get; }

    /// <summary>Markup for browser engines.</summary>
    public string BrowserMarkup { get; }

    /// <summary>Component path (root first) per element ordinal, or <c>null</c> when attribution is unavailable.</summary>
    public IReadOnlyList<string[]>? ComponentPaths { get; }

    /// <summary>Why attribution is limited, if it is.</summary>
    public string? AttributionWarning { get; }

    /// <summary>True when the component rendered any element or text.</summary>
    public bool HasContent { get; }

    public static MarkupSnapshot Capture<TComponent>(IRenderedComponent<TComponent> cut, Renderer? renderer)
        where TComponent : IComponent
    {
        var nodes = cut.Nodes;
        var elements = new List<IElement>();
        foreach (var node in nodes)
        {
            Collect(node, elements);
        }

        var hasContent = elements.Count > 0 || nodes.Any(n => !string.IsNullOrWhiteSpace(n.TextContent));
        var markup = BuildBrowserMarkup(nodes);

        string[][]? paths = null;
        string? warning = null;
        var rootType = ComponentAudits.TypeName(typeof(TComponent));
        if (GetFrames is null || renderer is null)
        {
            warning = "Component attribution is limited to the fixture root: the render tree is not accessible with this version of bUnit/ASP.NET Core.";
        }
        else
        {
            try
            {
                var walked = new List<(string Tag, string[] Path)>();
                Walk(renderer, cut.ComponentId, [rootType], walked);
                if (walked.Count == elements.Count && walked.Select(w => w.Tag).SequenceEqual(elements.Select(e => e.LocalName), StringComparer.OrdinalIgnoreCase))
                {
                    paths = walked.Select(w => w.Path).ToArray();
                }
                else
                {
                    warning = "Component attribution is limited to the fixture root: the parsed DOM differs from the render tree (for example the HTML parser inserted or moved elements, such as an implicit <tbody>).";
                }
            }
            catch (Exception ex) when (ex is TargetInvocationException or InvalidOperationException or ArgumentException)
            {
                warning = "Component attribution is limited to the fixture root: " + ex.Message;
            }
        }

        return new MarkupSnapshot(elements, markup, paths, warning, hasContent);
    }

    /// <summary>The component path for an element ordinal (root only when attribution is unavailable).</summary>
    public ComponentAttribution Attribute(int? ordinal, string rootType)
    {
        if (ordinal is { } index && ComponentPaths is { } paths && index >= 0 && index < paths.Count)
        {
            var path = paths[index];
            return new ComponentAttribution { ComponentType = path[^1], ComponentPath = path, Method = "render-tree" };
        }

        return new ComponentAttribution { ComponentType = rootType, ComponentPath = [rootType], Method = "fixture-root" };
    }

    private static void Collect(INode node, List<IElement> elements)
    {
        if (node is IElement element)
        {
            elements.Add(element);
        }

        foreach (var child in node.ChildNodes)
        {
            Collect(child, elements);
        }
    }

    private static string BuildBrowserMarkup(INodeList nodes)
    {
        var writer = new StringWriter();
        var ordinal = 0;
        foreach (var node in nodes)
        {
            var clone = node.Clone(deep: true);
            Prepare(clone, ref ordinal);
            clone.ToHtml(writer, HtmlMarkupFormatter.Instance);
        }

        return writer.ToString();
    }

    private static void Prepare(INode node, ref int ordinal)
    {
        if (node is IElement element)
        {
            foreach (var attribute in element.Attributes.Where(a => a.Name.StartsWith("blazor:", StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                element.RemoveAttribute(attribute.Name);
            }

            element.SetAttribute(MarkupAuditRequest.OrdinalAttribute, ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture));
            ordinal++;
        }

        foreach (var child in node.ChildNodes)
        {
            Prepare(child, ref ordinal);
        }
    }

    // Walks the render tree in document order and records the component path of every element, including elements
    // that are part of raw markup frames (static HTML in .razor files).
    private static void Walk(Renderer renderer, int componentId, string[] path, List<(string Tag, string[] Path)> output)
    {
        var frames = (ArrayRange<RenderTreeFrame>)GetFrames!.Invoke(renderer, [componentId])!;
        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames.Array[i];
            switch (frame.FrameType)
            {
                case RenderTreeFrameType.Element:
                    output.Add((frame.ElementName, path));
                    break;
                case RenderTreeFrameType.Markup:
                    foreach (var tag in ElementsInMarkup(frame.MarkupContent))
                    {
                        output.Add((tag, path));
                    }

                    break;
                case RenderTreeFrameType.Component:
                    var childPath = path.Append(ComponentAudits.TypeName(frame.ComponentType)).ToArray();
                    Walk(renderer, frame.ComponentId, childPath, output);
                    break;
            }
        }
    }

    private static readonly HtmlParser FragmentParser = new();

    private static IEnumerable<string> ElementsInMarkup(string markup)
    {
        if (string.IsNullOrEmpty(markup) || !markup.Contains('<', StringComparison.Ordinal))
        {
            return [];
        }

        var document = FragmentParser.ParseDocument(string.Empty);
        var fragment = FragmentParser.ParseFragment(markup, document.Body!);
        var tags = new List<string>();
        foreach (var node in fragment)
        {
            foreach (var element in node is IElement e ? new[] { e }.Concat(e.QuerySelectorAll("*")) : [])
            {
                tags.Add(element.LocalName);
            }
        }

        return tags;
    }
}
