using Microsoft.Extensions.Caching.Memory;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using SkiaSharp;
using MathPainter = CSharpMath.SkiaSharp.MathPainter;

namespace BeeCoding.Services;

/// <summary>
/// Renders a problem statement (markdown + sample tests) to a PNG so the client
/// never receives the text — there is nothing to select, copy, or read from the DOM.
/// A per-viewer identity watermark is baked into the pixels.
/// </summary>
public class StatementImageService : IDisposable
{
    private const int Width = 780;
    private const float Margin = 28f;
    private const int MaxHeight = 6000;

    private readonly SKTypeface _regular, _bold, _mono;
    // The key holds a fingerprint of the statement, so every edit makes a new entry; a plain dictionary would keep all the old ones until
    // restart. Size-limited and expiring instead: ~40 KB an entry, so the default holds well over a thousand statements in each theme.
    private readonly MemoryCache _baseCache;
    private static readonly TimeSpan KeepFor = TimeSpan.FromHours(1);

    /// <summary>Bytes of rendered statements currently kept (for tests and diagnostics).</summary>
    public long CachedBytes => _baseCache.GetCurrentStatistics()?.CurrentEstimatedSize ?? 0;

    public StatementImageService(IWebHostEnvironment env, long cacheLimitBytes = 64L * 1024 * 1024)
    {
        _baseCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = cacheLimitBytes, TrackStatistics = true });
        var dir = Path.Combine(env.ContentRootPath, "Assets", "fonts");
        _regular = LoadFace(Path.Combine(dir, "DejaVuSans.ttf"));
        _bold = LoadFace(Path.Combine(dir, "DejaVuSans-Bold.ttf"));
        _mono = LoadFace(Path.Combine(dir, "DejaVuSansMono.ttf"));
    }

    public void Dispose() => _baseCache.Dispose();

    private static SKTypeface LoadFace(string path) =>
        File.Exists(path) ? SKTypeface.FromFile(path) ?? SKTypeface.Default : SKTypeface.Default;

    // CSharpMath keeps static, non-thread-safe glyph caches (two renders at once corrupted them: "operations that change non-concurrent
    // collections must have exclusive access", and later renders failed too). Every call into it goes through this lock; it is only
    // taken on a cache miss of the statement picture, and a formula takes microseconds to a few milliseconds.
    private static readonly object MathLock = new();

    private sealed record Theme(SKColor Bg, SKColor Text, SKColor Muted, SKColor CodeBg, SKColor Rule);

    private static Theme Palette(bool dark) => dark
        ? new(new(0x0F, 0x17, 0x2A), new(0xE2, 0xE8, 0xF0), new(0x94, 0xA3, 0xB8), new(0x1E, 0x29, 0x3B), new(0x33, 0x41, 0x55))
        : new(new(0xFF, 0xFF, 0xFF), new(0x0F, 0x17, 0x2A), new(0x64, 0x74, 0x8B), new(0xF1, 0xF5, 0xF9), new(0xE2, 0xE8, 0xF0));

    /// <param name="images">Bytes of the statement's pictures by id (see ImagesController); a missing id is drawn as its alt text.</param>
    public byte[] RenderPng(string cacheKey, string markdown,
        IReadOnlyList<(string Input, string Output)> samples, string watermark, bool dark,
        IReadOnlyDictionary<string, byte[]>? images = null)
    {
        var basePng = _baseCache.GetOrCreate(cacheKey, e =>
        {
            var png = RenderBase(markdown, samples, dark, images ?? new Dictionary<string, byte[]>());
            e.Size = png.Length;
            e.SlidingExpiration = KeepFor;
            return png;
        })!;

        using var bmp = SKBitmap.Decode(basePng);
        using var canvas = new SKCanvas(bmp);
        StampWatermark(canvas, bmp.Width, bmp.Height, watermark, dark);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 92);
        return data.ToArray();
    }

    // Immediate-mode: draw onto an oversized surface, then crop to the used height.
    private byte[] RenderBase(string markdown, IReadOnlyList<(string Input, string Output)> samples, bool dark, IReadOnlyDictionary<string, byte[]> images)
    {
        var t = Palette(dark);
        using var surface = SKSurface.Create(new SKImageInfo(Width, MaxHeight));
        var canvas = surface.Canvas;
        canvas.Clear(t.Bg);

        using var reg = new SKFont(_regular, 15f);
        using var bold = new SKFont(_bold, 15f);
        using var mono = new SKFont(_mono, 13.5f);
        using var monoSmall = new SKFont(_mono, 13f);
        using var text = new SKPaint { Color = t.Text, IsAntialias = true };
        using var muted = new SKPaint { Color = t.Muted, IsAntialias = true };
        using var codeBg = new SKPaint { Color = t.CodeBg };
        using var rule = new SKPaint { Color = t.Rule, StrokeWidth = 1 };

        // Formulas are typeset (CSharpMath, drawn on the same canvas); what is left of the text only gets the old ASCII clean-up of bare commands.
        var (withoutMath, formulas) = StatementMath.Extract(markdown);
        var g = new Gfx(canvas, t, reg, bold, mono, monoSmall, text, muted, codeBg, rule) { Y = Margin, Formulas = formulas };

        foreach (var block in Markdown.Parse(PlainMath(withoutMath, stripDelimiters: false) ?? "")) RenderBlock(g, block, Margin, images);

        if (samples.Count > 0)
        {
            g.Y += 12;
            g.HLine();
            g.Y += 16;
            g.Heading("Examples", 2);
            for (int i = 0; i < samples.Count; i++)
            {
                g.Label($"Input {i + 1}");
                g.CodeBox(samples[i].Input);
                g.Label($"Output {i + 1}");
                g.CodeBox(samples[i].Output);
                g.Y += 8;
            }
        }

        int height = Math.Clamp((int)MathF.Ceiling(g.Y + Margin), 80, MaxHeight);
        using var full = surface.Snapshot();
        using var cropped = full.Subset(new SKRectI(0, 0, Width, height));
        using var data = cropped.Encode(SKEncodedImageFormat.Png, 92);
        return data.ToArray();
    }

    private void RenderBlock(Gfx g, Block block, float x, IReadOnlyDictionary<string, byte[]> images)
    {
        switch (block)
        {
            case HeadingBlock h:
                g.Y += 10;
                g.Heading(InlineText(h.Inline, g.Formulas), h.Level);
                break;
            case ParagraphBlock p:
                ParagraphWithImages(g, p, x, images);
                g.Y += 8;
                break;
            case ListBlock list:
                foreach (var item in list)
                    if (item is ListItemBlock li)
                        foreach (var child in li)
                            g.Paragraph(child is ParagraphBlock cp ? Runs(cp.Inline, g.Formulas)
                                : new List<Run> { new(child.ToString() ?? "", RunKind.Normal) },
                                x + 18, bullet: "•");
                g.Y += 8;
                break;
            case QuoteBlock q:
                float top = g.Y;
                foreach (var child in q) RenderBlock(g, child, x + 14, images);
                g.Canvas.DrawRect(x, top, 3, g.Y - top, g.MutedPaint);
                break;
            case FencedCodeBlock fc:
                g.CodeBox(LinesText(fc.Lines));
                break;
            case CodeBlock cb:
                g.CodeBox(LinesText(cb.Lines));
                break;
            case ThematicBreakBlock:
                g.Y += 8; g.HLine(); g.Y += 8;
                break;
        }
    }

    /// <summary>A paragraph is drawn as text, except that each <c>![](/api/images/id)</c> in it is drawn as a picture of its own.</summary>
    private static void ParagraphWithImages(Gfx g, ParagraphBlock p, float x, IReadOnlyDictionary<string, byte[]> images)
    {
        var pending = new List<Inline>();
        void Flush()
        {
            var runs = RunsOf(pending, g.Formulas);
            pending.Clear();
            if (runs.Any(r => r.Math >= 0 || r.Text.Trim().Length > 0)) g.Paragraph(runs, x);   // a paragraph that is only a formula counts
        }
        foreach (var node in p.Inline ?? Enumerable.Empty<Inline>())
        {
            if (node is LinkInline { IsImage: true } link && ImageBytes(link, images) is { } bytes)
            {
                Flush();
                DrawPicture(g, bytes, x);
            }
            else pending.Add(node);   // text, or an image we cannot show: its alt text is drawn instead
        }
        Flush();
    }

    private static byte[]? ImageBytes(LinkInline link, IReadOnlyDictionary<string, byte[]> images)
    {
        const string prefix = "/api/images/";
        var url = link.Url ?? "";
        return url.StartsWith(prefix, StringComparison.Ordinal) && images.TryGetValue(url[prefix.Length..], out var b) ? b : null;
    }

    private static void DrawPicture(Gfx g, byte[] data, float x)
    {
        using var bmp = SKBitmap.Decode(data);
        if (bmp is null) return;
        float scale = Math.Min(1f, Math.Min((Width - Margin - x) / bmp.Width, 700f / bmp.Height));
        var dest = new SKRect(x, g.Y, x + bmp.Width * scale, g.Y + bmp.Height * scale);
        using var img = SKImage.FromBitmap(bmp);
        g.Canvas.DrawImage(img, dest, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        g.Y += dest.Height + 10;
    }

    // There is no math typesetting in the PNG renderer (or the plain Markdown view).
    // Downgrade the common LaTeX bits to ASCII so a formula stays legible.
    // stripDelimiters: false for text whose formulas were already taken out (a lone $ there is money, not math).
    internal static string? PlainMath(string? s, bool stripDelimiters = true)
    {
        if (string.IsNullOrEmpty(s) || (!s.Contains('\\') && !s.Contains('$'))) return s;
        if (stripDelimiters)
        {
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\\\[|\\\]|\\\(|\\\)", "");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\$\$?([^$]+?)\$\$?", "$1");
        }
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\\d?frac\s*\{([^{}]+)\}\s*\{([^{}]+)\}", "($1)/($2)");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\\sqrt\s*\{([^{}]+)\}", "sqrt($1)");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\\left|\\right|\\displaystyle|\\,|\\;|\\!|\\quad|\\qquad", "");
        s = s.Replace(@"\cdot", "*").Replace(@"\times", "x").Replace(@"\div", "/");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\\leq|\\le\b", "<=");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\\geq|\\ge\b", ">=");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\\neq|\\ne\b", "!=");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\^\s*\{([^{}]+)\}", "^($1)");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"_\s*\{([^{}]+)\}", "_($1)");
        s = s.Replace(@"\%", "%");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\\[a-zA-Z]+", m => m.Value.Substring(1));
        return s;
    }

    private static string LinesText(Markdig.Helpers.StringLineGroup lines)
    {
        var arr = lines.Lines;
        return string.Join('\n', Enumerable.Range(0, lines.Count).Select(i => arr[i].ToString()));
    }

    private void StampWatermark(SKCanvas canvas, int w, int h, string text, bool dark)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        using var font = new SKFont(_regular, 12.5f);
        using var paint = new SKPaint
        {
            Color = (dark ? SKColors.White : SKColors.Black).WithAlpha(24),
            IsAntialias = true,
        };

        // Space the tiles by the real text width so instances never overlap into a smear.
        float textW = font.MeasureText(text);
        float stepX = textW + 100f;
        float stepY = 116f;

        canvas.Save();
        canvas.RotateDegrees(-22, w / 2f, h / 2f);
        int row = 0;
        for (float y = -h; y < h * 2f; y += stepY, row++)
        {
            float brick = (row & 1) * (stepX / 2f);   // stagger alternate rows
            for (float x = -w - stepX; x < w * 2f; x += stepX)
                canvas.DrawText(text, x + brick, y, SKTextAlign.Left, font, paint);
        }
        canvas.Restore();
    }

    // ---- drawing helper ----------------------------------------------------

    private sealed class Gfx(SKCanvas canvas, StatementImageService.Theme t, SKFont reg, SKFont bold, SKFont mono, SKFont monoSmall,
        SKPaint text, SKPaint muted, SKPaint codeBg, SKPaint rule)
    {
        public readonly SKCanvas Canvas = canvas;
        private readonly Theme _t = t;
        private readonly SKFont _reg = reg, _bold = bold, _mono = mono, _monoSmall = monoSmall;
        private readonly SKPaint _text = text, _muted = muted, _codeBg = codeBg, _rule = rule;
        public float Y;

        public SKPaint MutedPaint => _muted;

        public void HLine() => Canvas.DrawLine(Margin, Y, Width - Margin, Y, _rule);

        public void Heading(string s, int level)
        {
            float size = level <= 1 ? 24 : level == 2 ? 19 : 16;
            using var f = new SKFont(_bold.Typeface, size);
            Y += size;
            Canvas.DrawText(s, Margin, Y, SKTextAlign.Left, f, _text);
            Y += size * 0.35f;
        }

        public void Label(string s)
        {
            using var f = new SKFont(_reg.Typeface, 11.5f);
            Y += 13;
            Canvas.DrawText(s, Margin, Y, SKTextAlign.Left, f, _muted);
            Y += 3;
        }

        public void CodeBox(string code)
        {
            const float size = 13f, lineH = 18f;
            var lines = (code ?? "").Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            if (lines.Length == 1 && lines[0].Length == 0) lines = new[] { " " };
            float top = Y + 4;
            float boxH = lines.Length * lineH + 12;
            Canvas.DrawRoundRect(new SKRect(Margin, top, Width - Margin, top + boxH), 6, 6, _codeBg);
            float y = top + 6 + size;
            foreach (var ln in lines)
            {
                Canvas.DrawText(ln, Margin + 8, y, SKTextAlign.Left, _monoSmall, _text);
                y += lineH;
            }
            Y = top + boxH + 4;
        }

        /// <summary>The formulas of the statement being drawn (see <see cref="StatementMath"/>).</summary>
        public StatementMath.Set Formulas { get; init; } = StatementMath.Set.Empty;

        private const float MathSize = 16.5f;

        // One piece of a line: a word / space (Font set) or a typeset formula (Painter set).
        private sealed record Item(string Text, SKFont? Font, RunKind Kind, float Width, MathPainter? Painter = null, float Ascent = 0, float Descent = 0, bool Display = false);

        /// <summary>A painter for formula <paramref name="index"/>, or null when it cannot be typeset (then the ASCII form is drawn as text).</summary>
        private MathPainter? Typeset(StatementMath.Formula f)
        {
            try
            {
                lock (MathLock)
                {
                    var painter = new MathPainter { LaTeX = StatementMath.Shim(f.Tex), FontSize = f.Display ? MathSize + 3 : MathSize, TextColor = _text.Color };
                    return string.IsNullOrEmpty(painter.ErrorMessage?.ToString()) ? painter : null;
                }
            }
            catch (Exception) { return null; }   // a formula the typesetter chokes on must not take the statement down
        }

        public void Paragraph(List<Run> runs, float x, string? bullet = null)
        {
            const float bodySize = 15f, lineH = 22f;
            float maxRight = Width - Margin;

            // Pass 1: break into lines (a formula can be taller than text, so a line's height is only known once it is full).
            var lines = new List<List<Item>> { new() };
            float cursorX = x;
            bool breakBefore = false;   // set after a display formula: whatever comes next starts a new line
            void NewLine() { lines.Add(new()); cursorX = x; breakBefore = false; }

            foreach (var run in runs)
            {
                if (run.Math >= 0 && Formulas.At(run.Math) is { } formula)
                {
                    if (Typeset(formula) is { } painter)
                    {
                        System.Drawing.RectangleF m;
                        lock (MathLock) m = painter.Measure();
                        float w = m.Width + 2, ascent = -m.Y, descent = m.Height + m.Y;
                        if (formula.Display)
                        {
                            if (lines[^1].Count > 0) NewLine();
                            lines[^1].Add(new("", null, run.Kind, w, painter, ascent, descent, Display: true));
                            breakBefore = true;
                            continue;
                        }
                        if (breakBefore) NewLine();
                        if (cursorX + w > maxRight && cursorX > x) NewLine();
                        lines[^1].Add(new("", null, run.Kind, w, painter, ascent, descent));
                        cursorX += w + 2;
                        continue;
                    }
                    // not typesettable: fall through and draw its plain-text form
                }

                var font = run.Kind switch { RunKind.Bold => _bold, RunKind.Code => _mono, _ => _reg };
                var text = run.Math >= 0 ? PlainMath(Formulas.At(run.Math)?.Tex) ?? "" : run.Text;
                foreach (var token in Tokenize(text))
                {
                    if (token == "\n") { NewLine(); continue; }
                    if (breakBefore) NewLine();
                    float w = font.MeasureText(token);
                    if (cursorX + w > maxRight && cursorX > x)
                    {
                        NewLine();
                        if (token.Trim().Length == 0) continue;
                    }
                    lines[^1].Add(new(token, font, run.Kind, w));
                    cursorX += w;
                }
            }

            // Pass 2: draw. A line with a tall formula gets room above and below it; plain lines keep the old 22 px rhythm.
            float below = 0;
            bool first = true;
            foreach (var line in lines)
            {
                float above = line.Select(i => i.Ascent).DefaultIfEmpty(0).Max(), under = line.Select(i => i.Descent).DefaultIfEmpty(0).Max();
                bool display = line.Count == 1 && line[0].Display;
                float pad = display ? 8f : 0f;   // a display formula gets some air above and below it
                Y += lineH + Math.Max(0, above - 17f) + pad + below;
                below = Math.Max(0, under - 6f) + pad;

                if (first && bullet is not null) Canvas.DrawText(bullet, x - 14, Y, SKTextAlign.Left, _reg, _text);
                first = false;

                float cx = x;
                if (display) cx = x + Math.Max(0, (maxRight - x - line[0].Width) / 2);
                foreach (var item in line)
                {
                    if (item.Painter is { } painter)
                    {
                        float room = maxRight - cx;
                        if (item.Width > room && room > 40)   // wider than the page: shrink it to fit rather than clip it
                        {
                            Canvas.Save();
                            float k = room / item.Width;
                            Canvas.Scale(k, k, cx, Y);
                            lock (MathLock) painter.Draw(Canvas, cx, Y);
                            Canvas.Restore();
                        }
                        else lock (MathLock) painter.Draw(Canvas, cx, Y);
                        cx += item.Width + 2;
                        continue;
                    }
                    if (item.Kind == RunKind.Code)
                        Canvas.DrawRoundRect(new SKRect(cx - 1, Y - bodySize + 3, cx + item.Width + 1, Y + 4), 3, 3, _codeBg);
                    Canvas.DrawText(item.Text, cx, Y, SKTextAlign.Left, item.Font!, _text);
                    cx += item.Width;
                }
            }
            Y += below;   // the last line's formula hangs below its baseline: keep the next paragraph clear of it
        }
    }

    // ---- markdown inline helpers -----------------------------------------------

    private enum RunKind { Normal, Bold, Code }
    /// <summary><paramref name="Math"/> is an index into the statement's formulas (-1 = ordinary text).</summary>
    private sealed record Run(string Text, RunKind Kind, int Math = -1);

    private static List<Run> Runs(ContainerInline? inline, StatementMath.Set math) => RunsOf(inline ?? Enumerable.Empty<Inline>(), math);

    private static List<Run> RunsOf(IEnumerable<Inline> nodes, StatementMath.Set math)
    {
        var runs = new List<Run>();
        void Text(string text, RunKind kind)
        {
            int at = 0;
            foreach (System.Text.RegularExpressions.Match m in StatementMath.Placeholder.Matches(text))
            {
                if (m.Index > at) runs.Add(new(text[at..m.Index], kind));
                runs.Add(new("", kind, int.Parse(m.Groups[1].Value)));
                at = m.Index + m.Length;
            }
            if (at < text.Length) runs.Add(new(text[at..], kind));
        }
        void Walk(Inline node, RunKind kind)
        {
            switch (node)
            {
                case LiteralInline lit: Text(lit.Content.ToString(), kind); break;
                case CodeInline code: runs.Add(new(code.Content, RunKind.Code)); break;
                case LineBreakInline: runs.Add(new("\n", kind)); break;
                case HtmlInline: break;   // a raw tag cannot be drawn (its ToString() is the class name)
                case HtmlEntityInline entity: Text(entity.Transcoded.ToString(), kind); break;   // &lt; &amp; ...
                case EmphasisInline em:
                    var k = em.DelimiterCount >= 2 ? RunKind.Bold : kind;
                    foreach (var child in em) Walk(child, k);
                    break;
                case ContainerInline cont:
                    foreach (var child in cont) Walk(child, kind);
                    break;
                default:
                    Text(node.ToString() ?? "", kind);
                    break;
            }
        }
        foreach (var n in nodes) Walk(n, RunKind.Normal);
        return runs;
    }

    /// <summary>Plain text of an inline (for a heading): a formula becomes its ASCII form.</summary>
    private static string InlineText(ContainerInline? inline, StatementMath.Set math) =>
        string.Concat(Runs(inline, math).Select(r => r.Math >= 0 ? PlainMath(math.At(r.Math)?.Tex) ?? "" : r.Text)).Replace("\n", " ").Trim();

    private static IEnumerable<string> Tokenize(string s)
    {
        int i = 0;
        while (i < s.Length)
        {
            if (s[i] == '\n') { yield return "\n"; i++; continue; }
            int start = i;
            bool space = char.IsWhiteSpace(s[i]);
            while (i < s.Length && s[i] != '\n' && char.IsWhiteSpace(s[i]) == space) i++;
            yield return s[start..i];
        }
    }
}
