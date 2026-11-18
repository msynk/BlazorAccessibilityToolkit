using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;

namespace BlazorAccessibilityToolkit.Analyzers;

/// <summary>
/// Offers the fixes that the Razor accessibility analyzer attaches to its findings, for example renaming
/// <c>aria-labeledby</c> to <c>aria-labelledby</c> or removing a redundant <c>role</c>. The analyzer computes each edit
/// from the markup; this provider applies it to the <c>.razor</c> file (an additional document), and only when the text
/// still matches what the analyzer saw.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RazorAccessibilityCodeFixProvider), DocumentKinds = [nameof(TextDocumentKind.AdditionalDocument)], DocumentExtensions = [".razor", ".cshtml"])]
[Shared]
public sealed class RazorAccessibilityCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => MarkupFixProperties.FixableIds;

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => RazorFixAllProvider.Instance;

    /// <inheritdoc />
    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var document = context.TextDocument;
        foreach (var diagnostic in context.Diagnostics)
        {
            foreach (var fix in MarkupFixProperties.Decode(diagnostic.Properties))
            {
                context.RegisterCodeFix(
                    CodeAction.Create(fix.Title, cancellationToken => ApplyAsync(document, fix, cancellationToken), fix.EquivalenceKey),
                    diagnostic);
            }
        }

        return Task.CompletedTask;
    }

    private static async Task<Solution> ApplyAsync(TextDocument document, MarkupFix fix, CancellationToken cancellationToken)
    {
        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var fixedText = MarkupFixProperties.Apply(text, fix);
        return fixedText is null ? document.Project.Solution : document.Project.Solution.WithAdditionalDocumentText(document.Id, fixedText);
    }

    // "Fix all" for findings in .razor files: the built-in providers only handle C# documents. Findings are grouped by
    // file and applied together, skipping a fix that overlaps one already applied.
    private sealed class RazorFixAllProvider : FixAllProvider
    {
        public static readonly RazorFixAllProvider Instance = new();

        public override IEnumerable<FixAllScope> GetSupportedFixAllScopes() => [FixAllScope.Project, FixAllScope.Solution];

        public override async Task<CodeAction?> GetFixAsync(FixAllContext fixAllContext)
        {
            var projects = fixAllContext.Scope == FixAllScope.Solution ? fixAllContext.Solution.Projects : [fixAllContext.Project];
            var solution = fixAllContext.Solution;
            foreach (var project in projects)
            {
                var diagnostics = await fixAllContext.GetAllDiagnosticsAsync(project).ConfigureAwait(false);
                foreach (var file in diagnostics.Where(d => fixAllContext.DiagnosticIds.Contains(d.Id) && d.Location.Kind == LocationKind.ExternalFile).GroupBy(d => d.Location.GetLineSpan().Path, StringComparer.OrdinalIgnoreCase))
                {
                    var document = project.AdditionalDocuments.FirstOrDefault(d => string.Equals(d.FilePath, file.Key, StringComparison.OrdinalIgnoreCase));
                    if (document is null)
                    {
                        continue;
                    }

                    var fixes = file.SelectMany(d => MarkupFixProperties.Decode(d.Properties)).Where(f => f.EquivalenceKey == fixAllContext.CodeActionEquivalenceKey).ToArray();
                    if (fixes.Length == 0)
                    {
                        continue;
                    }

                    var text = await document.GetTextAsync(fixAllContext.CancellationToken).ConfigureAwait(false);
                    solution = solution.WithAdditionalDocumentText(document.Id, MarkupFixProperties.ApplyAll(text, fixes));
                }
            }

            var title = fixAllContext.Scope == FixAllScope.Solution ? "Fix all in solution" : "Fix all in project";
            return CodeAction.Create(title, _ => Task.FromResult(solution), fixAllContext.CodeActionEquivalenceKey);
        }
    }
}
