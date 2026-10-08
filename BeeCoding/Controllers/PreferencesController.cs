using System.Text.Json;
using BeeCoding.Data;
using BeeCoding.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Controllers;

/// <summary>
/// Per-user display preferences kept on the server so they follow the user across devices: the browser mirrors
/// these localStorage keys (see ClientApp/src/lib/prefs.js). Values are opaque strings; only an allowlist of keys
/// is accepted, with a size cap, so this can't be used as general storage. Draft code, celebration markers and
/// other per-device state deliberately stay in the browser.
/// </summary>
[ApiController]
[Authorize]
[Route("api/me/preferences")]
public class PreferencesController(AppDbContext db) : ApiControllerBase
{
    private const int MaxValueChars = 400;
    private const int MaxThemeChars = 150_000;
    private const int MaxSnippetChars = 130_000;
    private const int MaxTotalChars = 450_000;

    private static readonly HashSet<string> Keys = new()
    {
        "beecoding.theme", "beecoding.tableView", "beecoding.boardView", "beecoding.playground.focus",
        "beecoding.lang", "beecoding.aiLang",
        "beecoding.editor.theme", "beecoding.editor.fontFamily", "beecoding.editor.fontSize",
        "beecoding.editor.customTheme", "beecoding.editor.customThemeName", "beecoding.editor.cppFormatStyle", "beecoding.editor.lsp", "beecoding.editor.snippets",
        "beecoding.account.engagementGranularity",
        "beecoding.board.stats.engagementGranularity", "beecoding.board.stats.aiGranularity",
        "beecoding.orgAdmin.engagementGranularity", "beecoding.orgAdmin.aiGranularity",
        "beecoding.admin.engagementGranularity", "beecoding.admin.aiGranularity",
    };

    private static Dictionary<string, string> Parse(string? json)
    {
        try { return string.IsNullOrEmpty(json) ? new() : JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new(); }
        catch (JsonException) { return new(); }
    }

    [HttpGet]
    public async Task<ActionResult<PreferencesDto>> Get()
    {
        var json = await db.Users.Where(u => u.Id == UserId).Select(u => u.Preferences).FirstOrDefaultAsync();
        return new PreferencesDto(UserId, Parse(json));
    }

    /// <summary>Merge changes: a string sets a key, null removes it. Unknown keys are ignored.</summary>
    [HttpPut]
    public async Task<ActionResult<PreferencesDto>> Put(PreferencesPatchDto dto)
    {
        var user = await db.Users.FindAsync(UserId);
        if (user is null) return Unauthorized();

        var values = Parse(user.Preferences);
        foreach (var (key, value) in dto.Values ?? new())
        {
            if (!Keys.Contains(key)) continue;
            if (value is null) { values.Remove(key); continue; }
            if (value.Length > (key == "beecoding.editor.customTheme" ? MaxThemeChars : key == "beecoding.editor.snippets" ? MaxSnippetChars : MaxValueChars))
                return BadRequest($"The value for {key} is too large.");
            values[key] = value;
        }

        var json = JsonSerializer.Serialize(values);
        if (json.Length > MaxTotalChars) return BadRequest("Too many preferences.");
        user.Preferences = values.Count == 0 ? null : json;
        await db.SaveChangesAsync();
        return new PreferencesDto(UserId, values);
    }
}
