using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace BlazorAccessibilityToolkit.Demo.WebApp.Lab;

/// <summary>A small syntax highlighter for the code blocks: comments, strings, keywords, numbers and type names.</summary>
public static partial class CodeHighlighter
{
    public static string ToHtml(string code, string language) => language switch
    {
        "csharp" => Highlight(code, CSharp()),
        "razor" => Highlight(code, Razor()),
        "yaml" => Highlight(code, Yaml()),
        "shell" => Highlight(code, Shell()),
        _ => WebUtility.HtmlEncode(code),
    };

    private static string Highlight(string code, Regex tokens)
    {
        var sb = new StringBuilder(code.Length * 2);
        var last = 0;
        foreach (Match match in tokens.Matches(code))
        {
            sb.Append(WebUtility.HtmlEncode(code[last..match.Index]));
            var kind = match.Groups.Keys.FirstOrDefault(k => !char.IsDigit(k[0]) && match.Groups[k].Success) ?? "plain";
            sb.Append("<span class=\"tok-").Append(kind).Append("\">").Append(WebUtility.HtmlEncode(match.Value)).Append("</span>");
            last = match.Index + match.Length;
        }

        sb.Append(WebUtility.HtmlEncode(code[last..]));
        return sb.ToString();
    }

    [GeneratedRegex("""
        (?<comment>//[^\n]*)
        |(?<string>"{3}[\s\S]*?"{3}|\$?@?"(?:[^"\\\n]|\\.)*"|'(?:[^'\\\n]|\\.)')
        |(?<keyword>\b(?:await|using|var|new|async|return|true|false|null|static|public|private|if|else|foreach|for|in|is|not|of|typeof|default)\b)
        |(?<type>(?<![.\w])[A-Z][A-Za-z0-9]*)
        |(?<number>\b\d+(?:\.\d+)?\b)
        """, RegexOptions.IgnorePatternWhitespace | RegexOptions.Multiline)]
    private static partial Regex CSharp();

    [GeneratedRegex("""
        (?<comment>@\*[\s\S]*?\*@|<!--[\s\S]*?-->)
        |(?<string>"[^"\n]*")
        |(?<tag></?[A-Za-z][\w.-]*|/?>)
        |(?<keyword>@(?:code|implements|inject|if|else|for|foreach|ref|onclick|onkeydown|onchange|bind-Value|rendermode)\b)
        """, RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex Razor();

    [GeneratedRegex("""
        (?<comment>\#[^\n]*)
        |(?<string>"[^"\n]*")
        |(?<keyword>\[[^\]\n]+\])
        |(?<tag>^\s*-\s[a-z]+(?=[\s:]|$))
        """, RegexOptions.IgnorePatternWhitespace | RegexOptions.Multiline)]
    private static partial Regex Yaml();

    [GeneratedRegex("""
        (?<comment>\#[^\n]*)
        |(?<string>"[^"\n]*")
        |(?<keyword>(?<=\s|^)--?[a-z][\w-]*)
        """, RegexOptions.IgnorePatternWhitespace | RegexOptions.Multiline)]
    private static partial Regex Shell();
}
