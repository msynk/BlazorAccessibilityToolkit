using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Playwright;

namespace BlazorAccessibilityToolkit.Playwright;

/// <summary>Settings for stored ARIA snapshots.</summary>
public sealed record AriaSnapshotOptions
{
    /// <summary>Environment variable that switches to update mode when set to <c>1</c> or <c>true</c>.</summary>
    public const string UpdateEnvironmentVariable = "BLAZOR_A11Y_UPDATE_SNAPSHOTS";

    /// <summary>Environment variable that overrides the snapshot directory.</summary>
    public const string DirectoryEnvironmentVariable = "BLAZOR_A11Y_SNAPSHOT_DIR";

    /// <summary>Snapshot directory. Default: <see cref="DirectoryEnvironmentVariable"/>, else <c>__a11y_snapshots__</c> next to the calling test file.</summary>
    public string? Directory { get; init; }

    /// <summary>Overwrite stored snapshots with the current tree. Default: <see cref="UpdateEnvironmentVariable"/>.</summary>
    public bool? Update { get; init; }

    /// <summary>How long Playwright retries the comparison while the UI settles. Default 5 s.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);
}

/// <summary>Thrown when an ARIA snapshot does not match, or is missing.</summary>
public sealed class AriaSnapshotMismatchException : AccessibilityAssertionException
{
    /// <summary>Creates the exception.</summary>
    public AriaSnapshotMismatchException()
    {
    }

    /// <summary>Creates the exception.</summary>
    public AriaSnapshotMismatchException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    public AriaSnapshotMismatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Accessibility-tree regression snapshots based on Playwright's ARIA snapshots (roles, accessible names, states and
/// hierarchy, in YAML). Stored snapshots are templates: they may be edited to keep only the relevant part of the tree
/// and to use regular expressions for dynamic text (for example <c>- status: /\d+ results/</c>), because comparison uses
/// Playwright's partial-matching semantics.
/// </summary>
/// <remarks>
/// Snapshots detect semantic regressions; they do not prove accessibility. Their content depends on Playwright's
/// accessibility-tree implementation, so upgrade Microsoft.Playwright deliberately and review snapshot diffs.
/// </remarks>
public static class AriaSnapshots
{
    /// <summary>Captures the ARIA snapshot (YAML) of <paramref name="locator"/>.</summary>
    public static Task<string> CaptureAriaSnapshotAsync(this ILocator locator)
    {
        ArgumentNullException.ThrowIfNull(locator);
        return locator.AriaSnapshotAsync();
    }

    /// <summary>Asserts that <paramref name="locator"/> matches an inline template (partial matching and regular expressions allowed).</summary>
    public static async Task ShouldMatchAriaSnapshotAsync(this ILocator locator, string template, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(template);
        try
        {
            await Assertions.Expect(locator).ToMatchAriaSnapshotAsync(template, new LocatorAssertionsToMatchAriaSnapshotOptions { Timeout = (float)(timeout ?? TimeSpan.FromSeconds(5)).TotalMilliseconds }).ConfigureAwait(false);
        }
        catch (PlaywrightException ex)
        {
            var actual = await SafeCaptureAsync(locator).ConfigureAwait(false);
            throw new AriaSnapshotMismatchException($"The accessibility tree does not match the expected template.{Environment.NewLine}{Diff(template, actual)}", ex);
        }
    }

    /// <summary>
    /// Compares <paramref name="locator"/> with the stored snapshot <paramref name="name"/>. A missing snapshot is written
    /// and the assertion fails so it gets reviewed (on CI, where <c>CI=true</c>, it is not written). In update mode the
    /// snapshot is overwritten and the assertion passes.
    /// </summary>
    public static async Task ShouldMatchStoredAriaSnapshotAsync(this ILocator locator, string name, AriaSnapshotOptions? options = null, [CallerFilePath] string callerFilePath = "")
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        options ??= new AriaSnapshotOptions();
        var path = Path.Combine(ResolveDirectory(options, callerFilePath), SafeName(name) + ".aria.yml");
        var update = options.Update ?? IsSet(AriaSnapshotOptions.UpdateEnvironmentVariable);

        if (update)
        {
            Write(path, await locator.AriaSnapshotAsync().ConfigureAwait(false));
            return;
        }

