using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace BlazorAccessibilityToolkit.Analyzers.Tests;

public sealed class RazorAccessibilityAnalyzerTests
{
    private static string[] Ids(string markup) => RazorAccessibilityAnalyzer.Analyze(markup).Select(r => r.Descriptor.Id).ToArray();

    [Theory]
    [InlineData("<div @onclick=\"Open\">Open</div>", "BA11Y001")]
    [InlineData("<span role=\"button\" tabindex=\"0\" @onclick=\"Open\">Open</span>", "BA11Y001")] // no key handler
    [InlineData("<div role=\"button\" tabindex=\"-1\" @onclick=\"Open\" @onkeydown=\"Key\">Open</div>", "BA11Y001")]
    [InlineData("<a @onclick=\"Open\">Open</a>", "BA11Y001")]
    [InlineData("<img src=\"logo.png\">", "BA11Y002")]
    [InlineData("<button tabindex=\"3\">Save</button>", "BA11Y003")]
    [InlineData("<input aria-labeledby=\"x\" id=\"i\">", "BA11Y004")]
    [InlineData("<div role=\"buton\">x</div>", "BA11Y005")]
    [InlineData("<div aria-hidden=\"true\"><button>Close</button></div>", "BA11Y006")]
    [InlineData("<input type=\"text\">", "BA11Y007")]
    [InlineData("<InputText @bind-Value=\"model.Name\" />", "BA11Y007")]
    [InlineData("<button><i class=\"bi bi-x\"></i></button>", "BA11Y008")]
    [InlineData("<a href=\"/cart\"><span aria-hidden=\"true\">🛒</span></a>", "BA11Y008")]
    [InlineData("<h2></h2>", "BA11Y009")]
    [InlineData("<html><body></body></html>", "BA11Y010")]
    [InlineData("<iframe src=\"/map\"></iframe>", "BA11Y011")]
    [InlineData("<a href=\"#\" @onclick=\"Delete\">Delete</a>", "BA11Y012")]
    [InlineData("<div role=\"checkbox\" aria-checked=\"false\">Remember me</div>", "BA11Y013")]
    public void Reports_defects(string markup, string expected) => Assert.Contains(expected, Ids(markup));

    [Theory]
    [InlineData("<button type=\"button\" @onclick=\"Open\">Open</button>")]
    [InlineData("<div role=\"button\" tabindex=\"0\" @onclick=\"Open\" @onkeydown=\"Key\">Open</div>")]
    [InlineData("<div role=\"@Role\" tabindex=\"@Index\" @onclick=\"Open\" @onkeydown=\"Key\">Open</div>")]
    [InlineData("<button @onclick=\"Open\"><span @onclick=\"Inner\">Open</span></button>")]
    [InlineData("<label @onclick=\"Toggle\"><input type=\"checkbox\"> Remember</label>")]
    [InlineData("<label @onclick=\"Toggle\"><InputCheckbox @bind-Value=\"x\" /> Remember</label>")]
    [InlineData("<img src=\"a.png\" alt=\"\">")]
    [InlineData("<img src=\"a.png\" alt=\"@Alt\">")]
    [InlineData("<img src=\"a.png\" @attributes=\"Extra\">")]
    [InlineData("<input id=\"email\" type=\"email\">")]
    [InlineData("<label>Name <input></label>")]
    [InlineData("<InputText id=\"name\" @bind-Value=\"model.Name\" />")]
    [InlineData("<input aria-label=\"Search\">")]
    [InlineData("<input type=\"hidden\" name=\"token\">")]
    [InlineData("<button>@Label</button>")]
    [InlineData("<button>@(busy ? \"Saving\" : \"Save\")</button>")]
    [InlineData("<button aria-label=\"Close\"><i class=\"bi bi-x\"></i></button>")]
    [InlineData("<button><Icon Name=\"close\" /></button>")]
    [InlineData("<a href=\"/\"><img src=\"logo.png\" alt=\"Home\"></a>")]
    [InlineData("<h1>@Title</h1>")]
    [InlineData("<div aria-hidden=\"true\"><button tabindex=\"-1\">Close</button></div>")]
    [InlineData("<div hidden=\"@(!open)\"><button>Close</button></div>")]
    [InlineData("<html lang=\"en\"><body></body></html>")]
    [InlineData("<div role=\"tab\" tabindex=\"-1\">One</div>")]
    [InlineData("<Card @onclick=\"Open\" />")]
    [InlineData("<p>Contact me at someone@example.com</p>")]
    public void Accepts_accessible_markup(string markup) => Assert.Empty(Ids(markup));

