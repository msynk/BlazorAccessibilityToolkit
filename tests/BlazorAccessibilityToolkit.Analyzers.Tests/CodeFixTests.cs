using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace BlazorAccessibilityToolkit.Analyzers.Tests;

public sealed class CodeFixTests
{
    // Applies the fix (by index, for rules that offer choices) of the only finding with the rule id.
    private static string Fix(string markup, string id, int index = 0)
    {
        var fixes = FixesOf(markup, id);
        Assert.True(fixes.Count > index, $"{id} offers no fix #{index} for: {markup}");
        var text = MarkupFixProperties.Apply(SourceText.From(markup), fixes[index]);
        Assert.NotNull(text);
        return text.ToString();
    }

    private static IReadOnlyList<MarkupFix> FixesOf(string markup, string id)
    {
        var finding = Assert.Single(RazorAccessibilityAnalyzer.Analyze(markup), r => r.Descriptor.Id == id);
        return RazorAccessibilityAnalyzer.FixesFor(finding.Descriptor, finding.Element, finding.Span, finding.Args, markup);
    }

    [Theory]
    [InlineData("<div role=\"region\" aria-label=\"Log\" tabindex=\"3\">x</div>", "BA11Y003", "<div role=\"region\" aria-label=\"Log\" tabindex=\"0\">x</div>")]
    [InlineData("<input id=\"q\" aria-labeledby=\"l\">", "BA11Y004", "<input id=\"q\" aria-labelledby=\"l\">")]
    [InlineData("<div aria-discribedby=\"h\">x</div>", "BA11Y004", "<div aria-describedby=\"h\">x</div>")]
    [InlineData("<button role=\"button\">Save</button>", "BA11Y023", "<button>Save</button>")]
    [InlineData("<button class=\"a\" role='button' type=\"button\">Save</button>", "BA11Y023", "<button class=\"a\" type=\"button\">Save</button>")]
    [InlineData("<button\n    role=\"button\"\n    class=\"a\">Save</button>", "BA11Y023", "<button\n    class=\"a\">Save</button>")]
    [InlineData("<p lang=\"en_US\">Hello</p>", "BA11Y028", "<p lang=\"en-US\">Hello</p>")]
    [InlineData("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1, maximum-scale=1, user-scalable=no\">", "BA11Y029", "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")]
    [InlineData("<meta name=\"viewport\" content=\"user-scalable=0\">", "BA11Y029", "<meta name=\"viewport\" content=\"width=device-width\">")]
    [InlineData("<input id=\"c\" @onpaste:preventDefault>", "BA11Y033", "<input id=\"c\">")]
    [InlineData("<input id=\"c\" onpaste=\"return false;\">", "BA11Y033", "<input id=\"c\">")]
    [InlineData("<button aria-expanded=\"@open\">Menu</button>\n@code { bool open; }", "BA11Y035", "<button aria-expanded=\"@(open ? \"true\" : \"false\")\">Menu</button>\n@code { bool open; }")]
    [InlineData("<button aria-pressed=\"@(!muted)\">Mute</button>\n@code { bool muted; }", "BA11Y035", "<button aria-pressed=\"@(!muted ? \"true\" : \"false\")\">Mute</button>\n@code { bool muted; }")]
    [InlineData("<button aria-expanded=@open>Menu</button>\n@code { bool open; }", "BA11Y035", "<button aria-expanded=\"@(open ? \"true\" : \"false\")\">Menu</button>\n@code { bool open; }")]
    [InlineData("@page \"/orders\"\n<a href=\"#list\">Skip to the list</a>", "BA11Y038", "@page \"/orders\"\n<a href=\"/orders#list\">Skip to the list</a>")]
    public void Applies_the_fix(string markup, string id, string expected) => Assert.Equal(expected, Fix(markup, id));

