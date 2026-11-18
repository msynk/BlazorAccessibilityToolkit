using Microsoft.CodeAnalysis.Text;

namespace BlazorAccessibilityToolkit.Analyzers;

/// <summary>A fix applied to (or, for a dry run, found in) a Razor file.</summary>
internal sealed class AppliedFix
{
    public AppliedFix(string ruleId, string title, int line, int column)
    {
        RuleId = ruleId;
        Title = title;
        Line = line;
        Column = column;
    }

    public string RuleId { get; }

    public string Title { get; }

    /// <summary>One-based line of the finding, in the text the fix was applied to.</summary>
    public int Line { get; }

    /// <summary>One-based column of the finding.</summary>
    public int Column { get; }
}

/// <summary>
/// Applies the analyzer's fixes to Razor markup outside the IDE (the <c>blazor-a11y fix</c> command). Only findings with
/// exactly one fix are changed; those that offer a choice (such as <c>current-password</c> or <c>new-password</c>) are
/// reported for a person to decide.
/// </summary>
internal static class RazorFixer
{
    public static (string Text, IReadOnlyList<AppliedFix> Applied, IReadOnlyList<AppliedFix> NeedChoice) Fix(string markup, ICollection<string>? ruleIds = null, AnalyzerSettings? settings = null)
    {
        var applied = new List<AppliedFix>();
        var needChoice = new List<AppliedFix>();
        var text = SourceText.From(markup);

        // Overlapping fixes are skipped by ApplyAll and picked up by the next pass on the changed text.
        for (var pass = 0; pass < 5; pass++)
        {
            var current = text.ToString();
            var single = new Dictionary<MarkupFix, AppliedFix>();
            needChoice.Clear();
            foreach (var (descriptor, element, span, args) in RazorAccessibilityAnalyzer.Analyze(current, settings))
            {
                if (ruleIds is not null && !ruleIds.Contains(descriptor.Id))
                {
                    continue;
                }

                var fixes = RazorAccessibilityAnalyzer.FixesFor(descriptor, element, span, args, current);
                var position = text.Lines.GetLinePosition(span.Start);
                if (fixes.Count == 1)
                {
                    single[fixes[0]] = new AppliedFix(descriptor.Id, fixes[0].Title, position.Line + 1, position.Character + 1);
                }
                else if (fixes.Count > 1)
                {
                    needChoice.Add(new AppliedFix(descriptor.Id, string.Join(" or ", fixes.Select(f => f.Title)), position.Line + 1, position.Character + 1));
                }
            }

            var appliedThisPass = new List<MarkupFix>();
            text = MarkupFixProperties.ApplyAll(text, single.Keys, appliedThisPass);
            if (appliedThisPass.Count == 0)
            {
                break;
            }

            applied.AddRange(appliedThisPass.Select(f => single[f]));
        }

        return (text.ToString(), applied, needChoice);
    }
}
