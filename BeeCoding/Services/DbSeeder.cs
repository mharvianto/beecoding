using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Services;

public static class DbSeeder
{
    /// <summary>Curated 20-problem starter bank, owned by the first teacher, shared publicly.</summary>
    public static async Task SeedBankAsync(AppDbContext db)
    {
        if (await db.BankProblems.AnyAsync()) return;
        var owner = await db.Users.Where(u => u.Role == UserRole.Teacher)
            .OrderBy(u => u.Id).FirstOrDefaultAsync();
        if (owner is null) return;

        foreach (var s in BankSeed.Problems)
        {
            var b = new BankProblem
            {
                OwnerId = owner.Id,
                Title = s.Title,
                StatementMarkdown = s.Statement,
                AllowedLanguages = "",
                TimeLimitMs = 1000,
                MemoryLimitKb = 32_768,
                Level = s.Level,
                Tags = s.Tags,
                IsPublic = true,
            };
            int pos = 0;
            foreach (var (input, output, sample) in s.Tests)
                b.TestCases.Add(new BankTestCase
                {
                    Stdin = input,
                    ExpectedStdout = output,
                    IsSample = sample,
                    Points = sample ? 0 : 1,
                    Position = pos++,
                });
            db.BankProblems.Add(b);
        }
        await db.SaveChangesAsync();
    }

    public static async Task SeedAsync(AppDbContext db, PasswordService pw)
    {
        if (await db.Users.AnyAsync()) return;

        var teacher = new User
        {
            Email = "teacher@demo.test",
            DisplayName = "Demo Teacher",
            Role = UserRole.Teacher,
            EmailVerifiedAt = DateTime.UtcNow,   // demo account: nothing to confirm
        };
        teacher.PasswordHash = pw.Hash(teacher, "password");
        db.Users.Add(teacher);
        await db.SaveChangesAsync();

        var board = new Board { Title = "Demo Board", OwnerId = teacher.Id, JoinCode = "DEMO01", Slug = "demo-board" };
        db.Boards.Add(board);
        db.BoardMemberships.Add(new BoardMembership
        {
            Board = board, UserId = teacher.Id, Role = MembershipRole.Owner,
        });
        await db.SaveChangesAsync();

        var problem = new Problem
        {
            BoardId = board.Id,
            Title = "A + B",
            StatementMarkdown =
                "Read two integers `a` and `b` on one line, print `a + b`.\n\n" +
                "**Input:** `2 3` — **Output:** `5`",
            AllowedLanguages = "",
            Tags = "math",
            Level = ProblemLevel.Easy,
            TimeLimitMs = 1000,
            MemoryLimitKb = 32_768,
            Position = 0,
        };
        problem.TestCases.Add(new TestCase { Stdin = "2 3\n", ExpectedStdout = "5\n", IsSample = true, Points = 0, Position = 0 });
        problem.TestCases.Add(new TestCase { Stdin = "100 -40\n", ExpectedStdout = "60\n", IsSample = false, Points = 1, Position = 1 });
        problem.TestCases.Add(new TestCase { Stdin = "1000000000 1000000000\n", ExpectedStdout = "2000000000\n", IsSample = false, Points = 1, Position = 2 });
        db.Problems.Add(problem);
        await db.SaveChangesAsync();
    }
}
