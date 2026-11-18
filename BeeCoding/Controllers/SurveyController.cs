using System.Text.Json;
using System.Text.RegularExpressions;
using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Controllers;

/// <summary>
/// The in-app feedback survey. Each signed-in user keeps one response they can change later. The questions live in the
/// browser, so the server only stores what it is given: a small map of question id → text, capped in size. Only platform
/// admins can read everyone's answers, and what they get has no user id, name or email (just the role and the date).
/// </summary>
[ApiController]
[Authorize]
[Route("api/survey")]
public partial class SurveyController(AppDbContext db, AdminAccess admin, IConfiguration cfg) : ApiControllerBase
{
    private const int MaxQuestions = 60, MaxAnswerChars = 500;
    /// <summary>Finished submissions (practice + boards) a student needs before they can answer, so the answers come from people who used the judge.</summary>
    private const int StudentNeeds = 3;

    /// <summary><c>Survey:Open</c> (default true): false stops new and changed answers and hides the banner.</summary>
    private bool Open => cfg.GetValue("Survey:Open", true);

    /// <summary>Students: <see cref="StudentNeeds"/> finished submissions. Teachers: at least one problem of their own (a board problem counts, it has a bank row). Admins: always.</summary>
    private async Task<(bool Ok, int Have, int Need)> EligibleAsync()
    {
        if (IsAdminUser(admin)) return (true, 1, 1);
        if (CurrentRole == "Teacher")
        {
            var own = await db.BankProblems.CountAsync(b => b.OwnerId == UserId) > 0 ? 1 : 0;
            return (own >= 1, own, 1);
        }
        var done = await db.Submissions.CountAsync(x => x.UserId == UserId && x.Status == SubmissionStatus.Done)
                 + await db.BankSubmissions.CountAsync(x => x.UserId == UserId && x.Status == SubmissionStatus.Done);
        return (done >= StudentNeeds, Math.Min(done, StudentNeeds), StudentNeeds);
    }

    /// <summary>What the page and the banner need: is the survey open, may this user answer yet (and how far along are they), have they answered.</summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status()
    {
        var (ok, have, need) = await EligibleAsync();
        var done = await db.SurveyResponses.AnyAsync(x => x.UserId == UserId);
        return Ok(new { open = Open, eligible = ok, have, need, done, teacher = CurrentRole == "Teacher" });
    }

    [GeneratedRegex("^[a-z][a-z0-9_]{0,29}$")] private static partial Regex KeyRx();

    [HttpGet("mine")]
    public async Task<ActionResult<Dictionary<string, string>>> Mine()
    {
        var json = await db.SurveyResponses.Where(x => x.UserId == UserId).Select(x => x.Answers).FirstOrDefaultAsync();
        return Parse(json);
    }

    [HttpPut]
    public async Task<IActionResult> Save(Dictionary<string, string> answers)
    {
        if (!Open) return StatusCode(403, "The survey is closed.");
        if (!(await EligibleAsync()).Ok) return StatusCode(403, "Use BeeCoding a little more before answering.");
        if (answers.Count > MaxQuestions) return BadRequest("Too many answers.");
        var clean = new Dictionary<string, string>();
        foreach (var (k, v) in answers)
        {
            if (!KeyRx().IsMatch(k)) return BadRequest("Bad question id.");
            var text = (v ?? "").Trim();
            if (text.Length > MaxAnswerChars) return BadRequest("An answer is too long.");
            if (text.Length > 0) clean[k] = text;
        }

        var row = await db.SurveyResponses.FirstOrDefaultAsync(x => x.UserId == UserId);
        if (row is null) db.SurveyResponses.Add(row = new SurveyResponse { UserId = UserId, Role = CurrentRole });
        row.Role = CurrentRole;
        row.Answers = JsonSerializer.Serialize(clean);
        row.UpdatedAt = DateTime.UtcNow;
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return Conflict("Please try again."); }   // two tabs saving a first response at once: the unique index refuses the second
        return NoContent();
    }

    /// <summary>Everyone's answers, without who they are. Admins only.</summary>
    [HttpGet("results")]
    public async Task<IActionResult> Results()
    {
        if (!IsAdminUser(admin)) return Forbid();
        var rows = await db.SurveyResponses.OrderBy(x => x.Id).Select(x => new { x.Role, x.UpdatedAt, x.Answers }).ToListAsync();
        // the day only: a timestamp to the second could be matched against submission times to guess who answered
        return Ok(rows.Select(x => new { role = x.Role, at = x.UpdatedAt.ToString("yyyy-MM-dd"), answers = Parse(x.Answers) }));
    }

    private static Dictionary<string, string> Parse(string? json)
    {
        try { return string.IsNullOrEmpty(json) ? new() : JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new(); }
        catch (JsonException) { return new(); }
    }
}
