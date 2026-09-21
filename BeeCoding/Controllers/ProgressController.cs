using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace BeeCoding.Controllers;

// RankDelta: yesterday's rank minus today's rank in the SAME scope, "all time" period only
// (see ProgressController.GetYesterdayRanksAsync) — positive = moved up, negative = moved
// down, null = not computed (a windowed period) or the user has no solves before today.
public record LeaderRowDto(int Rank, int UserId, string DisplayName, string Role, int Xp, int Level, bool Me, int? RankDelta = null, int SolvedCount = 0, int? XpDelta = null);
public record LeaderboardPageDto(List<LeaderRowDto> Rows, int Total, int Page, int PageSize);
public record MyOrgDto(int Id, string Name, string Slug);
public record MyBoardDto(int Id, string Slug, string Title);

[ApiController]
[Authorize]
public class ProgressController(AppDbContext db, ProgressService progress, IMemoryCache cache) : ApiControllerBase
{
    private readonly AppDbContext _db = db;
    private readonly ProgressService _progress = progress;
    private readonly IMemoryCache _cache = cache;

    private static DateOnly? ParseLocalDay(string? s) =>
        DateOnly.TryParseExact(s, "yyyy-MM-dd", out var d) ? d : null;

    [HttpGet("api/me/progress")]
    public Task<ProgressDto> Mine([FromQuery] string? localDay = null) => _progress.GetAsync(UserId, ParseLocalDay(localDay));

    /// <summary>Personal dashboard: progress aggregated across every board the user has
    /// joined plus practice/bank activity — not scoped to any one board (see
    /// BoardsController.Stats for the teacher's per-board equivalent).</summary>
    [HttpGet("api/me/dashboard")]
    public async Task<ActionResult<StudentDashboardDto>> Dashboard([FromQuery] string? localDay = null)
    {
        var p = await _progress.GetAsync(UserId, ParseLocalDay(localDay));
        var rankedUsers = await _db.Users.CountAsync(u => u.Xp > 0);
        var higher = await _db.Users.CountAsync(u => u.Xp > p.Xp);
        var rank = p.Xp > 0 ? higher + 1 : 0;
        var boardsJoined = await _db.BoardMemberships.CountAsync(m => m.UserId == UserId);

        var boardVerdicts = await _db.Submissions.Where(s => s.UserId == UserId)
            .Select(s => new { s.Verdict, s.Score }).ToListAsync();
        var bankVerdicts = await _db.BankSubmissions.Where(s => s.UserId == UserId)
            .Select(s => new { s.Verdict, s.Score }).ToListAsync();
        var totalAttempts = boardVerdicts.Count + bankVerdicts.Count;
        var accepted = boardVerdicts.Count(x => x.Verdict == Verdict.Accepted && x.Score >= 1.0)
                     + bankVerdicts.Count(x => x.Verdict == Verdict.Accepted && x.Score >= 1.0);

        return new StudentDashboardDto(p.Xp, p.Level, p.LevelStartXp, p.NextLevelXp, p.SolvedCount,
            rank, rankedUsers, boardsJoined, totalAttempts, accepted, p.Streak, p.LongestStreak, p.MaxSolvedInADay);
    }

    /// <summary>Attempts + solves (first-time accepts, matching XP awards) across every
    /// board + practice/bank, with a selectable bucket size (hour/day/week) — same
    /// bucketing as the admin dashboard's engagement chart (see TimeBucketing).</summary>
    [HttpGet("api/me/dashboard/engagement")]
    public async Task<ActionResult<List<MyEngagementPointDto>>> DashboardEngagement(
        [FromQuery] string granularity = "week", [FromQuery] int periods = 12)
    {
        var g = TimeBucketing.NormalizeGranularity(granularity);
        periods = Math.Clamp(periods, 1, g == "hour" ? 168 : g == "day" ? 90 : 52);

        var boardDates = await _db.Submissions.Where(s => s.UserId == UserId).Select(s => s.CreatedAt).ToListAsync();
        var bankDates = await _db.BankSubmissions.Where(s => s.UserId == UserId).Select(s => s.CreatedAt).ToListAsync();
        var solveDates = await _db.SolveRecords.Where(s => s.UserId == UserId).Select(s => s.CreatedAt).ToListAsync();

        var attemptsByBucket = boardDates.Concat(bankDates)
            .GroupBy(dt => TimeBucketing.BucketStart(dt, g)).ToDictionary(x => x.Key, x => x.Count());
        var solvedByBucket = solveDates
            .GroupBy(dt => TimeBucketing.BucketStart(dt, g)).ToDictionary(x => x.Key, x => x.Count());

        var keys = attemptsByBucket.Keys.Union(solvedByBucket.Keys)
            .OrderByDescending(k => k).Take(periods).OrderBy(k => k);

        return keys.Select(k => new MyEngagementPointDto(TimeBucketing.FormatPeriodStart(k, g),
            attemptsByBucket.GetValueOrDefault(k), solvedByBucket.GetValueOrDefault(k))).ToList();
    }

