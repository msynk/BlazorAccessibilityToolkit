using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using Axe.Windows.Automation;
using Axe.Windows.Automation.Data;
using BlazorAccessibilityToolkit.Playwright;
using BlazorAccessibilityToolkit.Reporting;

namespace BlazorAccessibilityToolkit.Windows;

/// <summary>Settings for the native audits of <see cref="NativeAudits"/>.</summary>
public sealed record NativeAuditOptions
{
    /// <summary>
    /// Also scan the web content of WebView2 and other Chromium-based views through UI Automation. Default
    /// <c>false</c>: a Blazor Hybrid app's web content is audited in the browser (axe-core and the toolkit's checks on
    /// <see cref="BlazorHybridApp.Page"/>), which knows the DOM; Axe.Windows would report it again, less precisely.
    /// </summary>
    public bool IncludeWebContent { get; init; }

    /// <summary>Axe.Windows rule ids to skip, for example <c>NameNotNull</c>, and documented suppressions.</summary>
    public AccessibilityAuditOptions? AuditOptions { get; init; }
}

/// <summary>
/// UI Automation audits of Windows apps with Axe.Windows (the engine of Accessibility Insights for Windows): the native
/// window, menus, dialogs and controls of a Blazor Hybrid app, which the browser audits of its web view do not see.
/// </summary>
[SupportedOSPlatform("windows")]
public static class NativeAudits
{
    /// <summary>The engine name in audits.</summary>
    public const string EngineName = "axe-windows";

    /// <summary>
    /// Audits the native user interface of a Blazor Hybrid app that <see cref="BlazorHybridApp.LaunchAsync"/> started:
    /// every top-level window of its process. Use <see cref="AuditProcessAsync"/> for an app you connected to.
    /// </summary>
    public static Task<AccessibilityAudit> AuditNativeWindowAsync(this BlazorHybridApp app, NativeAuditOptions? options = null, AuditLabel? label = null)
    {
        ArgumentNullException.ThrowIfNull(app);
        var process = app.Process ?? throw new InvalidOperationException("The app was connected, not started, so its process is not known. Use NativeAudits.AuditProcessAsync with its process id.");
        return AuditProcessAsync(process.Id, options, label);
    }

    /// <summary>
    /// Audits the top-level windows of a Windows process with Axe.Windows. Every error becomes a violation with the
    /// engine <see cref="EngineName"/>. Audits never pass silently: a scan that fails (the process has no window, or
    /// it exited) gives a failed audit.
    /// </summary>
    public static async Task<AccessibilityAudit> AuditProcessAsync(int processId, NativeAuditOptions? options = null, AuditLabel? label = null)
    {
        options ??= new NativeAuditOptions();
        var auditOptions = options.AuditOptions ?? AccessibilityAuditOptions.Default;
        var processName = ProcessName(processId);
        var context = new AccessibilityContext
        {
            Kind = AuditTargetKind.Page,
            RenderMode = "native",
            Scenario = label?.Scenario,
            State = label?.State ?? "native window",
            Url = $"process:{processName}",
            Metadata = label?.Metadata ?? new Dictionary<string, string>(),
        };
        var builder = new AccessibilityAuditBuilder(label?.Name ?? $"{processName} [native window]", context, auditOptions);
        builder.AddEngine(EngineName, typeof(ScannerFactory).Assembly.GetName().Version?.ToString(3));
        try
        {
            var config = Config.Builder.ForProcessId(processId).WithOutputFileFormat(OutputFileFormat.None);
            if (options.IncludeWebContent)
            {
                config = config.WithTestAllChromiumContent();
            }

            var scanner = ScannerFactory.CreateScanner(config.Build());
            var output = await Task.Run(() => scanner.Scan(new ScanOptions())).ConfigureAwait(false);
            var disabled = auditOptions.DisabledRules.ToHashSet(StringComparer.Ordinal);
            var failed = new HashSet<string>(StringComparer.Ordinal);
            var webContent = 0;
            foreach (var window in output.WindowScanOutputs)
            {
                foreach (var error in window.Errors)
                {
                    var ruleId = error.Rule.ID.ToString();
                    if (disabled.Contains(ruleId))
                    {
                        continue;
                    }

                    // WebView2 (Chromium) content and its internal views: audited in the browser, or not the app's own.
                    if (!options.IncludeWebContent && IsInChromium(error.Element))
                    {
                        webContent++;
                        continue;
                    }

                    failed.Add(ruleId);
                    builder.AddFinding(ToFinding(error));
                }
            }

            if (webContent > 0)
            {
                builder.AddWarning(string.Create(CultureInfo.InvariantCulture, $"{webContent} UI Automation findings inside WebView2 were skipped: the web content is audited in the browser, and WebView2's internal views are not the app's (NativeAuditOptions.IncludeWebContent includes them)."));
            }

            if (failed.Count == 0)
            {
                builder.AddPassedRule(EngineName);
            }
        }
        catch (Exception ex) when (ex is AxeWindowsAutomationException or InvalidOperationException or ArgumentException)
        {
            builder.AddError(ExecutionError.FromException(EngineName, ex));
        }

        var audit = builder.Build();
        var warning = AccessibilityResultStore.TrySaveToDefault(audit);
        return warning is null ? audit : audit with { Warnings = [.. audit.Warnings, warning] };
    }

