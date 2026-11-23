using ModelContextProtocol;

namespace BlazorAccessibilityToolkit.Cli.Mcp;

/// <summary>
/// The folders the MCP server may read and change. A coding agent passes paths it was given or guessed; the server
/// refuses any that resolve outside these folders, so a confused or manipulated agent cannot read or edit other files.
/// </summary>
internal sealed class McpWorkspace
{
    public McpWorkspace(IEnumerable<string> roots)
    {
        Roots = roots.Select(r => Path.TrimEndingDirectorySeparator(Path.GetFullPath(r))).Distinct(PathComparer).ToArray();
        if (Roots.Count == 0)
        {
            throw new ArgumentException("At least one root is required.", nameof(roots));
        }
    }

    /// <summary>Folders on Windows and macOS are case-insensitive by default.</summary>
    public static StringComparer PathComparer { get; } = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    private static StringComparison PathComparison => OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>The allowed folders, as full paths. Relative paths are resolved against the first.</summary>
    public IReadOnlyList<string> Roots { get; }

    /// <summary>
    /// Resolves a path against the first root and checks that it is inside one of the roots, also after following the
    /// symbolic links and junctions on the way.
    /// </summary>
    /// <exception cref="McpException">The path is outside the roots.</exception>
    public string Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new McpException("A path is empty.");
        }

        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path, Roots[0]));
        if (!IsInside(full, depth: 0))
        {
            throw new McpException($"'{path}' is outside the folders this server may use ({string.Join(", ", Roots)}). Start the server with --root to allow more folders.");
        }

        return full;
    }

    // Checks the path itself, then every existing folder or file below its root that is a link: a link inside a root
    // that points elsewhere would otherwise let the server read and write outside the roots. The roots themselves may
    // be links; the user chose them.
    private bool IsInside(string full, int depth)
    {
        var root = Roots.Where(r => IsUnder(full, r)).OrderByDescending(r => r.Length).FirstOrDefault();
        if (root is null || depth > 16)
        {
            return false;
        }

        var current = root;
        foreach (var part in Path.GetRelativePath(root, full).Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
            {
                continue;
            }

            current = Path.Combine(current, part);
            var info = new FileInfo(current);
            if (!RazorSources.IsLink(info))
            {
                if (!info.Exists && !Directory.Exists(current))
                {
                    return true;
                }

                continue;
            }

            string? target;
            try
            {
                target = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }

            if (target is null || !IsInside(Path.TrimEndingDirectorySeparator(target), depth + 1))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The path relative to the root that contains it, with forward slashes, for compact and stable output.</summary>
    public string Display(string fullPath)
    {
        var root = Roots.Where(r => IsUnder(fullPath, r)).OrderByDescending(r => r.Length).FirstOrDefault();
        if (root is null)
        {
            return fullPath;
        }

        var relative = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
        return Roots.Count == 1 || root == Roots[0] ? relative : Path.Combine(root, relative).Replace('\\', '/');
    }

    // Path.GetFullPath has already turned '/' into '\' on Windows. A drive or file system root ends with a separator.
    private static bool IsUnder(string path, string root) =>
        string.Equals(path, root, PathComparison)
        || path.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar, PathComparison);
}
