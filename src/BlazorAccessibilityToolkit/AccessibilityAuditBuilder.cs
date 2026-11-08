using System.Diagnostics;
using System.Globalization;
using BlazorAccessibilityToolkit.Internal;

namespace BlazorAccessibilityToolkit;

/// <summary>
/// Assembles an <see cref="AccessibilityAudit"/> from engine output: sanitizes evidence, computes fingerprints,
/// applies documented suppressions, de-duplicates and sorts findings, and derives the execution status.
/// </summary>
/// <remarks>
/// Built-in engines use this type, and custom engines or rules should use it too so that their findings are
/// normalized consistently. An audit that recorded any <see cref="ExecutionError"/> is always
/// <see cref="AuditStatus.Failed"/>.
/// </remarks>
public sealed class AccessibilityAuditBuilder
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly List<AccessibilityFinding> _findings = [];
    private readonly List<EngineInfo> _engines = [];
    private readonly SortedSet<string> _passed = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _inapplicable = new(StringComparer.Ordinal);
    private readonly List<ExecutionError> _errors = [];
    private readonly List<string> _warnings = [];
    private string? _skipReason;

    /// <summary>Starts a new audit.</summary>
    public AccessibilityAuditBuilder(string name, AccessibilityContext context, AccessibilityAuditOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(context);
        Name = name;
        Context = context;
        Options = options ?? AccessibilityAuditOptions.Default;
        StartedAt = Options.TimeProvider.GetUtcNow();
    }

    /// <summary>Audit name.</summary>
    public string Name { get; }

    /// <summary>Audit context.</summary>
    public AccessibilityContext Context { get; set; }

    /// <summary>Options in effect.</summary>
    public AccessibilityAuditOptions Options { get; }

    /// <summary>Start time.</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>True when an error has been recorded.</summary>
    public bool HasErrors => _errors.Count > 0;

    /// <summary>Records an engine that participated.</summary>
    public AccessibilityAuditBuilder AddEngine(string name, string? version)
    {
        if (!_engines.Any(e => e.Name == name))
        {
            _engines.Add(new EngineInfo { Name = name, Version = version });
        }

        return this;
    }

    /// <summary>Adds a finding.</summary>
    public AccessibilityAuditBuilder AddFinding(AccessibilityFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        _findings.Add(finding);
        return this;
    }

    /// <summary>Adds findings.</summary>
    public AccessibilityAuditBuilder AddFindings(IEnumerable<AccessibilityFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        foreach (var finding in findings)
        {
            AddFinding(finding);
        }

        return this;
    }

    /// <summary>Records a rule that ran without finding problems.</summary>
    public AccessibilityAuditBuilder AddPassedRule(string ruleId)
    {
        _passed.Add(ruleId);
        return this;
    }

    /// <summary>Records a rule that did not apply.</summary>
    public AccessibilityAuditBuilder AddInapplicableRule(string ruleId)
    {
        _inapplicable.Add(ruleId);
        return this;
    }

    /// <summary>Records an execution error; the audit will be <see cref="AuditStatus.Failed"/>.</summary>
    public AccessibilityAuditBuilder AddError(ExecutionError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        _errors.Add(error);
        return this;
    }

    /// <summary>Records an execution error from an exception.</summary>
    public AccessibilityAuditBuilder AddError(string source, Exception exception) => AddError(ExecutionError.FromException(source, exception));

    /// <summary>Records a non-fatal warning.</summary>
    public AccessibilityAuditBuilder AddWarning(string warning)
    {
        if (!_warnings.Contains(warning))
        {
            _warnings.Add(warning);
        }

        return this;
    }

    /// <summary>Marks the audit as intentionally skipped.</summary>
    public AccessibilityAuditBuilder MarkSkipped(string reason)
    {
        _skipReason = reason;
        return this;
    }

    /// <summary>Builds the normalized audit. The builder is not changed, so calling it again gives an equivalent audit.</summary>
    public AccessibilityAudit Build()
    {
        // Warnings derived here go to a local list so that repeated calls do not accumulate them in the builder.
        var warnings = new List<string>();
        if (_skipReason is not null)
        {
            warnings.Add("Skipped: " + _skipReason);
        }

        warnings.AddRange(_warnings);
        void Warn(string warning)
        {
            if (!warnings.Contains(warning))
            {
                warnings.Add(warning);
            }
        }

        var today = DateOnly.FromDateTime(Options.TimeProvider.GetUtcNow().UtcDateTime);
        var activeSuppressions = new List<Suppression>();
        var expiredSuppressions = new List<Suppression>();
        foreach (var suppression in Options.Suppressions)
        {
            if (string.IsNullOrWhiteSpace(suppression.Reason))
            {
                Warn($"Suppression for rule '{suppression.RuleId}' was ignored because it has no reason.");
                continue;
            }

            if (suppression.Expires is { } expires && expires < today)
            {
                expiredSuppressions.Add(suppression);
                Warn(string.Create(CultureInfo.InvariantCulture, $"Suppression for rule '{suppression.RuleId}' expired on {expires:yyyy-MM-dd} and no longer applies (owner: {suppression.Owner ?? "unassigned"})."));
                continue;
            }

            activeSuppressions.Add(suppression);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var normalized = new List<AccessibilityFinding>(_findings.Count);
        foreach (var raw in _findings)
        {
            var finding = raw with
            {
                Target = raw.Target with
                {
                    Html = EvidenceSanitizer.Sanitize(raw.Target.Html, Options.RedactFormValues, Options.MaxEvidenceLength),
                    AccessibleName = EvidenceSanitizer.Truncate(raw.Target.AccessibleName, Options.MaxEvidenceLength),
                },
                Checks = raw.Checks.Select(c => c with { DataJson = EvidenceSanitizer.Truncate(c.DataJson, Options.MaxEvidenceLength) }).ToArray(),
                Data = raw.Data.ToDictionary(kv => kv.Key, kv => EvidenceSanitizer.Truncate(kv.Value, Options.MaxEvidenceLength) ?? string.Empty, StringComparer.Ordinal),
            };

            // Exact duplicates (same engine, rule, status, raw selector, message and data) are reported once. Different
            // problems of one rule on the same element (for example two broken id references) differ in their message or
            // data and are all kept, as are findings that only share a fingerprint (for example selectors that differ
            // only in generated numbers); baselines count them.
            var exactKey = string.Join(
                '\u001f',
                finding.Engine,
                finding.RuleId,
                finding.Status,
                finding.Target.Selector,
                string.Join(">>>", finding.Target.SelectorPath),
                finding.Remediation,
                string.Join('\u001e', finding.Data.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value)));
            if (!seen.Add(exactKey))
            {
                continue;
            }

            finding = finding with { Fingerprint = FindingFingerprint.Compute(finding, Context) };

            var suppression = activeSuppressions.FirstOrDefault(s => Matches(s, finding));
            if (suppression is not null)
            {
                finding = finding with
                {
                    Suppression = new AppliedSuppression { Reason = suppression.Reason, Owner = suppression.Owner, Expires = suppression.Expires },
                };
            }

            normalized.Add(finding);
        }

        normalized.Sort(FindingComparer.Instance);
        var failingRules = normalized.Select(f => f.RuleId).ToHashSet(StringComparer.Ordinal);

        var status = _errors.Count > 0 ? AuditStatus.Failed : _skipReason is not null ? AuditStatus.Skipped : AuditStatus.Completed;
        return new AccessibilityAudit
        {
            Name = Name,
            Context = Context,
            Status = status,
            StartedAt = StartedAt,
            DurationMs = Math.Round(_stopwatch.Elapsed.TotalMilliseconds, 1),
            Engines = _engines.ToArray(),
            Configuration = Options.ToConfiguration(Context.Kind),
            Findings = normalized,
            PassedRules = _passed.Where(r => !failingRules.Contains(r)).ToArray(),
            InapplicableRules = _inapplicable.ToArray(),
            Errors = _errors.ToArray(),
            Warnings = warnings.ToArray(),
            ExpiredSuppressions = expiredSuppressions.ToArray(),
        };
    }

    internal bool Matches(Suppression suppression, AccessibilityFinding finding)
    {
        if (!string.Equals(suppression.RuleId, finding.RuleId, StringComparison.Ordinal))
        {
            return false;
        }

        if (suppression.Selector is { } selector && !WildcardPattern.IsMatch(finding.Target.Selector, selector))
        {
            return false;
        }

        if (suppression.Scenario is { } scenario && !WildcardPattern.IsMatch(Context.Scenario, scenario))
        {
            return false;
        }

        if (suppression.Component is { } component)
        {
            var candidates = new List<string>();
            if (finding.Component is not null)
            {
                candidates.AddRange(finding.Component.ComponentPath);
                candidates.Add(finding.Component.ComponentType);
            }

            if (Context.ComponentType is not null)
            {
                candidates.Add(Context.ComponentType);
            }

            var matched = candidates.Any(c => WildcardPattern.IsMatch(c, component) || WildcardPattern.IsMatch(SimpleName(c), component));
            if (!matched)
            {
                return false;
            }
        }

        return true;
    }

    private static string SimpleName(string typeName)
    {
        var generic = typeName.IndexOf('`', StringComparison.Ordinal);
        var name = generic >= 0 ? typeName[..generic] : typeName;
        var dot = name.LastIndexOf('.');
        return dot >= 0 ? name[(dot + 1)..] : name;
    }

    /// <summary>Deterministic ordering: status, impact (highest first), rule, selector, fingerprint.</summary>
    internal sealed class FindingComparer : IComparer<AccessibilityFinding>
    {
        public static readonly FindingComparer Instance = new();

        public int Compare(AccessibilityFinding? x, AccessibilityFinding? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            var c = x.Status.CompareTo(y.Status);
            if (c != 0)
            {
                return c;
            }

            c = ((int?)y.Impact ?? 0).CompareTo((int?)x.Impact ?? 0);
            if (c != 0)
            {
                return c;
            }

            c = string.CompareOrdinal(x.RuleId, y.RuleId);
            if (c != 0)
            {
                return c;
            }

            c = string.CompareOrdinal(x.Target.Selector, y.Target.Selector);
            return c != 0 ? c : string.CompareOrdinal(x.Fingerprint, y.Fingerprint);
        }
    }
}
