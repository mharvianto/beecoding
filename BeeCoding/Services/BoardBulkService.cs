using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services.Judge;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Services;

/// <summary>
/// Shared engine behind the admin and org-admin "bulk add" actions: apply the same groups and
/// the same bank problems to many boards. Callers decide *which* boards and bank problems the
/// actor may touch; this only does the work, one board at a time so a failure on one board
/// never blocks the rest.
/// </summary>
public class BoardBulkService(AppDbContext db, IBoardNotifier notifier)
{
    private readonly AppDbContext _db = db;
    private readonly IBoardNotifier _notifier = notifier;

    public async Task<List<BulkAddBoardRow>> ApplyAsync(
        IReadOnlyList<Board> boards, IReadOnlyList<UpsertProblemGroupDto> groups,
        IReadOnlyList<BankProblem> bank, string? problemGroupTitle)
    {
        var rows = new List<BulkAddBoardRow>();
        foreach (var b in boards)
        {
            int gAdded = 0, gSkipped = 0, pAdded = 0, pSkipped = 0;
            try
            {
                var board = await _db.Boards.Include(x => x.Groups).Include(x => x.Problems)
                    .FirstAsync(x => x.Id == b.Id);

                ProblemGroup Ensure(UpsertProblemGroupDto spec, bool countIt)
                {
                    var title = spec.Title.Trim();
                    var existing = board.Groups.FirstOrDefault(g => string.Equals(g.Title, title, StringComparison.OrdinalIgnoreCase));
                    if (existing is not null) { if (countIt) gSkipped++; return existing; }
                    var g = new ProblemGroup { BoardId = board.Id, Position = (board.Groups.Select(x => (int?)x.Position).Max() ?? -1) + 1 };
                    GroupAccess.Apply(g, spec);
                    board.Groups.Add(g);
                    if (countIt) gAdded++;
                    return g;
                }

                foreach (var spec in groups) Ensure(spec, countIt: true);

                ProblemGroup? target = null;
                if (bank.Count > 0 && !string.IsNullOrWhiteSpace(problemGroupTitle))
                    target = Ensure(new UpsertProblemGroupDto(problemGroupTitle, false, false, null, null), countIt: false);

                await _db.SaveChangesAsync();   // assigns ids to any new groups

                var nextPos = (board.Problems.Select(p => (int?)p.Position).Max() ?? -1) + 1;
                foreach (var src in bank)
                {
                    if (board.Problems.Any(p => p.SourceBankProblemId == src.Id)) { pSkipped++; continue; }
                    var copy = ProblemFromBank(src, board.Id, nextPos++);
                    copy.GroupId = target?.Id;
                    _db.Problems.Add(copy);
                    board.Problems.Add(copy);
                    pAdded++;
                }
                await _db.SaveChangesAsync();

                if (gAdded + pAdded > 0)
                {
                    await _notifier.ProblemChangedAsync(board.Id);
                    await _notifier.ExamModeChangedAsync(board.Id, board.ExamMode);
                }
                rows.Add(new(board.Slug, board.Title, gAdded, gSkipped, pAdded, pSkipped, null));
            }
            catch (Exception ex)
            {
                _db.ChangeTracker.Clear();
                rows.Add(new(b.Slug, b.Title, 0, 0, 0, 0, ex.Message));
            }
        }
        return rows;
    }

    /// <summary>Independent copy of a bank problem (with all its tests) for a board.</summary>
    public static Problem ProblemFromBank(BankProblem bank, int boardId, int position)
    {
        var p = new Problem
        {
            BoardId = boardId,
            Title = bank.Title,
            StatementMarkdown = bank.StatementMarkdown,
            AllowedLanguages = bank.AllowedLanguages,
            Tags = bank.Tags,
            Level = bank.Level,
            GeneratedByAi = bank.GeneratedByAi,
            BannedHeaders = bank.BannedHeaders,
            BannedSymbols = bank.BannedSymbols,
            InputFileName = bank.InputFileName,
            TimeLimitMs = bank.TimeLimitMs,
            MemoryLimitKb = bank.MemoryLimitKb,
            Position = position,
            SourceBankProblemId = bank.Id,
        };
        foreach (var t in bank.TestCases.OrderBy(t => t.Position).ThenBy(t => t.Id))
            p.TestCases.Add(new TestCase
            {
                Stdin = t.Stdin,
                ExpectedStdout = t.ExpectedStdout,
                IsSample = t.IsSample,
                Points = t.Points,
                Position = t.Position,
            });
        return p;
    }

    /// <summary>Validate a bulk request's group specs; returns an error message or null.</summary>
    public static string? ValidateGroups(IEnumerable<UpsertProblemGroupDto> groups)
    {
        foreach (var g in groups)
            if (GroupAccess.Validate(g) is { } bad) return $"Group '{g.Title}': {bad}";
        return null;
    }
}
