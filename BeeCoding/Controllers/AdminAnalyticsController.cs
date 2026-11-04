using System.Text;
using System.Text.Json;
using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using BeeCoding.Services.Ai;
using BeeCoding.Services.Judge;
using BeeCoding.Services.Lsp;
using BeeCoding.Services.Realtime;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StackExchange.Redis;


namespace BeeCoding.Controllers;

/// <summary>Admin panel: analytics export (CSV + Excel). Same route prefix as <see cref="AdminUiController"/>.</summary>
[ApiController]
[Authorize(Policy = "Admin")]
[Route("api/admin-ui")]
public class AdminAnalyticsController(AppDbContext db) : ApiControllerBase
{
    private readonly AppDbContext _db = db;

    /// <summary>Top tags by attempts, for the dashboard's ranking chart — same
    /// aggregation as the Reports tab's topic-solve-rate export.</summary>
    [HttpGet("dashboard/topics")]
    public async Task<ActionResult<List<AdminTopicStatDto>>> DashboardTopics([FromQuery] int take = 8)
    {
        take = Math.Clamp(take, 1, 50);
        var agg = await TopicsAggAsync();
        return agg.Take(take)
            .Select(a => new AdminTopicStatDto(a.Tag, a.Attempts, a.Accepted, a.Attempts > 0 ? a.Accepted / (double)a.Attempts : 0))
            .ToList();
    }

    [HttpGet("analytics/ai-usage.csv")]
    public async Task<IActionResult> AiUsageCsv() => CsvFile(await AiUsageRowsAsync(), "ai-usage.csv");

    /// <summary>Shared by the AI-usage page (AdminAiController) and the CSV/Excel exports.</summary>
    internal static async Task<List<AdminAiUsageRow>> AiUsageAllRowsAsync(AppDbContext _db)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        var rows = await _db.AiUsages.Include(x => x.User).ToListAsync();

        static AdminAiUsageBucket Sum(IEnumerable<AiUsage> xs)
        {
            int c = 0; long p = 0, k = 0;
            foreach (var x in xs) { c += x.Calls; p += x.PromptTokens; k += x.CompletionTokens; }
            return new AdminAiUsageBucket(c, p, k, p + k);
        }

