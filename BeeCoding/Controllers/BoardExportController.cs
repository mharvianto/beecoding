using System.IO.Compression;
using System.Text;
using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Controllers;

/// <summary>
/// Staff-only downloads for one board: an Excel workbook with every student's verdict and score per
/// problem (plus the full attempt log), and a ZIP with the students' source code. Staff already see
/// every answer in the UI, so exam mode and hide flags do not apply here.
/// </summary>
[Authorize]
[Route("api/boards/{slug}/export")]
public class BoardExportController(AppDbContext db, AdminAccess admin, AuditLog audit) : ApiControllerBase
{
    private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly AppDbContext _db = db;
    private readonly AdminAccess _admin = admin;
    private readonly AuditLog _audit = audit;

    /// <param name="ScopeFile">Short label for the file name ("" = the whole board).</param>
    /// <param name="ScopeNote">One sentence naming what the download covers, for the workbook notes and the audit log.</param>
    /// <param name="Labels">Problem id to its column label: "A", "B", … once the board has <see cref="LetterThreshold"/> or more problems
    /// (the same letters the board's progress grid shows), otherwise "1", "2", …. Taken over the whole board, not the downloaded scope.</param>
    private sealed record Export(Board Board, List<BoardMembership> Students, List<Problem> Problems, string ScopeFile, string ScopeNote,
        Dictionary<int, string> Labels);

    /// <summary>From this many problems on, the progress grid (and these downloads) label problems A, B, C … instead of by full title.</summary>
    private const int LetterThreshold = 5;

    /// <summary>A, B, … Z, AA, AB, …</summary>
    private static string Letter(int i)
    {
        var s = "";
        do { s = (char)('A' + i % 26) + s; i = i / 26 - 1; } while (i >= 0);
        return s;
    }

    /// <summary>Labels in the order and over the set of problems the progress grid shows (student-visible ones, by position);
    /// problems the grid leaves out (hidden, or in a hidden or not yet open group) are labelled "-".</summary>
    private static Dictionary<int, string> LabelProblems(IEnumerable<Problem> ordered)
    {
        var all = ordered.ToList();
        var now = DateTime.UtcNow;
        var shown = all.Where(p => GroupAccess.StudentVisible(p, now)).ToList();
        bool letters = shown.Count >= LetterThreshold;
        var labels = all.ToDictionary(p => p.Id, _ => "-");
        for (var i = 0; i < shown.Count; i++) labels[shown[i].Id] = letters ? Letter(i) : (i + 1).ToString();
        return labels;
    }

    /// <summary>The board with its students and problems, or the failure to return. Owner, teacher or platform admin only.
    /// <paramref name="problem"/> (a problem slug) or <paramref name="group"/> (a group id; 0 = problems without a group)
    /// narrows the download to that problem or group.</summary>
    private async Task<(Export? Data, ActionResult? Failure)> LoadAsync(string slug, string? problem = null, int? group = null)
    {
        var board = await _db.Boards
            .Include(b => b.Members).ThenInclude(m => m.User)
            .Include(b => b.Problems)
            .Include(b => b.Groups)
            .FirstOrDefaultAsync(b => b.Slug == slug);
        if (board is null) return (null, NotFound());
        var membership = board.Members.FirstOrDefault(m => m.UserId == UserId);
        if (!IsAdminUser(_admin) && (membership is null || membership.Role == MembershipRole.Student)) return (null, Forbid());

        var students = board.Members.Where(m => m.Role == MembershipRole.Student)
            .OrderBy(m => m.User!.DisplayName).ThenBy(m => m.UserId).ToList();
        var problems = board.Problems.OrderBy(p => p.Position).ThenBy(p => p.Id).ToList();
        var labels = LabelProblems(problems);

        string scopeFile = "", scopeNote = "Semua soal";
        if (!string.IsNullOrWhiteSpace(problem))
        {
            var one = problems.FirstOrDefault(p => p.Slug == problem);
            if (one is null) return (null, NotFound());
            problems = [one];
            scopeFile = $"soal-{Slugify(one.Slug)}";
            scopeNote = $"Soal: {one.Title}";
        }
        else if (group is int gid)
        {
            if (gid == 0)
            {
                problems = problems.Where(p => p.GroupId == null).ToList();
                scopeFile = "tanpa-grup";
                scopeNote = "Soal tanpa grup";
            }
            else
            {
                var g = board.Groups.FirstOrDefault(x => x.Id == gid);
                if (g is null) return (null, NotFound());
                problems = problems.Where(p => p.GroupId == gid).ToList();
                scopeFile = $"grup-{Slugify(g.Title)}";
                scopeNote = $"Grup: {g.Title}";
            }
        }
        return (new Export(board, students, problems, scopeFile, scopeNote, labels), null);
    }

