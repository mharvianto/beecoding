using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using BeeCoding.Services.Judge;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Controllers;

/// <summary>
/// Free practice: any signed-in user can browse and solve every public bank problem,
/// independent of any board. Solving a problem for the first time awards XP.
/// </summary>
[ApiController]
[Authorize]
[Route("api/practice")]
public class PracticeController(AppDbContext db, IJudgeQueue queue, RateLimiter rate, SubmitCooldown submitCooldown,
    Services.Ai.AiTutorService ai, Services.Ai.AiUsageService aiUsage, OrgResolver orgs, AdminAccess admin,
    OrgAccess orgAccess) : ApiControllerBase
{
    private readonly AppDbContext _db = db;
    private readonly IJudgeQueue _queue = queue;
    private readonly RateLimiter _rate = rate;
    private readonly SubmitCooldown _submitCooldown = submitCooldown;
    private readonly Services.Ai.AiTutorService _ai = ai;
    private readonly Services.Ai.AiUsageService _aiUsage = aiUsage;
    private readonly OrgResolver _orgs = orgs;
    private readonly AdminAccess _admin = admin;
    private readonly OrgAccess _orgAccess = orgAccess;

    // per-user cache of AI picks (they cost a model call); short TTL.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, (long Ts, List<RecommendationDto> Recs)> _aiCache = new();
    private static readonly long AiTtlTicks = TimeSpan.FromMinutes(10).Ticks;

    private IQueryable<BankProblem> Pool() => _db.BankProblems.Where(b => b.IsPublic);

    [HttpGet]
    public async Task<ActionResult<PracticePageDto>> List(
        [FromQuery] string? q, [FromQuery] string? tag, [FromQuery] string? level, [FromQuery] string? status,
        [FromQuery] string? sort = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = Pool();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var n = q.Trim();
            query = query.Where(b => EF.Functions.Like(b.Title, $"%{n}%") || EF.Functions.Like(b.Tags, $"%{n}%"));
        }
        if (!string.IsNullOrWhiteSpace(tag))
        {
            var t = tag.Trim().ToLowerInvariant();
            query = query.Where(b => EF.Functions.Like(b.Tags, $"%{t}%"));
        }
        if (Enum.TryParse<ProblemLevel>(level, ignoreCase: true, out var lvl))
            query = query.Where(b => b.Level == lvl);

        var problems = await query
            .OrderBy(b => b.Level).ThenBy(b => b.Title)
            .Select(b => new { b.Id, b.Slug, b.Title, b.AllowedLanguages, b.Level, b.Tags })
            .Take(2000)
            .ToListAsync();
        var ids = problems.Select(p => p.Id).ToList();

        // best verdict/score per problem for this user
        var mine = await _db.BankSubmissions
            .Where(s => s.UserId == UserId && ids.Contains(s.BankProblemId) && s.Status == SubmissionStatus.Done)
            .GroupBy(s => s.BankProblemId)
            .Select(g => new
            {
                BankProblemId = g.Key,
                Best = g.Max(x => x.Score),
                Solved = g.Any(x => x.Verdict == Verdict.Accepted && x.Score >= 1.0),
                Latest = g.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).First().Verdict,
            })
            .ToListAsync();
        var byId = mine.ToDictionary(m => m.BankProblemId);

        // Global stats (every user, not just the caller) — how many have attempted each
        // problem and what fraction of those attempts were a full Accepted.
        var stats = await _db.BankSubmissions
            .Where(s => ids.Contains(s.BankProblemId) && s.Status == SubmissionStatus.Done)
            .GroupBy(s => s.BankProblemId)
            .Select(g => new
            {
                BankProblemId = g.Key,
                Total = g.Count(),
                Accepted = g.Count(x => x.Verdict == Verdict.Accepted && x.Score >= 1.0),
            })
            .ToListAsync();
        var statsById = stats.ToDictionary(s => s.BankProblemId);

        var list = problems.Select(p =>
        {
            byId.TryGetValue(p.Id, out var m);
            statsById.TryGetValue(p.Id, out var st);
            return new PracticeSummaryDto(
                p.Id, p.Slug, p.Title, p.AllowedLanguages, p.Level.ToString(), p.Tags,
                m?.Latest.ToString() ?? "None",
                m?.Best ?? 0,
                m?.Solved ?? false,
                st?.Total ?? 0,
                st is { Total: > 0 } ? (double)st.Accepted / st.Total : 0);
        });

        list = status?.ToLowerInvariant() switch
        {
            "solved" => list.Where(x => x.Solved),
            "unsolved" => list.Where(x => !x.Solved),
            "attempted" => list.Where(x => x.MyVerdict != "None" && !x.Solved),
            _ => list,
        };

        list = sort?.ToLowerInvariant() switch
        {
            "submissions" => list.OrderByDescending(x => x.SubmissionCount).ThenBy(x => x.Title),
            "acrate" => list.OrderByDescending(x => x.AcRate).ThenBy(x => x.Title),
            "acrate_asc" => list.OrderBy(x => x.SubmissionCount == 0 ? 2.0 : x.AcRate).ThenBy(x => x.Title),
            _ => list,   // default: already Level, Title from the query above
        };

        var all = list.ToList();
        var pageCount = Math.Max(1, (int)Math.Ceiling(all.Count / (double)pageSize));
        page = Math.Min(page, pageCount);
        var pageItems = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new PracticePageDto(all.Count, all.Count(x => x.Solved), page, pageSize, pageItems);
    }

    private static IEnumerable<string> TagsOf(string? t) =>
        (t ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .Select(x => x.ToLowerInvariant()).Distinct();

    /// <summary>
    /// "What should I do next?" for Practice: per-topic progress + up to 3 recommended
    /// unsolved problems, chosen to keep the difficulty ramp gentle (continue a topic
    /// you've started, one step up in difficulty, avoid problems you've bounced off).
    /// </summary>
    [HttpGet("guide")]
    public async Task<ActionResult<PracticeGuideDto>> Guide([FromQuery] bool ai = false)
    {
        var problems = await Pool()
            .Select(b => new { b.Id, b.Slug, b.Title, b.AllowedLanguages, b.Level, b.Tags })
            .ToListAsync();
        if (problems.Count == 0) return new PracticeGuideDto(new(), new(), "heuristic", _ai.Available);

        var ids = problems.Select(p => p.Id).ToHashSet();
        var subs = await _db.BankSubmissions
            .Where(s => s.UserId == UserId && s.Status == SubmissionStatus.Done && ids.Contains(s.BankProblemId))
            .GroupBy(s => s.BankProblemId)
            .Select(g => new
            {
                Id = g.Key,
                Solved = g.Any(x => x.Verdict == Verdict.Accepted && x.Score >= 1.0),
                Fails = g.Count(x => !(x.Verdict == Verdict.Accepted && x.Score >= 1.0)),
            })
            .ToListAsync();
        var solvedSet = subs.Where(s => s.Solved).Select(s => s.Id).ToHashSet();
        var attemptedSet = subs.Select(s => s.Id).ToHashSet();
        var fails = subs.ToDictionary(s => s.Id, s => s.Fails);

        // ---- per-topic progress ----
        var byTag = new Dictionary<string, (int Total, int Solved, int Attempted)>();
        foreach (var p in problems)
            foreach (var tag in TagsOf(p.Tags))
            {
                var c = byTag.GetValueOrDefault(tag);
                byTag[tag] = (c.Total + 1,
                    c.Solved + (solvedSet.Contains(p.Id) ? 1 : 0),
                    c.Attempted + (attemptedSet.Contains(p.Id) ? 1 : 0));
            }

        var topics = byTag
            .Select(kv => new TopicProgressDto(kv.Key, kv.Value.Total, kv.Value.Solved, kv.Value.Attempted))
            .OrderByDescending(t => t.Attempted > 0)                          // topics you've started first
            .ThenBy(t => t.Total == 0 ? 1.0 : (double)t.Solved / t.Total)     // least complete first
            .ThenByDescending(t => t.Total)
            .Take(16)
            .ToList();

        // ---- target difficulty per topic: ramp up once the lower tier is mostly cleared ----
        var topicTarget = new Dictionary<string, int>();
        foreach (var tag in byTag.Keys)
        {
            var inTag = problems.Where(p => TagsOf(p.Tags).Contains(tag)).ToList();
            int solvedAt(int lvl) => inTag.Count(p => (int)p.Level == lvl && solvedSet.Contains(p.Id));
            int totalAt(int lvl) => inTag.Count(p => (int)p.Level == lvl);
            int target = 1;
            if (totalAt(1) > 0 && solvedAt(1) >= Math.Ceiling(totalAt(1) * 0.6)) target = 2;
            if (target == 2 && totalAt(2) > 0 && solvedAt(2) >= Math.Ceiling(totalAt(2) * 0.5)) target = 3;
            topicTarget[tag] = target;
        }

        var startedTopics = byTag
            .Where(kv => kv.Value.Attempted > 0 && kv.Value.Solved < kv.Value.Total)
            .Select(kv => kv.Key).ToHashSet();

        var recommended = problems
            .Where(p => !solvedSet.Contains(p.Id))
            .Select(p =>
            {
                var tags = TagsOf(p.Tags).ToList();
                bool inStarted = tags.Any(startedTopics.Contains);
                int target = tags.Where(topicTarget.ContainsKey).Select(t => topicTarget[t])
                                 .DefaultIfEmpty(1).Min();
                double score = (inStarted ? 100 : 0)
                             - 25 * Math.Abs((int)p.Level - target)
                             - 40 * Math.Max(0, fails.GetValueOrDefault(p.Id) - 1)
                             - 3 * ((int)p.Level - 1);
                var startedTag = tags.FirstOrDefault(startedTopics.Contains);
                string reason = attemptedSet.Contains(p.Id) ? "Give this another go"
                    : startedTag is not null ? $"Continue with {startedTag}"
                    : (int)p.Level == 1 ? "A gentle place to start"
                    : $"Try a {p.Level.ToString().ToLowerInvariant()} one";
                return (p, score, reason);
            })
            .OrderByDescending(x => x.score)
            .ThenBy(x => (int)x.p.Level).ThenBy(x => x.p.Title)
            .Take(3)
            .Select(x => new RecommendationDto(
                x.p.Id, x.p.Slug, x.p.Title, x.p.AllowedLanguages, x.p.Level.ToString(), x.p.Tags, x.reason))
            .ToList();

        var source = "heuristic";
        // Best-effort: a paused/banned/over-quota AI just falls back to the heuristic list,
        // no error surfaced — this endpoint's AI use is a nice-to-have, not a request the
        // student explicitly made (unlike the hint/generate endpoints).
        var practiceOrgId = await _orgs.ForUserAsync(UserId);
        _ai.UseOrganization(practiceOrgId);
        if (ai && _ai.Available && (await _aiUsage.CheckGateAsync(UserId, CurrentRole, practiceOrgId)).Allowed)
        {
            var cached = _aiCache.GetValueOrDefault(UserId);
            if (cached.Recs is { Count: > 0 } && DateTime.UtcNow.Ticks - cached.Ts < AiTtlTicks)
            {
                recommended = cached.Recs;
                source = "ai";
            }
            else
            {
                var byId = problems.ToDictionary(p => p.Id);
                var unsolved = problems.Where(p => !solvedSet.Contains(p.Id))
                    .OrderBy(p => (int)p.Level).ThenBy(p => p.Title).Take(150).ToList();

                var msg = new System.Text.StringBuilder();
                msg.AppendLine("## Topic progress (tag: solved/total, attempted)");
                foreach (var t in topics)
                    msg.AppendLine($"- {t.Tag}: {t.Solved}/{t.Total}, attempted {t.Attempted}");
                var recentFails = fails.Where(kv => kv.Value >= 2).Select(kv => kv.Key).ToList();
                if (recentFails.Count > 0)
                    msg.AppendLine($"\nProblems the student has failed 2+ times (avoid piling on): {string.Join(", ", recentFails)}");
                msg.AppendLine("\n## Unsolved catalog (id | title | level | tags)");
                foreach (var p in unsolved)
                    msg.AppendLine($"{p.Id} | {p.Title} | {p.Level} | {p.Tags}");

                try
                {
                    var result = await _ai.PickNextAsync(msg.ToString(), HttpContext.RequestAborted);
                    await _aiUsage.RecordAsync(UserId, result.PromptTokens, result.CompletionTokens);
                    var chosen = result.Picks
                        .Where(x => byId.ContainsKey(x.Id) && !solvedSet.Contains(x.Id))
                        .DistinctBy(x => x.Id).Take(3)
                        .Select(x =>
                        {
                            var p = byId[x.Id];
                            return new RecommendationDto(p.Id, p.Slug, p.Title, p.AllowedLanguages, p.Level.ToString(), p.Tags,
                                string.IsNullOrWhiteSpace(x.Reason) ? "AI pick" : x.Reason);
                        }).ToList();

                    if (chosen.Count > 0)
                    {
                        // top up to 3 from the heuristic list if the model gave fewer
                        foreach (var h in recommended)
                            if (chosen.Count < 3 && chosen.All(c => c.Id != h.Id)) chosen.Add(h);
                        recommended = chosen;
                        source = "ai";
                        _aiCache[UserId] = (DateTime.UtcNow.Ticks, chosen);
                    }
                }
                catch (Services.Ai.AiUnavailableException)
                {
                    // fall back to the heuristic list silently
                }
            }
        }

        return new PracticeGuideDto(topics, recommended, source, _ai.Available);
    }

    [HttpGet("{slug}")]
    public async Task<ActionResult<PracticeProblemDto>> Get(string slug)
    {
        var b = await Pool().Include(x => x.TestCases).FirstOrDefaultAsync(x => x.Slug == slug);
        if (b is null) return NotFound();
        bool solved = await _db.BankSubmissions.AnyAsync(
            s => s.UserId == UserId && s.BankProblemId == b.Id && s.Verdict == Verdict.Accepted && s.Score >= 1.0);
        return Mapping.ToPracticeDto(b, solved);
    }

    [HttpPost("{slug}/submit")]
    public async Task<ActionResult<object>> Submit(string slug, SubmitDto dto)
    {
        var problem = await Pool().Include(b => b.TestCases).FirstOrDefaultAsync(b => b.Slug == slug);
        if (problem is null) return NotFound();
        if (string.IsNullOrWhiteSpace(dto.Code)) return BadRequest("Code is empty.");
        if (dto.Code.Length > 200_000) return BadRequest("Code is too large.");
        if (!_rate.TryAcquire(UserId)) return StatusCode(429, "Slow down a moment and try again.");
        if (!_submitCooldown.TryAcquire(UserId))
            return StatusCode(429, $"Please wait {Math.Ceiling(_submitCooldown.SecondsRemaining(UserId))}s before submitting again.");

        var lang = dto.Language is "c" or "cpp" ? dto.Language : Languages.Default(problem.AllowedLanguages);
        if (!Languages.Allows(problem.AllowedLanguages, lang))
            return BadRequest($"This problem only accepts {Languages.Label(problem.AllowedLanguages)}.");

        DateOnly? localDay = DateOnly.TryParseExact(dto.LocalDay, "yyyy-MM-dd", out var ld) ? ld : null;
        var sub = new BankSubmission
        {
            BankProblemId = problem.Id,
            UserId = UserId,
            Code = dto.Code,
            Language = lang,
            LocalDay = localDay,
        };
        _db.BankSubmissions.Add(sub);
        await _db.SaveChangesAsync();

        var tests = problem.TestCases.OrderBy(t => t.Position).ThenBy(t => t.Id)
            .Select(t => new TestSpec(t.Stdin, t.ExpectedStdout, t.Points, t.IsSample)).ToList();
        await _queue.EnqueueGradeAsync(new GradeJob(
            "practice", sub.Id, lang, sub.Code,
            problem.TimeLimitMs, problem.MemoryLimitKb, problem.BannedHeaders, problem.BannedSymbols, tests,
            problem.InputFileName));
        return Accepted(new { submissionId = sub.Id });
    }

    [HttpGet("{slug}/submissions")]
    public async Task<ActionResult<IEnumerable<BankSubmissionDto>>> Mine(string slug)
    {
        var id = await Pool().Where(b => b.Slug == slug).Select(b => (int?)b.Id).FirstOrDefaultAsync();
        if (id is null) return NotFound();
        var rows = await _db.BankSubmissions
            .Where(s => s.BankProblemId == id && s.UserId == UserId)
            .OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id)
            .ToListAsync();
        return rows.Select(s => Mapping.ToDto(s)).ToList();
    }

    [HttpGet("submissions/{sid:int}")]
    public async Task<ActionResult<BankSubmissionDto>> One(int sid)
    {
        var s = await _db.BankSubmissions.FirstOrDefaultAsync(x => x.Id == sid);
        if (s is null) return NotFound();
        if (s.UserId != UserId && !IsAdminUser(_admin)
            && !await _orgAccess.CanManageMemberAsync(UserId, ActorEmail, s.UserId))
            return Forbid();

        var previousId = await _db.BankSubmissions
            .Where(x => x.UserId == s.UserId && x.BankProblemId == s.BankProblemId && x.Id < s.Id)
            .OrderByDescending(x => x.Id)
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync();
        return Mapping.ToDto(s, previousSubmissionId: previousId);
    }
}
