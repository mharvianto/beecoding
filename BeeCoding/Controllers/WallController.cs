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
public class WallController(AppDbContext db, WallService wall, VisibilityService vis,
    IBoardNotifier notifier, BoardService boards) : ApiControllerBase
{
    private readonly AppDbContext _db = db;
    private readonly WallService _wall = wall;
    private readonly VisibilityService _vis = vis;
    private readonly IBoardNotifier _notifier = notifier;
    private readonly BoardService _boards = boards;

    [HttpGet("api/boards/{slug}/wall")]
    public async Task<ActionResult<WallDto>> Get(string slug)
    {
        var boardId = await _boards.ResolveBoardIdAsync(slug);
        if (boardId is null) return NotFound();
        var wall = await _wall.BuildWallAsync(boardId.Value, UserId);
        return wall is null ? Forbid() : wall;
    }

    /// <summary>Ensure a wall post exists for the current student + problem (called when they open it).</summary>
    [HttpPost("api/problems/{problemId:int}/post")]
    public async Task<ActionResult<object>> EnsurePost(int problemId)
    {
        var problem = await _db.Problems.FirstOrDefaultAsync(p => p.Id == problemId);
        if (problem is null) return NotFound();
        var membership = await _db.BoardMemberships
            .FirstOrDefaultAsync(m => m.BoardId == problem.BoardId && m.UserId == UserId);
        if (membership is null) return Forbid();

        var post = await _db.Posts.FirstOrDefaultAsync(p => p.ProblemId == problemId && p.UserId == UserId);
        if (post is null)
        {
            post = new Post { BoardId = problem.BoardId, ProblemId = problemId, UserId = UserId };
            _db.Posts.Add(post);
            await _db.SaveChangesAsync();
            await _notifier.WallChangedAsync(problem.BoardId);
        }
        return new { postId = post.Id, hiddenByStudent = post.HiddenByStudent };
    }

    [HttpPut("api/posts/{postId:int}/note")]
    public async Task<IActionResult> SetNote(int postId, NoteDto dto)
    {
        var post = await _db.Posts.FirstOrDefaultAsync(p => p.Id == postId);
        if (post is null) return NotFound();
        if (post.UserId != UserId) return Forbid();

        post.Note = (dto.Note ?? "").Trim();
        if (post.Note.Length > 500) post.Note = post.Note[..500];
        post.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _notifier.WallChangedAsync(post.BoardId);
        return NoContent();
    }

    /// <summary>Author hides/shows this problem's work (live draft + card) from peers.</summary>
    [HttpPatch("api/posts/{postId:int}/visibility")]
    public async Task<IActionResult> SetVisibility(int postId, UpdateSubmissionDto dto)
    {
        var post = await _db.Posts.FirstOrDefaultAsync(p => p.Id == postId);
        if (post is null) return NotFound();
        if (post.UserId != UserId) return Forbid();

        post.HiddenByStudent = dto.HiddenByStudent;
        post.UpdatedAt = DateTime.UtcNow;
        // keep submissions in sync so the submissions list & older checks agree
        await _db.Submissions
            .Where(s => s.ProblemId == post.ProblemId && s.UserId == post.UserId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.HiddenByStudent, dto.HiddenByStudent));
        await _db.SaveChangesAsync();
        await _notifier.WallChangedAsync(post.BoardId);
        return NoContent();
    }

    /// <summary>The author acknowledges the reactions/comments on their own post.</summary>
    [HttpPost("api/posts/{postId:int}/seen")]
    public async Task<IActionResult> MarkSeen(int postId)
    {
        var post = await _db.Posts.FirstOrDefaultAsync(p => p.Id == postId);
        if (post is null) return NotFound();
        if (post.UserId != UserId) return Forbid();

        post.ActivitySeenAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("api/posts/{postId:int}/reactions")]
    public async Task<ActionResult<IEnumerable<ReactionDto>>> React(int postId, ReactDto dto)
    {
        var emoji = dto.Emoji ?? "";
        if (!WallService.AllowedEmojis.Contains(emoji)) return BadRequest("Unsupported reaction.");

        var (post, board, viewer, author, _) = await _wall.LoadPostContextAsync(postId, UserId);
        if (post is null || board is null || viewer is null || author is null) return NotFound();
        bool staff = _vis.IsStaff(viewer.Role);
        if (!_wall.CanViewPost(await GroupAccess.ExamModeAsync(_db, board, post.ProblemId), UserId, staff, author, post)) return Forbid();

        var existing = await _db.PostReactions
            .FirstOrDefaultAsync(r => r.PostId == postId && r.UserId == UserId && r.Emoji == emoji);
        if (existing is null)
            _db.PostReactions.Add(new PostReaction { PostId = postId, UserId = UserId, Emoji = emoji });
        else
            _db.PostReactions.Remove(existing);
        await _db.SaveChangesAsync();
        await _notifier.WallChangedAsync(post.BoardId);

        var reactions = await _db.PostReactions.Where(r => r.PostId == postId).ToListAsync();
        return reactions
            .GroupBy(r => r.Emoji)
            .Select(g => new ReactionDto(g.Key, g.Count(), g.Any(r => r.UserId == UserId)))
            .OrderByDescending(r => r.Count)
            .ToList();
    }

    [HttpPost("api/posts/{postId:int}/comments")]
    public async Task<ActionResult<CommentDto>> Comment(int postId, CommentBodyDto dto)
    {
        var body = (dto.Body ?? "").Trim();
        if (body.Length == 0) return BadRequest("Comment is empty.");
        if (body.Length > 2000) body = body[..2000];

        var (post, board, viewer, author, _) = await _wall.LoadPostContextAsync(postId, UserId);
        if (post is null || board is null || viewer is null || author is null) return NotFound();
        bool staff = _vis.IsStaff(viewer.Role);
        if (!_wall.CanViewPost(await GroupAccess.ExamModeAsync(_db, board, post.ProblemId), UserId, staff, author, post)) return Forbid();

        var c = new PostComment { PostId = postId, UserId = UserId, Body = body };
        _db.PostComments.Add(c);
        await _db.SaveChangesAsync();
        await _notifier.WallChangedAsync(post.BoardId);

        var me = await _db.Users.FindAsync(UserId);
        return new CommentDto(c.Id, UserId, me?.DisplayName ?? "user", c.Body, c.CreatedAt, true);
    }

    [HttpDelete("api/posts/{postId:int}/comments/{commentId:int}")]
    public async Task<IActionResult> DeleteComment(int postId, int commentId)
    {
        var c = await _db.PostComments.Include(x => x.Post)
            .FirstOrDefaultAsync(x => x.Id == commentId && x.PostId == postId);
        if (c is null) return NotFound();

        bool isBoardOwner = await _db.Boards.AnyAsync(b => b.Id == c.Post!.BoardId && b.OwnerId == UserId);
        if (c.UserId != UserId && !isBoardOwner) return Forbid();

        _db.PostComments.Remove(c);
        await _db.SaveChangesAsync();
        await _notifier.WallChangedAsync(c.Post!.BoardId);
        return NoContent();
    }
}