    private static AccessibilityFinding ToFinding(ScanResult error)
    {
        var properties = error.Element.Properties ?? new Dictionary<string, string>();
        string? Property(string name) => properties.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
        var data = new Dictionary<string, string>();
        foreach (var name in new[] { "ControlType", "LocalizedControlType", "AutomationId", "ClassName", "FrameworkId" })
        {
            if (Property(name) is { } value)
            {
                data[name] = value;
            }
        }

        data["criteria"] = error.Rule.Standard.ToString();
        return new AccessibilityFinding
        {
            RuleId = error.Rule.ID.ToString(),
            Engine = EngineName,
            Status = FindingStatus.Violation,
            Impact = AccessibilityImpact.Serious,
            Description = error.Rule.Description,
            Help = error.Rule.Description,
            Remediation = error.Rule.HowToFix,
            HelpUrl = string.IsNullOrWhiteSpace(error.Rule.FrameworkIssueLink) ? "https://accessibilityinsights.io/docs/windows/overview/" : error.Rule.FrameworkIssueLink,
            Wcag = [WcagCatalog.Get(Criterion(error.Rule.Standard.ToString()))],
            Target = new FindingTarget
            {
                Selector = PathOf(error.Element),
                Role = Property("LocalizedControlType") ?? Property("ControlType"),
                AccessibleName = Property("Name"),
            },
            Data = data,
        };
    }

    private static bool IsInChromium(ElementInfo element)
    {
        for (var current = element; current is not null; current = current.Parent)
        {
            if (current.Properties is { } properties && properties.TryGetValue("FrameworkId", out var framework) && string.Equals(framework, "Chrome", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // Axe.Windows groups rules by the criteria of Section 508 and WCAG they serve.
    private static string Criterion(string criteria) => criteria switch
    {
        "InfoAndRelationships" => "1.3.1",
        "Keyboard" => "2.1.1",
        _ => "4.1.2", // NameRoleValue, ObjectInformation, AvailableActions
    };

    // A readable path from the window to the element, such as: window "Orders" > pane > button #save "Save".
    private static string PathOf(ElementInfo element)
    {
        var segments = new List<string>();
        for (var current = element; current is not null && segments.Count < 16; current = current.Parent)
        {
            var properties = current.Properties ?? new Dictionary<string, string>();
            var type = properties.TryGetValue("LocalizedControlType", out var localized) && !string.IsNullOrWhiteSpace(localized)
                ? localized
                : properties.TryGetValue("ControlType", out var controlType) ? controlType : "element";
            var segment = type;
            // Windows Forms gives unnamed controls their window handle as AutomationId, which changes on every run.
            if (properties.TryGetValue("AutomationId", out var id) && !string.IsNullOrWhiteSpace(id) && !id.All(char.IsAsciiDigit))
            {
                segment += " #" + id;
            }

            if (properties.TryGetValue("Name", out var name) && !string.IsNullOrWhiteSpace(name))
            {
                segment += string.Create(CultureInfo.InvariantCulture, $" \"{(name.Length > 40 ? name[..40] + "…" : name)}\"");
            }

            segments.Add(segment);
        }

        segments.Reverse();
        return string.Join(" > ", segments);
    }

    private static string ProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return processId.ToString(CultureInfo.InvariantCulture);
        }
    }
}
