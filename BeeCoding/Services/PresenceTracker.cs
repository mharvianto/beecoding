using System.Collections.Concurrent;

namespace BeeCoding.Services;

public record PresenceUser(int UserId, string DisplayName, bool IsStaff);

/// <summary>Map of live SignalR connections per board (who is currently on which board).</summary>
public interface IPresenceTracker
{
    ValueTask AddAsync(string connectionId, int boardId, int userId, string name, bool isStaff);
    /// <summary>Removes the connection; returns the board it was on, or null if unknown.</summary>
    ValueTask<int?> RemoveAsync(string connectionId);
    ValueTask<IReadOnlyList<PresenceUser>> ForBoardAsync(int boardId);
    /// <summary>Every live connection of this user on this board (a user can have several tabs).</summary>
    ValueTask<IReadOnlyList<string>> ConnectionsOfAsync(int boardId, int userId);
}

/// <summary>Per-process implementation.</summary>
public sealed class InMemoryPresenceTracker : IPresenceTracker
{
    private record Entry(int BoardId, int UserId, string DisplayName, bool IsStaff);

    private readonly ConcurrentDictionary<string, Entry> _byConnection = new();

    public ValueTask AddAsync(string connectionId, int boardId, int userId, string name, bool isStaff)
    {
        _byConnection[connectionId] = new Entry(boardId, userId, name, isStaff);
        return ValueTask.CompletedTask;
    }

    public ValueTask<int?> RemoveAsync(string connectionId) =>
        ValueTask.FromResult(_byConnection.TryRemove(connectionId, out var e) ? e.BoardId : (int?)null);

    public ValueTask<IReadOnlyList<string>> ConnectionsOfAsync(int boardId, int userId) =>
        ValueTask.FromResult<IReadOnlyList<string>>(_byConnection.Where(kv => kv.Value.BoardId == boardId && kv.Value.UserId == userId).Select(kv => kv.Key).ToList());

    public ValueTask<IReadOnlyList<PresenceUser>> ForBoardAsync(int boardId) =>
        ValueTask.FromResult<IReadOnlyList<PresenceUser>>(_byConnection.Values
            .Where(e => e.BoardId == boardId)
            .GroupBy(e => e.UserId)
            .Select(g => new PresenceUser(g.Key, g.First().DisplayName, g.First().IsStaff))
            .OrderBy(u => u.DisplayName)
            .ToList());
}
