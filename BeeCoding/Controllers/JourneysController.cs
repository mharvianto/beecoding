using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Controllers;

public record WorldEditDto(string Name, int UnlockAfter, string? BossSlug, int BossXp, string? BadgeName, List<string> Slugs);
public record JourneyEditDto(string Title, bool Published, List<WorldEditDto> Worlds);

/// <summary>
/// Journeys: an ordered path of public bank problems, easy to hard, in worlds that each end in a boss. Order is a suggestion, any
/// problem can be skipped ("Skip for now"); the boss opens after <c>UnlockAfter</c> problems of its world are solved. Progress is
/// read from the student's existing practice solves (<c>BankSubmission</c>), so nobody starts from zero. Boss bonus XP is a
/// <c>SolveRecord</c> keyed by the boss problem, so it is paid once and shows in the period leaderboards.
/// Students cannot be blocked from the boss problem itself: it stays an ordinary public Practice problem, the lock is only on the map.
/// </summary>
[Authorize]
[Route("api/journeys")]
public class JourneysController(AppDbContext db, ProgressService progress, AuditLog audit, PlatformRuntimeConfig runtime) : ApiControllerBase
{
    private const string Off = "The Journey feature is switched off.";
    private const int MaxWorlds = 20, MaxItems = 30;

    private IQueryable<Journey> Full() => db.Journeys.Include(j => j.Worlds.OrderBy(w => w.Position)).ThenInclude(w => w.Items.OrderBy(i => i.Position));

    private static IEnumerable<int> ProblemIds(Journey j) =>
        j.Worlds.SelectMany(w => w.Items.Select(i => i.BankProblemId).Concat(w.BossBankProblemId is int b ? [b] : []));

    private Task<HashSet<int>> SolvedAsync(IEnumerable<int> ids)
    {
        var list = ids.ToList();
        return db.BankSubmissions.Where(s => s.UserId == UserId && list.Contains(s.BankProblemId) && s.Verdict == Verdict.Accepted && s.Score >= 1.0)
            .Select(s => s.BankProblemId).Distinct().ToHashSetAsync();
    }

    /// <summary>What the menu needs, and always 200: whether the feature is on and whether a journey is published. (The other endpoints
    /// answer 404 while it is off, and a menu that probed them would put a red line in every browser console.)</summary>
    [HttpGet("status")]
    public async Task<ActionResult> Status() =>
        Ok(new { enabled = runtime.JourneyEnabled, open = runtime.JourneyEnabled && await db.Journeys.AnyAsync(j => j.Published) });

