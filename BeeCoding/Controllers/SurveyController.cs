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
public partial class SurveyController(AppDbContext db, AdminAccess admin) : ApiControllerBase
{
    private const int MaxQuestions = 60, MaxAnswerChars = 2000;

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
        return Ok(rows.Select(x => new { role = x.Role, at = x.UpdatedAt, answers = Parse(x.Answers) }));
    }

    private static Dictionary<string, string> Parse(string? json)
    {
        try { return string.IsNullOrEmpty(json) ? new() : JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new(); }
        catch (JsonException) { return new(); }
    }
}
