using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BlazorAccessibilityToolkit;

/// <summary>
/// Computes stable fingerprints for findings so that baselines survive unrelated changes.
/// </summary>
/// <remarks>
/// The fingerprint covers: engine, rule id, status class (violation / non-violation), component or URL path,
/// scenario, state label and a normalized selector. It deliberately excludes HTML evidence, messages, host names,
/// ports, timestamps and generated identifiers (GUIDs, long numbers, Blazor CSS-isolation scope attributes),
/// which change between runs without the underlying problem changing.
/// </remarks>
public static partial class FindingFingerprint
{
    /// <summary>Fingerprint format version. Changing the algorithm requires a new version.</summary>
    public const string Version = "v1";

    /// <summary>Computes the fingerprint of <paramref name="finding"/> in <paramref name="context"/>.</summary>
    public static string Compute(AccessibilityFinding finding, AccessibilityContext context)
    {
        ArgumentNullException.ThrowIfNull(finding);
        ArgumentNullException.ThrowIfNull(context);

        var location = context.Kind == AuditTargetKind.Component
            ? context.ComponentType ?? string.Empty
            : NormalizeUrl(context.Url);

        var component = finding.Component is null ? string.Empty : string.Join('>', finding.Component.ComponentPath);
        var selector = finding.Target.SelectorPath.Count > 0
            ? string.Join(" >>> ", finding.Target.SelectorPath.Select(NormalizeSelector))
            : NormalizeSelector(finding.Target.Selector);

        var payload = string.Join(
            '\u001f',
            Version,
            finding.Engine,
            finding.RuleId,
            finding.Status == FindingStatus.Violation ? "violation" : finding.Status.ToString().ToLowerInvariant(),
            location,
            component,
            context.Scenario ?? string.Empty,
            context.State ?? string.Empty,
            selector,
            finding.Data.TryGetValue("fingerprintKey", out var key) ? key : string.Empty);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexStringLower(hash.AsSpan(0, 16));
    }

    /// <summary>Removes volatile parts from a selector (GUIDs, long numbers, CSS-isolation scopes).</summary>
    public static string NormalizeSelector(string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return string.Empty;
        }

        var s = BlazorScope().Replace(selector, string.Empty);
        s = Guid().Replace(s, "{guid}");
        s = LongHex().Replace(s, "{hex}");
        s = LongNumber().Replace(s, "{n}");
        return Whitespace().Replace(s, " ").Trim();
    }

    /// <summary>Keeps only the path (and fragment-less, query-less) part of a URL.</summary>
    public static string NormalizeUrl(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return uri.Scheme is "http" or "https" ? uri.AbsolutePath : uri.Scheme + ":";
        }

        return url;
    }

    [GeneratedRegex(@"\[b-[a-z0-9]{10}\]", RegexOptions.CultureInvariant)]
    private static partial Regex BlazorScope();

    [GeneratedRegex(@"[0-9a-fA-F]{8}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{12}", RegexOptions.CultureInvariant)]
    private static partial Regex Guid();

    [GeneratedRegex(@"(?<![0-9a-zA-Z])(?=[0-9a-f]*[0-9])(?=[0-9a-f]*[a-f])[0-9a-f]{12,}(?![0-9a-zA-Z])", RegexOptions.CultureInvariant)]
    private static partial Regex LongHex();

    [GeneratedRegex(@"\d{4,}", RegexOptions.CultureInvariant)]
    private static partial Regex LongNumber();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
