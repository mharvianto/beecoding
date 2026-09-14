using BeeCoding.Data;
using BeeCoding.Models;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Services;

public class BoardService(AppDbContext db, VisibilityService vis)
{
    private readonly AppDbContext _db = db;
    private readonly VisibilityService _vis = vis;
    private static readonly char[] CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray();

    private string RandomCode(int len) =>
        string.Concat(Enumerable.Range(0, len).Select(_ => CodeAlphabet[Random.Shared.Next(CodeAlphabet.Length)]));

    // IgnoreQueryFilters(): JoinCode/Slug are unique across every Board row, soft-deleted
    // or not, so a freshly-generated value must be checked against all of them too.
    public async Task<string> GenerateJoinCodeAsync()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var code = RandomCode(6);
            if (!await _db.Boards.IgnoreQueryFilters().AnyAsync(b => b.JoinCode == code)) return code;
        }
        throw new InvalidOperationException("could not allocate a unique join code");
    }

    /// <summary>Unguessable public id for URLs (~61 bits).</summary>
    public async Task<string> GenerateSlugAsync()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var slug = RandomCode(12);
            if (!await _db.Boards.IgnoreQueryFilters().AnyAsync(b => b.Slug == slug)) return slug;
        }
        throw new InvalidOperationException("could not allocate a unique slug");
    }

    public Task<BoardMembership?> GetMembershipAsync(int boardId, int userId) =>
        _db.BoardMemberships.FirstOrDefaultAsync(m => m.BoardId == boardId && m.UserId == userId);

    /// <summary>Resolve a public slug to the internal board id, or null if unknown.</summary>
    public async Task<int?> ResolveBoardIdAsync(string slug) =>
        await _db.Boards.Where(b => b.Slug == slug).Select(b => (int?)b.Id).FirstOrDefaultAsync();

    public async Task<ProgressBoardDto?> BuildProgressAsync(int boardId, int viewerUserId, bool viewerIsAdmin = false)
    {
        var board = await _db.Boards
            .Include(b => b.Members).ThenInclude(m => m.User)
            .Include(b => b.Problems)
            .FirstOrDefaultAsync(b => b.Id == boardId);
        if (board is null) return null;

        var viewer = board.Members.FirstOrDefault(m => m.UserId == viewerUserId);
        if (viewer is null && !viewerIsAdmin) return null;
        bool viewerIsStaff = viewerIsAdmin || (viewer is not null && _vis.IsStaff(viewer.Role));

        var problems = board.Problems.OrderBy(p => p.Position).ThenBy(p => p.Id).ToList();
        var problemIds = problems.Select(p => p.Id).ToList();

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

        var studentMembers = board.Members
            .Where(m => m.Role == MembershipRole.Student)
            .OrderBy(m => m.User!.DisplayName)
            .ToList();

        var visibleStudents = studentMembers
            .Where(m => _vis.CanSeePeerRow(viewerUserId, viewerIsStaff, board, m))
            .ToList();

        var studentDtos = visibleStudents
            .Select(m => new MemberDto(
                m.UserId, m.User!.DisplayName, m.Role.ToString(),
                viewerIsStaff && m.HiddenByTeacher))
            .ToList();

        var cells = new List<ProgressCellDto>();
        foreach (var m in visibleStudents)
        {
            foreach (var p in problems)
            {
                if (!latestByKey.TryGetValue((m.UserId, p.Id), out var agg)) continue;

                bool canSeeFull = _vis.CanSeePeerSubmission(viewerUserId, viewerIsStaff, board, m, agg.Latest);
                if (canSeeFull)
                {
                    cells.Add(new ProgressCellDto(
                        m.UserId, p.Id,
                        agg.Latest.Verdict.ToString(),
                        agg.BestScore,
                        agg.Attempts,
                        Redacted: false,
                        Latest: agg.Latest.Verdict == Verdict.Accepted,
                        LastAt: agg.Latest.CreatedAt,
                        LatestSubmissionId: agg.Latest.Id));
                }
                else
                {
                    cells.Add(new ProgressCellDto(
                        m.UserId, p.Id, "Hidden", 0, agg.Attempts,
                        Redacted: true, Latest: false, LastAt: agg.Latest.CreatedAt));
                }
            }
        }

        return new ProgressBoardDto(
            board.Id, board.ExamMode, viewerIsStaff,
            studentDtos,
            problems.Select(Mapping.ToSummary).ToList(),
            cells);
    }
}
