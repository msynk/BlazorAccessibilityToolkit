using System.Globalization;
using BlazorAccessibilityToolkit.Baselines;
using BlazorAccessibilityToolkit.ManualReview;
using BlazorAccessibilityToolkit.Reporting;

namespace BlazorAccessibilityToolkit.Cli;

/// <summary>Command-line entry point (testable: writes to the supplied writers and returns the exit code).</summary>
public static class CliApp
{
    private const string Usage = """
        blazor-a11y — Blazor Accessibility Toolkit CI tool

        Usage:
          blazor-a11y report   [options]   Aggregate results, compare with a baseline, write reports, apply the failure policy
          blazor-a11y baseline [options]   Write a baseline from the current results
          blazor-a11y manual init --name <name> [--scope <scope>] --output <file>
          blazor-a11y manual validate <file>...
          blazor-a11y --version | --help

        report / baseline options:
          --input <path>          Result directory or file (repeatable). Default: $BLAZOR_A11Y_RESULTS_DIR or ./a11y-results
          --baseline <file>       Baseline to compare with (report) or to write (baseline)
          --manual-review <file>  Manual-review checklist to include (repeatable)
          --format <name>         console, json, junit, sarif, html (repeatable). Default: all
          --output <dir>          Report directory. Default: ./a11y-report
          --fail-on <level>       none, minor, moderate, serious, critical. Default: minor (any violation)
          --fail-on-potential     Also fail on toolkit heuristics (potential issues)
          --fail-on-inconclusive  Also fail on inconclusive results
          --fail-on-manual-review Also fail when manual checks are outstanding
          --strict                Baselines do not excuse findings; stale baseline entries, expired suppressions and skipped audits fail
          --allow-empty           Do not fail when no results are found
          --source-root <dir>     Map component types to .razor files (by unique file name) for SARIF locations
          --sarif-fallback-uri <path>  Repository-relative file for SARIF results without a component file (page audits)
          --allow-failed          (baseline) write a baseline even if some audits failed

        Exit codes: 0 success, 1 accessibility findings failed the policy, 2 audits failed/no results/unreadable input, 3 usage error.
        Automated checks cannot prove WCAG conformance.
        """;

