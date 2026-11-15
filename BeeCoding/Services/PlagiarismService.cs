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

    /// <summary>The problems of a board in the order students see them, for the "compare one problem" pickers.</summary>
    public Task<List<PlagiarismProblemDto>> ProblemsOfBoardAsync(int boardId) =>
        _db.Problems.Where(p => p.BoardId == boardId).OrderBy(p => p.Position).ThenBy(p => p.Id)
            .Select(p => new PlagiarismProblemDto(p.Id, p.Title)).ToListAsync();

    /// <summary>Links at or above this similarity join students into one group (see <see cref="ComputeForBoardAsync"/>).</summary>
    public const double GroupThreshold = 0.65;
    public const int MinGroupSize = 3;
    private const int MaxGroups = 100, MaxPairsPerGroup = 20;

    /// <summary>
    /// With <paramref name="problemId"/> only that problem is compared (smaller, and the pairs the teacher is actually looking at).
    /// Students on one problem whose code is linked at <see cref="GroupThreshold"/> or more (A~B and B~C put A, B and C together, even if A and C
    /// are not alike: each group says its lowest similarity) become a group once there are <see cref="MinGroupSize"/> of them; every other pair at
    /// <paramref name="minSimilarity"/> or more stays a pair. Grouping happens before the pair cap, so a big group is not cut off by it.
    /// </summary>
    public async Task<PlagiarismResultDto> ComputeForBoardAsync(int boardId, int? problemId = null, double minSimilarity = 0.5, int maxResults = 300)
    {
        var subs = await _db.Submissions
            .Where(s => s.Problem!.BoardId == boardId && (problemId == null || s.ProblemId == problemId))
            .Select(s => new { s.Id, s.ProblemId, ProblemTitle = s.Problem!.Title, s.UserId, UserName = s.User!.DisplayName, s.Code, s.CreatedAt })
            .ToListAsync();

        var latest = subs.GroupBy(s => (s.ProblemId, s.UserId))
            .Select(g => g.OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id).First())
            .Select(s => new { s.Id, s.ProblemId, s.ProblemTitle, s.UserId, s.UserName, Tokens = Tokenize(s.Code) })
            .Where(s => s.Tokens.Count >= MinTokens)
            .Select(s => new Entry(s.Id, s.ProblemId, s.ProblemTitle, s.UserId, s.UserName, Shingles(s.Tokens)))
            .ToList();

        var groups = new List<PlagiarismGroupDto>();
        var loose = new List<PlagiarismPairDto>();
        foreach (var problem in latest.GroupBy(s => s.ProblemId))
        {
            var e = problem.ToList();
            var score = new double[e.Count, e.Count];
            var parent = Enumerable.Range(0, e.Count).ToArray();
            int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
            for (var i = 0; i < e.Count; i++)
                for (var j = i + 1; j < e.Count; j++)
                {
                    score[i, j] = Similarity(e[i].Shingles, e[j].Shingles);
                    if (score[i, j] >= GroupThreshold) parent[Find(i)] = Find(j);
                }

            var inGroup = new HashSet<int>();
            foreach (var comp in Enumerable.Range(0, e.Count).GroupBy(Find).Where(c => c.Count() >= MinGroupSize))
            {
                var m = comp.OrderBy(i => e[i].UserName).ToList();
                var all = new List<(int A, int B, double S)>();
                for (var x = 0; x < m.Count; x++)
                    for (var y = x + 1; y < m.Count; y++)
                        all.Add((m[x], m[y], score[Math.Min(m[x], m[y]), Math.Max(m[x], m[y])]));
                foreach (var i in m) inGroup.Add(i);
                groups.Add(new PlagiarismGroupDto(
                    e[m[0]].ProblemId, e[m[0]].ProblemTitle,
                    m.Select(i => new PlagiarismMemberDto(e[i].UserId, e[i].UserName, e[i].Id)).ToList(),
                    all.Max(p => p.S), all.Min(p => p.S), all.Average(p => p.S),
                    all.OrderByDescending(p => p.S).Take(MaxPairsPerGroup).Select(p => Pair(e[p.A], e[p.B], p.S)).ToList()));
            }

            // a pair is loose unless both students sit in a group (they may be in different groups, or one outside)
            var groupOf = groups.Where(g => g.ProblemId == problem.Key).SelectMany(g => g.Members.Select(m => (m.UserId, g))).ToDictionary(x => x.UserId, x => x.g);
            for (var i = 0; i < e.Count; i++)
                for (var j = i + 1; j < e.Count; j++)
                {
                    if (score[i, j] < minSimilarity) continue;
                    if (groupOf.TryGetValue(e[i].UserId, out var gi) && groupOf.TryGetValue(e[j].UserId, out var gj) && gi == gj) continue;
                    loose.Add(Pair(e[i], e[j], score[i, j]));
                }
        }

        return new PlagiarismResultDto(
            groups.OrderByDescending(g => g.Members.Count).ThenByDescending(g => g.MaxSimilarity).Take(MaxGroups).ToList(),
            loose.OrderByDescending(r => r.Similarity).Take(maxResults).ToList());
    }

    private sealed record Entry(int Id, int ProblemId, string ProblemTitle, int UserId, string UserName, HashSet<string> Shingles);

    private static PlagiarismPairDto Pair(Entry a, Entry b, double score) =>
        new(a.ProblemId, a.ProblemTitle, a.UserId, a.UserName, a.Id, b.UserId, b.UserName, b.Id, score);
}
