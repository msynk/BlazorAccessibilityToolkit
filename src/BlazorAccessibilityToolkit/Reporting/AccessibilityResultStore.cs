using System.Text;
using System.Text.Json;

namespace BlazorAccessibilityToolkit.Reporting;

/// <summary>
/// Persists each audit as its own JSON file so that parallel tests (and parallel test processes) can contribute
/// results without shared state. The CLI (<c>blazor-a11y report</c>) aggregates the directory into reports.
/// </summary>
/// <remarks>
/// Enable it by setting the <c>BLAZOR_A11Y_RESULTS_DIR</c> environment variable, or create an instance explicitly.
/// Built-in engines save every audit to <see cref="Default"/> automatically when it is configured.
/// </remarks>
public sealed class AccessibilityResultStore
{
    /// <summary>Environment variable that enables automatic result persistence.</summary>
    public const string DirectoryEnvironmentVariable = "BLAZOR_A11Y_RESULTS_DIR";

    /// <summary>File name suffix of audit result files.</summary>
    public const string FileSuffix = ".a11y.json";

    private static readonly Lazy<AccessibilityResultStore?> LazyDefault = new(() =>
    {
        var directory = Environment.GetEnvironmentVariable(DirectoryEnvironmentVariable);
        return string.IsNullOrWhiteSpace(directory) ? null : new AccessibilityResultStore(directory);
    });

    /// <summary>Creates a store writing to <paramref name="directory"/>.</summary>
    public AccessibilityResultStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory = Path.GetFullPath(directory);
    }

    /// <summary>The store configured through <see cref="DirectoryEnvironmentVariable"/>, or <c>null</c>.</summary>
    public static AccessibilityResultStore? Default => LazyDefault.Value;

    /// <summary>Target directory.</summary>
    public string Directory { get; }

    /// <summary>Writes the audit atomically and returns the file path.</summary>
    public string Save(AccessibilityAudit audit)
    {
        ArgumentNullException.ThrowIfNull(audit);
        System.IO.Directory.CreateDirectory(Directory);
        var path = Path.Combine(Directory, $"{SafeFileName(audit.Name)}-{audit.Id}{FileSuffix}");
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(audit, ReportJson.Options), new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
        return path;
    }

    /// <summary>Saves the audit to <see cref="Default"/> when it is configured. Never throws for I/O problems; returns the error instead.</summary>
    internal static string? TrySaveToDefault(AccessibilityAudit audit)
    {
        var store = Default;
        if (store is null)
        {
            return null;
        }

        try
        {
            store.Save(audit);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Could not persist audit results to '{store.Directory}': {ex.Message}";
        }
    }

    internal static string SafeFileName(string name)
    {
        var sb = new StringBuilder(Math.Min(name.Length, 80));
        foreach (var c in name)
        {
            if (sb.Length >= 80)
            {
                break;
            }

            sb.Append(char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_');
        }

        return sb.Length == 0 ? "audit" : sb.ToString();
    }
}

/// <summary>Loads audits and reports from files and directories.</summary>
public static class ReportLoader
{
    /// <summary>
    /// Loads audits from files (<c>*.a11y.json</c> audit files or report files) and directories (searched recursively).
    /// Unreadable files are returned as load errors rather than ignored.
    /// </summary>
    public static (IReadOnlyList<AccessibilityAudit> Audits, IReadOnlyList<string> Errors) LoadAudits(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var audits = new List<AccessibilityAudit>();
        var errors = new List<string>();
        foreach (var path in paths)
        {
            IEnumerable<string> files;
            if (System.IO.Directory.Exists(path))
            {
                files = System.IO.Directory.EnumerateFiles(path, "*" + AccessibilityResultStore.FileSuffix, SearchOption.AllDirectories).Order(StringComparer.Ordinal);
            }
            else if (File.Exists(path))
            {
                files = [path];
            }
            else
            {
                errors.Add($"Input '{path}' does not exist.");
                continue;
            }

            foreach (var file in files)
            {
                try
                {
                    audits.AddRange(LoadFile(file));
                }
                catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException or NotSupportedException)
                {
                    errors.Add($"Could not read '{file}': {ex.Message}");
                }
            }
        }

        return (audits, errors);
    }

    /// <summary>Loads a report file, or wraps a single audit file into a report.</summary>
    public static AccessibilityReport LoadReport(string path)
    {
        var json = File.ReadAllText(path);
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        if (document.RootElement.TryGetProperty("audits", out _))
        {
            var report = JsonSerializer.Deserialize<AccessibilityReport>(json, ReportJson.Options)
                ?? throw new InvalidDataException($"'{path}' is not a report.");
            EnsureSupportedSchema(report.SchemaVersion, path);
            return report;
        }

        return AccessibilityReport.Create(LoadFile(path));
    }

    private static IEnumerable<AccessibilityAudit> LoadFile(string file)
    {
        var json = File.ReadAllText(file);
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Expected a JSON object.");
        }

        if (document.RootElement.TryGetProperty("audits", out _))
        {
            var report = JsonSerializer.Deserialize<AccessibilityReport>(json, ReportJson.Options) ?? throw new InvalidDataException("Empty report.");
            EnsureSupportedSchema(report.SchemaVersion, file);
            return report.Audits;
        }

        if (!document.RootElement.TryGetProperty("name", out _) || !document.RootElement.TryGetProperty("status", out _))
        {
            throw new InvalidDataException("The file is neither an audit nor a report.");
        }

        var audit = JsonSerializer.Deserialize<AccessibilityAudit>(json, ReportJson.Options) ?? throw new InvalidDataException("Empty audit.");
        return [audit];
    }

    private static void EnsureSupportedSchema(string version, string path)
    {
        if (!version.StartsWith("1.", StringComparison.Ordinal))
        {
            throw new NotSupportedException($"'{path}' uses report schema {version}; this version of the toolkit reads schema 1.x.");
        }
    }
}
