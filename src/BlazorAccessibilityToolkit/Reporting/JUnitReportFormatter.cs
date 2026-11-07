using System.Globalization;
using System.Text;
using System.Xml;

namespace BlazorAccessibilityToolkit.Reporting;

/// <summary>
/// JUnit XML: one <c>testsuite</c> per audit, one <c>testcase</c> per rule with findings (failure for violations,
/// skipped with reason for inconclusive, potential and suppressed results), one <c>testcase</c> per passed rule, and an
/// <c>error</c> test case for audits that failed to execute.
/// </summary>
public sealed class JUnitReportFormatter : IReportFormatter
{
    /// <inheritdoc />
    public string Name => "junit";

    /// <inheritdoc />
    public string DefaultFileName => "accessibility-junit.xml";

    /// <summary>Emit passing test cases for rules that passed. Default <c>true</c>.</summary>
    public bool IncludePassedRules { get; init; } = true;

    /// <inheritdoc />
    public void Write(AccessibilityReport report, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(writer);
        var suites = report.Audits.Select(BuildSuite).ToArray();

        using var xml = XmlWriter.Create(writer, new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false), OmitXmlDeclaration = false });
        xml.WriteStartDocument();
        xml.WriteStartElement("testsuites");
        xml.WriteAttributeString("name", "accessibility");
        xml.WriteAttributeString("tests", Count(suites.Sum(s => s.Cases.Count)));
        xml.WriteAttributeString("failures", Count(suites.Sum(s => s.Cases.Count(c => c.Kind == CaseKind.Failure))));
        xml.WriteAttributeString("errors", Count(suites.Sum(s => s.Cases.Count(c => c.Kind == CaseKind.Error))));
        xml.WriteAttributeString("skipped", Count(suites.Sum(s => s.Cases.Count(c => c.Kind == CaseKind.Skipped))));
        xml.WriteAttributeString("time", Seconds(report.Audits.Sum(a => a.DurationMs)));

        foreach (var suite in suites)
        {
            xml.WriteStartElement("testsuite");
            xml.WriteAttributeString("name", suite.Audit.Name);
            xml.WriteAttributeString("tests", Count(suite.Cases.Count));
            xml.WriteAttributeString("failures", Count(suite.Cases.Count(c => c.Kind == CaseKind.Failure)));
            xml.WriteAttributeString("errors", Count(suite.Cases.Count(c => c.Kind == CaseKind.Error)));
            xml.WriteAttributeString("skipped", Count(suite.Cases.Count(c => c.Kind == CaseKind.Skipped)));
            xml.WriteAttributeString("time", Seconds(suite.Audit.DurationMs));
            xml.WriteAttributeString("timestamp", suite.Audit.StartedAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));

            xml.WriteStartElement("properties");
            Property(xml, "status", suite.Audit.Status.ToString());
            Property(xml, "kind", suite.Audit.Context.Kind.ToString());
            Property(xml, "component", suite.Audit.Context.ComponentType);
            Property(xml, "scenario", suite.Audit.Context.Scenario);
            Property(xml, "state", suite.Audit.Context.State);
            Property(xml, "url", suite.Audit.Context.Url);
            Property(xml, "browser", suite.Audit.Context.Browser);
            Property(xml, "engines", string.Join(", ", suite.Audit.Engines.Select(e => $"{e.Name} {e.Version}")));
            xml.WriteEndElement();

            foreach (var testCase in suite.Cases)
            {
                xml.WriteStartElement("testcase");
                xml.WriteAttributeString("classname", suite.Audit.Name);
                xml.WriteAttributeString("name", testCase.Name);
                xml.WriteAttributeString("time", "0");
                switch (testCase.Kind)
                {
                    case CaseKind.Failure:
                        xml.WriteStartElement("failure");
                        xml.WriteAttributeString("message", testCase.Message);
                        xml.WriteAttributeString("type", testCase.Type);
                        xml.WriteString(testCase.Details);
                        xml.WriteEndElement();
                        break;
                    case CaseKind.Error:
                        xml.WriteStartElement("error");
                        xml.WriteAttributeString("message", testCase.Message);
                        xml.WriteAttributeString("type", testCase.Type);
                        xml.WriteString(testCase.Details);
                        xml.WriteEndElement();
                        break;
                    case CaseKind.Skipped:
                        xml.WriteStartElement("skipped");
                        xml.WriteAttributeString("message", testCase.Message);
                        xml.WriteEndElement();
                        if (testCase.Details.Length > 0)
                        {
                            xml.WriteElementString("system-out", testCase.Details);
                        }

                        break;
                }

                xml.WriteEndElement();
            }

            xml.WriteEndElement();
        }

        xml.WriteEndElement();
        xml.WriteEndDocument();
    }

    private Suite BuildSuite(AccessibilityAudit audit)
    {
        var cases = new List<Case>();
        if (audit.Status == AuditStatus.Failed)
        {
            var details = string.Join(Environment.NewLine, audit.Errors.Select(e => $"[{e.Source}] {e.ExceptionType}: {e.Message}"));
            cases.Add(new Case("audit execution", CaseKind.Error, "Audit failed to execute; results are not trustworthy.", "execution-error", details));
        }
        else if (audit.Status == AuditStatus.Skipped)
        {
            cases.Add(new Case("audit execution", CaseKind.Skipped, audit.Warnings.FirstOrDefault() ?? "Audit skipped.", "skipped", string.Empty));
        }

        foreach (var group in audit.Findings.GroupBy(f => (f.RuleId, f.Engine)).OrderBy(g => g.Key.RuleId, StringComparer.Ordinal))
        {
            var findings = group.ToArray();
            var violations = findings.Where(f => f.Status == FindingStatus.Violation && !f.IsSuppressed).ToArray();
            var details = string.Join(Environment.NewLine + Environment.NewLine, findings.Select(FindingFormatter.Format));
            var name = $"{group.Key.RuleId} ({group.Key.Engine})";
            if (violations.Length > 0)
            {
                var first = violations[0];
                cases.Add(new Case(name, CaseKind.Failure, string.Create(CultureInfo.InvariantCulture, $"{violations.Length} violation(s): {first.Help ?? first.Description}"), first.Impact?.ToString().ToLowerInvariant() ?? "violation", details));
            }
            else
            {
                var reason = findings.All(f => f.IsSuppressed)
                    ? "Suppressed: " + findings[0].Suppression!.Reason
                    : string.Join(", ", findings.Where(f => !f.IsSuppressed).Select(f => f.Status).Distinct()) + " — needs human review; not a pass";
                cases.Add(new Case(name, CaseKind.Skipped, reason, "review", details));
            }
        }

        if (IncludePassedRules && audit.Status == AuditStatus.Completed)
        {
            cases.AddRange(audit.PassedRules.Select(r => new Case(r, CaseKind.Passed, string.Empty, string.Empty, string.Empty)));
        }

        return new Suite(audit, cases);
    }

    private static void Property(XmlWriter xml, string name, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        xml.WriteStartElement("property");
        xml.WriteAttributeString("name", name);
        xml.WriteAttributeString("value", value);
        xml.WriteEndElement();
    }

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Seconds(double ms) => (ms / 1000d).ToString("0.###", CultureInfo.InvariantCulture);

    private enum CaseKind
    {
        Passed,
        Failure,
        Error,
        Skipped,
    }

    private sealed record Case(string Name, CaseKind Kind, string Message, string Type, string Details);

    private sealed record Suite(AccessibilityAudit Audit, IReadOnlyList<Case> Cases);
}
