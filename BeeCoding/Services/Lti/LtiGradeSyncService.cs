using System.Net.Http.Headers;
using System.Net.Http.Json;
using BeeCoding.Data;
using BeeCoding.Models;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Services.Lti;

/// <summary>
/// Pushes a score to the LMS gradebook (AGS) when a student solves a new problem —
/// best-effort: a platform being unreachable, mis-scoped, or having revoked the grading
/// permission never fails the judge pipeline that calls this, only gets logged. A board
/// placement (<see cref="SyncBoardAsync"/>) scores "problems solved / total problems in
/// the board"; a single-problem placement (<see cref="SyncPracticeAsync"/>) scores that
/// one problem's best result (0..1) — see LtiProvisioningService.ResolveResourceLinkAsync.
/// </summary>
public class LtiGradeSyncService(AppDbContext db, LtiTokenService tokens, IHttpClientFactory httpFactory, ILogger<LtiGradeSyncService> log)
{
    private readonly AppDbContext _db = db;
    private readonly LtiTokenService _tokens = tokens;
    private readonly IHttpClientFactory _http = httpFactory;
    private readonly ILogger<LtiGradeSyncService> _log = log;

    public async Task SyncBoardAsync(int boardId, int userId, CancellationToken ct = default)
    {
        try
        {
            var link = await _db.LtiResourceLinks.Include(l => l.LtiPlatform)
                .FirstOrDefaultAsync(l => l.BoardId == boardId && l.LineItemUrl != null, ct);
            if (link?.LtiPlatform is null || link.LineItemUrl is null) return;

            var userLink = await _db.LtiUserLinks
                .FirstOrDefaultAsync(u => u.LtiPlatformId == link.LtiPlatformId && u.UserId == userId, ct);
            if (userLink is null) return;   // this account didn't arrive via this platform

            var totalProblems = await _db.Problems.CountAsync(p => p.BoardId == boardId, ct);
            if (totalProblems == 0) return;
            var solved = await _db.Submissions
                .Where(s => s.Problem!.BoardId == boardId && s.UserId == userId && s.Verdict == Verdict.Accepted && s.Score >= 1.0)
                .Select(s => s.ProblemId).Distinct().CountAsync(ct);

            var accessToken = await _tokens.GetAccessTokenAsync(link.LtiPlatform, ct);
            var client = _http.CreateClient("lti");
            var scoreUrl = link.LineItemUrl.TrimEnd('/') + "/scores";
            var req = new HttpRequestMessage(HttpMethod.Post, scoreUrl)
            {
                Content = JsonContent.Create(new
                {
                    userId = userLink.Subject,
                    scoreGiven = solved,
                    scoreMaximum = totalProblems,
                    activityProgress = solved >= totalProblems ? "Completed" : "InProgress",
                    gradingProgress = "FullyGraded",
                    timestamp = DateTime.UtcNow.ToString("o"),
                }, mediaType: new MediaTypeHeaderValue("application/vnd.ims.lis.v1.score+json")),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var res = await client.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
                _log.LogWarning("LTI grade sync rejected for board {BoardId} user {UserId}: {Status}", boardId, userId, res.StatusCode);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "LTI grade sync failed for board {BoardId} user {UserId}", boardId, userId);
        }
    }

    /// <summary>Same idea as <see cref="SyncBoardAsync"/> but for a single-problem
    /// placement (see LtiProvisioningService.ResolveResourceLinkAsync) — the score is
    /// just that problem's best score (0..1) for this student, since one LTI resource
    /// link here maps to exactly one problem.</summary>
    public async Task SyncPracticeAsync(int bankProblemId, int userId, CancellationToken ct = default)
    {
        try
        {
            var link = await _db.LtiResourceLinks.Include(l => l.LtiPlatform)
                .FirstOrDefaultAsync(l => l.BankProblemId == bankProblemId && l.LineItemUrl != null, ct);
            if (link?.LtiPlatform is null || link.LineItemUrl is null) return;

            var userLink = await _db.LtiUserLinks
                .FirstOrDefaultAsync(u => u.LtiPlatformId == link.LtiPlatformId && u.UserId == userId, ct);
            if (userLink is null) return;   // this account didn't arrive via this platform

            var best = await _db.BankSubmissions
                .Where(s => s.BankProblemId == bankProblemId && s.UserId == userId && s.Status == SubmissionStatus.Done)
                .MaxAsync(s => (double?)s.Score, ct) ?? 0;

            var accessToken = await _tokens.GetAccessTokenAsync(link.LtiPlatform, ct);
            var client = _http.CreateClient("lti");
            var scoreUrl = link.LineItemUrl.TrimEnd('/') + "/scores";
            var req = new HttpRequestMessage(HttpMethod.Post, scoreUrl)
            {
                Content = JsonContent.Create(new
                {
                    userId = userLink.Subject,
                    scoreGiven = best,
                    scoreMaximum = 1.0,
                    activityProgress = best >= 1.0 ? "Completed" : "InProgress",
                    gradingProgress = "FullyGraded",
                    timestamp = DateTime.UtcNow.ToString("o"),
                }, mediaType: new MediaTypeHeaderValue("application/vnd.ims.lis.v1.score+json")),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var res = await client.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
                _log.LogWarning("LTI grade sync rejected for practice problem {ProblemId} user {UserId}: {Status}", bankProblemId, userId, res.StatusCode);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "LTI grade sync failed for practice problem {ProblemId} user {UserId}", bankProblemId, userId);
        }
    }
}