    [Fact]
    public void Understands_razor_code_blocks_comments_and_expressions()
    {
        const string markup = """
            @page "/items"
            @using System.Linq
            @* <div @onclick="Commented"></div> *@
            <!-- <img src="commented.png"> -->
            <ul>
                @foreach (var item in Items)
                {
                    var label = item.Name;
                    if (item.Visible)
                    {
                        <li><button @onclick="() => Select(item)">@label</button></li>
                    }
                    else
                    {
                        <li><button @onclick="@(() => Select("x"))"></button></li>
                    }
                }
            </ul>
            <script>const html = '<img src="x.png">';</script>
            @code {
                private string Markup => "<div @onclick=\"Nope\"></div>";
                private void Select(object item) { }
            }
            """;
        var results = RazorAccessibilityAnalyzer.Analyze(markup).ToArray();

        // The routable page has no <PageTitle> (reported at the @page directive).
        Assert.Equal(0, Assert.Single(results, r => r.Descriptor.Id == "BA11Y031").Span.Start);
        var only = Assert.Single(results, r => r.Descriptor.Id != "BA11Y031");
        Assert.Equal("BA11Y008", only.Descriptor.Id);
        Assert.Equal(14, markup[..only.Span.Start].Count(c => c == '\n')); // the empty button in the else branch
    }

    [Fact]
    public void Razor_comments_ignore_rules_for_the_next_element_or_the_file()
    {
        const string markup = """
            @* blazor-a11y-ignore BA11Y002 *@
            <img src="decorative-but-legacy.png">
            <img src="still-reported.png">
            @* blazor-a11y-ignore *@
            <div @onclick="Open"><img src="x.png"></div>
            """;
        var results = RazorAccessibilityAnalyzer.Analyze(markup).ToArray();
        Assert.Equal(2, results.Length);
        Assert.All(results, r => Assert.Equal("BA11Y002", r.Descriptor.Id));

        Assert.Empty(Ids("@* blazor-a11y-ignore-file BA11Y002 *@\n<img src=\"a.png\">\n<img src=\"b.png\">"));
        Assert.Equal(["BA11Y001"], Ids("@* blazor-a11y-ignore-file BA11Y002 *@\n<div @onclick=\"X\"><img src=\"b.png\"></div>"));
    }