    [HttpGet]
    public async Task<ActionResult> List()
    {
        if (!runtime.JourneyEnabled) return NotFound(Off);
        var journeys = await Full().Where(j => j.Published).OrderBy(j => j.Id).ToListAsync();
        var solved = await SolvedAsync(journeys.SelectMany(ProblemIds));
        return Ok(journeys.Select(j => new
        {
            j.Id, j.Title, Worlds = j.Worlds.Count,
            Total = j.Worlds.Sum(w => w.Items.Count),
            Done = j.Worlds.Sum(w => w.Items.Count(i => solved.Contains(i.BankProblemId))),
        }));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult> Get(int id)
    {
        if (!runtime.JourneyEnabled) return NotFound(Off);
        var j = await Full().FirstOrDefaultAsync(x => x.Id == id && x.Published);
        if (j is null) return NotFound();

        var ids = ProblemIds(j).Distinct().ToList();
        var problems = await db.BankProblems.Where(b => b.IsPublic && ids.Contains(b.Id))
            .Select(b => new { b.Id, b.Slug, b.Title, b.Level }).ToDictionaryAsync(b => b.Id);
        var solved = await SolvedAsync(ids);
        var skipped = await db.JourneySkips.Where(s => s.UserId == UserId && ids.Contains(s.BankProblemId)).Select(s => s.BankProblemId).ToHashSetAsync();

        var defeated = new List<object>();
        string? nextSlug = null;
        var worlds = new List<object>();
        foreach (var w in j.Worlds)
        {
            var items = w.Items.Where(i => problems.ContainsKey(i.BankProblemId)).Select(i =>
            {
                var p = problems[i.BankProblemId];
                var state = solved.Contains(p.Id) ? "done" : skipped.Contains(p.Id) ? "skipped" : "todo";
                if (state == "todo" && nextSlug is null) { nextSlug = p.Slug; state = "next"; }
                return new { p.Slug, p.Title, Level = p.Level.ToString(), State = state };
            }).ToList();
            var done = items.Count(i => i.State == "done");
            var need = Math.Min(w.UnlockAfter, items.Count);

            object? boss = null;
            if (w.BossBankProblemId is int bid && problems.TryGetValue(bid, out var bp))
            {
                var beaten = solved.Contains(bid);
                if (beaten && w.BossXp > 0 && await progress.AwardSolveAsync(UserId, $"journey-boss:{bid}", ProblemLevel.Hard, xpOverride: w.BossXp) > 0)
                    defeated.Add(new { World = w.Name, Badge = w.BadgeName, Xp = w.BossXp });
                boss = new { bp.Slug, bp.Title, Level = bp.Level.ToString(), w.BossXp, Badge = w.BadgeName, Need = need, State = beaten ? "defeated" : done >= need ? "ready" : "locked" };
            }
            worlds.Add(new { w.Id, w.Name, Done = done, Count = items.Count, Items = items, Boss = boss });
        }
        return Ok(new { j.Id, j.Title, Worlds = worlds, NewlyDefeated = defeated });
    }

    private async Task<int?> ItemProblemAsync(int journeyId, string slug) =>
        await db.JourneyItems.Where(i => i.World!.JourneyId == journeyId && i.World.Journey!.Published)
            .Join(db.BankProblems.Where(b => b.IsPublic && b.Slug == slug), i => i.BankProblemId, b => b.Id, (i, b) => (int?)b.Id).FirstOrDefaultAsync();

    [HttpPost("{id:int}/skip/{slug}")]
    public async Task<IActionResult> Skip(int id, string slug)
    {
        if (!runtime.JourneyEnabled) return NotFound(Off);
        if (await ItemProblemAsync(id, slug) is not int pid) return NotFound();
        if (await db.JourneySkips.AnyAsync(s => s.UserId == UserId && s.BankProblemId == pid)) return NoContent();
        db.JourneySkips.Add(new JourneySkip { UserId = UserId, BankProblemId = pid });
        try { await db.SaveChangesAsync(); } catch (DbUpdateException) { /* a double click lost the race on the unique index: already skipped */ }
        return NoContent();
    }

    [HttpDelete("{id:int}/skip/{slug}")]
    public async Task<IActionResult> Unskip(int id, string slug)
    {
        if (!runtime.JourneyEnabled) return NotFound(Off);
        if (await ItemProblemAsync(id, slug) is not int pid) return NotFound();
        await db.JourneySkips.Where(s => s.UserId == UserId && s.BankProblemId == pid).ExecuteDeleteAsync();
        return NoContent();
    }

    // ---- admin editor ----

    [HttpGet("admin")]
    [Authorize(Policy = "Admin")]
    public async Task<ActionResult> AdminList()
    {
        var journeys = await Full().OrderBy(j => j.Id).ToListAsync();
        var ids = journeys.SelectMany(ProblemIds).Distinct().ToList();
        var problems = await db.BankProblems.Where(b => ids.Contains(b.Id)).Select(b => new { b.Id, b.Slug, b.Title, Level = b.Level.ToString(), b.IsPublic }).ToDictionaryAsync(b => b.Id);
        object? P(int id) => problems.TryGetValue(id, out var p) ? p : null;
        return Ok(journeys.Select(j => new
        {
            j.Id, j.Title, j.Published,
            Worlds = j.Worlds.Select(w => new
            {
                w.Name, w.UnlockAfter, w.BossXp, w.BadgeName,
                Boss = w.BossBankProblemId is int b ? P(b) : null,
                Items = w.Items.Select(i => P(i.BankProblemId)).Where(p => p is not null),
            }),
        }));
    }

    [HttpPost("admin")]
    [Authorize(Policy = "Admin")]
    public async Task<ActionResult> Create([FromBody] JourneyEditDto dto)
    {
        var j = new Journey();
        if (await Apply(j, dto) is { } bad) return bad;
        db.Journeys.Add(j);
        await db.SaveChangesAsync();
        await audit.RecordAsync(UserId, ActorEmail, "journey.create", "journey", j.Id, j.Title);
        return Ok(new { j.Id });
    }

    [HttpPut("admin/{id:int}")]
    [Authorize(Policy = "Admin")]
    public async Task<ActionResult> Update(int id, [FromBody] JourneyEditDto dto)
    {
        var j = await Full().FirstOrDefaultAsync(x => x.Id == id);
        if (j is null) return NotFound();
        if (await Apply(j, dto) is { } bad) return bad;
        await db.SaveChangesAsync();
        await audit.RecordAsync(UserId, ActorEmail, "journey.update", "journey", j.Id, j.Title);
        return NoContent();
    }

    [HttpDelete("admin/{id:int}")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var j = await db.Journeys.FindAsync(id);
        if (j is null) return NotFound();
        db.Journeys.Remove(j);
        await db.SaveChangesAsync();
        await audit.RecordAsync(UserId, ActorEmail, "journey.delete", "journey", id, j.Title);
        return NoContent();
    }

    /// <summary>Replaces the whole structure from the editor's payload. Slugs must name public bank problems, each used once.</summary>
    private async Task<ActionResult?> Apply(Journey j, JourneyEditDto dto)
    {
        var title = (dto.Title ?? "").Trim();
        if (title.Length is 0 or > 120) return BadRequest("Give the journey a title (up to 120 characters).");
        if (dto.Worlds is null || dto.Worlds.Count > MaxWorlds) return BadRequest($"A journey has at most {MaxWorlds} worlds.");

        var slugs = dto.Worlds.SelectMany(w => (w.Slugs ?? []).Concat(string.IsNullOrEmpty(w.BossSlug) ? [] : [w.BossSlug!])).ToList();
        if (slugs.Distinct().Count() != slugs.Count) return BadRequest("A problem can appear only once in a journey.");
        var ids = await db.BankProblems.Where(b => b.IsPublic && slugs.Contains(b.Slug)).ToDictionaryAsync(b => b.Slug, b => b.Id);
        if (slugs.FirstOrDefault(s => !ids.ContainsKey(s)) is { } missing) return BadRequest($"Not a public problem: {missing}");

        var worlds = new List<JourneyWorld>();
        foreach (var (w, pos) in dto.Worlds.Select((w, i) => (w, i)))
        {
            var name = (w.Name ?? "").Trim();
            if (name.Length is 0 or > 120) return BadRequest("Every world needs a name (up to 120 characters).");
            if ((w.Slugs?.Count ?? 0) > MaxItems) return BadRequest($"A world has at most {MaxItems} problems.");
            worlds.Add(new JourneyWorld
            {
                Position = pos, Name = name,
                UnlockAfter = Math.Clamp(w.UnlockAfter, 0, w.Slugs?.Count ?? 0),
                BossBankProblemId = string.IsNullOrEmpty(w.BossSlug) ? null : ids[w.BossSlug!],
                BossXp = Math.Clamp(w.BossXp, 0, 1000),
                BadgeName = (w.BadgeName ?? "").Trim()[..Math.Min(60, (w.BadgeName ?? "").Trim().Length)],
                Items = (w.Slugs ?? []).Select((s, i) => new JourneyItem { Position = i, BankProblemId = ids[s] }).ToList(),
            });
        }
        j.Title = title;
        j.Published = dto.Published;
        db.JourneyWorlds.RemoveRange(j.Worlds);   // new rows below: boss XP is keyed by the boss problem, so replacing world ids never pays twice
        j.Worlds = worlds;
        return null;
    }
}
