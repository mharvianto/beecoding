using BeeCoding.Data;
using BeeCoding.Models;

namespace BeeCoding.Services;

/// <summary>Records moderation actions (delete/restore/purge) for the admin panel's audit log.</summary>
public class AuditLog(AppDbContext db)
{
    private readonly AppDbContext _db = db;

    public async Task RecordAsync(int actorUserId, string actorEmail, string action, string targetType, int targetId, string targetLabel)
    {
        _db.AuditLogEntries.Add(new AuditLogEntry
        {
            ActorUserId = actorUserId,
            ActorEmail = actorEmail,
            Action = Clip(action, 64),
            TargetType = targetType,
            TargetId = targetId,
            TargetLabel = Clip(targetLabel, 4000),
        });
        await _db.SaveChangesAsync();
    }

    // A server database refuses text longer than the column (SQLite never did): a long title must not fail the action it records.
    private static string Clip(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