    [Fact]
    public void A_password_field_offers_both_autocomplete_tokens()
    {
        const string markup = "<input id=\"p\" type=\"password\" autocomplete=\"off\">";

        Assert.Equal(["Change to autocomplete=\"current-password\"", "Change to autocomplete=\"new-password\""], FixesOf(markup, "BA11Y033").Select(f => f.Title));
        Assert.Equal("<input id=\"p\" type=\"password\" autocomplete=\"new-password\">", Fix(markup, "BA11Y033", 1));
    }

    [Theory]
    [InlineData("<div aria-zzzzzz=\"x\">x</div>", "BA11Y004")] // no close attribute name
    [InlineData("<input id=\"q\" aria-labeledby=\"l\" aria-labelledby=\"m\">", "BA11Y004")] // the suggestion is already there
    [InlineData("<html lang=\"english\"></html>", "BA11Y028")] // not a mistyped tag
    [InlineData("<input id=\"c\" onpaste=\"if (locked) return false\">", "BA11Y033")] // more than blocking
    [InlineData("@page \"/orders/{id:int}\"\n<a href=\"#list\">Skip</a>", "BA11Y038")] // the path depends on a parameter
    [InlineData("@page \"/orders\"\n@page \"/sales\"\n<a href=\"#list\">Skip</a>", "BA11Y038")] // two routes
    [InlineData("<a href=\"#list\">Skip</a>", "BA11Y038")] // not a page
    public void Offers_no_fix_when_the_right_edit_is_not_certain(string markup, string id) => Assert.Empty(FixesOf(markup, id));

    [Fact]
    public void A_fix_is_not_applied_after_the_text_changed()
    {
        const string markup = "<button role=\"button\">Save</button>";
        var fix = Assert.Single(FixesOf(markup, "BA11Y023"));

        Assert.Null(MarkupFixProperties.Apply(SourceText.From("<button  role=\"button\">Save</button>"), fix));
    }

    [Fact]
    public void Fixes_round_trip_through_the_diagnostic_properties()
    {
        const string markup = "<input id=\"p\" type=\"password\" autocomplete=\"off\">";
        var fixes = FixesOf(markup, "BA11Y033");

        var decoded = MarkupFixProperties.Decode(MarkupFixProperties.Encode(fixes));

        Assert.Equal(fixes.Select(f => (f.Title, f.EquivalenceKey, f.Span, f.Original, f.NewText)), decoded.Select(f => (f.Title, f.EquivalenceKey, f.Span, f.Original, f.NewText)));
    }

    [Fact]
    public void Every_rule_that_produces_a_fix_is_declared_fixable()
    {
        string[] samples =
        [
            "<div tabindex=\"3\" role=\"region\" aria-label=\"x\">x</div>", "<input id=\"q\" aria-labeledby=\"l\">", "<button role=\"button\">Save</button>",
            "<p lang=\"en_US\">x</p>", "<meta name=\"viewport\" content=\"user-scalable=no\">", "<input id=\"c\" @onpaste:preventDefault>",
            "<button aria-expanded=\"@open\">M</button>\n@code { bool open; }", "@page \"/o\"\n<a href=\"#l\">Skip</a>",
        ];

        var withFixes = samples.SelectMany(markup => RazorAccessibilityAnalyzer.Analyze(markup)
            .Where(r => RazorAccessibilityAnalyzer.FixesFor(r.Descriptor, r.Element, r.Span, r.Args, markup).Count > 0)
            .Select(r => r.Descriptor.Id)).Distinct().OrderBy(id => id, StringComparer.Ordinal);

        Assert.Equal(MarkupFixProperties.FixableIds.OrderBy(id => id, StringComparer.Ordinal), withFixes);
    }

    [Fact]
    public async Task The_code_fix_provider_edits_the_razor_file()
    {
        var (document, diagnostics) = await AnalyzeAsync(("Pages/Orders.razor", "<input id=\"q\" aria-labeledby=\"l\">\n<button role=\"button\">Save</button>"));
        var diagnostic = Assert.Single(diagnostics, d => d.Id == "BA11Y004");

        var actions = await RegisterAsync(document[0], diagnostic);

        var action = Assert.Single(actions);
        Assert.Equal("Rename to 'aria-labelledby'", action.Title);
        Assert.Equal("<input id=\"q\" aria-labelledby=\"l\">\n<button role=\"button\">Save</button>", await ApplyAsync(action, document[0]));
    }