    /// <summary>Topics ordered by lowest accept rate first ("what you're struggling with"),
    /// across board submissions + bank practice submissions.</summary>
    [HttpGet("api/me/dashboard/topics")]
    public async Task<ActionResult<List<AdminTopicStatDto>>> DashboardTopics([FromQuery] int take = 8)
    {
        take = Math.Clamp(take, 1, 50);
        var boardSubs = await _db.Submissions.Where(s => s.UserId == UserId)
            .Select(s => new { s.Verdict, s.Score, Tags = s.Problem!.Tags }).ToListAsync();
        var bankSubs = await _db.BankSubmissions.Where(s => s.UserId == UserId)
            .Select(s => new { s.Verdict, s.Score, Tags = s.BankProblem!.Tags }).ToListAsync();

        var byTag = new Dictionary<string, (int Attempts, int Accepted)>();
        static IEnumerable<string> TagsOf(string? t) =>
            (t ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Select(x => x.ToLowerInvariant()).Distinct();

        foreach (var s in boardSubs.Concat(bankSubs))
        {
            foreach (var tag in TagsOf(s.Tags))
            {
                var (attempts, acc) = byTag.TryGetValue(tag, out var v) ? v : (0, 0);
                attempts++;
                if (s.Verdict == Verdict.Accepted && s.Score >= 1.0) acc++;
                byTag[tag] = (attempts, acc);
            }
        }

        return byTag.Where(kv => kv.Value.Attempts > 0)
            .OrderBy(kv => kv.Value.Accepted / (double)kv.Value.Attempts)
            .ThenByDescending(kv => kv.Value.Attempts)
            .Take(take)
            .Select(kv => new AdminTopicStatDto(kv.Key, kv.Value.Attempts, kv.Value.Accepted,
                kv.Value.Accepted / (double)kv.Value.Attempts))
            .ToList();
    }

    /// <summary>Organizations the caller belongs to (any role) — for the leaderboard's
    /// "my organization" scope picker. Not the same as OrgAccess.ManagedOrgsAsync, which is
    /// for orgs the caller ADMINISTERS.</summary>
    [HttpGet("api/me/organizations")]
    public async Task<ActionResult<List<MyOrgDto>>> MyOrganizations() =>
        await _db.OrganizationMemberships.Where(m => m.UserId == UserId)
            .OrderBy(m => m.Organization!.Name)
            .Select(m => new MyOrgDto(m.Organization!.Id, m.Organization.Name, m.Organization.Slug))
            .ToListAsync();

    /// <summary>Boards the caller belongs to (any role) — for the leaderboard's "this
    /// board" scope picker.</summary>
    [HttpGet("api/me/boards-brief")]
    public async Task<ActionResult<List<MyBoardDto>>> MyBoards() =>
        await _db.BoardMemberships.Where(m => m.UserId == UserId)
            .OrderBy(m => m.Board!.Title)
            .Select(m => new MyBoardDto(m.Board!.Id, m.Board.Slug, m.Board.Title))
            .ToListAsync();

    /// <summary>Ranked by XP, competition-style: tied users share a rank, and the next
    /// distinct XP value's rank accounts for how many were tied above it (1, 1, 3 — not
    /// 1, 1, 2 or 1, 2, 3). `period` narrows to XP earned within a rolling window (via
    /// SolveRecord, since Xp itself is a lifetime total with no history) — "all" uses the
    /// fast denormalized User.Xp column when also unscoped. `organizationId`/`boardId` narrow
    /// to that org's/board's own members (mutually exclusive — pass at most one); the caller
    /// must belong to it (any role) — leaderboard rows name real students, so this is a data
    /// isolation boundary like everywhere else. On the "all time" view, each row also gets
    /// RankDelta vs. yesterday (see GetYesterdayRanksAsync) — not computed for a windowed
    /// period, where "since yesterday" isn't a meaningful comparison.</summary>
    [HttpGet("api/leaderboard")]
    public async Task<ActionResult<LeaderboardPageDto>> Leaderboard(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string period = "all",
        [FromQuery] int? organizationId = null, [FromQuery] int? boardId = null,
        [FromQuery] DateTime? localMidnight = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var skip = (page - 1) * pageSize;

        if (organizationId is int orgId
            && !await _db.OrganizationMemberships.AnyAsync(m => m.OrganizationId == orgId && m.UserId == UserId))
            return Forbid();
        if (boardId is int bId0 && !await _db.BoardMemberships.AnyAsync(m => m.BoardId == bId0 && m.UserId == UserId))
            return Forbid();

        // (UserId, Xp, SolvedCount) for the whole scoped/windowed population — loaded fully
        // (not paginated at the DB level) so rank can be computed once, correctly, over the
        // real ordering; a page is then just a slice of it. Fine at this app's scale (a
        // classroom/institution's worth of users), and it's what already made RankDelta's
        // "yesterday" ranking simple below.
        List<(int UserId, int Xp, int Solved)> scored;
        if (organizationId is null && boardId is null && period == "all")
        {
            var users = await _db.Users.Where(u => u.Xp > 0).Select(u => new { u.Id, u.Xp }).ToListAsync();
            var solvedCounts = await _db.SolveRecords.GroupBy(r => r.UserId)
                .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
            scored = users.Select(u => (u.Id, u.Xp, solvedCounts.GetValueOrDefault(u.Id))).ToList();
        }
        else
        {
            DateTime? cutoff = period == "1m" ? DateTime.UtcNow.AddMonths(-1) : null;   // "all" only other option now

            var records = _db.SolveRecords.AsQueryable();
            if (cutoff is DateTime c) records = records.Where(r => r.CreatedAt >= c);
            if (organizationId is int oid)
            {
                var memberIds = _db.OrganizationMemberships.Where(m => m.OrganizationId == oid).Select(m => m.UserId);
                records = records.Where(r => memberIds.Contains(r.UserId));
            }
            if (boardId is int bId1)
            {
                var memberIds = _db.BoardMemberships.Where(m => m.BoardId == bId1).Select(m => m.UserId);
                records = records.Where(r => memberIds.Contains(r.UserId));
            }

            var grouped = await records.GroupBy(r => r.UserId)
                .Select(g => new { UserId = g.Key, Xp = g.Sum(r => r.XpAwarded), Solved = g.Count() })
                .ToListAsync();
            scored = grouped.Select(g => (g.UserId, g.Xp, g.Solved)).ToList();
        }

        // Stable secondary order by UserId so tied rows don't reshuffle between requests —
        // the previous code only sorted by Xp here, which let pagination be inconsistent
        // for tied users (a row could shift page, or appear twice/not at all across two
        // requests, since EF/SQL make no ordering guarantee among equal keys).
        var ordered = scored.OrderByDescending(x => x.Xp).ThenBy(x => x.UserId).ToList();
        var total = ordered.Count;

        var rankByUser = new Dictionary<int, int>(ordered.Count);
        int prevXp = int.MinValue, prevRank = 0, idx = 0;
        foreach (var x in ordered)
        {
            idx++;
            if (x.Xp != prevXp) { prevRank = idx; prevXp = x.Xp; }
            rankByUser[x.UserId] = prevRank;
        }

        var pageSlice = ordered.Skip(skip).Take(pageSize).ToList();
        var userIds = pageSlice.Select(x => x.UserId).ToList();
        var users2 = await _db.Users.Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.Role }).ToDictionaryAsync(u => u.Id);

