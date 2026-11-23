using System.IO.Pipelines;
using System.Text.Json;
using BlazorAccessibilityToolkit.Baselines;
using BlazorAccessibilityToolkit.Cli;
using BlazorAccessibilityToolkit.Cli.Mcp;
using BlazorAccessibilityToolkit.Reporting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace BlazorAccessibilityToolkit.Tests;

/// <summary>
/// <c>blazor-a11y mcp</c>, driven by the official MCP client over in-memory streams, as a coding agent would use it.
/// </summary>
public sealed class McpServerTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "a11y-mcp-" + Guid.NewGuid().ToString("N"));
    private readonly string _outside = Path.Combine(Path.GetTempPath(), "a11y-mcp-outside-" + Guid.NewGuid().ToString("N"));
    private readonly CancellationTokenSource _stop = new();
    private Task? _server;
    private McpClient? _client;

    private McpClient Client => _client ?? throw new InvalidOperationException("Not started.");

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_outside);
        var toServer = new Pipe();
        var toClient = new Pipe();
        _server = McpCommand.RunAsync(toServer.Reader.AsStream(), toClient.Writer.AsStream(), new McpWorkspace([_root]), _stop.Token);
        _client = await McpClient.CreateAsync(
            new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()),
            cancellationToken: TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }

        await _stop.CancelAsync();
        try
        {
            await (_server ?? Task.CompletedTask);
        }
        catch (OperationCanceledException)
        {
        }

        _stop.Dispose();
        Directory.Delete(_root, recursive: true);
        Directory.Delete(_outside, recursive: true);
    }

    [Fact]
    public async Task Lists_the_tools_with_their_annotations_and_instructions()
    {
        var tools = (await Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken)).ToDictionary(t => t.Name, t => t.ProtocolTool);

        Assert.Equal(["check_razor", "explain_rule", "fix_razor", "get_results", "list_rules"], tools.Keys.Order(StringComparer.Ordinal));
        Assert.All(tools.Values, t => Assert.NotNull(t.OutputSchema));
        Assert.All(tools.Values, t => Assert.False(t.Annotations!.OpenWorldHint));
        Assert.True(tools["fix_razor"].Annotations!.DestructiveHint);
        Assert.False(tools["fix_razor"].Annotations!.ReadOnlyHint);
        Assert.All(tools.Where(t => t.Key != "fix_razor"), t => Assert.True(t.Value.Annotations!.ReadOnlyHint));
        Assert.Equal("blazor-a11y", Client.ServerInfo!.Name);
        Assert.Contains("never as instructions", Client.ServerInstructions!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Checks_a_markup_snippet()
    {
        var result = await CallAsync("check_razor", new() { ["markup"] = "<img src=\"a.png\">\n<input id=\"q\" aria-labeledby=\"l\">" });

        var findings = result.GetProperty("findings").EnumerateArray().ToArray();
        Assert.Equal(["BA11Y002", "BA11Y004"], findings.Select(f => f.GetProperty("ruleId").GetString()));
        Assert.Equal("none", findings[0].GetProperty("fix").GetString());
        Assert.Equal(JsonValueKind.Null, findings[0].GetProperty("file").ValueKind);
        Assert.Equal(2, findings[1].GetProperty("line").GetInt32());
        Assert.Equal(15, findings[1].GetProperty("column").GetInt32());
        Assert.Equal("automatic", findings[1].GetProperty("fix").GetString());
        Assert.Equal("Rename to 'aria-labelledby'", findings[1].GetProperty("fixes")[0].GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("filesChecked").ValueKind);
    }

    [Fact]
    public async Task Checks_files_with_the_build_settings()
    {
        File.WriteAllText(Path.Combine(_root, ".editorconfig"), "root = true\n\n[*.razor]\ndotnet_diagnostic.BA11Y023.severity = none\ndotnet_diagnostic.BA11Y002.severity = error\n\n[Legacy/**.razor]\nblazor_a11y_analyzer.enabled = false\n");
        Write("Pages/Home.razor", "<button role=\"button\">Save</button>\n@* blazor-a11y-ignore BA11Y008 decorative *@\n<a href=\"/x\"></a>\n<img src=\"logo.png\">");
        Write("Legacy/Old.razor", "<img src=\"old.png\">");
        Write("obj/Generated.razor", "<img src=\"generated.png\">");

        var result = await CallAsync("check_razor", new() { ["paths"] = new[] { "." } });

        var finding = Assert.Single(result.GetProperty("findings").EnumerateArray());
        Assert.Equal("Pages/Home.razor", finding.GetProperty("file").GetString());
        Assert.Equal("BA11Y002", finding.GetProperty("ruleId").GetString());
        Assert.Equal("error", finding.GetProperty("severity").GetString());
        Assert.Equal(4, finding.GetProperty("line").GetInt32());
        Assert.Equal(1, result.GetProperty("filesChecked").GetInt32());

        var limited = await CallAsync("check_razor", new() { ["markup"] = "<img><img><img>", ["limit"] = 2 });
        Assert.Equal(3, limited.GetProperty("total").GetInt32());
        Assert.Equal(2, limited.GetProperty("findings").GetArrayLength());
        Assert.Contains("first 2 of 3", limited.GetProperty("truncated").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fixes_files_and_leaves_choices_alone()
    {
        const string markup = "<input id=\"q\" aria-labeledby=\"l\">\r\n<input id=\"p\" type=\"password\" autocomplete=\"off\">\r\n";
        var file = Write("Pages/Login.razor", markup);

        var dryRun = await CallAsync("fix_razor", new() { ["paths"] = new[] { "Pages" }, ["dryRun"] = true });
        Assert.Equal(markup, File.ReadAllText(file));
        Assert.Equal(["Pages/Login.razor"], dryRun.GetProperty("changedFiles").EnumerateArray().Select(e => e.GetString()));

        var result = await CallAsync("fix_razor", new() { ["paths"] = new[] { "Pages/Login.razor" } });

        Assert.Equal("<input id=\"q\" aria-labelledby=\"l\">\r\n<input id=\"p\" type=\"password\" autocomplete=\"off\">\r\n", File.ReadAllText(file));
        var applied = Assert.Single(result.GetProperty("applied").EnumerateArray());
        Assert.Equal("BA11Y004", applied.GetProperty("ruleId").GetString());
        var choice = Assert.Single(result.GetProperty("needChoice").EnumerateArray());
        Assert.Equal("BA11Y033", choice.GetProperty("ruleId").GetString());
        Assert.Contains("current-password", choice.GetProperty("title").GetString(), StringComparison.Ordinal);

        var error = await CallForErrorAsync("fix_razor", new() { ["paths"] = new[] { "Pages" }, ["rules"] = new[] { "BA11Y002" } });
        Assert.Contains("BA11Y002 has no automatic fix", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refuses_paths_outside_the_workspace()
    {
        var outsideFile = Path.Combine(_outside, "Secret.razor");
        const string markup = "<input aria-labeledby=\"l\">";
        File.WriteAllText(outsideFile, markup);

        foreach (var path in new[] { outsideFile, _outside, Path.Combine("..", Path.GetFileName(_outside), "Secret.razor") })
        {
            var error = await CallForErrorAsync("fix_razor", new() { ["paths"] = new[] { path } });
            Assert.Contains("outside the folders this server may use", error, StringComparison.Ordinal);
        }

        Assert.Contains("outside the folders", await CallForErrorAsync("check_razor", new() { ["paths"] = new[] { outsideFile } }), StringComparison.Ordinal);
        Assert.Contains("outside the folders", await CallForErrorAsync("get_results", new() { ["inputs"] = new[] { _outside } }), StringComparison.Ordinal);
        Assert.Equal(markup, File.ReadAllText(outsideFile));
        Assert.Contains("is not a .razor or .cshtml file", await CallForErrorAsync("fix_razor", new() { ["paths"] = new[] { Write("Program.cs", "class P;") } }), StringComparison.Ordinal);
        Assert.Contains("Pass the files or folders", await CallForErrorAsync("fix_razor", new() { ["paths"] = Array.Empty<string>() }), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reads_test_results_with_filters_paging_and_a_baseline()
    {
        var store = new AccessibilityResultStore(Path.Combine(_root, "a11y-results"));
        var home = TestData.Audit(name: "Home page", findings: [TestData.Finding(), TestData.Finding(selector: "button.other")]);
        store.Save(home);
        AccessibilityBaseline.FromAudits([home]).Save(Path.Combine(_root, "a11y-baseline.json"));
        store.Save(TestData.Audit(name: "Search form", findings: [TestData.Finding(ruleId: "label", selector: "input", html: "<input " + new string('x', 600) + ">")]));
        store.Save(TestData.FailedAudit());

        var result = await CallAsync("get_results", new() { ["baseline"] = "a11y-baseline.json" });

        Assert.Equal(3, result.GetProperty("audits").GetInt32());
        Assert.Equal(3, result.GetProperty("violations").GetInt32());
        Assert.False(result.GetProperty("gate").GetProperty("passed").GetBoolean());
        Assert.Contains("Broken", Assert.Single(result.GetProperty("failedAudits").EnumerateArray()).GetString(), StringComparison.Ordinal);
        Assert.Equal(1, result.GetProperty("baseline").GetProperty("new").GetInt32());
        var findings = result.GetProperty("findings").EnumerateArray().ToArray();
        Assert.Equal(3, findings.Length);
        var label = Assert.Single(findings, f => f.GetProperty("ruleId").GetString() == "label");
        Assert.True(label.GetProperty("new").GetBoolean());
        Assert.EndsWith("…", label.GetProperty("html").GetString(), StringComparison.Ordinal);
        Assert.True(label.GetProperty("html").GetString()!.Length <= 401);
        Assert.All(findings.Where(f => f.GetProperty("ruleId").GetString() == "button-name"), f => Assert.False(f.GetProperty("new").GetBoolean()));

        var page = await CallAsync("get_results", new() { ["ruleId"] = "button-name", ["limit"] = 1 });
        Assert.Equal(2, page.GetProperty("total").GetInt32());
        Assert.Equal(1, page.GetProperty("nextOffset").GetInt32());
        var last = await CallAsync("get_results", new() { ["ruleId"] = "button-name", ["limit"] = 1, ["offset"] = 1 });
        Assert.Equal("button.other", Assert.Single(last.GetProperty("findings").EnumerateArray()).GetProperty("selector").GetString());
        Assert.Equal(JsonValueKind.Null, last.GetProperty("nextOffset").ValueKind);

        var search = await CallAsync("get_results", new() { ["where"] = "search" });
        Assert.Equal("Search form", Assert.Single(search.GetProperty("findings").EnumerateArray()).GetProperty("audit").GetString());
        Assert.Contains("Unknown status", await CallForErrorAsync("get_results", new() { ["status"] = "bad" }), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Says_when_there_are_no_results()
    {
        var result = await CallAsync("get_results", []);

        Assert.Equal(0, result.GetProperty("audits").GetInt32());
        Assert.False(result.GetProperty("gate").GetProperty("passed").GetBoolean());
        Assert.Contains("Run the app's accessibility tests", result.GetProperty("note").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Explains_rules_and_success_criteria()
    {
        var analyzer = await CallAsync("explain_rule", new() { ["id"] = "ba11y004" });
        Assert.Equal("BA11Y004", analyzer.GetProperty("id").GetString());
        Assert.Equal("razor-analyzer", analyzer.GetProperty("source").GetString());
        Assert.Equal("Name, Role, Value", analyzer.GetProperty("wcag")[0].GetProperty("title").GetString());
        Assert.Contains("fix_razor", analyzer.GetProperty("automaticFix").GetString(), StringComparison.Ordinal);
        Assert.EndsWith("analyzers.md#ba11y004", analyzer.GetProperty("helpUrl").GetString(), StringComparison.Ordinal);

        var toolkit = await CallAsync("explain_rule", new() { ["id"] = "blazor-click-handler-semantics" });
        Assert.Equal("toolkit", toolkit.GetProperty("source").GetString());
        Assert.Contains("<button>", toolkit.GetProperty("remediation").GetString(), StringComparison.Ordinal);

        var criterion = await CallAsync("explain_rule", new() { ["id"] = "1.1.1" });
        Assert.Equal("wcag", criterion.GetProperty("source").GetString());
        Assert.Contains("BA11Y002", criterion.GetProperty("checkedBy").EnumerateArray().Select(e => e.GetString()));

        var axe = await CallAsync("explain_rule", new() { ["id"] = "color-contrast" });
        Assert.Equal("https://dequeuniversity.com/rules/axe/4.14/color-contrast", axe.GetProperty("helpUrl").GetString());

        new AccessibilityResultStore(Path.Combine(_root, "a11y-results")).Save(TestData.Audit(findings: TestData.Finding()));
        var fromResults = await CallAsync("explain_rule", new() { ["id"] = "button-name" });
        Assert.Equal("Add text", fromResults.GetProperty("remediation").GetString());
        Assert.Equal("4.1.2", fromResults.GetProperty("wcag")[0].GetProperty("id").GetString());

        Assert.Contains("is not a rule id", await CallForErrorAsync("explain_rule", new() { ["id"] = "<script>" }), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lists_rules_by_source()
    {
        var all = await CallAsync("list_rules", []);
        var analyzer = await CallAsync("list_rules", new() { ["source"] = "analyzer" });

        var ids = analyzer.GetProperty("rules").EnumerateArray().Select(r => r.GetProperty("id").GetString()!).ToArray();
        Assert.All(ids, id => Assert.StartsWith("BA11Y", id, StringComparison.Ordinal));
        Assert.Contains(analyzer.GetProperty("rules").EnumerateArray(), r => r.GetProperty("id").GetString() == "BA11Y004" && r.GetProperty("automaticFix").GetBoolean());
        Assert.True(all.GetProperty("rules").GetArrayLength() > ids.Length);
        Assert.Contains("Unknown source", await CallForErrorAsync("list_rules", new() { ["source"] = "eslint" }), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Marks_each_occurrence_new_or_existing_as_the_baseline_counts_them()
    {
        // Selectors that differ only in long numbers share a fingerprint; the baseline allows one occurrence.
        var store = new AccessibilityResultStore(Path.Combine(_root, "a11y-results"));
        var before = TestData.Audit(findings: [TestData.Finding(selector: "#row-1111111")]);
        AccessibilityBaseline.FromAudits([before]).Save(Path.Combine(_root, "a11y-baseline.json"));
        store.Save(TestData.Audit(findings: [TestData.Finding(selector: "#row-1111111"), TestData.Finding(selector: "#row-1222222"), TestData.Finding(selector: "#row-1333333")]));

        var result = await CallAsync("get_results", new() { ["baseline"] = "a11y-baseline.json" });

        Assert.Equal(2, result.GetProperty("baseline").GetProperty("new").GetInt32());
        Assert.Equal(2, result.GetProperty("findings").EnumerateArray().Count(f => f.GetProperty("new").GetBoolean()));
    }

    [Fact]
    public async Task Reports_the_rule_default_severity_and_checks_files_that_are_not_utf8()
    {
        var info = Assert.Single((await CallAsync("check_razor", new() { ["markup"] = "<button role=\"button\">Save</button>" })).GetProperty("findings").EnumerateArray());
        Assert.Equal("BA11Y023", info.GetProperty("ruleId").GetString());
        Assert.Equal("info", info.GetProperty("severity").GetString());

        // The compiler reads such a file too; it is only refused for fixing, where it must be written back unchanged.
        File.WriteAllBytes(Path.Combine(_root, "Legacy.razor"), [.. System.Text.Encoding.ASCII.GetBytes("<p>Caf"), 0xE9, .. System.Text.Encoding.ASCII.GetBytes("</p>\n<img src=\"a.png\">")]);
        var result = await CallAsync("check_razor", new() { ["paths"] = new[] { "Legacy.razor" }, ["rules"] = new string?[] { null, "BA11Y002" } });
        Assert.Equal(2, Assert.Single(result.GetProperty("findings").EnumerateArray()).GetProperty("line").GetInt32());
    }

    [Fact]
    public async Task Explains_why_a_call_failed()
    {
        Assert.Contains("nope.json", await CallForErrorAsync("get_results", new() { ["baseline"] = "nope.json" }), StringComparison.Ordinal);
        Write("bad-baseline.json", "{ not json");
        Assert.DoesNotContain("An error occurred invoking 'get_results'.", await CallForErrorAsync("get_results", new() { ["baseline"] = "bad-baseline.json" }), StringComparison.Ordinal);
        Assert.Contains("BA11Y999 is not a Razor analyzer rule", await CallForErrorAsync("explain_rule", new() { ["id"] = "BA11Y999" }), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fix_reports_files_it_could_not_write_and_keeps_going()
    {
        const string markup = "<input aria-labeledby=\"l\">";
        var writable = Write("Fix/A.razor", markup);
        var locked = Write("Fix/B.razor", markup);
        File.SetAttributes(locked, FileAttributes.ReadOnly);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead);
        }

        try
        {
            if (!OperatingSystem.IsWindows() && CanWrite(locked))
            {
                Assert.Skip("The tests run with permission to write read-only files.");
            }

            var result = await CallAsync("fix_razor", new() { ["paths"] = new[] { "Fix" } });

            Assert.Equal(["Fix/A.razor"], result.GetProperty("changedFiles").EnumerateArray().Select(e => e.GetString()));
            Assert.StartsWith("Fix/B.razor: not changed", Assert.Single(result.GetProperty("skipped").EnumerateArray()).GetString(), StringComparison.Ordinal);
            Assert.NotEqual(markup, File.ReadAllText(writable));
            Assert.Equal(markup, File.ReadAllText(locked));
        }
        finally
        {
            File.SetAttributes(locked, FileAttributes.Normal);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
    }

    [Fact]
    public async Task Does_not_follow_links_out_of_the_workspace_or_in_circles()
    {
        File.WriteAllText(Path.Combine(_outside, "Secret.razor"), "<input aria-labeledby=\"l\">");
        Write("Pages/Home.razor", "<img src=\"a.png\">");
        var outLink = Path.Combine(_root, "Pages", "outside");
        var loop = Path.Combine(_root, "Pages", "loop");
        if (!TryCreateDirectoryLink(outLink, _outside) || !TryCreateDirectoryLink(loop, _root))
        {
            Assert.Skip("This system does not allow creating symbolic links or junctions.");
        }

        Assert.Contains("outside the folders", await CallForErrorAsync("fix_razor", new() { ["paths"] = new[] { "Pages/outside/Secret.razor" } }), StringComparison.Ordinal);
        Assert.Contains("outside the folders", await CallForErrorAsync("check_razor", new() { ["paths"] = new[] { "Pages/outside" } }), StringComparison.Ordinal);

        // Searching a folder neither leaves the workspace nor walks the loop.
        var result = await CallAsync("check_razor", new() { ["paths"] = new[] { "." } });
        Assert.Equal(1, result.GetProperty("filesChecked").GetInt32());
        Assert.Equal(2, result.GetProperty("skipped").GetArrayLength());

        // A link that stays inside the workspace is fine when named directly.
        Assert.Equal(1, (await CallAsync("check_razor", new() { ["paths"] = new[] { "Pages/loop/Pages/Home.razor" } })).GetProperty("filesChecked").GetInt32());
    }

    [Theory]
    [InlineData("mcp", "extra")]
    [InlineData("mcp", "--root", "does-not-exist-anywhere")]
    [InlineData("mcp", "--format", "json")]
    public void Mcp_usage_errors_return_exit_code_3(params string[] args) =>
        Assert.Equal(QualityGateResult.InvalidUsage, CliApp.Run(args, TextWriter.Null, TextWriter.Null));

    [Fact]
    public void Workspace_resolves_paths_inside_its_roots_only()
    {
        var workspace = new McpWorkspace([_root, _outside]);

        Assert.Equal(Path.Combine(_root, "Pages", "Home.razor"), workspace.Resolve("Pages/Home.razor"));
        Assert.Equal(_outside, workspace.Resolve(_outside + Path.DirectorySeparatorChar));
        Assert.Equal("Pages/Home.razor", workspace.Display(Path.Combine(_root, "Pages", "Home.razor")));
        Assert.Throws<ModelContextProtocol.McpException>(() => workspace.Resolve(_root + "-sibling"));
        Assert.Throws<ModelContextProtocol.McpException>(() => workspace.Resolve(Path.Combine(_root, "..")));
        Assert.Throws<ModelContextProtocol.McpException>(() => workspace.Resolve(" "));
    }

    [Fact]
    public void Package_describes_the_server_for_NuGet_org()
    {
        // NuGet.org reads .mcp/server.json (schema: static.modelcontextprotocol.io, 2025-10-17) to offer client configuration.
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(SourceDirectory(), "..", "..", "src", "BlazorAccessibilityToolkit.Cli", ".mcp", "server.json")));
        var root = json.RootElement;
        Assert.InRange(root.GetProperty("description").GetString()!.Length, 1, 100);
        Assert.Equal("__PACKAGE_VERSION__", root.GetProperty("version").GetString());
        var package = root.GetProperty("packages")[0];
        Assert.Equal("nuget", package.GetProperty("registryType").GetString());
        Assert.Equal("BlazorAccessibilityToolkit.Cli", package.GetProperty("identifier").GetString());
        Assert.Equal("__PACKAGE_VERSION__", package.GetProperty("version").GetString());
        Assert.Equal("stdio", package.GetProperty("transport").GetProperty("type").GetString());
        var argument = Assert.Single(package.GetProperty("packageArguments").EnumerateArray());
        Assert.Equal("positional", argument.GetProperty("type").GetString());
        Assert.Equal("mcp", argument.GetProperty("value").GetString());
    }

    private static string SourceDirectory([System.Runtime.CompilerServices.CallerFilePath] string source = "") => Path.GetDirectoryName(source)!;

    private static bool CanWrite(string path)
    {
        try
        {
            using var stream = File.OpenWrite(path);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    // A symbolic link where allowed; on Windows without developer mode, a junction, which needs no privilege.
    private static bool TryCreateDirectoryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }
        }

        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", ["/c", "mklink", "/J", link, target])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        process.WaitForExit();
        return process.ExitCode == 0 && Directory.Exists(link);
    }

    private string Write(string relativePath, string content)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private async Task<JsonElement> CallAsync(string tool, Dictionary<string, object?> arguments)
    {
        var result = await Client.CallToolAsync(tool, arguments, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsError is not true, Text(result));

        // The text content carries the same JSON as the structured content, for clients that read only text.
        var structured = JsonSerializer.SerializeToElement(result.StructuredContent);
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(Text(result)), System.Text.Json.Nodes.JsonNode.Parse(structured.GetRawText())));
        return structured;
    }

    private async Task<string> CallForErrorAsync(string tool, Dictionary<string, object?> arguments)
    {
        var result = await Client.CallToolAsync(tool, arguments, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsError, Text(result));
        return Text(result);
    }

    private static string Text(CallToolResult result) => string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));
}