    [Fact]
    public async Task The_code_fix_provider_offers_nothing_for_findings_without_a_fix()
    {
        var (document, diagnostics) = await AnalyzeAsync(("Pages/Logo.razor", "<img src=\"logo.png\">"));

        Assert.Empty(await RegisterAsync(document[0], Assert.Single(diagnostics)));
    }

    [Fact]
    public async Task Fix_all_applies_every_fix_of_the_same_kind_in_the_project()
    {
        var (documents, diagnostics) = await AnalyzeAsync(
            ("Pages/A.razor", "<button role=\"button\">Save</button> <a href=\"/\" role=\"link\">Home</a>"),
            ("Pages/B.razor", "<ul><li role=\"listitem\">x</li></ul> <input id=\"q\" aria-labeledby=\"l\">"));
        var provider = new RazorAccessibilityCodeFixProvider();
        var first = Assert.Single(await RegisterAsync(documents[0], diagnostics.First(d => d.Id == "BA11Y023")));
        var context = new FixAllContext(
            documents[0].Project, provider, FixAllScope.Project, first.EquivalenceKey, provider.FixableDiagnosticIds, new FixedDiagnostics(diagnostics), TestContext.Current.CancellationToken);

        var fixAll = await provider.GetFixAllProvider().GetFixAsync(context);

        Assert.NotNull(fixAll);
        Assert.Equal("<button>Save</button> <a href=\"/\">Home</a>", await ApplyAsync(fixAll, documents[0]));
        Assert.Equal("<ul><li>x</li></ul> <input id=\"q\" aria-labeledby=\"l\">", await ApplyAsync(fixAll, documents[1]));
    }

    private static async Task<(TextDocument[] Documents, ImmutableArray<Diagnostic> Diagnostics)> AnalyzeAsync(params (string Path, string Text)[] files)
    {
        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("App", LanguageNames.CSharp).AddDocument("C.cs", "class C { }").Project;
        var ids = new List<DocumentId>();
        foreach (var (path, text) in files)
        {
            var document = project.AddAdditionalDocument(Path.GetFileName(path), SourceText.From(text), filePath: "/src/" + path);
            ids.Add(document.Id);
            project = document.Project;
        }

        var compilation = (await project.GetCompilationAsync(TestContext.Current.CancellationToken))!;
        var additional = project.AdditionalDocuments.Select(d => (AdditionalText)new DocumentText(d)).ToImmutableArray();
        var diagnostics = await compilation.WithAnalyzers([new RazorAccessibilityAnalyzer()], new AnalyzerOptions(additional)).GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
        return (ids.Select(id => project.GetAdditionalDocument(id)!).ToArray(), diagnostics);
    }

