using BeeCoding.Data;
using BeeCoding.Models;
using Microsoft.EntityFrameworkCore;

namespace BeeCoding.Services;

/// <summary>
/// What survives an account. A user's public practice problems are a contribution other students have solved, so on deletion they
/// can be handed to the placeholder account "Deleted user" instead of being cascaded away (which would also wipe every other
/// student's attempts at them). Private drafts, pending reviews, revisions and board-only copies are never kept. Call it inside the
/// caller's transaction, before the user row is removed.
/// </summary>
public sealed class AccountErasure(AppDbContext db)
{
    public const string SystemEmail = "deleted-user@system.invalid";

    /// <summary>The placeholder account, created on first use. Soft-deleted from the start, so no one can sign in as it and it is
    /// left out of every user list; <c>IsSystem</c> keeps it out of the Trash, the purge and the retention sweep.</summary>
    public async Task<int> SystemUserIdAsync(CancellationToken ct = default)
    {
        var id = await db.Users.Where(u => u.IsSystem).Select(u => (int?)u.Id).FirstOrDefaultAsync(ct);
        if (id is int existing) return existing;
        var user = new User
        {
            Email = SystemEmail,
            DisplayName = "Deleted user",
            Role = UserRole.Student,
            IsSystem = true,
            HasPassword = false,
            PasswordHash = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48)),   // unknowable
            DeletedAt = DateTime.UtcNow,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user.Id;
    }

    /// <summary>Moves the user's public, non-board-only problems to the placeholder account; returns how many.</summary>
    public async Task<int> KeepPublicProblemsAsync(int userId, CancellationToken ct = default)
    {
        var ids = await db.BankProblems.Where(b => b.OwnerId == userId && b.IsPublic && !b.BoardOnly).Select(b => b.Id).ToListAsync(ct);
        if (ids.Count == 0) return 0;
        var systemId = await SystemUserIdAsync(ct);
        await db.BankProblemShares.Where(s => ids.Contains(s.BankProblemId)).ExecuteDeleteAsync(ct);   // a share was a private arrangement between two people
        await db.BankProblems.Where(b => ids.Contains(b.Id)).ExecuteUpdateAsync(u => u.SetProperty(b => b.OwnerId, systemId), ct);
        return ids.Count;
    }
}
