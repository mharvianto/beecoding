using System.Collections.Concurrent;

namespace BeeCoding.Services;

/// <summary>
/// Fixed-window failed-login throttle, counted independently by client IP and by account.
/// Once either counter passes its cap inside the window, further attempts are refused until
/// the window rolls over. A successful login clears both counters.
/// </summary>
public sealed class LoginThrottle
{
    private sealed class Bucket
    {
        public int Fails;
        public long WindowTicks;
    }

    private static readonly long Window = TimeSpan.FromMinutes(15).Ticks;
    private const int IpCap = 5000;   // very generous: a whole classroom can share one NAT/wifi
    // A single account being wrong 1000 times in 15 minutes isn't a real cap — that's enough
    // attempts for a scripted brute force to get well into a small wordlist. One account is
    // never shared the way an IP is, so this can be strict without punishing real students.
    private const int AccountCap = 10;

    private readonly ConcurrentDictionary<string, Bucket> _byIp = new();
    private readonly ConcurrentDictionary<string, Bucket> _byAccount = new();

    public bool IsBlocked(string ip, string account) =>
        Over(_byIp, ip, IpCap) || Over(_byAccount, account, AccountCap);

    public void RecordFailure(string ip, string account)
    {
        Bump(_byIp, ip);
        Bump(_byAccount, account);
        Prune();
    }

    public void RecordSuccess(string ip, string account)
    {
        _byIp.TryRemove(ip, out _);
        _byAccount.TryRemove(account, out _);
    }

    private static bool Over(ConcurrentDictionary<string, Bucket> map, string key, int cap)
    {
        if (string.IsNullOrEmpty(key) || !map.TryGetValue(key, out var b)) return false;
        if (DateTime.UtcNow.Ticks - b.WindowTicks > Window) return false;
        return b.Fails >= cap;
    }

    private static void Bump(ConcurrentDictionary<string, Bucket> map, string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        var now = DateTime.UtcNow.Ticks;
        map.AddOrUpdate(key,
            _ => new Bucket { Fails = 1, WindowTicks = now },
            (_, b) =>
            {
                if (now - b.WindowTicks > Window) { b.Fails = 0; b.WindowTicks = now; }
                b.Fails++;
                return b;
            });
    }

    private int _pruneGuard;
    private void Prune()
    {
        // opportunistic: at most one pruning pass at a time, and only occasionally.
        if ((_byIp.Count + _byAccount.Count) < 5_000) return;
        if (Interlocked.Exchange(ref _pruneGuard, 1) == 1) return;
        try
        {
            var now = DateTime.UtcNow.Ticks;
            foreach (var map in new[] { _byIp, _byAccount })
                foreach (var kv in map)
                    if (now - kv.Value.WindowTicks > Window)
                        map.TryRemove(kv.Key, out _);
        }
        finally { Interlocked.Exchange(ref _pruneGuard, 0); }
    }
}