    private static async Task<List<CodeAction>> RegisterAsync(TextDocument document, Diagnostic diagnostic)
    {
        var actions = new List<CodeAction>();
        var context = new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), TestContext.Current.CancellationToken);
        await new RazorAccessibilityCodeFixProvider().RegisterCodeFixesAsync(context);
        return actions;
    }

    private static async Task<string> ApplyAsync(CodeAction action, TextDocument document)
    {
        var operations = await action.GetOperationsAsync(TestContext.Current.CancellationToken);
        var solution = Assert.Single(operations.OfType<ApplyChangesOperation>()).ChangedSolution;
        return (await solution.GetAdditionalDocument(document.Id)!.GetTextAsync(TestContext.Current.CancellationToken)).ToString();
    }

    private sealed class DocumentText(TextDocument document) : AdditionalText
    {
        public override string Path { get; } = document.FilePath!;

        public override SourceText GetText(CancellationToken cancellationToken = default) => document.GetTextAsync(cancellationToken).Result;
    }

    private sealed class FixedDiagnostics(ImmutableArray<Diagnostic> diagnostics) : FixAllContext.DiagnosticProvider
    {
        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken) => Task.FromResult<IEnumerable<Diagnostic>>(diagnostics);

        public override Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken) => Task.FromResult(Enumerable.Empty<Diagnostic>());

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken) => Task.FromResult<IEnumerable<Diagnostic>>(diagnostics);
    }

    [Fact]
    public void The_fixer_applies_overlapping_fixes_in_later_passes()
    {
        // Both fixes edit the same element; the second pass picks up the one skipped in the first.
        const string markup = "<button role=\"button\" aria-labeledby=\"t\" tabindex=\"2\">Menu</button>";

        var (text, applied, needChoice) = RazorFixer.Fix(markup);

        Assert.Equal("<button aria-labelledby=\"t\" tabindex=\"0\">Menu</button>", text);
        Assert.Equal(["BA11Y003", "BA11Y004", "BA11Y023"], applied.Select(f => f.RuleId).OrderBy(id => id, StringComparer.Ordinal));
        Assert.Empty(needChoice);
    }

    [Fact]
    public void The_fixer_leaves_choices_and_other_rules_alone()
    {
        const string markup = "<input id=\"p\" type=\"password\" autocomplete=\"off\" aria-labeledby=\"l\">";

        var (text, applied, needChoice) = RazorFixer.Fix(markup, ["BA11Y033"]);

        Assert.Equal(markup, text);
        Assert.Empty(applied);
        var choice = Assert.Single(needChoice);
        Assert.Equal((1, 31), (choice.Line, choice.Column)); // the autocomplete attribute
    }

    [Fact]
    public void Parentheses_that_do_not_enclose_the_whole_value_are_not_a_bool_expression()
    {
        // "@(IsA) && (IsB)" renders the value of IsA followed by the text " && (IsB)"; a fix would produce broken Razor.
        const string markup = "<button aria-expanded=\"@(IsA) && (IsB)\">Menu</button>\n@code { bool IsA; bool IsB; }";

        Assert.DoesNotContain(RazorAccessibilityAnalyzer.Analyze(markup), r => r.Descriptor.Id == "BA11Y035");
        Assert.Equal("<button aria-expanded=\"@(IsA && (IsB) ? \"true\" : \"false\")\">Menu</button>\n@code { bool IsA; bool IsB; }",
            Fix("<button aria-expanded=\"@(IsA && (IsB))\">Menu</button>\n@code { bool IsA; bool IsB; }", "BA11Y035"));
    }

    [Theory]
    [InlineData("<aside role=\"complementary\">Related</aside>", true)]
    [InlineData("<main><aside role=\"complementary\">Related</aside></main>", true)]
    [InlineData("<article><aside role=\"complementary\">Related</aside></article>", false)] // generic without the role
    [InlineData("<section><aside role=\"complementary\">Related</aside></section>", false)]
    [InlineData("<section><aside aria-label=\"Related\" role=\"complementary\">Related</aside></section>", true)]
    public void An_aside_in_sectioning_content_needs_its_complementary_role(string markup, bool redundant) =>
        Assert.Equal(redundant, RazorAccessibilityAnalyzer.Analyze(markup).Any(r => r.Descriptor.Id == "BA11Y023"));

    [Fact]
    public void A_fix_is_not_applied_to_text_that_moved()
    {
        // The same short value further along must not receive the fix: the whole attribute is compared.
        const string markup = "<div role=\"region\" aria-label=\"L\" tabindex=\"3\">x</div>";
        var fix = Assert.Single(FixesOf(markup, "BA11Y003"));

        Assert.Equal("tabindex=\"3\"", fix.Original);
        Assert.Null(MarkupFixProperties.Apply(SourceText.From("<p>" + markup), fix));
    }
}
