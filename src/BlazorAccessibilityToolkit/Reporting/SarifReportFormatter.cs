using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BlazorAccessibilityToolkit.Reporting;

/// <summary>
/// SARIF 2.1.0 output for code-scanning tools.
/// </summary>
/// <remarks>
/// Every result has a logical location (component path or URL) and a <c>partialFingerprints</c> entry with the
/// toolkit fingerprint. Physical (file) locations are emitted when <see cref="SourceResolver"/> maps a component type to a
/// file, or for other findings when <see cref="FallbackUri"/> is set. Code-scanning services such as GitHub only display
/// results with a physical location and a region, so file locations carry a file-level region (line 1) and the result
/// property <c>locationPrecision</c> is <c>file</c>: the toolkit never claims the source line of a finding. Suppressed
/// findings carry a SARIF suppression with the documented
/// justification. Failed audits are reported as tool execution notifications so that they are never mistaken for
/// clean results.
/// </remarks>
public sealed class SarifReportFormatter : IReportFormatter
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <inheritdoc />
    public string Name => "sarif";

    /// <inheritdoc />
    public string DefaultFileName => "accessibility.sarif";

    /// <summary>Optional mapping from a component type name to a repository-relative file path (for example <c>src/App/Components/Dialog.razor</c>).</summary>
    public Func<string, string?>? SourceResolver { get; init; }

    /// <summary>
    /// Repository-relative file used for findings that cannot be mapped to a component file (for example page audits),
    /// such as the end-to-end test file. Without it those results have only logical locations.
    /// </summary>
    public string? FallbackUri { get; init; }

    /// <inheritdoc />
    public void Write(AccessibilityReport report, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(writer);

        var newFingerprints = report.Baseline?.New.Select(n => (n.AuditId, n.Fingerprint)).ToHashSet();
        var findings = report.Audits
            .Where(a => a.Status == AuditStatus.Completed)
            .SelectMany(a => a.Findings.Select(f => (Audit: a, Finding: f)))
            .ToArray();

        var rules = findings
            .Select(x => x.Finding)
            .GroupBy(f => f.RuleId, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToArray();
        var ruleIndex = rules.Select((r, i) => (r.RuleId, i)).ToDictionary(x => x.RuleId, x => x.i, StringComparer.Ordinal);

        var results = new JsonArray();
        foreach (var (audit, finding) in findings)
        {
            var result = new JsonObject
            {
                ["ruleId"] = finding.RuleId,
                ["ruleIndex"] = ruleIndex[finding.RuleId],
                ["kind"] = finding.Status switch
                {
                    FindingStatus.Violation => "fail",
                    FindingStatus.ManualReviewRequired => "review",
                    _ => "review",
                },
                ["level"] = Level(finding),
                ["message"] = new JsonObject { ["text"] = Message(audit, finding) },
                ["locations"] = new JsonArray(Location(audit, finding)),
                ["partialFingerprints"] = new JsonObject { ["blazorA11y/" + FindingFingerprint.Version] = finding.Fingerprint },
                ["properties"] = Properties(audit, finding),
            };
            var hasFile = result["locations"]![0]!["physicalLocation"] is not null;
            result["properties"]!["locationPrecision"] = hasFile ? "file" : "logical";

            if (newFingerprints is not null)
            {
                result["baselineState"] = newFingerprints.Contains((audit.Id, finding.Fingerprint ?? string.Empty)) ? "new" : "unchanged";
            }

            if (finding.Suppression is { } suppression)
            {
                result["suppressions"] = new JsonArray(new JsonObject
                {
                    ["kind"] = "external",
                    ["status"] = "accepted",
                    ["justification"] = suppression.Reason + (suppression.Owner is null ? string.Empty : $" (owner: {suppression.Owner})"),
                });
            }

            results.Add(result);
        }

        var notifications = new JsonArray();
        foreach (var audit in report.Audits.Where(a => a.Status != AuditStatus.Completed))
        {
            notifications.Add(new JsonObject
            {
                ["level"] = audit.Status == AuditStatus.Failed ? "error" : "warning",
                ["message"] = new JsonObject
                {
                    ["text"] = $"Audit '{audit.Name}' {(audit.Status == AuditStatus.Failed ? "failed to execute" : "was skipped")}: " +
                        string.Join("; ", audit.Errors.Select(e => e.Message).Concat(audit.Warnings)),
                },
            });
        }

        var sarif = new JsonObject
        {
            ["$schema"] = "https://json.schemastore.org/sarif-2.1.0.json",
            ["version"] = "2.1.0",
            ["runs"] = new JsonArray(new JsonObject
            {
                ["tool"] = new JsonObject
                {
                    ["driver"] = new JsonObject
                    {
                        ["name"] = report.Tool.Name,
                        ["version"] = report.Tool.Version,
                        ["semanticVersion"] = report.Tool.Version,
                        ["informationUri"] = report.Tool.InformationUri,
                        ["rules"] = new JsonArray(rules.Select(Rule).ToArray<JsonNode?>()),
                    },
                },
                ["invocations"] = new JsonArray(new JsonObject
                {
                    ["executionSuccessful"] = report.Audits.All(a => a.Status != AuditStatus.Failed),
                    ["toolExecutionNotifications"] = notifications,
                }),
                ["originalUriBaseIds"] = new JsonObject { ["SRCROOT"] = new JsonObject { ["description"] = new JsonObject { ["text"] = "Repository root" } } },
                ["results"] = results,
            }),
        };

        writer.Write(sarif.ToJsonString(WriteOptions));
        writer.Write('\n');
    }

    private static string Level(AccessibilityFinding finding)
    {
        if (finding.Status != FindingStatus.Violation)
        {
            return "note";
        }

        return finding.Impact switch
        {
            AccessibilityImpact.Critical or AccessibilityImpact.Serious or null => "error",
            AccessibilityImpact.Moderate => "warning",
            _ => "note",
        };
    }

    private static string Message(AccessibilityAudit audit, AccessibilityFinding finding)
    {
        var parts = new List<string> { $"{finding.Help ?? finding.Description} ({finding.Status})" };
        if (finding.Target.Selector is { } selector)
        {
            parts.Add($"Element: {selector}");
        }

        parts.Add($"Audit: {audit.Name}");
        if (finding.Remediation is { } remediation)
        {
            parts.Add($"Fix: {remediation}");
        }

        return string.Join(". ", parts);
    }

    private JsonObject Location(AccessibilityAudit audit, AccessibilityFinding finding)
    {
        var logicalName = finding.Component is { } component
            ? string.Join(" > ", component.ComponentPath)
            : audit.Context.ComponentType ?? FindingFingerprint.NormalizeUrl(audit.Context.Url);
        var location = new JsonObject
        {
            ["logicalLocations"] = new JsonArray(new JsonObject
            {
                ["fullyQualifiedName"] = string.IsNullOrEmpty(logicalName) ? audit.Name : logicalName,
                ["kind"] = audit.Context.Kind == AuditTargetKind.Component ? "type" : "resource",
            }),
        };

        var componentType = finding.Component?.ComponentType ?? audit.Context.ComponentType;
        var file = (componentType is null ? null : SourceResolver?.Invoke(componentType)) ?? FallbackUri;
        if (file is not null)
        {
            location["physicalLocation"] = new JsonObject
            {
                ["artifactLocation"] = new JsonObject { ["uri"] = file.Replace('\\', '/'), ["uriBaseId"] = "SRCROOT" },
                ["region"] = new JsonObject { ["startLine"] = 1 },
            };
        }

        return location;
    }

    private static JsonObject Properties(AccessibilityAudit audit, AccessibilityFinding finding)
    {
        var properties = new JsonObject
        {
            ["status"] = finding.Status.ToString(),
            ["engine"] = finding.Engine,
            ["audit"] = audit.Name,
        };
        Add(properties, "impact", finding.Impact?.ToString().ToLowerInvariant());
        Add(properties, "selector", finding.Target.Selector);
        Add(properties, "html", finding.Target.Html);
        Add(properties, "scenario", audit.Context.Scenario);
        Add(properties, "state", audit.Context.State);
        Add(properties, "url", audit.Context.Url);
        Add(properties, "browser", audit.Context.Browser);
        if (finding.Wcag.Count > 0)
        {
            properties["wcag"] = new JsonArray(finding.Wcag.Select(w => (JsonNode?)JsonValue.Create(w.Id)).ToArray());
        }

        return properties;
    }

    private static JsonObject Rule(AccessibilityFinding finding)
    {
        var tags = new JsonArray(finding.Tags.Select(t => (JsonNode?)JsonValue.Create(t)).Append(JsonValue.Create("accessibility")).ToArray());
        var rule = new JsonObject
        {
            ["id"] = finding.RuleId,
            ["name"] = finding.RuleId,
            ["shortDescription"] = new JsonObject { ["text"] = finding.Help ?? finding.RuleId },
            ["fullDescription"] = new JsonObject { ["text"] = finding.Description ?? finding.Help ?? finding.RuleId },
            ["properties"] = new JsonObject { ["tags"] = tags, ["engine"] = finding.Engine },
        };
        if (finding.HelpUrl is { } helpUrl)
        {
            rule["helpUri"] = helpUrl;
        }

        return rule;
    }

    private static void Add(JsonObject obj, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            obj[name] = value;
        }
    }
}