        return rows.GroupBy(x => x.UserId)
            .Select(g => new AdminAiUsageRow(
                g.Key,
                g.First().User?.Email ?? "?",
                g.First().User?.DisplayName ?? "?",
                Sum(g.Where(x => x.Day == today)),
                Sum(g.Where(x => x.Day >= monthStart)),
                Sum(g)))
            .OrderByDescending(r => r.AllTime.TotalTokens)
            .ToList();
    }

    private async Task<List<string[]>> AiUsageRowsAsync()
    {
        var all = await AiUsageAllRowsAsync(_db);
        var rows = new List<string[]>
        {
            new[]
            {
                "UserId", "Email", "DisplayName",
                "TodayCalls", "TodayPromptTokens", "TodayCompletionTokens", "TodayTotalTokens",
                "MonthCalls", "MonthPromptTokens", "MonthCompletionTokens", "MonthTotalTokens",
                "AllTimeCalls", "AllTimePromptTokens", "AllTimeCompletionTokens", "AllTimeTotalTokens",
            },
        };
        rows.AddRange(all.Select(r => new[]
        {
            r.UserId.ToString(), r.Email, r.DisplayName,
            r.Today.Calls.ToString(), r.Today.PromptTokens.ToString(), r.Today.CompletionTokens.ToString(), r.Today.TotalTokens.ToString(),
            r.Month.Calls.ToString(), r.Month.PromptTokens.ToString(), r.Month.CompletionTokens.ToString(), r.Month.TotalTokens.ToString(),
            r.AllTime.Calls.ToString(), r.AllTime.PromptTokens.ToString(), r.AllTime.CompletionTokens.ToString(), r.AllTime.TotalTokens.ToString(),
        }));
        return rows;
    }


    // ---- analytics export (CSV + Excel) — also usable as grant/report evidence ----
    [HttpGet("analytics/topics.csv")]
    public async Task<IActionResult> TopicsCsv() => CsvFile(await TopicsRowsAsync(), "topic-stats.csv");

    [HttpGet("analytics/users.csv")]
    public async Task<IActionResult> UsersCsv() => CsvFile(await UsersRowsAsync(), "user-xp.csv");

    [HttpGet("analytics/engagement.csv")]
    public async Task<IActionResult> EngagementCsv([FromQuery] int weeks = 12) => CsvFile(await EngagementRowsAsync(weeks), "weekly-engagement.csv");

    [HttpGet("analytics/report.xlsx")]
    public async Task<IActionResult> ReportXlsx([FromQuery] int weeks = 12)
    {
        using var wb = new XLWorkbook();
        WriteSheet(wb, "Topics", await TopicsRowsAsync());
        WriteSheet(wb, "Users", await UsersRowsAsync());
        WriteSheet(wb, "Weekly engagement", await EngagementRowsAsync(weeks));
        WriteSheet(wb, "AI usage", await AiUsageRowsAsync());
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "beecoding-report.xlsx");
    }

    private static void WriteSheet(XLWorkbook wb, string sheetName, List<string[]> rows)
    {
        var ws = wb.Worksheets.Add(sheetName);
        for (var r = 0; r < rows.Count; r++)
            for (var c = 0; c < rows[r].Length; c++)
                ws.Cell(r + 1, c + 1).Value = rows[r][c];
        if (rows.Count > 0) ws.Row(1).Style.Font.Bold = true;
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();
    }

    private async Task<List<TopicAgg>> TopicsAggAsync()
    {
        var boardProblems = await _db.Problems.Select(p => new { p.Id, p.Tags }).ToListAsync();
        var bankProblems = await _db.BankProblems.Select(p => new { p.Id, p.Tags }).ToListAsync();
        var boardSubs = await _db.Submissions.Select(s => new { s.ProblemId, s.UserId, s.Verdict, s.Score }).ToListAsync();
        var bankSubs = await _db.BankSubmissions.Select(s => new { s.BankProblemId, s.UserId, s.Verdict, s.Score }).ToListAsync();

        var byTag = new Dictionary<string, TopicAgg>();
        TopicAgg Agg(string tag) { if (!byTag.TryGetValue(tag, out var a)) byTag[tag] = a = new TopicAgg { Tag = tag }; return a; }
        bool Solved(Verdict v, double score) => v == Verdict.Accepted && score >= 1.0;

        foreach (var p in boardProblems)
        {
            var subs = boardSubs.Where(s => s.ProblemId == p.Id).ToList();
            foreach (var tag in TagsOf(p.Tags))
            {
                var a = Agg(tag);
                a.ProblemIds.Add(("board", p.Id));
                a.Attempts += subs.Count;
                foreach (var s in subs.Where(s => Solved(s.Verdict, s.Score))) { a.Accepted++; a.Solvers.Add(s.UserId); }
            }
        }
        foreach (var p in bankProblems)
        {
            var subs = bankSubs.Where(s => s.BankProblemId == p.Id).ToList();
            foreach (var tag in TagsOf(p.Tags))
            {
                var a = Agg(tag);
                a.ProblemIds.Add(("bank", p.Id));
                a.Attempts += subs.Count;
                foreach (var s in subs.Where(s => Solved(s.Verdict, s.Score))) { a.Accepted++; a.Solvers.Add(s.UserId); }
            }
        }

        return byTag.Values.OrderByDescending(a => a.Attempts).ToList();
    }

    private async Task<List<string[]>> TopicsRowsAsync()
    {
        var agg = await TopicsAggAsync();
        var rows = new List<string[]> { new[] { "Tag", "Problems", "Attempts", "AcceptedSubmissions", "AcceptRate", "DistinctSolvers" } };
        foreach (var a in agg)
        {
            var rate = a.Attempts > 0 ? (a.Accepted / (double)a.Attempts).ToString("0.00") : "";
            rows.Add(new[] { a.Tag, a.ProblemIds.Count.ToString(), a.Attempts.ToString(), a.Accepted.ToString(), rate, a.Solvers.Count.ToString() });
        }
        return rows;
    }

    private sealed class TopicAgg
    {
        public string Tag { get; set; } = "";
        public HashSet<(string Kind, int Id)> ProblemIds { get; } = new();
        public int Attempts;
        public int Accepted;
        public HashSet<int> Solvers { get; } = new();
    }

    private async Task<List<string[]>> UsersRowsAsync()
    {
        var users = await _db.Users.OrderBy(u => u.Id)
            .Select(u => new { u.Id, u.Email, u.DisplayName, u.Role, u.Xp, u.CreatedAt }).ToListAsync();
        var rows = new List<string[]> { new[] { "Id", "Email", "DisplayName", "Role", "Xp", "Level", "CreatedAt" } };
        rows.AddRange(users.Select(u => new[]
        {
            u.Id.ToString(), u.Email, u.DisplayName, u.Role.ToString(),
            u.Xp.ToString(), ProgressService.LevelForXp(u.Xp).ToString(), u.CreatedAt.ToString("O"),
        }));
        return rows;
    }

    private async Task<List<(DateOnly WeekStart, int ActiveUsers, int Submissions)>> EngagementAggAsync(int weeks)
    {
        weeks = Math.Clamp(weeks, 1, 52);
        var boardActivity = await _db.Submissions.Select(s => new { s.UserId, s.CreatedAt }).ToListAsync();
        var bankActivity = await _db.BankSubmissions.Select(s => new { s.UserId, s.CreatedAt }).ToListAsync();
        var all = boardActivity.Select(x => (x.UserId, x.CreatedAt)).Concat(bankActivity.Select(x => (x.UserId, x.CreatedAt)));

        DateOnly WeekStart(DateTime dt)
        {
            var d = DateOnly.FromDateTime(dt);
            return d.AddDays(-(((int)d.DayOfWeek + 6) % 7));   // Monday of that week
        }

        return all.GroupBy(x => WeekStart(x.CreatedAt))
            .OrderByDescending(g => g.Key).Take(weeks).OrderBy(g => g.Key)
            .Select(g => (g.Key, g.Select(x => x.UserId).Distinct().Count(), g.Count()))
            .ToList();
    }

    private async Task<List<string[]>> EngagementRowsAsync(int weeks)
    {
        var agg = await EngagementAggAsync(weeks);
        var rows = new List<string[]> { new[] { "WeekStart", "ActiveUsers", "TotalSubmissions" } };
        rows.AddRange(agg.Select(a => new[] { a.WeekStart.ToString("yyyy-MM-dd"), a.ActiveUsers.ToString(), a.Submissions.ToString() }));
        return rows;
    }

    private FileContentResult CsvFile(IEnumerable<string[]> rows, string fileName)
    {
        var sb = new StringBuilder();
        foreach (var row in rows) sb.AppendLine(string.Join(',', row.Select(CsvEscape)));
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", fileName);
    }

    private static string CsvEscape(string? s)
    {
        s ??= "";
        return s.Contains(',') || s.Contains('"') || s.Contains('\n') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
    }

    private static IEnumerable<string> TagsOf(string? t) =>
        (t ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .Select(x => x.ToLowerInvariant()).Distinct();

}
