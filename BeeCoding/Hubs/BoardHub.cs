using System.Security.Claims;
using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Hubs;

[Authorize]
public class BoardHub(AppDbContext db, IPresenceTracker presence, IDraftStore drafts, ILectureStore lectures, AdminAccess admin) : Hub
{
    private readonly AppDbContext _db = db;
    private readonly IPresenceTracker _presence = presence;
    private readonly IDraftStore _drafts = drafts;
    private readonly ILectureStore _lectures = lectures;
    private readonly AdminAccess _admin = admin;

    private int UserId => int.Parse(Context.User!.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static string BoardGroup(int boardId) => $"board-{boardId}";
    public static string StaffGroup(int boardId) => $"board-{boardId}-staff";

    public async Task JoinBoard(int boardId)
    {
        var membership = await _db.BoardMemberships
            .FirstOrDefaultAsync(m => m.BoardId == boardId && m.UserId == UserId);
        bool isAdmin = membership is null && _admin.IsAdminEmail(Context.User!.FindFirstValue(ClaimTypes.Email));
        if (membership is null && !isAdmin) throw new HubException("Not a member of this board");

        var name = Context.User!.FindFirstValue(ClaimTypes.Name) ?? "user";
        bool isStaff = isAdmin || membership!.Role is MembershipRole.Owner or MembershipRole.Teacher;

        await Groups.AddToGroupAsync(Context.ConnectionId, BoardGroup(boardId));
        if (isStaff) await Groups.AddToGroupAsync(Context.ConnectionId, StaffGroup(boardId));

        await _presence.AddAsync(Context.ConnectionId, boardId, UserId, name, isStaff);
        await Clients.Group(BoardGroup(boardId)).SendAsync("presence", await _presence.ForBoardAsync(boardId));
    }

    public async Task LeaveBoard(int boardId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, BoardGroup(boardId));
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, StaffGroup(boardId));
        await _presence.RemoveAsync(Context.ConnectionId);
        await Clients.Group(BoardGroup(boardId)).SendAsync("presence", await _presence.ForBoardAsync(boardId));
    }

    /// <summary>
    /// Student streams their current editor buffer. Broadcast to staff only so a teacher
    /// can watch progress without the student running or submitting.
    /// </summary>
    public async Task PushDraft(int boardId, int problemId, string code)
    {
        var membership = await _db.BoardMemberships
            .FirstOrDefaultAsync(m => m.BoardId == boardId && m.UserId == UserId);
        if (membership is null) return;
        if (code is { Length: > 200_000 }) code = code[..200_000];

        var name = Context.User!.FindFirstValue(ClaimTypes.Name) ?? "user";
        var draft = await _drafts.SetAsync(boardId, problemId, UserId, name, code ?? "");

        // Staff always see live code; peers see it too unless exam mode / teacher-hidden /
        // the student hid this problem's work. (Staff are in BoardGroup, so in the visible
        // case they just receive the event twice — the client handler is idempotent.)
        await Clients.Group(StaffGroup(boardId)).SendAsync("draftUpdated", draft);
        if (await DraftVisibleToPeersAsync(boardId, problemId, membership))
            await Clients.Group(BoardGroup(boardId)).SendAsync("draftUpdated", draft);
    }

    /// <summary>
    /// Lecturing mode: a teacher streams their own editor buffer so students can follow along.
    /// Staff-only, and only while the board has LecturingMode on.
    /// </summary>
    public async Task PushLecture(int boardId, int problemId, string code, string language, string? stdin = null)
    {
        var m = await _db.BoardMemberships
            .FirstOrDefaultAsync(x => x.BoardId == boardId && x.UserId == UserId);
        if (m is null || m.Role is not (MembershipRole.Owner or MembershipRole.Teacher)) return;
        var lecturingMode = await _db.Boards.Where(b => b.Id == boardId)
            .Select(b => (bool?)b.LecturingMode).FirstOrDefaultAsync() ?? false;   // false if the board was deleted
        if (!lecturingMode) return;
        if (code is { Length: > 200_000 }) code = code[..200_000];
        if (stdin is { Length: > 20_000 }) stdin = stdin[..20_000];

        var name = Context.User!.FindFirstValue(ClaimTypes.Name) ?? "teacher";
        var lec = await _lectures.SetAsync(boardId, problemId, code ?? "", language is "c" or "cpp" ? language : "cpp", name, stdin ?? "");
        await Clients.Group(BoardGroup(boardId)).SendAsync("lectureUpdated", lec);
    }

    public async Task<Lecture?> GetLecture(int boardId, int problemId) =>
        await _lectures.GetAsync(boardId, problemId);

    private async Task<bool> DraftVisibleToPeersAsync(int boardId, int problemId, BoardMembership me)
    {
        var examMode = await _db.Boards.Where(b => b.Id == boardId)
            .Select(b => (bool?)b.ExamMode).FirstOrDefaultAsync() ?? true;   // treat a deleted board as locked down
        var hiddenByStudent = await _db.Posts
            .Where(p => p.ProblemId == problemId && p.UserId == UserId)
            .Select(p => (bool?)p.HiddenByStudent).FirstOrDefaultAsync() ?? false;
        return Services.WallService.PeerCanSee(examMode, me.HiddenByTeacher, hiddenByStudent);
    }

    /// <summary>Current draft snapshot: full for staff, peer-visible-only for students.</summary>
    public async Task<IEnumerable<Draft>> GetDrafts(int boardId)
    {
        var me = await _db.BoardMemberships
            .Include(m => m.Board)
            .FirstOrDefaultAsync(m => m.BoardId == boardId && m.UserId == UserId);
        if (me is null) return Enumerable.Empty<Draft>();
        if (me.Role is MembershipRole.Owner or MembershipRole.Teacher)
            return await _drafts.ForBoardAsync(boardId);

        if (me.Board is null || me.Board.ExamMode) return Enumerable.Empty<Draft>();   // deleted board -> locked down
        var hiddenUserIds = await _db.BoardMemberships
            .Where(m => m.BoardId == boardId && m.HiddenByTeacher)
            .Select(m => m.UserId).ToListAsync();
        var hiddenPosts = await _db.Posts
            .Where(p => p.BoardId == boardId && p.HiddenByStudent)
            .Select(p => new { p.ProblemId, p.UserId }).ToListAsync();
        var hiddenPostSet = hiddenPosts.Select(p => (p.ProblemId, p.UserId)).ToHashSet();

        return (await _drafts.ForBoardAsync(boardId))
            .Where(d => d.UserId != UserId
                        && !hiddenUserIds.Contains(d.UserId)
                        && !hiddenPostSet.Contains((d.ProblemId, d.UserId)));
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var boardId = await _presence.RemoveAsync(Context.ConnectionId);
        if (boardId is int b)
            await Clients.Group(BoardGroup(b)).SendAsync("presence", await _presence.ForBoardAsync(b));
        await base.OnDisconnectedAsync(exception);
    }
}
