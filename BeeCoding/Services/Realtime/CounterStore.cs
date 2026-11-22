using System.Collections.Concurrent;
using StackExchange.Redis;

namespace BeeCoding.Services.Realtime;

/// <summary>
/// Small shared counters with an expiry: what the login/submit throttles and the "who runs this background job" claim need.
/// In memory by default (right for ONE instance); with <c>Realtime:Backend=redis</c> every web instance shares them, so a
/// guesser cannot get the cap once per instance and two instances do not both sweep the same table.
/// </summary>
public interface ICounterStore
{
    /// <summary>Adds 1 and returns the new value. The window starts when the key is created (fixed window, not sliding).</summary>
    long Increment(string key, TimeSpan window);
    /// <summary>Takes 1 back off a live counter (an attempt that was counted and then refused); a counter at 0 is dropped.</summary>
    void Decrement(string key);
    long Get(string key);
    void Remove(string key);
    /// <summary>Takes the key for <paramref name="ttl"/> if nobody holds it (SET NX). False = someone else has it.</summary>
    bool TryClaim(string key, TimeSpan ttl);
    /// <summary>Time left on a key, or null if it is not held.</summary>
    TimeSpan? Remaining(string key);
}

public sealed class InMemoryCounterStore : ICounterStore
{
    private readonly Dictionary<string, (long Value, long ExpiresTicks)> _map = [];
    private int _ops;

    private static long Now => DateTime.UtcNow.Ticks;

    public long Increment(string key, TimeSpan window)
    {
        lock (_map)
        {
            Prune();
            var v = _map.TryGetValue(key, out var e) && e.ExpiresTicks > Now ? e : (0, Now + window.Ticks);
            _map[key] = (v.Item1 + 1, v.Item2);
            return v.Item1 + 1;
        }
    }

    public void Decrement(string key)
    {
        lock (_map)
        {
            if (!_map.TryGetValue(key, out var e) || e.ExpiresTicks <= Now) return;
            if (e.Value <= 1) _map.Remove(key); else _map[key] = (e.Value - 1, e.ExpiresTicks);
        }
    }

    public long Get(string key)
    {
        lock (_map) return _map.TryGetValue(key, out var e) && e.ExpiresTicks > Now ? e.Value : 0;
    }

    public void Remove(string key) { lock (_map) _map.Remove(key); }

    public bool TryClaim(string key, TimeSpan ttl)
    {
        lock (_map)
        {
            if (_map.TryGetValue(key, out var e) && e.ExpiresTicks > Now) return false;
            _map[key] = (1, Now + ttl.Ticks);
            return true;
        }
    }

    public TimeSpan? Remaining(string key)
    {
        lock (_map) return _map.TryGetValue(key, out var e) && e.ExpiresTicks > Now ? TimeSpan.FromTicks(e.ExpiresTicks - Now) : null;
    }

    // Expired keys are dropped now and then (every 1000th write), so a long-running process does not keep every IP it ever saw.
    private void Prune()
    {
        if (++_ops % 1000 != 0) return;
        var now = Now;
        foreach (var k in _map.Where(kv => kv.Value.ExpiresTicks <= now).Select(kv => kv.Key).ToList()) _map.Remove(k);
    }
}

/// <summary>
/// Redis-backed. The calls are synchronous on purpose (the throttle API is, and each is one sub-millisecond round trip). If Redis
/// cannot be reached the call falls back to this instance's own memory: protection per instance is better than none, and better than
/// locking everyone out of sign-in because the cache is down.
/// </summary>
public sealed class RedisCounterStore(IConnectionMultiplexer mux, Microsoft.Extensions.Options.IOptions<RealtimeStoreOptions> opt, ILogger<RedisCounterStore> log) : ICounterStore
{
    // INCR and set the expiry in one step, so a crash between the two cannot leave a counter that never expires.
    private const string IncrScript = "local v = redis.call('INCR', KEYS[1]) if v == 1 then redis.call('PEXPIRE', KEYS[1], ARGV[1]) end return v";

    // DECR only a key that is still there: on an expired one it would create -1 with no expiry.
    private const string DecrScript = "if redis.call('EXISTS', KEYS[1]) == 1 then local v = redis.call('DECR', KEYS[1]) if v <= 0 then redis.call('DEL', KEYS[1]) end end return 0";

    private readonly IDatabase _db = mux.GetDatabase();
    private readonly string _prefix = opt.Value.KeyPrefix + "ctr:";
    private readonly InMemoryCounterStore _fallback = new();
    private long _lastWarn;

    private T Try<T>(Func<T> redis, Func<T> local)
    {
        try { return redis(); }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            var now = Environment.TickCount64;
            if (now - Interlocked.Exchange(ref _lastWarn, now) > 60_000) log.LogWarning(ex, "Redis counters unavailable; using this instance's memory");
            return local();
        }
    }

    public long Increment(string key, TimeSpan window) =>
        Try(() => (long)_db.ScriptEvaluate(IncrScript, [(RedisKey)(_prefix + key)], [(RedisValue)(long)Math.Max(1, window.TotalMilliseconds)]),
            () => _fallback.Increment(key, window));

    public void Decrement(string key) =>
        Try(() => { _db.ScriptEvaluate(DecrScript, [(RedisKey)(_prefix + key)]); return 0; }, () => { _fallback.Decrement(key); return 0; });

    public long Get(string key) =>
        Try(() => (long?)_db.StringGet(_prefix + key) ?? 0, () => _fallback.Get(key));

    public void Remove(string key) =>
        Try(() => { _db.KeyDelete(_prefix + key); return 0; }, () => { _fallback.Remove(key); return 0; });

    public bool TryClaim(string key, TimeSpan ttl) =>
        Try(() => _db.StringSet(_prefix + key, 1, ttl, When.NotExists), () => _fallback.TryClaim(key, ttl));

    public TimeSpan? Remaining(string key) =>
        Try(() => _db.KeyTimeToLive(_prefix + key), () => _fallback.Remaining(key));
}

/// <summary>"Run this periodic job on one instance only": the job claims its name for just under its own interval before a sweep.
/// With one instance the claim always succeeds; with several, whoever sweeps first keeps the job until it stops.</summary>
public sealed class JobLock(ICounterStore store)
{
    public bool TryClaim(string job, TimeSpan every) => store.TryClaim("job:" + job, every * 0.9);
}

public static class JobLockExtensions
{
    /// <summary>True when there is no lock (tests build janitors by hand) or this instance holds the job.</summary>
    public static bool ShouldRun(this JobLock? locks, string job, TimeSpan every) => locks?.TryClaim(job, every) ?? true;
}
