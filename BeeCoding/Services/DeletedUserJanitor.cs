using BeeCoding.Data;
using Microsoft.EntityFrameworkCore;
using BeeCoding.Services.Realtime;

namespace BeeCoding.Services;

/// <summary>
/// An admin-deleted account is only soft-deleted (restorable any time). Left alone that keeps a person's data forever, so after the
/// retention set in Admin → System it is purged for good, the same way as the Trash's Purge: public problems stay under "Deleted
/// user", everything else of theirs goes. Accounts that still own boards, admin addresses and the placeholder account are skipped.
/// </summary>
public sealed class DeletedUserJanitor(IServiceScopeFactory scopes, PlatformRuntimeConfig runtime, ILogger<DeletedUserJanitor> log, JobLock? locks = null) : BackgroundService
{
    private static readonly TimeSpan FirstRunDelay = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(12);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(FirstRunDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            if (locks.ShouldRun("deleted-users", Interval)) await SweepAsync(stoppingToken);
            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task SweepAsync(CancellationToken ct)
    {
        if (runtime.DeletedUserRetentionDays <= 0) return;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var admin = scope.ServiceProvider.GetRequiredService<AdminAccess>();
            var erasure = scope.ServiceProvider.GetRequiredService<AccountErasure>();
            var cutoff = DateTime.UtcNow.AddDays(-runtime.DeletedUserRetentionDays);
            var due = await db.Users.Where(u => u.DeletedAt != null && u.DeletedAt < cutoff && !u.IsSystem).Select(u => u.Id).ToListAsync(ct);
            var purged = 0;
            foreach (var id in due)
            {
                try
                {
                    var user = await db.Users.FirstAsync(u => u.Id == id, ct);
                    if (admin.IsAdminEmail(user.Email)) continue;
                    if (await db.Boards.IgnoreQueryFilters().AnyAsync(b => b.OwnerId == id, ct)) continue;
                    await using var tx = await db.Database.BeginTransactionAsync(ct);
                    await erasure.KeepPublicProblemsAsync(id, ct);
                    db.Users.Remove(user);
                    await db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    purged++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogWarning(ex, "Could not purge deleted account #{Id}", id);
                    db.ChangeTracker.Clear();
                }
            }
            if (purged > 0) log.LogInformation("Purged {Count} account(s) deleted more than {Days} days ago", purged, runtime.DeletedUserRetentionDays);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { log.LogWarning(ex, "Deleted-account sweep failed"); }
    }
}