        if (!File.Exists(path))
        {
            var actual = await locator.AriaSnapshotAsync().ConfigureAwait(false);
            if (IsSet("CI"))
            {
                throw new AriaSnapshotMismatchException($"ARIA snapshot '{path}' does not exist. Snapshots are not created on CI; run the test locally (or with {AriaSnapshotOptions.UpdateEnvironmentVariable}=1) and commit the file.{Environment.NewLine}Actual:{Environment.NewLine}{actual}");
            }

            Write(path, actual);
            throw new AriaSnapshotMismatchException($"ARIA snapshot '{path}' did not exist and was created. Review it, commit it and re-run the test.{Environment.NewLine}{actual}");
        }

        var template = (await File.ReadAllTextAsync(path).ConfigureAwait(false)).ReplaceLineEndings("\n");
        try
        {
            await Assertions.Expect(locator).ToMatchAriaSnapshotAsync(template, new LocatorAssertionsToMatchAriaSnapshotOptions { Timeout = (float)options.Timeout.TotalMilliseconds }).ConfigureAwait(false);
        }
        catch (PlaywrightException ex)
        {
            var actual = await SafeCaptureAsync(locator).ConfigureAwait(false);
            var received = path[..^".aria.yml".Length] + ".received.aria.yml";
            Write(received, actual);
            throw new AriaSnapshotMismatchException(
                $"ARIA snapshot '{name}' changed. The current tree was written to '{received}'. If the change is intended, run with {AriaSnapshotOptions.UpdateEnvironmentVariable}=1 (or copy the received file over the snapshot).{Environment.NewLine}{Diff(template, actual)}",
                ex);
        }
    }

    /// <summary>Line diff (expected vs. actual) in a unified-like format.</summary>
    public static string Diff(string expected, string actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        var a = expected.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
        var b = actual.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
        var lcs = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
        {
            for (var j = b.Length - 1; j >= 0; j--)
            {
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var sb = new StringBuilder("--- expected\n+++ actual\n");
        int x = 0, y = 0;
        while (x < a.Length || y < b.Length)
        {
            if (x < a.Length && y < b.Length && a[x] == b[y])
            {
                sb.Append("  ").Append(a[x]).Append('\n');
                x++;
                y++;
            }
            else if (y < b.Length && (x >= a.Length || lcs[x, y + 1] >= lcs[x + 1, y]))
            {
                sb.Append("+ ").Append(b[y]).Append('\n');
                y++;
            }
            else
            {
                sb.Append("- ").Append(a[x]).Append('\n');
                x++;
            }
        }

        return sb.ToString().TrimEnd('\n');
    }

    private static async Task<string> SafeCaptureAsync(ILocator locator)
    {
        try
        {
            return await locator.AriaSnapshotAsync(new LocatorAriaSnapshotOptions { Timeout = 2000 }).ConfigureAwait(false);
        }
        catch (PlaywrightException ex)
        {
            return $"(could not capture: {ex.Message})";
        }
    }

    private static string ResolveDirectory(AriaSnapshotOptions options, string callerFilePath)
    {
        if (options.Directory is { } directory)
        {
            return directory;
        }

        if (Environment.GetEnvironmentVariable(AriaSnapshotOptions.DirectoryEnvironmentVariable) is { Length: > 0 } fromEnvironment)
        {
            return fromEnvironment;
        }

        var callerDirectory = string.IsNullOrEmpty(callerFilePath) ? null : Path.GetDirectoryName(callerFilePath);
        if (callerDirectory is not null && System.IO.Directory.Exists(callerDirectory))
        {
            return Path.Combine(callerDirectory, "__a11y_snapshots__");
        }

        // Deterministic CI builds map source paths (for example "/_/tests/App.Tests/DialogTests.cs"). Find the
        // repository-relative directory by walking up from the test output directory.
        if (callerDirectory is not null && (callerDirectory.StartsWith("/_/", StringComparison.Ordinal) || callerDirectory.StartsWith("/_\\", StringComparison.Ordinal)))
        {
            var relative = callerDirectory[3..].Replace('\\', '/');
            for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            {
                var candidate = Path.Combine(current.FullName, relative);
                if (System.IO.Directory.Exists(candidate))
                {
                    return Path.Combine(candidate, "__a11y_snapshots__");
                }
            }
        }

        throw new InvalidOperationException(
            $"Cannot locate the snapshot directory: the caller source path '{callerFilePath}' does not exist on this machine (deterministic/CI builds map source paths). Set AriaSnapshotOptions.Directory or the {AriaSnapshotOptions.DirectoryEnvironmentVariable} environment variable.");
    }

    private static void Write(string path, string content)
    {
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.ReplaceLineEndings("\n").TrimEnd('\n') + "\n", new UTF8Encoding(false));
    }

    private static bool IsSet(string variable)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        return value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static string SafeName(string name)
    {
        var chars = name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-').ToArray();
        return new string(chars);
    }
}