    /// <summary>Runs the CLI.</summary>
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        try
        {
            if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
            {
                output.WriteLine(Usage);
                return args.Length == 0 ? QualityGateResult.InvalidUsage : QualityGateResult.Success;
            }

            if (args[0] == "--version")
            {
                output.WriteLine(ToolInfo.Toolkit.Version);
                return QualityGateResult.Success;
            }

            var parsed = Arguments.Parse(args.Skip(1).ToArray());
            return args[0] switch
            {
                "report" => Report(parsed, output, error),
                "baseline" => Baseline(parsed, output, error),
                "manual" => Manual(parsed, output, error),
                _ => throw new UsageException($"Unknown command '{args[0]}'."),
            };
        }
        catch (UsageException ex)
        {
            error.WriteLine("error: " + ex.Message);
            error.WriteLine("Run 'blazor-a11y --help' for usage.");
            return QualityGateResult.InvalidUsage;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or NotSupportedException)
        {
            error.WriteLine("error: " + ex.Message);
            return QualityGateResult.ExecutionFailed;
        }
    }

    private static int Report(Arguments args, TextWriter output, TextWriter error)
    {
        // Validate all options before reading or writing anything.
        var gateOptions = GateOptions(args);
        var formats = args.All("format").SelectMany(f => f.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToList();
        if (formats.Count == 0)
        {
            formats = ReportFormatters.All.Select(f => f.Name).ToList();
        }

        var formatters = formats.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => ReportFormatters.Find(name) ?? throw new UsageException($"Unknown format '{name}'. Use console, json, junit, sarif or html."))
            .ToArray();
        var sourceResolver = CreateSourceResolver(args.One("source-root"));

        var (audits, loadErrors) = ReportLoader.LoadAudits(Inputs(args));
        var checklists = args.All("manual-review").Select(ManualReviewEvaluator.Load).ToArray();
        foreach (var checklist in checklists)
        {
            foreach (var issue in ManualReviewEvaluator.Validate(checklist))
            {
                error.WriteLine($"warning: manual review '{checklist.Name}' item '{issue.ItemId}': {issue.Message}");
            }
        }

        var baselinePath = args.One("baseline");
        var baseline = baselinePath is null ? null : AccessibilityBaseline.Load(baselinePath);
        var report = AccessibilityReport.Create(audits, checklists, baseline);

        var outputDirectory = args.One("output") ?? "a11y-report";
        foreach (var candidate in formatters)
        {
            var formatter = candidate;
            if (formatter is SarifReportFormatter)
            {
                formatter = new SarifReportFormatter { SourceResolver = sourceResolver, FallbackUri = args.One("sarif-fallback-uri") };
            }

            if (formatter is ConsoleReportFormatter)
            {
                formatter.Write(report, output);
                continue;
            }

            var path = Path.Combine(outputDirectory, formatter.DefaultFileName);
            formatter.WriteToFile(report, path);
            output.WriteLine($"Wrote {formatter.Name} report: {path}");
        }

        var gate = QualityGate.Evaluate(report, gateOptions, loadErrors);
        foreach (var note in gate.Notes)
        {
            output.WriteLine("note: " + note);
        }

        foreach (var message in gate.Errors)
        {
            error.WriteLine("error: " + message);
        }

        foreach (var failure in gate.Failures.Take(50))
        {
            error.WriteLine("fail: " + failure);
        }

        if (gate.Failures.Count > 50)
        {
            error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"fail: ... and {gate.Failures.Count - 50} more"));
        }

        output.WriteLine(gate.ExitCode switch
        {
            QualityGateResult.Success => "Accessibility gate passed.",
            QualityGateResult.FindingsFailed => "Accessibility gate FAILED: findings exceed the configured policy.",
            _ => "Accessibility gate FAILED: audits did not run completely; results cannot be trusted.",
        });
        return gate.ExitCode;
    }

    private static int Baseline(Arguments args, TextWriter output, TextWriter error)
    {
        var path = args.One("baseline") ?? args.One("output") ?? throw new UsageException("baseline requires --baseline <file>.");
        var (audits, loadErrors) = ReportLoader.LoadAudits(Inputs(args));
        foreach (var message in loadErrors)
        {
            error.WriteLine("error: " + message);
        }

        var failed = audits.Where(a => a.Status != AuditStatus.Completed).ToArray();
        if (audits.Count == 0 || loadErrors.Count > 0 || (failed.Length > 0 && !args.Flag("allow-failed")))
        {
            foreach (var audit in failed)
            {
                error.WriteLine($"error: audit '{audit.Name}' {audit.Status.ToString().ToLowerInvariant()}; its findings are unknown.");
            }

            error.WriteLine("error: refusing to write a baseline from incomplete results (a baseline written now could hide findings). Use --allow-failed to override.");
            return QualityGateResult.ExecutionFailed;
        }

        var baseline = AccessibilityBaseline.FromAudits(audits);
        baseline.Save(path);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Wrote baseline with {baseline.Entries.Count} entr(ies) ({baseline.Entries.Sum(e => e.Count)} finding(s)) from {audits.Count} audit(s): {path}"));
        return QualityGateResult.Success;
    }

    private static int Manual(Arguments args, TextWriter output, TextWriter error)
    {
        var sub = args.Positional.FirstOrDefault() ?? throw new UsageException("manual requires a subcommand: init or validate.");
        switch (sub)
        {
            case "init":
                var name = args.One("name") ?? throw new UsageException("manual init requires --name.");
                var path = args.One("output") ?? throw new UsageException("manual init requires --output.");
                if (File.Exists(path))
                {
                    throw new UsageException($"'{path}' already exists; refusing to overwrite recorded results.");
                }

                ManualReviewEvaluator.Save(ManualReviewTemplates.Standard(name, args.One("scope")), path);
                output.WriteLine($"Wrote manual-review checklist: {path}");
                return QualityGateResult.Success;
            case "validate":
                var files = args.Positional.Skip(1).ToArray();
                if (files.Length == 0)
                {
                    throw new UsageException("manual validate requires at least one file.");
                }

                var invalid = false;
                foreach (var file in files)
                {
                    var checklist = ManualReviewEvaluator.Load(file);
                    var issues = ManualReviewEvaluator.Validate(checklist);
                    var coverage = ManualReviewEvaluator.Coverage([checklist]);
                    output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{file}: {coverage.Passed} passed, {coverage.Failed} failed, {coverage.NotApplicable} not applicable, {coverage.Outstanding} outstanding of {coverage.Total}"));
                    foreach (var issue in issues)
                    {
                        invalid = true;
                        error.WriteLine($"{file}: item '{issue.ItemId}': {issue.Message}");
                    }
                }

                return invalid ? QualityGateResult.FindingsFailed : QualityGateResult.Success;
            default:
                throw new UsageException($"Unknown manual subcommand '{sub}'.");
        }
    }

    private static QualityGateOptions GateOptions(Arguments args)
    {
        var failOn = args.One("fail-on") ?? "minor";
        AccessibilityImpact? impact = failOn.ToLowerInvariant() switch
        {
            "none" => null,
            "minor" => AccessibilityImpact.Minor,
            "moderate" => AccessibilityImpact.Moderate,
            "serious" => AccessibilityImpact.Serious,
            "critical" => AccessibilityImpact.Critical,
            _ => throw new UsageException($"Invalid --fail-on value '{failOn}'."),
        };
        return new QualityGateOptions
        {
            FailOnImpact = impact,
            FailOnPotentialIssues = args.Flag("fail-on-potential"),
            FailOnInconclusive = args.Flag("fail-on-inconclusive"),
            FailOnOutstandingManualReview = args.Flag("fail-on-manual-review"),
            Strict = args.Flag("strict"),
            AllowEmpty = args.Flag("allow-empty"),
        };
    }

    private static IReadOnlyList<string> Inputs(Arguments args)
    {
        var inputs = args.All("input");
        if (inputs.Count > 0)
        {
            return inputs;
        }

        return [Environment.GetEnvironmentVariable(AccessibilityResultStore.DirectoryEnvironmentVariable) is { Length: > 0 } fromEnvironment ? fromEnvironment : "a11y-results"];
    }

    private static Func<string, string?>? CreateSourceResolver(string? sourceRoot)
    {
        if (sourceRoot is null)
        {
            return null;
        }

        if (!Directory.Exists(sourceRoot))
        {
            throw new UsageException($"--source-root '{sourceRoot}' does not exist.");
        }

        var root = Path.GetFullPath(sourceRoot);

        // SARIF consumers resolve URIs against the repository root. When the source root is inside the working directory
        // (the usual case: the CLI runs from the repository root), paths are made relative to the working directory.
        var workingDirectory = Path.GetFullPath(Directory.GetCurrentDirectory());
        var baseDirectory = (root + Path.DirectorySeparatorChar).StartsWith(workingDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? workingDirectory
            : root;
        var files = Directory.EnumerateFiles(root, "*.razor", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .GroupBy(f => Path.GetFileNameWithoutExtension(f), StringComparer.Ordinal)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => Path.GetRelativePath(baseDirectory, g.Single()).Replace('\\', '/'), StringComparer.Ordinal);
        return type => files.GetValueOrDefault(FindingFormatter.ShortTypeName(type));
    }

    private sealed class UsageException(string message) : Exception(message);

    private sealed class Arguments
    {
        private static readonly HashSet<string> Flags = new(StringComparer.Ordinal) { "fail-on-potential", "fail-on-inconclusive", "fail-on-manual-review", "strict", "allow-empty", "allow-failed" };

        private readonly Dictionary<string, List<string>> _values = new(StringComparer.Ordinal);

        public List<string> Positional { get; } = [];

        public static Arguments Parse(string[] args)
        {
            var result = new Arguments();
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (!arg.StartsWith("--", StringComparison.Ordinal))
                {
                    result.Positional.Add(arg);
                    continue;
                }

                var name = arg[2..];
                string? value = null;
                var equals = name.IndexOf('=', StringComparison.Ordinal);
                if (equals >= 0)
                {
                    value = name[(equals + 1)..];
                    name = name[..equals];
                }

                if (Flags.Contains(name))
                {
                    result.Add(name, value ?? "true");
                    continue;
                }

                if (value is null)
                {
                    if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        throw new UsageException($"Option --{name} requires a value.");
                    }

                    value = args[++i];
                }

                result.Add(name, value);
            }

            return result;
        }

        public string? One(string name) => _values.TryGetValue(name, out var values) ? values[^1] : null;

        public IReadOnlyList<string> All(string name) => _values.TryGetValue(name, out var values) ? values : [];

        public bool Flag(string name) => One(name) is { } value && !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);

        private void Add(string name, string value)
        {
            var known = new[] { "input", "baseline", "manual-review", "format", "output", "fail-on", "source-root", "sarif-fallback-uri", "name", "scope" };
            if (!Flags.Contains(name) && !known.Contains(name))
            {
                throw new UsageException($"Unknown option --{name}.");
            }

            if (!_values.TryGetValue(name, out var values))
            {
                _values[name] = values = [];
            }

            values.Add(value);
        }
    }
}
