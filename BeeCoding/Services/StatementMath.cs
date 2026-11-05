using System.Text;
using System.Text.RegularExpressions;

namespace BeeCoding.Services;

/// <summary>
/// Formulas in a problem statement ($x^2$, $$...$$, \(...\), \[...\]). They are pulled out of the markdown BEFORE it is parsed, so
/// that '_' and '*' inside a formula are not read as emphasis and the parser never sees LaTeX, and are put back by the renderer.
/// The browser view does the same in ClientApp/src/lib/math.js with the same delimiters and the same limits — keep the two in step.
/// </summary>
public static class StatementMath
{
    public const int MaxFormulas = 200;   // per statement
    public const int MaxTexChars = 500;   // per formula: bounds the nesting depth the typesetter has to walk

    // A placeholder is two private-use characters around the index: they pass through the markdown parser as plain text.
    public const char Open = '\uE000', Close = '\uE001';
    public static readonly Regex Placeholder = new($"{Open}(\\d+){Close}", RegexOptions.Compiled);

    private static readonly Regex Code = new(@"(```[\s\S]*?```|`[^`\n]*`)", RegexOptions.Compiled);
    private static readonly Regex DisplayBracket = new(@"\\\[([\s\S]+?)\\\]", RegexOptions.Compiled);
    private static readonly Regex DisplayDollar = new(@"\$\$([\s\S]+?)\$\$", RegexOptions.Compiled);
    private static readonly Regex InlineParen = new(@"\\\(([\s\S]+?)\\\)", RegexOptions.Compiled);
    // "$5 and $10" is money, not math: the opening $ must be followed by a non-space, the closing one preceded by a non-space and not followed by a digit.
    private static readonly Regex InlineDollar = new(@"(?<![\\$])\$(?!\s)([^$\n]+?)(?<!\s)\$(?!\d)", RegexOptions.Compiled);

    public sealed record Formula(string Tex, bool Display);

    public sealed class Set
    {
        public static readonly Set Empty = new();
        public List<Formula> Items { get; } = [];
        public Formula? At(int index) => index >= 0 && index < Items.Count ? Items[index] : null;
    }

    /// <summary>The markdown with each formula replaced by a placeholder, and the formulas. Code (fenced or inline) is left alone;
    /// a formula that is too long, or beyond the cap, stays in the text as typed.</summary>
    public static (string Text, Set Formulas) Extract(string? markdown)
    {
        var set = new Set();
        if (string.IsNullOrEmpty(markdown) || (!markdown.Contains('$') && !markdown.Contains('\\'))) return (markdown ?? "", set);

        string Stash(Match m, string tex, bool display)
        {
            if (set.Items.Count >= MaxFormulas || tex.Length > MaxTexChars) return m.Value;
            set.Items.Add(new Formula(tex.Trim(), display));
            return $"{Open}{set.Items.Count - 1}{Close}";
        }

        var parts = Code.Split(markdown);   // odd entries are the code, kept as they are
        var sb = new StringBuilder();
        for (int i = 0; i < parts.Length; i++)
        {
            var p = parts[i];
            if (i % 2 == 0)
            {
                p = DisplayBracket.Replace(p, m => Stash(m, m.Groups[1].Value, true));
                p = DisplayDollar.Replace(p, m => Stash(m, m.Groups[1].Value, true));
                p = InlineParen.Replace(p, m => Stash(m, m.Groups[1].Value, false));
                p = InlineDollar.Replace(p, m => Stash(m, m.Groups[1].Value, false));
            }
            sb.Append(p);
        }
        return (sb.ToString(), set);
    }

    /// <summary>CSharpMath lacks a few common commands: \dfrac, \tfrac, \lt, \gt, \bmod, \mod, \pmod{..}. Rewrite them to what it knows.</summary>
    public static string Shim(string tex)
    {
        tex = tex.Replace(@"\dfrac", @"\frac").Replace(@"\tfrac", @"\frac");
        tex = Regex.Replace(tex, @"\\lt(?![a-zA-Z])", "<");
        tex = Regex.Replace(tex, @"\\gt(?![a-zA-Z])", ">");
        tex = Regex.Replace(tex, @"\\pmod\s*\{([^{}]*)\}", @"\ (\mathrm{mod}\ $1)");
        return tex.Replace(@"\bmod", @"\ \mathrm{mod}\ ").Replace(@"\mod", @"\ \mathrm{mod}\ ");
    }
}
