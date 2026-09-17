using System.Text.RegularExpressions;
using BeeCoding.Data;
using BeeCoding.Models;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Services;

/// <summary>Heuristic pairwise code-similarity across students who attempted the same
/// problem on a board — a spot-check signal for a teacher/org-admin/platform-admin, not
/// proof of copying. Compares the latest submission per (problem, student) using
/// token-shingle Jaccard similarity: source is tokenized, every non-keyword identifier is
/// collapsed to a single placeholder (so simple variable/function renaming doesn't defeat
/// it), then compared as a set of 5-token shingles. Short/boilerplate submissions are
/// skipped since a trivial problem's "obvious" solution can coincidentally look identical
/// between honest students.</summary>
public class PlagiarismService(AppDbContext db)
{
    private readonly AppDbContext _db = db;

    private const int ShingleSize = 5;
    private const int MinTokens = 20;   // below this, too little signal — skip to avoid false positives

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "auto", "break", "case", "char", "const", "continue", "default", "do", "double", "else", "enum",
        "extern", "float", "for", "goto", "if", "int", "long", "register", "return", "short", "signed",
        "sizeof", "static", "struct", "switch", "typedef", "union", "unsigned", "void", "volatile", "while",
        "bool", "catch", "class", "delete", "namespace", "new", "private", "protected", "public", "template",
        "this", "throw", "try", "using", "virtual", "friend", "inline", "operator", "typename", "explicit",
        "mutable", "true", "false", "nullptr", "include", "define", "ifdef", "ifndef", "endif", "pragma", "std",
    };

    private static readonly Regex CommentRe = new(@"//.*?$|/\*.*?\*/", RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex TokenRe = new(@"[A-Za-z_]\w*|\d+\.?\d*|[^\sA-Za-z0-9_]", RegexOptions.Compiled);
    private static readonly Regex IdentRe = new(@"^[A-Za-z_]\w*$", RegexOptions.Compiled);

    public static List<string> Tokenize(string? code)
    {
        var stripped = CommentRe.Replace(code ?? "", "");
        var tokens = new List<string>();
        foreach (Match m in TokenRe.Matches(stripped))
            tokens.Add(IdentRe.IsMatch(m.Value) && !Keywords.Contains(m.Value) ? "ID" : m.Value);
        return tokens;
    }

    private static HashSet<string> Shingles(List<string> tokens)
    {
        var set = new HashSet<string>();
        for (var i = 0; i <= tokens.Count - ShingleSize; i++)
            set.Add(string.Join(' ', tokens.Skip(i).Take(ShingleSize)));
        return set;
    }

    private static double Similarity(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        var (smaller, larger) = a.Count <= b.Count ? (a, b) : (b, a);
        var inter = smaller.Count(larger.Contains);
        var union = a.Count + b.Count - inter;
        return union == 0 ? 0 : (double)inter / union;
    }

    public async Task<List<PlagiarismPairDto>> ComputeForBoardAsync(int boardId, double minSimilarity = 0.5, int maxResults = 300)
    {
        var subs = await _db.Submissions
            .Where(s => s.Problem!.BoardId == boardId)
            .Select(s => new { s.Id, s.ProblemId, ProblemTitle = s.Problem!.Title, s.UserId, UserName = s.User!.DisplayName, s.Code, s.CreatedAt })
            .ToListAsync();

        var latest = subs.GroupBy(s => (s.ProblemId, s.UserId))
            .Select(g => g.OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id).First())
            .Select(s => new { s.Id, s.ProblemId, s.ProblemTitle, s.UserId, s.UserName, Tokens = Tokenize(s.Code) })
            .Where(s => s.Tokens.Count >= MinTokens)
            .Select(s => new { s.Id, s.ProblemId, s.ProblemTitle, s.UserId, s.UserName, Shingles = Shingles(s.Tokens) })
            .ToList();

        var results = new List<PlagiarismPairDto>();
        foreach (var group in latest.GroupBy(s => s.ProblemId))
        {
            var entries = group.ToList();
            for (var i = 0; i < entries.Count; i++)
                for (var j = i + 1; j < entries.Count; j++)
                {
                    var score = Similarity(entries[i].Shingles, entries[j].Shingles);
                    if (score < minSimilarity) continue;
                    results.Add(new PlagiarismPairDto(
                        entries[i].ProblemId, entries[i].ProblemTitle,
                        entries[i].UserId, entries[i].UserName, entries[i].Id,
                        entries[j].UserId, entries[j].UserName, entries[j].Id,
                        score));
                }
        }

        return results.OrderByDescending(r => r.Similarity).Take(maxResults).ToList();
    }
}
