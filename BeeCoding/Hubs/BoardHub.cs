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

    private const string DraftAtKey = "draftAt";
    private const string PresentingKey = "presenting";
    private const int MinDraftGapMs = 250;

    public static string BoardGroup(int boardId) => $"board-{boardId}";
    public static string StaffGroup(int boardId) => $"board-{boardId}-staff";

    public async Task JoinBoard(int boardId)
    {
        var membership = await _db.BoardMemberships
            .FirstOrDefaultAsync(m => m.BoardId == boardId && m.UserId == UserId);
        bool isAdmin = membership is null && _admin.HasAdminPowers(Context.User!.FindFirstValue(ClaimTypes.Email));
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
        await ReleaseLectureAsync();
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
        // The editors send at most every ~600 ms. A faster sender is a broken or hostile client, and each push costs queries and a
        // broadcast of up to 200 KB to the whole class: drop it (the next push carries the newer buffer anyway).
        var now = Environment.TickCount64;
        if (Context.Items.TryGetValue(DraftAtKey, out var last) && last is long at && now - at < MinDraftGapMs) return;
        Context.Items[DraftAtKey] = now;

        var membership = await _db.BoardMemberships
            .FirstOrDefaultAsync(m => m.BoardId == boardId && m.UserId == UserId);
        if (membership is null) return;
        if (problemId != 0 && !await _db.Problems.AnyAsync(p => p.Id == problemId && p.BoardId == boardId)) return;   // problemId is client-chosen: drafts exist only for this board's problems; 0 = the Live Code scratch buffer
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
    /// Staff-only, and only while the board has LecturingMode on. A board has ONE presenter at
    /// a time: a push by another teacher is refused unless <paramref name="takeOver"/> is set, so two teachers never
    /// overwrite each other on the students' screens. On the Live Code page (problem 0) the presenter keeps the slot until
    /// their connection leaves (<see cref="ReleaseLectureAsync"/>), however long they stay quiet; on a problem page, which
    /// has no take-over button, it frees after <see cref="PresenterIdleSeconds"/> without a push.
    /// </summary>
    public async Task<LecturePushResult> PushLecture(int boardId, int problemId, string code, string language, string? stdin = null, bool takeOver = false)
    {
        var m = await _db.BoardMemberships
            .FirstOrDefaultAsync(x => x.BoardId == boardId && x.UserId == UserId);
        if (m is null || m.Role is not (MembershipRole.Owner or MembershipRole.Teacher)) return new(false, 0, "");
        var lecturingMode = await _db.Boards.Where(b => b.Id == boardId)
            .Select(b => (bool?)b.LecturingMode).FirstOrDefaultAsync() ?? false;   // false if the board was deleted
        if (!lecturingMode) return new(false, 0, "");

        var current = await _lectures.GetAsync(boardId, problemId);
        if (!takeOver && current is { TeacherId: > 0 } && current.TeacherId != UserId
            && (problemId == 0 || (DateTime.UtcNow - current.UpdatedAt).TotalSeconds < PresenterIdleSeconds))
            return new(false, current.TeacherId, current.TeacherName);

        if (code is { Length: > 200_000 }) code = code[..200_000];
        if (stdin is { Length: > 20_000 }) stdin = stdin[..20_000];

        var name = Context.User!.FindFirstValue(ClaimTypes.Name) ?? "teacher";
        var lec = await _lectures.SetAsync(boardId, problemId, code ?? "", language is "c" or "cpp" ? language : "cpp", name, stdin ?? "", UserId);
        Context.Items[PresentingKey] = (boardId, problemId);
        await Clients.Group(BoardGroup(boardId)).SendAsync("lectureUpdated", lec);
        return new(true, UserId, name);
    }

    /// <summary>The presenter's connection is leaving: free the slot (teacher id 0) but keep the code on the students' screens.</summary>
    // shortcut: keyed by teacher, so closing one of a presenter's two tabs frees the slot until the other pushes again.
    private async Task ReleaseLectureAsync()
    {
        if (!Context.Items.Remove(PresentingKey, out var v) || v is not ValueTuple<int, int> (var boardId, var problemId)) return;
        var l = await _lectures.GetAsync(boardId, problemId);
        if (l is null || l.TeacherId != UserId) return;   // someone took over in the meantime
        var freed = await _lectures.SetAsync(boardId, problemId, l.Code, l.Language, l.TeacherName, l.Stdin, 0);
        await Clients.Group(BoardGroup(boardId)).SendAsync("lectureUpdated", freed);
    }

    /// <summary>On a problem page: a presenter that hasn't pushed for this long is considered gone; another
    /// teacher's next push then takes the slot without an explicit take-over.</summary>
    public const int PresenterIdleSeconds = 90;

    /// <summary>The teacher's live buffer. Members of the board (and platform admins) only: board ids are small consecutive numbers,
    /// so without this any signed-in account could read the code of every board that is lecturing.</summary>
    public async Task<Lecture?> GetLecture(int boardId, int problemId)
    {
        bool member = await _db.BoardMemberships.AnyAsync(m => m.BoardId == boardId && m.UserId == UserId);
        if (!member && !_admin.HasAdminPowers(Context.User!.FindFirstValue(ClaimTypes.Email))) return null;
        return await _lectures.GetAsync(boardId, problemId);
    }

    private async Task<bool> DraftVisibleToPeersAsync(int boardId, int problemId, BoardMembership me)
    {
        var boardExam = await _db.Boards.Where(b => b.Id == boardId)
            .Select(b => (bool?)b.ExamMode).FirstOrDefaultAsync();
        // A deleted board is treated as locked down; a grouped problem uses its group's exam mode.
        var groupExam = await _db.Problems.Where(p => p.Id == problemId)
            .Select(p => p.Group != null ? (bool?)p.Group.ExamMode : null).FirstOrDefaultAsync();
        var examMode = boardExam is null || (groupExam ?? boardExam.Value);
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
        {
            // A student who was removed from the board may still have a lingering draft.
            var memberIds = (await _db.BoardMemberships.Where(m => m.BoardId == boardId).Select(m => m.UserId).ToListAsync()).ToHashSet();
            return (await _drafts.ForBoardAsync(boardId)).Where(d => memberIds.Contains(d.UserId));
        }

        if (me.Board is null) return Enumerable.Empty<Draft>();   // deleted board -> locked down
        // Per-problem exam mode: a group's own flag, else the board's (for ungrouped problems).
        var groupExam = await _db.Problems.Where(p => p.BoardId == boardId)
            .Select(p => new { p.Id, Exam = p.Group != null ? (bool?)p.Group.ExamMode : null }).ToListAsync();
        var examByProblem = groupExam.ToDictionary(p => p.Id, p => p.Exam ?? me.Board.ExamMode);
        var hiddenUserIds = await _db.BoardMemberships
            .Where(m => m.BoardId == boardId && m.HiddenByTeacher)
            .Select(m => m.UserId).ToListAsync();
        var hiddenPosts = await _db.Posts
            .Where(p => p.BoardId == boardId && p.HiddenByStudent)
            .Select(p => new { p.ProblemId, p.UserId }).ToListAsync();
        var hiddenPostSet = hiddenPosts.Select(p => (p.ProblemId, p.UserId)).ToHashSet();

        return (await _drafts.ForBoardAsync(boardId))
            .Where(d => d.UserId != UserId
                        && !examByProblem.GetValueOrDefault(d.ProblemId, me.Board.ExamMode)
                        && !hiddenUserIds.Contains(d.UserId)
                        && !hiddenPostSet.Contains((d.ProblemId, d.UserId)));
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await ReleaseLectureAsync();
        var boardId = await _presence.RemoveAsync(Context.ConnectionId);
        if (boardId is int b)
            await Clients.Group(BoardGroup(b)).SendAsync("presence", await _presence.ForBoardAsync(b));
        await base.OnDisconnectedAsync(exception);
    }
}

/// <summary>Outcome of <see cref="BoardHub.PushLecture"/>: whether the push was accepted, and who holds the presenter slot.</summary>
public record LecturePushResult(bool Accepted, int PresenterId, string PresenterName);
