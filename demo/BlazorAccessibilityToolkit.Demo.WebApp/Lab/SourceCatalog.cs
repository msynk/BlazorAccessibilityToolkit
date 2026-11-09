using System.Collections.Frozen;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace BlazorAccessibilityToolkit.Demo.WebApp.Lab;

/// <summary>
/// Source code shown on the pages, read from embedded resources (see the project file): example snippets, the demo
/// components' Razor files and a few data files.
/// </summary>
public static partial class SourceCatalog
{
    private static readonly Assembly Assembly = typeof(SourceCatalog).Assembly;
    private static readonly Lazy<FrozenDictionary<string, string>> Snippets = new(LoadSnippets);

    /// <summary>The code of the example <paramref name="id"/>, without the lines that pass results to the page.</summary>
    public static string Snippet(string id) =>
        Snippets.Value.TryGetValue(id, out var code) ? code : throw new KeyNotFoundException($"No '#region snippet:{id}' in Lab/Examples.");

    /// <summary>All snippet ids, to validate the examples at startup.</summary>
    public static IEnumerable<string> SnippetIds => Snippets.Value.Keys;

    /// <summary>The Razor source of a demo component, for example <c>ClickableCard.razor</c>.</summary>
    public static string Component(string fileName) => Read("components/" + fileName);

    /// <summary>A data file such as <c>components.css</c> or <c>modal-dialog.json</c>.</summary>
    public static string Asset(string fileName) => Read("assets/" + fileName);

    private static string Read(string name)
    {
        using var stream = Assembly.GetManifestResourceStream(name) ?? throw new FileNotFoundException($"Embedded resource '{name}' not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().ReplaceLineEndings("\n").TrimEnd() + "\n";
    }

    private static FrozenDictionary<string, string> LoadSnippets()
    {
        var snippets = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in Assembly.GetManifestResourceNames().Where(n => n.StartsWith("snippets/", StringComparison.Ordinal)))
        {
            var lines = Read(name).Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (RegionStart().Match(lines[i]) is not { Success: true } start)
                {
                    continue;
                }

                var body = new List<string>();
                for (i++; i < lines.Length && !RegionEnd().IsMatch(lines[i]); i++)
                {
                    // env.Show(...) hands results to the page; it is not part of the test.
                    if (!lines[i].TrimStart().StartsWith("env.Show(", StringComparison.Ordinal))
                    {
                        body.Add(lines[i]);
                    }
                }

                snippets.Add(start.Groups["id"].Value, Format(body));
            }
        }

        return snippets.ToFrozenDictionary(StringComparer.Ordinal);
    }

    // Removes the common indentation and blank lines left at the edges or doubled by hidden lines.
    private static string Format(List<string> lines)
    {
        var indent = lines.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
        var result = new List<string>();
        foreach (var line in lines.Select(l => l.Trim().Length == 0 ? string.Empty : l[indent..].TrimEnd()))
        {
            if (line.Length == 0 && (result.Count == 0 || result[^1].Length == 0))
            {
                continue;
            }

            result.Add(line);
        }

        while (result.Count > 0 && result[^1].Length == 0)
        {
            result.RemoveAt(result.Count - 1);
        }

        return string.Join('\n', result);
    }

    [GeneratedRegex(@"^\s*#region snippet:(?<id>[\w-]+)\s*$")]
    private static partial Regex RegionStart();

    [GeneratedRegex(@"^\s*#endregion\b")]
    private static partial Regex RegionEnd();
}
