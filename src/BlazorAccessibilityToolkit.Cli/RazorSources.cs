using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BlazorAccessibilityToolkit.Analyzers;
using Microsoft.CodeAnalysis;

namespace BlazorAccessibilityToolkit.Cli;

/// <summary>A finding of the Razor analyzer in a file or a markup snippet, with the fixes it offers.</summary>
internal sealed record RazorFinding(string RuleId, string Title, string Message, string Severity, int Line, int Column, int EndLine, int EndColumn, IReadOnlyList<string> Fixes);

/// <summary>
/// Finds, reads and checks <c>.razor</c> and <c>.cshtml</c> files the way the Razor analyzer does in a build: the same
/// <c>.editorconfig</c> settings (files the analyzer is turned off for, rules set to <c>none</c>, component mapping),
/// and only files that can be written back byte for byte. Shared by <c>blazor-a11y fix</c> and the MCP server.
/// </summary>
internal static partial class RazorSources
{
    public static bool IsRazorFile(string path) =>
        path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase);

    // Walks the tree without entering build output, dependencies or hidden folders (.git, .vs), and reports folders it
    // cannot read instead of failing the run. Symbolic links and junctions are not followed: a link to a parent folder
    // would make the walk endless, and a link to another folder would lead outside the tree.
    public static IEnumerable<string> FindFiles(string root, Action<string> skipped, CancellationToken cancellationToken = default)
    {
        var pending = new Stack<string>([root]);
        var files = new List<string>();
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            string[] entries, subdirectories;
            try
            {
                entries = Directory.GetFiles(directory);
                subdirectories = Directory.GetDirectories(directory);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                skipped($"{directory}: skipped, {ex.Message}");
                continue;
            }

            foreach (var file in entries.Where(IsRazorFile))
            {
                if (IsLink(new FileInfo(file)))
                {
                    skipped($"{file}: skipped, a symbolic link");
                }
                else
                {
                    files.Add(file);
                }
            }

            foreach (var subdirectory in subdirectories)
            {
                var name = Path.GetFileName(subdirectory);
                if (name is "bin" or "obj" or "node_modules" || name.StartsWith('.'))
                {
                    continue;
                }

                if (IsLink(new DirectoryInfo(subdirectory)))
                {
                    skipped($"{subdirectory}: skipped, a symbolic link or junction");
                }
                else
                {
                    pending.Push(subdirectory);
                }
            }
        }

        return files.Order(StringComparer.Ordinal);
    }

    /// <summary>Whether the file or folder is a symbolic link or junction (not other reparse points, such as cloud files).</summary>
    public static bool IsLink(FileSystemInfo info)
    {
        try
        {
            return info.LinkTarget is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>The <c>.editorconfig</c> files in the folders above the files, which the analyzer would also read.</summary>
    public static AnalyzerConfigSet EditorConfigFor(IEnumerable<string> files)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in files.Select(f => Path.GetDirectoryName(Path.GetFullPath(f))).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            for (var current = directory; current is not null; current = Path.GetDirectoryName(current))
            {
                var candidate = Path.Combine(current, ".editorconfig");
                if (!paths.Contains(candidate) && File.Exists(candidate))
                {
                    paths.Add(candidate);
                }
            }
        }

        return AnalyzerConfigSet.Create(paths.Select(path => AnalyzerConfig.Parse(File.ReadAllText(path), path)).ToList());
    }

    /// <summary>The rules to run on a file, or <see langword="null"/> when the analyzer is turned off for it.</summary>
    public static (HashSet<string>? Rules, AnalyzerSettings Settings, IReadOnlyDictionary<string, ReportDiagnostic> Severities) OptionsFor(AnalyzerConfigSet editorConfig, string file, IEnumerable<string> candidates)
    {
        var options = editorConfig.GetOptionsForSourcePath(Path.GetFullPath(file));
        var settings = AnalyzerSettings.From(options.AnalyzerOptions);
        if (options.AnalyzerOptions.TryGetValue("blazor_a11y_analyzer.enabled", out var enabled) && string.Equals(enabled.Trim(), "false", StringComparison.OrdinalIgnoreCase))
        {
            return (null, settings, options.TreeOptions);
        }

        var rules = candidates
            .Where(id => !(options.TreeOptions.TryGetValue(id, out var severity) && severity == ReportDiagnostic.Suppress))
            .ToHashSet(StringComparer.Ordinal);
        return (rules, settings, options.TreeOptions);
    }

    /// <summary>
    /// Decodes strictly and only accepts files that encode back to the same bytes, so that writing the file back changes
    /// nothing but the fixes. Keeps the byte order mark as it was.
    /// </summary>
    public static bool TryDecode(byte[] bytes, out Encoding encoding, out int preamble, out string text)
    {
        (encoding, preamble) = bytes switch
        {
            [0xFF, 0xFE, 0x00, 0x00, ..] or [0x00, 0x00, 0xFE, 0xFF, ..] => (Encoding.UTF32, -1),
            [0xEF, 0xBB, 0xBF, ..] => (new UTF8Encoding(false, true), 3),
            [0xFF, 0xFE, ..] => (new UnicodeEncoding(false, false, true), 2),
            [0xFE, 0xFF, ..] => (new UnicodeEncoding(true, false, true), 2),
            _ => ((Encoding)new UTF8Encoding(false, true), 0),
        };
        text = string.Empty;
        if (preamble < 0)
        {
            preamble = 0;
            return false;
        }

        try
        {
            text = encoding.GetString(bytes, preamble, bytes.Length - preamble);
            return encoding.GetBytes(text).AsSpan().SequenceEqual(bytes.AsSpan(preamble));
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>
    /// Decodes a file for checking. Like the compiler, it falls back from UTF-8 to a single-byte encoding instead of
    /// refusing the file, so that line and column numbers still match. Writing back needs <see cref="TryDecode"/>.
    /// </summary>
    public static string DecodeForReading(byte[] bytes) =>
        TryDecode(bytes, out _, out _, out var text) ? text : Encoding.Latin1.GetString(bytes);

    /// <summary>Runs the analyzer's rules on markup, as the build would report them.</summary>
    public static IReadOnlyList<RazorFinding> Analyze(string markup, ICollection<string>? rules = null, AnalyzerSettings? settings = null, IReadOnlyDictionary<string, ReportDiagnostic>? severities = null)
    {
        var text = Microsoft.CodeAnalysis.Text.SourceText.From(markup);
        var findings = new List<RazorFinding>();
        foreach (var (descriptor, element, span, args) in RazorAccessibilityAnalyzer.Analyze(markup, settings))
        {
            if (rules is not null && !rules.Contains(descriptor.Id))
            {
                continue;
            }

            // An .editorconfig severity for the rule, otherwise the rule's default (several rules are info by default).
            var severity = severities is not null && severities.TryGetValue(descriptor.Id, out var configured) && configured != ReportDiagnostic.Default
                ? configured switch
                {
                    ReportDiagnostic.Error => "error",
                    ReportDiagnostic.Info => "info",
                    ReportDiagnostic.Hidden => "hidden",
                    _ => "warning",
                }
                : descriptor.DefaultSeverity switch
                {
                    DiagnosticSeverity.Error => "error",
                    DiagnosticSeverity.Info => "info",
                    DiagnosticSeverity.Hidden => "hidden",
                    _ => "warning",
                };
            var position = text.Lines.GetLinePositionSpan(span);
            var fixes = RazorAccessibilityAnalyzer.FixesFor(descriptor, element, span, args, markup).Select(f => f.Title).ToArray();
            findings.Add(new RazorFinding(
                descriptor.Id,
                descriptor.Title.ToString(CultureInfo.InvariantCulture),
                string.Format(CultureInfo.InvariantCulture, descriptor.MessageFormat.ToString(CultureInfo.InvariantCulture), args),
                severity,
                position.Start.Line + 1,
                position.Start.Character + 1,
                position.End.Line + 1,
                position.End.Character + 1,
                fixes));
        }

        return findings;
    }

    /// <summary>
    /// The WCAG success criteria of an analyzer rule. The analyzer writes them at the start of the rule description, as
    /// "WCAG 1.3.1, 4.1.2." or "Best practice.".
    /// </summary>
    public static IReadOnlyList<string> WcagOf(DiagnosticDescriptor descriptor) =>
        WcagPrefix().Match(descriptor.Description.ToString(CultureInfo.InvariantCulture)) is { Success: true } match
            ? match.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            : [];

    [GeneratedRegex(@"^WCAG ([0-9.,\s]+[0-9])\.")]
    private static partial Regex WcagPrefix();
}