    [Fact]
    public void Unknown_aria_attributes_get_a_suggestion()
    {
        var result = Assert.Single(RazorAccessibilityAnalyzer.Analyze("<input id=\"x\" aria-labeledby=\"lbl\">"));
        Assert.Contains("aria-labelledby", string.Join(" ", result.Args), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_on_razor_additional_files_with_file_and_line()
    {
        var diagnostics = await RunAsync(("Pages/Home.razor", "<h1>Home</h1>\n<div @onclick=\"Go\">Go</div>"), ("Pages/Other.txt", "<img>"));
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("BA11Y001", diagnostic.Id);
        var span = diagnostic.Location.GetLineSpan();
        Assert.EndsWith("Home.razor", span.Path, StringComparison.Ordinal);
        Assert.Equal(1, span.StartLinePosition.Line);
        Assert.Equal(1, span.StartLinePosition.Character);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.StartsWith("https://", diagnostic.Descriptor.HelpLinkUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Editorconfig_can_turn_the_analyzer_off_for_files()
    {
        var diagnostics = await RunAsync(
            [("Legacy/Old.razor", "<img src=\"a.png\">"), ("New/Fresh.razor", "<img src=\"b.png\">")],
            "is_global = true\n[/src/Legacy/*.razor]\nblazor_a11y_analyzer.enabled = false\n");
        var diagnostic = Assert.Single(diagnostics);
        Assert.EndsWith("Fresh.razor", diagnostic.Location.GetLineSpan().Path, StringComparison.Ordinal);
    }

    [Fact]
    public void Demo_components_are_judged_correctly()
    {
        var root = Path.GetFullPath(Path.Combine(SourceDirectory(), "..", "..", "demo", "BlazorAccessibilityToolkit.Demo.Components"));
        var showcase = Path.GetFullPath(Path.Combine(root, "..", "BlazorAccessibilityToolkit.Demo.WebApp", "Components"));
        var accessible = Directory.GetFiles(Path.Combine(root, "Accessible"), "*.razor")
            .Concat(Directory.GetFiles(showcase, "*.razor", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles(Path.GetFullPath(Path.Combine(root, "..", "..", "src", "BlazorAccessibilityToolkit.Components")), "*.razor"))
            .ToArray();
        Assert.True(accessible.Length > 20);
        foreach (var file in accessible)
        {
            var findings = RazorAccessibilityAnalyzer.Analyze(File.ReadAllText(file)).Select(r => $"{r.Descriptor.Id} at {r.Span}").ToArray();
            Assert.True(findings.Length == 0, $"{Path.GetFileName(file)}: {string.Join(", ", findings)}");
        }

        Assert.Equal(["BA11Y001"], Ids(File.ReadAllText(Path.Combine(root, "Defects", "ClickableCard.razor"))));
        Assert.Equal(["BA11Y008"], Ids(File.ReadAllText(Path.Combine(root, "Defects", "UnlabelledIconButton.razor"))));
    }

    private static Task<ImmutableArray<Diagnostic>> RunAsync(params (string Path, string Text)[] files) => RunAsync(files, null);

    private static async Task<ImmutableArray<Diagnostic>> RunAsync((string Path, string Text)[] files, string? globalConfig)
    {
        var compilation = CSharpCompilation.Create("Test", [CSharpSyntaxTree.ParseText("class C { }")], options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var additional = files.Select(f => (AdditionalText)new InMemoryAdditionalText("/src/" + f.Path, f.Text)).ToImmutableArray();
        var optionsProvider = new GlobalConfigOptionsProvider(globalConfig, additional);
        var options = new AnalyzerOptions(additional, optionsProvider);
        var withAnalyzers = compilation.WithAnalyzers([new RazorAccessibilityAnalyzer()], options);
        return await withAnalyzers.GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
    }

    private static string SourceDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

    private sealed class InMemoryAdditionalText(string path, string text) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
    }

    // A minimal stand-in for .editorconfig: "[glob]" sections with key = value pairs, where the glob is a path prefix
    // followed by "*.razor".
    private sealed class GlobalConfigOptionsProvider(string? config, ImmutableArray<AdditionalText> files) : AnalyzerConfigOptionsProvider
    {
        private readonly Dictionary<string, Dictionary<string, string>> _byFile = Parse(config, files);

        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options([]);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new Options([]);

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new Options(_byFile.GetValueOrDefault(textFile.Path) ?? []);

        private static Dictionary<string, Dictionary<string, string>> Parse(string? config, ImmutableArray<AdditionalText> files)
        {
            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            string? section = null;
            foreach (var line in (config ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (line.StartsWith('['))
                {
                    section = line.Trim('[', ']').Replace("*.razor", string.Empty, StringComparison.Ordinal);
                    continue;
                }

                var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
                if (section is null || parts.Length != 2)
                {
                    continue;
                }

                foreach (var file in files.Where(f => f.Path.StartsWith(section, StringComparison.Ordinal)))
                {
                    if (!result.TryGetValue(file.Path, out var values))
                    {
                        result[file.Path] = values = new Dictionary<string, string>(StringComparer.Ordinal);
                    }

                    values[parts[0]] = parts[1];
                }
            }

            return result;
        }

        private sealed class Options(Dictionary<string, string> values) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value)
            {
                var found = values.TryGetValue(key, out var v);
                value = v ?? string.Empty;
                return found;
            }
        }
    }
}
