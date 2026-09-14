using BeeCoding.Data;
using BeeCoding.Models;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Services;

public class WallService(AppDbContext db, VisibilityService vis)
{
    private readonly AppDbContext _db = db;
    private readonly VisibilityService _vis = vis;
    public static readonly string[] AllowedEmojis = { "👍", "⭐", "🎉", "🔥", "👀" };

    public static string CodePreview(string code, int maxLines = 14)
    {
        var lines = (code ?? "").Replace("\r\n", "\n").Split('\n');
        var head = string.Join('\n', lines.Take(maxLines));
        return lines.Length > maxLines ? head + "\n…" : head;
    }

    /// <summary>Load a post + the context needed to authorize an action on it.</summary>
    public async Task<(Post? post, Board? board, BoardMembership? viewer, BoardMembership? author, Submission? latest)>
        LoadPostContextAsync(int postId, int viewerUserId)
    {
        var post = await _db.Posts
            .Include(p => p.Reactions)
            .Include(p => p.Comments).ThenInclude(c => c.User)
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.Id == postId);
        if (post is null) return (null, null, null, null, null);

        var board = await _db.Boards.Include(b => b.Members).ThenInclude(m => m.User)
            .FirstOrDefaultAsync(b => b.Id == post.BoardId);
        var viewer = board?.Members.FirstOrDefault(m => m.UserId == viewerUserId);
        var author = board?.Members.FirstOrDefault(m => m.UserId == post.UserId);
        var latest = await _db.Submissions
            .Where(s => s.ProblemId == post.ProblemId && s.UserId == post.UserId)
            .OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id)
            .FirstOrDefaultAsync();
        return (post, board, viewer, author, latest);
    }

    /// <summary>Whether a peer (non-staff, non-author) may see a student's work for a problem.</summary>
    public static bool PeerCanSee(bool examMode, bool hiddenByTeacher, bool hiddenByStudent) =>
        !examMode && !hiddenByTeacher && !hiddenByStudent;

    public bool CanViewPost(Board board, int viewerUserId, bool viewerIsStaff, BoardMembership author, Post post)
    {
        if (viewerIsStaff || author.UserId == viewerUserId) return true;
        return PeerCanSee(board.ExamMode, author.HiddenByTeacher, post.HiddenByStudent);
    }

    public async Task<WallDto?> BuildWallAsync(int boardId, int viewerUserId)
    {
        var board = await _db.Boards
            .Include(b => b.Members).ThenInclude(m => m.User)
            .Include(b => b.Problems)
            .FirstOrDefaultAsync(b => b.Id == boardId);
        if (board is null) return null;

        var viewer = board.Members.FirstOrDefault(m => m.UserId == viewerUserId);
        if (viewer is null) return null;
        bool staff = _vis.IsStaff(viewer.Role);

        var problems = board.Problems.OrderBy(p => p.Position).ThenBy(p => p.Id).ToList();
        var problemIds = problems.Select(p => p.Id).ToHashSet();
        var langByProblem = problems.ToDictionary(p => p.Id, p => Languages.Default(p.AllowedLanguages));

        var posts = await _db.Posts
            .Where(p => p.BoardId == boardId)
            .Include(p => p.Reactions)
            .Include(p => p.Comments).ThenInclude(c => c.User)
            .Include(p => p.User)
            .ToListAsync();

        var subs = await _db.Submissions
            .Where(s => problemIds.Contains(s.ProblemId))
            .ToListAsync();
        var latestByKey = subs
            .GroupBy(s => (s.UserId, s.ProblemId))
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    Latest = g.OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id).First(),
                    Attempts = g.Count(),
                    BestScore = g.Max(s => s.Score),
                });

        var membersById = board.Members.ToDictionary(m => m.UserId);
        var dtos = new List<WallPostDto>();

        foreach (var post in posts.Where(p => problemIds.Contains(p.ProblemId)))
        {
            if (!membersById.TryGetValue(post.UserId, out var author)) continue;
            latestByKey.TryGetValue((post.UserId, post.ProblemId), out var agg);
            var latest = agg?.Latest;

            bool mine = post.UserId == viewerUserId;

            // Exam mode removes peers' cards entirely for a student; other hides just redact.
            if (!staff && !mine && board.ExamMode) continue;

            bool full = CanViewPost(board, viewerUserId, staff, author, post);

            var reactions = post.Reactions
                .GroupBy(r => r.Emoji)
                .Select(g => new ReactionDto(g.Key, g.Count(), g.Any(r => r.UserId == viewerUserId)))
                .OrderByDescending(r => r.Count)
                .ToList();

            var comments = full
                ? post.Comments
                    .OrderBy(c => c.CreatedAt)
                    .Select(c => new CommentDto(
                        c.Id, c.UserId, c.User?.DisplayName ?? "user", c.Body, c.CreatedAt,
                        c.UserId == viewerUserId || board.OwnerId == viewerUserId))
                    .ToList()
                : new List<CommentDto>();

            dtos.Add(new WallPostDto(
                post.Id, post.ProblemId, post.UserId,
                author.User!.DisplayName,
                post.Note,
                mine,
                Redacted: !full,
                HiddenByStudent: post.HiddenByStudent,
                Verdict: full ? (latest?.Verdict.ToString() ?? "None") : "Hidden",
                Score: full ? (agg?.BestScore ?? 0) : 0,
                Attempts: agg?.Attempts ?? 0,
                RuntimeMs: full ? (latest?.RuntimeMs ?? 0) : 0,
                MemoryKb: full ? (latest?.MemoryKb ?? 0) : 0,
                Language: langByProblem.GetValueOrDefault(post.ProblemId, "cpp"),
                CodePreview: full && latest is not null ? CodePreview(latest.Code) : null,
                LatestSubmissionId: full && latest is not null ? latest.Id : null,
                UpdatedAt: post.UpdatedAt,
                Reactions: full ? reactions : new List<ReactionDto>(),
                Comments: comments));
        }

        return new WallDto(
            board.Id, board.ExamMode, staff,
            problems.Select(Mapping.ToSummary).ToList(),
            dtos.OrderByDescending(d => d.UpdatedAt).ToList());
    }
}