        // "Yesterday" is measured against the CALLER's local midnight (localMidnight, sent
        // as an ISO instant so it converts correctly regardless of the viewer's timezone —
        // matches how streak/best-day already key off the student's local day, not UTC's).
        // Falls back to UTC midnight for an older client build or a non-browser caller.
        var yesterdayCutoff = localMidnight?.ToUniversalTime() ?? DateTime.UtcNow.Date;
        var yesterday = period == "all" ? await GetYesterdayRanksAsync(organizationId, boardId, yesterdayCutoff) : null;
        var rows = pageSlice.Select(x =>
        {
            int rank = rankByUser[x.UserId];
            int? delta = yesterday is not null && yesterday.TryGetValue(x.UserId, out var y) ? y.Rank - rank : null;
            int? xpDelta = yesterday is not null
                ? x.Xp - (yesterday.TryGetValue(x.UserId, out var y2) ? y2.Xp : 0)
                : null;
            return new LeaderRowDto(rank, x.UserId, users2[x.UserId].DisplayName, users2[x.UserId].Role.ToString(), x.Xp,
                ProgressService.LevelForXp(x.Xp), x.UserId == UserId, delta, x.Solved, xpDelta);
        }).ToList();
        return new LeaderboardPageDto(rows, total, page, pageSize);
    }

    /// <summary>Each user's competition rank (1 = best, ties share a rank — same method as
    /// Leaderboard itself, so a RankDelta comparison is apples-to-apples) in the given scope,
    /// as of the start of today UTC — i.e. excluding anything earned today, so "today vs.
    /// this" is a same-day comparison. Computed from SolveRecord (an append-only ledger), so
    /// no separate snapshot table is needed; cached in memory per scope until the next UTC
    /// midnight, since re-ranking everyone on every leaderboard request would be wasteful
    /// when "yesterday" only changes once a day.</summary>
    /// <summary>Each user's (Rank, Xp) as of <paramref name="cutoff"/> (the caller's local
    /// midnight, converted to UTC by the caller) — the Xp side powers LeaderRowDto.XpDelta
    /// (today's Xp minus this), the Rank side powers RankDelta. The cache key includes the
    /// exact cutoff instant, so it naturally repeats (and hits cache) for repeat requests
    /// from the same timezone on the same calendar day, without needing to round it.</summary>
    private async Task<Dictionary<int, (int Rank, int Xp)>> GetYesterdayRanksAsync(int? organizationId, int? boardId, DateTime cutoff)
    {
        var cacheKey = $"leaderboard:yesterday:org={organizationId}:board={boardId}:asof={cutoff:O}";
        if (_cache.TryGetValue(cacheKey, out Dictionary<int, (int Rank, int Xp)>? cached) && cached is not null)
            return cached;

        var records = _db.SolveRecords.Where(r => r.CreatedAt < cutoff);
        if (organizationId is int oid)
        {
            var memberIds = _db.OrganizationMemberships.Where(m => m.OrganizationId == oid).Select(m => m.UserId);
            records = records.Where(r => memberIds.Contains(r.UserId));
        }
        if (boardId is int bid)
        {
            var memberIds = _db.BoardMemberships.Where(m => m.BoardId == bid).Select(m => m.UserId);
            records = records.Where(r => memberIds.Contains(r.UserId));
        }

        var grouped = await records.GroupBy(r => r.UserId)
            .Select(g => new { UserId = g.Key, Xp = g.Sum(r => r.XpAwarded) })
            .OrderByDescending(x => x.Xp).ThenBy(x => x.UserId)
            .ToListAsync();

        var map = new Dictionary<int, (int Rank, int Xp)>(grouped.Count);
        int prevXp = int.MinValue, prevRank = 0, idx = 0;
        foreach (var g in grouped)
        {
            idx++;
            if (g.Xp != prevXp) { prevRank = idx; prevXp = g.Xp; }
            map[g.UserId] = (prevRank, g.Xp);
        }

        _cache.Set(cacheKey, map, new DateTimeOffset(cutoff.AddDays(1)));
        return map;
    }
}