    /// <summary>The caller's own UTC offset (stored from the browser, see UtcOffsetMiddleware), so timestamps read in their local time.</summary>
    private async Task<TimeSpan> LocalOffsetAsync() =>
        TimeSpan.FromMinutes(await _db.Users.Where(u => u.Id == UserId).Select(u => u.UtcOffsetMinutes).FirstOrDefaultAsync());

    private static string OffsetLabel(TimeSpan o) =>
        o == TimeSpan.Zero ? "UTC" : $"UTC{(o < TimeSpan.Zero ? '-' : '+')}{Math.Abs(o.Hours)}{(o.Minutes != 0 ? ":" + Math.Abs(o.Minutes).ToString("00") : "")}";

    private static string VerdictLabel(Verdict v) => v == Verdict.None ? "Belum dinilai" : v.ToString();

    private static double Pct(double score) => Math.Round(score * 100, 1);

    /// <summary>Verdict and score of one attempt come from the same submission: the best one (highest score, then the newest).</summary>
    private static Submission Best(IEnumerable<Submission> subs) =>
        subs.OrderByDescending(s => s.Score).ThenByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id).First();

    private static Submission Latest(IEnumerable<Submission> subs) =>
        subs.OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id).First();

    /// <summary>Excel: one row per student with verdict, score and attempts for every problem, and a sheet with every attempt.</summary>
    [HttpGet("results.xlsx")]
    public async Task<IActionResult> Results(string slug, [FromQuery] string? problem = null, [FromQuery] int? group = null)
    {
        var (data, failure) = await LoadAsync(slug, problem, group);
        if (data is null) return failure!;
        var (board, students, problems) = (data.Board, data.Students, data.Problems);
        var offset = await LocalOffsetAsync();

        var studentIds = students.Select(s => s.UserId).ToList();
        var problemIds = problems.Select(p => p.Id).ToList();
        var subs = await _db.Submissions.AsNoTracking()
            .Where(s => problemIds.Contains(s.ProblemId) && studentIds.Contains(s.UserId))
            .Select(s => new Submission
            {
                Id = s.Id, ProblemId = s.ProblemId, UserId = s.UserId, Language = s.Language, Verdict = s.Verdict,
                Score = s.Score, RuntimeMs = s.RuntimeMs, MemoryKb = s.MemoryKb, CreatedAt = s.CreatedAt,
            })
            .ToListAsync();
        var byKey = subs.GroupBy(s => (s.UserId, s.ProblemId)).ToDictionary(g => g.Key, g => g.ToList());

        using var wb = new XLWorkbook();

        // ---- Rekap: one row per student ----
        var ws = wb.Worksheets.Add("Rekap");
        var headers = new List<string> { "Nama", "Email" };
        foreach (var p in problems)
        {
            var name = Display(data, p);
            headers.Add($"{name} - Verdict");
            headers.Add($"{name} - Skor (%)");
            headers.Add($"{name} - Percobaan");
        }
        headers.Add("Soal Accepted");
        headers.Add("Rata-rata skor (%)");
        for (var c = 0; c < headers.Count; c++) ws.Cell(1, c + 1).Value = headers[c];

        var green = XLColor.FromHtml("#D1FAE5"); var amber = XLColor.FromHtml("#FEF3C7"); var grey = XLColor.FromHtml("#F1F5F9");
        for (var r = 0; r < students.Count; r++)
        {
            var m = students[r];
            var row = r + 2;
            ws.Cell(row, 1).Value = m.User!.DisplayName;
            ws.Cell(row, 2).Value = m.User.Email;
            var col = 3; var solved = 0; double total = 0;
            foreach (var p in problems)
            {
                byKey.TryGetValue((m.UserId, p.Id), out var list);
                var best = list is { Count: > 0 } ? Best(list) : null;
                ws.Cell(row, col).Value = best is null ? "Belum mencoba" : VerdictLabel(best.Verdict);
                ws.Cell(row, col).Style.Fill.BackgroundColor = best is null ? grey : best.Verdict == Verdict.Accepted && best.Score >= 1.0 ? green : amber;
                ws.Cell(row, col + 1).Value = best is null ? 0 : Pct(best.Score);
                ws.Cell(row, col + 2).Value = list?.Count ?? 0;
                if (best is { Verdict: Verdict.Accepted } && best.Score >= 1.0) solved++;
                total += best?.Score ?? 0;
                col += 3;
            }
            ws.Cell(row, col).Value = solved;
            ws.Cell(row, col + 1).Value = problems.Count == 0 ? 0 : Pct(total / problems.Count);
        }
        ws.Row(1).Style.Font.Bold = true;
        ws.SheetView.FreezeRows(1);
        ws.SheetView.FreezeColumns(2);
        ws.Columns().AdjustToContents();

        var note = wb.Worksheets.Add("Catatan");
        var lines = new[]
        {
            $"Board: {board.Title}",
            $"Cakupan: {data.ScopeNote}",
            $"Diunduh: {(DateTime.UtcNow + offset):yyyy-MM-dd HH:mm} ({OffsetLabel(offset)}) oleh {ActorEmail}",
            "Huruf atau angka di depan nama soal sama dengan label kolom pada grid board (\"-\" = soal yang disembunyikan dari siswa).",
            "Verdict dan skor tiap soal diambil dari percobaan terbaik siswa (skor tertinggi; bila sama, yang terbaru).",
            "Skor adalah persentase poin test case yang lulus. Soal yang belum dicoba dihitung skor 0 pada rata-rata.",
            "\"Soal Accepted\" menghitung soal dengan verdict Accepted dan skor 100%.",
            "Sheet \"Percobaan\" memuat setiap pengiriman siswa, termasuk yang bukan terbaik.",
        };
        for (var n = 0; n < lines.Length; n++) note.Cell(n + 1, 1).Value = lines[n];
        note.Column(1).AdjustToContents();

        // ---- Percobaan: every attempt ----
        var log = wb.Worksheets.Add("Percobaan");
        string[] logHeaders = { $"Waktu ({OffsetLabel(offset)})", "Nama", "Email", "Soal", "Bahasa", "Verdict", "Skor (%)", "Runtime (ms)", "Memori (KB)" };
        for (var c = 0; c < logHeaders.Length; c++) log.Cell(1, c + 1).Value = logHeaders[c];
        var byStudent = students.ToDictionary(s => s.UserId, s => s.User!);
        var titleById = problems.ToDictionary(p => p.Id, p => Display(data, p));
        var rowNo = 2;
        foreach (var s in subs.OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id))
        {
            log.Cell(rowNo, 1).Value = s.CreatedAt + offset;
            log.Cell(rowNo, 1).Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
            log.Cell(rowNo, 2).Value = byStudent[s.UserId].DisplayName;
            log.Cell(rowNo, 3).Value = byStudent[s.UserId].Email;
            log.Cell(rowNo, 4).Value = titleById[s.ProblemId];
            log.Cell(rowNo, 5).Value = s.Language ?? "";
            log.Cell(rowNo, 6).Value = VerdictLabel(s.Verdict);
            log.Cell(rowNo, 7).Value = Pct(s.Score);
            log.Cell(rowNo, 8).Value = s.RuntimeMs;
            log.Cell(rowNo, 9).Value = s.MemoryKb;
            rowNo++;
        }
        log.Row(1).Style.Font.Bold = true;
        log.SheetView.FreezeRows(1);
        log.Columns().AdjustToContents();
        note.Position = wb.Worksheets.Count;

        await _audit.RecordAsync(UserId, ActorEmail, "export", "Board", board.Id, $"{board.Title}: results.xlsx ({data.ScopeNote})");
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(), XlsxType, $"{board.Slug}-hasil-{Scoped(data)}{DateTime.UtcNow + offset:yyyyMMdd-HHmm}.xlsx");
    }

    /// <summary>ZIP of the students' source code: one folder per problem, one file per student. <c>pick</c> is
    /// <c>best</c> (default: highest score, then newest) or <c>latest</c>.</summary>
    [HttpGet("answers.zip")]
    public async Task<IActionResult> Answers(string slug, [FromQuery] string? pick = "best", [FromQuery] string? problem = null, [FromQuery] int? group = null)
    {
        var (data, failure) = await LoadAsync(slug, problem, group);
        if (data is null) return failure!;
        var (board, students, problems) = (data.Board, data.Students, data.Problems);
        var latest = string.Equals(pick, "latest", StringComparison.OrdinalIgnoreCase);
        var offset = await LocalOffsetAsync();

        var studentIds = students.Select(s => s.UserId).ToList();
        var problemIds = problems.Select(p => p.Id).ToList();
        var subs = await _db.Submissions.AsNoTracking()
            .Where(s => problemIds.Contains(s.ProblemId) && studentIds.Contains(s.UserId))
            .ToListAsync();
        var byKey = subs.GroupBy(s => (s.UserId, s.ProblemId)).ToDictionary(g => g.Key, g => g.ToList());

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in problems)
            {
                var label = data.Labels[p.Id];
                var folder = label == "-" ? $"{Safe(p.Title)} (tersembunyi)" : $"{(label.All(char.IsDigit) ? label.PadLeft(2, '0') : label)}. {Safe(p.Title)}";
                foreach (var m in students)
                {
                    if (!byKey.TryGetValue((m.UserId, p.Id), out var list) || list.Count == 0) continue;
                    var s = latest ? Latest(list) : Best(list);
                    var lang = string.IsNullOrEmpty(s.Language) ? Languages.Default(p.AllowedLanguages) : s.Language;
                    var path = $"{folder}/{Safe(m.User!.DisplayName)}_{m.UserId}.{(lang == "c" ? "c" : "cpp")}";
                    if (!used.Add(path)) continue;
                    var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
                    var stamp = new DateTimeOffset(DateTime.SpecifyKind(s.CreatedAt, DateTimeKind.Utc));
                    entry.LastWriteTime = stamp.Year < 1980 ? new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero) : stamp;
                    await using var w = entry.Open();
                    await w.WriteAsync(new UTF8Encoding(false).GetBytes(s.Code ?? ""));
                }
            }
        }

        await _audit.RecordAsync(UserId, ActorEmail, "export", "Board", board.Id, $"{board.Title}: answers.zip ({(latest ? "latest" : "best")}, {data.ScopeNote})");
        return File(ms.ToArray(), "application/zip", $"{board.Slug}-jawaban-{(latest ? "terakhir" : "terbaik")}-{Scoped(data)}{DateTime.UtcNow + offset:yyyyMMdd-HHmm}.zip");
    }

    /// <summary>"Sesi 1: dasar / tes" becomes "sesi-1-dasar-tes": lower case, letters and digits only, joined by hyphens.</summary>
    private static string Slugify(string s)
    {
        var t = System.Text.RegularExpressions.Regex.Replace((s ?? "").ToLowerInvariant(), @"[^\p{L}\p{N}]+", "-").Trim('-');
        return t.Length == 0 ? "x" : t.Length > 40 ? t[..40].TrimEnd('-') : t;
    }

    /// <summary>"A. Title", or "Title (tersembunyi)" for a problem the grid leaves out.</summary>
    private static string Display(Export d, Problem p) => d.Labels[p.Id] == "-" ? $"{p.Title} (tersembunyi)" : $"{d.Labels[p.Id]}. {p.Title}";

    private static string Scoped(Export d) => d.ScopeFile.Length == 0 ? "" : d.ScopeFile + "-";

    /// <summary>A name that is safe as a file or folder name on Windows, macOS and Linux.</summary>
    private static string Safe(string s, int max = 60)
    {
        var sb = new StringBuilder();
        foreach (var ch in (s ?? "").Trim()) sb.Append(char.IsControl(ch) || "\\/:*?\"<>|".Contains(ch) ? '_' : ch);
        var t = sb.ToString().Trim().TrimEnd('.');
        if (t.Length == 0) t = "_";
        return t.Length > max ? t[..max].TrimEnd() : t;
    }
}
