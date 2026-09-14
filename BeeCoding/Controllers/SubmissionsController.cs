using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using BeeCoding.Services.Judge;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Controllers;

[ApiController]
[Authorize]
public class SubmissionsController(AppDbContext db, BoardService boards, VisibilityService vis,
    IJudgeQueue queue, RateLimiter rate, IBoardNotifier notifier, AdminAccess admin) : ApiControllerBase
{
    private readonly AppDbContext _db = db;
    private readonly BoardService _boards = boards;
    private readonly VisibilityService _vis = vis;
    private readonly IJudgeQueue _queue = queue;
    private readonly RateLimiter _rate = rate;
    private readonly IBoardNotifier _notifier = notifier;
    private readonly AdminAccess _admin = admin;

    [HttpPost("api/problems/{problemId:int}/submit")]
    public async Task<ActionResult<object>> Submit(int problemId, SubmitDto dto)
    {
        var problem = await _db.Problems.Include(p => p.TestCases).FirstOrDefaultAsync(p => p.Id == problemId);
        if (problem is null) return NotFound();

        var membership = await _boards.GetMembershipAsync(problem.BoardId, UserId);
        if (membership is null) return Forbid();
        if (string.IsNullOrWhiteSpace(dto.Code)) return BadRequest("Code is empty.");
        if (dto.Code.Length > 200_000) return BadRequest("Code is too large.");
        if (!_rate.TryAcquire(UserId)) return StatusCode(429, "Slow down a moment and try again.");

        var lang = dto.Language is "c" or "cpp" ? dto.Language : Languages.Default(problem.AllowedLanguages);
        if (!Languages.Allows(problem.AllowedLanguages, lang))
            return BadRequest($"This problem only accepts {Languages.Label(problem.AllowedLanguages)}.");

        var sub = new Submission
        {
            ProblemId = problemId,
            UserId = UserId,
            Code = dto.Code,
            Language = lang,
            Status = SubmissionStatus.Queued,
        };
        _db.Submissions.Add(sub);

        // Upsert the wall post for (problem, student) so it exists as soon as they try.
        var post = await _db.Posts.FirstOrDefaultAsync(p => p.ProblemId == problemId && p.UserId == UserId);
        if (post is null)
            _db.Posts.Add(new Post { BoardId = problem.BoardId, ProblemId = problemId, UserId = UserId });
        else
            post.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var tests = problem.TestCases.OrderBy(t => t.Position).ThenBy(t => t.Id)
            .Select(t => new TestSpec(t.Stdin, t.ExpectedStdout, t.Points, t.IsSample)).ToList();
        await _queue.EnqueueGradeAsync(new GradeJob(
            "board", sub.Id, lang, sub.Code,
            problem.TimeLimitMs, problem.MemoryLimitKb, problem.BannedHeaders, problem.BannedSymbols, tests,
            problem.InputFileName));
        return Accepted(new { submissionId = sub.Id });
    }

    [HttpGet("api/problems/{problemId:int}/submissions")]
    public async Task<ActionResult<IEnumerable<SubmissionDto>>> ListForProblem(int problemId)
    {
        var problem = await _db.Problems.FirstOrDefaultAsync(p => p.Id == problemId);
        if (problem is null) return NotFound();

        var board = await _db.Boards.Include(b => b.Members).ThenInclude(m => m.User)
            .FirstOrDefaultAsync(b => b.Id == problem.BoardId);
        if (board is null) return NotFound();
        var viewer = board.Members.FirstOrDefault(m => m.UserId == UserId);
        bool isAdmin = IsAdminUser(_admin);
        if (viewer is null && !isAdmin) return Forbid();
        bool staff = viewer is not null ? _vis.IsStaff(viewer.Role) : isAdmin;

        var subs = await _db.Submissions
            .Where(s => s.ProblemId == problemId)
            .OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id)
            .ToListAsync();

        var membersById = board.Members.ToDictionary(m => m.UserId);
        var result = new List<SubmissionDto>();
        foreach (var s in subs)
        {
            if (s.UserId == UserId)
            {
                result.Add(Mapping.ToDto(s, UserId, canSeeCode: true, viewer?.User?.DisplayName ?? "You", isStaff: staff));
                continue;
            }
            if (!membersById.TryGetValue(s.UserId, out var author)) continue;
            if (!_vis.CanSeePeerRow(UserId, staff, board, author)) continue;
            bool full = _vis.CanSeePeerSubmission(UserId, staff, board, author, s);
            result.Add(Mapping.ToDto(s, UserId, full, author.User!.DisplayName, isStaff: staff));
        }
        return result;
    }

    [HttpGet("api/submissions/{id:int}")]
    public async Task<ActionResult<SubmissionDto>> Get(int id)
    {
        var s = await _db.Submissions.Include(x => x.Problem).Include(x => x.User)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (s is null) return NotFound();

        // The problem (or its board) may since have been soft-deleted — nothing left to
        // check staff-ness against then, but the author can still see their own basics.
        Board? board = null;
        bool staff = false;
        if (s.Problem is not null)
        {
            board = await _db.Boards.Include(b => b.Members).ThenInclude(m => m.User)
                .FirstOrDefaultAsync(b => b.Id == s.Problem.BoardId);
            var selfMembership = board?.Members.FirstOrDefault(m => m.UserId == UserId);
            bool isAdminForBoard = IsAdminUser(_admin);
            staff = (selfMembership is not null && _vis.IsStaff(selfMembership.Role)) || isAdminForBoard;
        }

        if (s.UserId == UserId)
            return Mapping.ToDto(s, UserId, canSeeCode: true, s.User!.DisplayName, isStaff: staff);

        if (board is null) return NotFound();
        var viewer = board.Members.FirstOrDefault(m => m.UserId == UserId);
        if (viewer is null && !IsAdminUser(_admin)) return Forbid();

        var author = board.Members.FirstOrDefault(m => m.UserId == s.UserId);
        if (author is null || !_vis.CanSeePeerRow(UserId, staff, board, author)) return Forbid();
        bool full = _vis.CanSeePeerSubmission(UserId, staff, board, author, s);
        return Mapping.ToDto(s, UserId, full, author.User!.DisplayName, isStaff: staff);
    }

    /// <summary>Feature 4: a student hides/unhides their own submission from other students.</summary>
    [HttpPatch("api/submissions/{id:int}")]
    public async Task<ActionResult<SubmissionDto>> Update(int id, UpdateSubmissionDto dto)
    {
        var s = await _db.Submissions.Include(x => x.User).Include(x => x.Problem)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (s is null) return NotFound();
        if (s.UserId != UserId) return Forbid();

        s.HiddenByStudent = dto.HiddenByStudent;
        await _db.SaveChangesAsync();
        if (s.Problem is not null)   // the problem may since have been soft-deleted
            await _notifier.ProgressChangedAsync(s.Problem.BoardId, s.ProblemId, s.UserId);
        return Mapping.ToDto(s, UserId, canSeeCode: true, s.User!.DisplayName);
    }
}
