using System.Diagnostics;
using BeeCoding.Services;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using Xunit;

namespace BeeCoding.Tests;

public class StatementMathExtractTests
{
    [Fact]
    public void Formulas_in_all_four_delimiters_are_pulled_out_and_numbered()
    {
        var (text, set) = StatementMath.Extract(@"a $x_1$ b $$\sum_i a_i$$ c \(y\) d \[z\]");
        Assert.Equal(4, set.Items.Count);
        Assert.Contains(set.Items, f => !f.Display && f.Tex == "x_1");
        Assert.Contains(set.Items, f => f.Display && f.Tex == @"\sum_i a_i");
        Assert.Contains(set.Items, f => f.Display && f.Tex == "z");
        Assert.Contains(set.Items, f => !f.Display && f.Tex == "y");
        Assert.DoesNotContain("$", text);
        Assert.Equal(4, StatementMath.Placeholder.Matches(text).Count);
    }

    [Theory]
    [InlineData("Harga $5 dan $10 per buah, total $15.")]       // money is not math
    [InlineData("Gunakan `printf(\"$x$\")` di sini")]            // inline code keeps its dollars
    [InlineData("```\nint a$b$c = 1;\n```")]                      // so does a fenced block
    [InlineData("Satu dolar $ saja")]
    public void Text_without_a_formula_is_left_exactly_as_it_was(string markdown)
    {
        var (text, set) = StatementMath.Extract(markdown);
        Assert.Empty(set.Items);
        Assert.Equal(markdown, text);
    }

    [Fact]
    public void The_caps_leave_the_excess_in_the_text_as_typed()
    {
        var many = string.Concat(Enumerable.Range(0, StatementMath.MaxFormulas + 20).Select(i => $"$a{i}$ "));
        var (_, set) = StatementMath.Extract(many);
        Assert.Equal(StatementMath.MaxFormulas, set.Items.Count);

        var (text, one) = StatementMath.Extract("$" + new string('x', StatementMath.MaxTexChars + 1) + "$");
        Assert.Empty(one.Items);
        Assert.StartsWith("$xxx", text);
    }

    [Theory]
    [InlineData(@"\dfrac{1}{2}", @"\frac{1}{2}")]
    [InlineData(@"a \lt b \left( c \gt d \right)", @"a < b \left( c > d \right)")]
    [InlineData(@"a \bmod p", @"a \ \mathrm{mod}\  p")]
    [InlineData(@"x \pmod{10^9+7}", @"x \ (\mathrm{mod}\ 10^9+7)")]
    public void Commands_the_typesetter_lacks_are_rewritten(string tex, string expected) => Assert.Equal(expected, StatementMath.Shim(tex));
}

[Collection("app")]
public class StatementMathRenderTests
{
    private static StatementImageService Service(TestApp app) =>
        new(app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>());

    private static int HeightOf(byte[] png) { using var b = SKBitmap.Decode(png); return b.Height; }

    [Fact]
    public void A_formula_is_typeset_into_the_statement_picture()
    {
        using var app = new TestApp();
        app.CreateClient();
        using var svc = Service(app);
        var plain = svc.RenderPng("m:plain", "Hitung jumlahnya lalu cetak hasilnya.", [], "w", dark: false);
        var tall = svc.RenderPng("m:tall", @"Hitung $\sum_{i=1}^{n}\frac{a_i}{b_i}$ lalu cetak hasilnya.", [], "w", dark: false);
        var disp = svc.RenderPng("m:disp", "Hitung\n\n$$\\frac{n(n+1)}{2}$$\n\nlalu cetak.", [], "w", dark: false);

        Assert.True(HeightOf(tall) > HeightOf(plain), "a line with a sum and a fraction needs more room than a plain line");
        Assert.True(HeightOf(disp) > HeightOf(plain));
        Assert.NotEqual(plain, tall);
    }

    [Fact]
    public void Bad_hostile_and_huge_input_still_gives_a_picture_quickly()
    {
        using var app = new TestApp();
        app.CreateClient();
        using var svc = Service(app);
        var nested = string.Concat(Enumerable.Repeat(@"\frac{", 80)) + "x" + string.Concat(Enumerable.Repeat("}{1}", 80));
        var deepBraces = new string('{', 240) + "x" + new string('}', 240);
        var markdown = string.Join("\n\n", new[]
        {
            $"${nested}$", $"${deepBraces}$",
            @"$\frac{1}{$", @"$\unknowncmd{x}$", @"$\def\a{\a\a}\a$", @"$\href{javascript:alert(1)}{x}$",
            "# Judul $\\alpha$",
            "- butir dengan $x^2$\n- lagi",
            string.Concat(Enumerable.Range(0, 250).Select(i => $"$a_{i}$ ")),
            "Harga $5 dan $10",
        });
        var sw = Stopwatch.StartNew();
        var png = svc.RenderPng("m:hostile", markdown, [("1 2", "3")], "w", dark: true);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15), $"took {sw.Elapsed}");
        Assert.True(HeightOf(png) > 100);
    }

    [Fact]
    public void Many_statements_with_formulas_can_be_drawn_at_the_same_time()
    {
        using var app = new TestApp();
        app.CreateClient();
        using var svc = Service(app);
        Parallel.For(0, 40, i =>
            Assert.True(HeightOf(svc.RenderPng($"m:par{i}", $"Soal {i}: $\\sum_{{k=1}}^{{n}} a_k \\le 10^9$ dan $$\\binom{{n}}{{k}}$$", [("1", "2")], "w", dark: i % 2 == 0)) > 80));
    }
}
