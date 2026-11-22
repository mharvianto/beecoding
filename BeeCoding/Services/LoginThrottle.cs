using BeeCoding.Services.Realtime;

namespace BeeCoding.Services;

/// <summary>
/// Fixed-window failed-login throttle, counted by client IP, by (account, IP) and by account overall.
/// Once any counter passes its cap inside the window, further attempts are refused until
/// the window rolls over. A successful login clears the counters. The counters live in an <see cref="ICounterStore"/>: this
/// instance's memory by default, Redis (shared by every web instance) with Realtime:Backend=redis.
/// </summary>
public sealed class LoginThrottle(ICounterStore? store = null)
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    private const int IpCap = 5000;   // very generous: a whole classroom can share one NAT/wifi
    // A single account being wrong 1000 times in 15 minutes isn't a real cap — that's enough
    // attempts for a scripted brute force to get well into a small wordlist. One account is
    // never shared the way an IP is, so this can be strict without punishing real students.
    private const int AccountCap = 10;
    // The 10-cap counts per (account, IP): otherwise anyone could type a teacher's email and wrong
    // passwords 10 times to lock them out of their own account. A spread-out guess (many IPs, one
    // account) is caught by this much higher total instead.
    private const int AccountTotalCap = 100;

    private readonly ICounterStore _store = store ?? new InMemoryCounterStore();

    // A counter whose subject is empty (no account typed) is left out, as before.
    private static IEnumerable<(string Key, int Cap)> Counters(string ip, string account, bool strict)
    {
        if (!string.IsNullOrEmpty(ip)) yield return ("lt:ip:" + ip, IpCap);
        if (!string.IsNullOrEmpty(account)) yield return ("lt:pair:" + account + "|" + ip, AccountCap);
        if (!string.IsNullOrEmpty(account)) yield return ("lt:acct:" + account, strict ? AccountCap : AccountTotalCap);
    }

    /// <param name="strict">For guessing a short code (2-step): the account-wide cap stays at <see cref="AccountCap"/>
    /// whatever IPs the guesses come from, since 100 tries at a 6-digit code is too generous.</param>
    public bool IsBlocked(string ip, string account, bool strict = false) =>
        Counters(ip, account, strict).Any(c => _store.Get(c.Key) >= c.Cap);

    /// <summary>Counts this attempt as a failure (a success later clears it with <see cref="RecordSuccess"/>) and says whether it may go on.
    /// Use it BEFORE checking the password or code: checking first and recording after lets a burst of simultaneous guesses all pass the
    /// check, so the cap would not hold. Each counter is incremented atomically (also across instances with Redis), strictest first;
    /// the first one over its cap refuses the attempt and takes back what this call added, so a refused attempt counts nowhere — a
    /// flood at one account from one address cannot push that account's overall counter up and lock its owner out elsewhere.
    /// False = refuse the attempt.</summary>
    public bool TryBegin(string ip, string account, bool strict = false)
    {
        var added = new List<string>(3);
        foreach (var (key, cap) in Counters(ip, account, strict).OrderBy(c => c.Cap))
        {
            added.Add(key);
            if (_store.Increment(key, Window) <= cap) continue;
            foreach (var k in added) _store.Decrement(k);
            return false;
        }
        return true;
    }

    public void RecordFailure(string ip, string account)
    {
        foreach (var (key, _) in Counters(ip, account, false)) _store.Increment(key, Window);
    }

    public void RecordSuccess(string ip, string account)
    {
        foreach (var (key, _) in Counters(ip, account, false)) _store.Remove(key);
    }
}
