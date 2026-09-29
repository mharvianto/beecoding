using System.Text.Json;
using StackExchange.Redis;

namespace BeeCoding.Services.Realtime;

/// <summary>
/// Selects the backend for the ephemeral realtime stores (drafts / lecture buffers /
/// presence). "memory" (default) is per-process — correct only for a single node. "redis"
/// shares them across web replicas (see DEPLOY.md §2.4).
/// </summary>
public sealed class RealtimeStoreOptions
{
    /// <summary>"memory" | "redis"</summary>
    public string Backend { get; set; } = "memory";

    /// <summary>StackExchange.Redis connection string, required when Backend = "redis".</summary>
    public string? RedisConnectionString { get; set; }

    /// <summary>Key prefix so several deployments can share one Redis instance.</summary>
    public string KeyPrefix { get; set; } = "bc:";

    /// <summary>TTL for these ephemeral keys, refreshed on every write. 0 = never expire.
    /// A safety net for crashed pods that never ran their disconnect cleanup.</summary>
    public int TtlSeconds { get; set; } = 43_200;   // 12h

    public bool UseRedis => string.Equals(Backend, "redis", StringComparison.OrdinalIgnoreCase);
}

internal static class RedisJson
{
    public static readonly JsonSerializerOptions Opts = new(JsonSerializerDefaults.Web);
    public static string Ser<T>(T v) => JsonSerializer.Serialize(v, Opts);
    public static T? De<T>(RedisValue v) => v.IsNullOrEmpty ? default : JsonSerializer.Deserialize<T>((string)v!, Opts);
}

// ---------------------------------------------------------------------------
// Drafts
// ---------------------------------------------------------------------------
public sealed class RedisDraftStore(IConnectionMultiplexer mux, Microsoft.Extensions.Options.IOptions<RealtimeStoreOptions> opt) : IDraftStore
{
    private readonly IDatabase _db = mux.GetDatabase();
    private readonly RealtimeStoreOptions _o = opt.Value;
    private TimeSpan? Ttl => _o.TtlSeconds > 0 ? TimeSpan.FromSeconds(_o.TtlSeconds) : null;
    private string Key(int boardId) => $"{_o.KeyPrefix}drafts:{boardId}";

    public async ValueTask<Draft> SetAsync(int boardId, int problemId, int userId, string authorName, string code)
    {
        var d = new Draft(boardId, problemId, userId, authorName, code, DateTime.UtcNow);
        var key = Key(boardId);
        await _db.HashSetAsync(key, $"{problemId}:{userId}", RedisJson.Ser(d));
        if (Ttl is { } t) await _db.KeyExpireAsync(key, t);
        return d;
    }

    public async ValueTask<IReadOnlyList<Draft>> ForBoardAsync(int boardId)
    {
        var entries = await _db.HashValuesAsync(Key(boardId));
        return entries.Select(v => RedisJson.De<Draft>(v)).Where(d => d is not null).Select(d => d!).ToList();
    }

    public ValueTask RemoveForBoardAsync(int boardId) => new(_db.KeyDeleteAsync(Key(boardId)));
}

// ---------------------------------------------------------------------------
// Lecture buffers
// ---------------------------------------------------------------------------
public sealed class RedisLectureStore(IConnectionMultiplexer mux, Microsoft.Extensions.Options.IOptions<RealtimeStoreOptions> opt) : ILectureStore
{
    private readonly IDatabase _db = mux.GetDatabase();
    private readonly RealtimeStoreOptions _o = opt.Value;
    private TimeSpan? Ttl => _o.TtlSeconds > 0 ? TimeSpan.FromSeconds(_o.TtlSeconds) : null;
    private string Key(int boardId) => $"{_o.KeyPrefix}lecture:{boardId}";

    public async ValueTask<Lecture> SetAsync(int boardId, int problemId, string code, string language, string teacherName, string stdin = "", int teacherId = 0)
    {
        var l = new Lecture(boardId, problemId, code, language, teacherName, DateTime.UtcNow, stdin, teacherId);
        var key = Key(boardId);
        await _db.HashSetAsync(key, problemId, RedisJson.Ser(l));
        if (Ttl is { } t) await _db.KeyExpireAsync(key, t);
        return l;
    }

    public async ValueTask<Lecture?> GetAsync(int boardId, int problemId) =>
        RedisJson.De<Lecture>(await _db.HashGetAsync(Key(boardId), problemId));

    public ValueTask RemoveForBoardAsync(int boardId) => new(_db.KeyDeleteAsync(Key(boardId)));
}

// ---------------------------------------------------------------------------
// Presence
// ---------------------------------------------------------------------------
public sealed class RedisPresenceTracker(IConnectionMultiplexer mux, Microsoft.Extensions.Options.IOptions<RealtimeStoreOptions> opt) : IPresenceTracker
{
    private readonly IDatabase _db = mux.GetDatabase();
    private readonly RealtimeStoreOptions _o = opt.Value;
    private TimeSpan? Ttl => _o.TtlSeconds > 0 ? TimeSpan.FromSeconds(_o.TtlSeconds) : null;

    private string BoardKey(int boardId) => $"{_o.KeyPrefix}presence:board:{boardId}";
    private string ConnKey(string connectionId) => $"{_o.KeyPrefix}presence:conn:{connectionId}";

    private sealed record Slot(int UserId, string DisplayName, bool IsStaff);

    public async ValueTask AddAsync(string connectionId, int boardId, int userId, string name, bool isStaff)
    {
        var bk = BoardKey(boardId);
        var ck = ConnKey(connectionId);
        await _db.HashSetAsync(bk, connectionId, RedisJson.Ser(new Slot(userId, name, isStaff)));
        await _db.StringSetAsync(ck, boardId);
        if (Ttl is { } t) { await _db.KeyExpireAsync(bk, t); await _db.KeyExpireAsync(ck, t); }
    }

    public async ValueTask<int?> RemoveAsync(string connectionId)
    {
        var ck = ConnKey(connectionId);
        var boardId = (int?)await _db.StringGetAsync(ck);
        if (boardId is int b)
        {
            await _db.HashDeleteAsync(BoardKey(b), connectionId);
            await _db.KeyDeleteAsync(ck);
        }
        return boardId;
    }

    public async ValueTask<IReadOnlyList<PresenceUser>> ForBoardAsync(int boardId)
    {
        var vals = await _db.HashValuesAsync(BoardKey(boardId));
        return vals.Select(v => RedisJson.De<Slot>(v)).Where(s => s is not null).Select(s => s!)
            .GroupBy(s => s.UserId)
            .Select(g => new PresenceUser(g.Key, g.First().DisplayName, g.First().IsStaff))
            .OrderBy(u => u.DisplayName)
            .ToList();
    }
}
