using BeeCoding.Data;
using BeeCoding.Services;
using BeeCoding.Services.Realtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Xunit;

namespace BeeCoding.Tests;

/// <summary>The counters behind the throttles and job claims: the same rules must hold in memory (one instance) and in Redis
/// (several instances). Redis runs when <c>BEECODING_TEST_REDIS</c> is set to a server (e.g. <c>127.0.0.1:6379</c>).</summary>
public class SharedStateTests
{
    private static readonly string? Redis = Environment.GetEnvironmentVariable("BEECODING_TEST_REDIS") is { Length: > 0 } s ? s : null;
    private static readonly Lazy<IConnectionMultiplexer> Mux = new(() => ConnectionMultiplexer.Connect(Redis!));

    public static IEnumerable<object[]> Kinds() => Redis is null ? [["memory"]] : [["memory"], ["redis"]];

    /// <summary>A store; for Redis, a fresh key prefix per test, so tests never see each other's keys.</summary>
    private static Func<ICounterStore> Factory(string kind)
    {
        if (kind == "memory") { var one = new InMemoryCounterStore(); return () => one; }
        var prefix = "t" + Guid.NewGuid().ToString("N")[..8] + ":";
        // each call is a separate store object over the same Redis, like a second web instance
        return () => new RedisCounterStore(Mux.Value, Options.Create(new RealtimeStoreOptions { KeyPrefix = prefix }), NullLogger<RedisCounterStore>.Instance);
    }

    [Theory, MemberData(nameof(Kinds))]
    public async Task Counters_count_expire_and_clear(string kind)
    {
        var s = Factory(kind)();
        Assert.Equal(0, s.Get("a"));
        Assert.Equal(1, s.Increment("a", TimeSpan.FromMilliseconds(300)));
        Assert.Equal(2, s.Increment("a", TimeSpan.FromMilliseconds(300)));
        Assert.Equal(2, s.Get("a"));
        s.Remove("a");
        Assert.Equal(0, s.Get("a"));

        s.Increment("b", TimeSpan.FromMilliseconds(200));
        await Task.Delay(400);
        Assert.Equal(0, s.Get("b"));                                       // the window ended
        Assert.Equal(1, s.Increment("b", TimeSpan.FromMilliseconds(200))); // and a new one starts at 1

        s.Increment("d", TimeSpan.FromSeconds(30)); s.Increment("d", TimeSpan.FromSeconds(30));
        s.Decrement("d"); Assert.Equal(1, s.Get("d"));
        s.Decrement("d"); Assert.Equal(0, s.Get("d"));
        s.Decrement("d"); Assert.Equal(0, s.Get("d"));                    // never below zero, and no phantom key
        Assert.Equal(1, s.Increment("d", TimeSpan.FromSeconds(30)));
    }

    [Theory, MemberData(nameof(Kinds))]
    public async Task A_claim_is_held_until_it_runs_out(string kind)
    {
        var s = Factory(kind)();
        Assert.Null(s.Remaining("c"));
        Assert.True(s.TryClaim("c", TimeSpan.FromMilliseconds(300)));
        Assert.False(s.TryClaim("c", TimeSpan.FromMilliseconds(300)));
        Assert.InRange(s.Remaining("c")!.Value.TotalMilliseconds, 1, 300);
        await Task.Delay(450);
        Assert.True(s.TryClaim("c", TimeSpan.FromMilliseconds(300)));
    }

    [Theory, MemberData(nameof(Kinds))]
    public void Two_instances_share_one_login_cap_even_when_the_guesses_race(string kind)
    {
        var f = Factory(kind);
        var a = new LoginThrottle(f()); var b = new LoginThrottle(f());
        var allowed = 0;
        Parallel.For(0, 60, i =>
        {
            if ((i % 2 == 0 ? a : b).TryBegin($"9.9.9.{i}", "mfa:7", strict: true)) Interlocked.Increment(ref allowed);
        });
        Assert.Equal(10, allowed);                                          // the cap is 10 in total, not 10 per instance
        Assert.True(b.IsBlocked("2.2.2.2", "mfa:7", strict: true));
        a.RecordSuccess("9.9.9.0", "mfa:7");                                // a success clears the account's counters for both
        Assert.False(b.IsBlocked("9.9.9.0", "mfa:7"));
    }

    [Theory, MemberData(nameof(Kinds))]
    public void A_flood_of_refused_attempts_does_not_lock_the_owner_out_elsewhere(string kind)
    {
        var t = new LoginThrottle(Factory(kind)());
        Parallel.For(0, 500, _ => t.TryBegin("1.1.1.1", "victim@x"));        // attacker: 10 count, the rest are refused
        Assert.False(t.TryBegin("1.1.1.1", "victim@x"));
        Assert.True(t.TryBegin("2.2.2.2", "victim@x"));                      // the owner, from another address, is fine
        Assert.False(t.IsBlocked("3.3.3.3", "victim@x"));                    // the account-wide counter stayed far below its 100
    }

    [Theory, MemberData(nameof(Kinds))]
    public void Submit_and_run_spacing_hold_across_instances(string kind)
    {
        var f = Factory(kind);
        var cfg = new PlatformRuntimeConfig(); cfg.Set(false, 1500);
        Assert.True(new RateLimiter(cfg, f()).TryAcquire(5));
        Assert.False(new RateLimiter(cfg, f()).TryAcquire(5));
        Assert.True(new RateLimiter(cfg, f()).TryAcquire(6));

        var c1 = new SubmitCooldown(f()); var c2 = new SubmitCooldown(f());
        Assert.True(c1.TryAcquire(5));
        Assert.False(c2.TryAcquire(5));
        Assert.InRange(c2.SecondsRemaining(5), 0.1, 5);
        Assert.Equal(0, c2.SecondsRemaining(6));
    }

    [Theory, MemberData(nameof(Kinds))]
    public void A_background_job_runs_on_one_instance_only(string kind)
    {
        var f = Factory(kind);
        var a = new JobLock(f()); var b = new JobLock(f());
        Assert.True(a.TryClaim("sweep", TimeSpan.FromMinutes(10)));
        Assert.False(b.TryClaim("sweep", TimeSpan.FromMinutes(10)));
        Assert.True(b.TryClaim("other-job", TimeSpan.FromMinutes(10)));
        Assert.True(((JobLock?)null).ShouldRun("x", TimeSpan.FromMinutes(1)));   // no lock configured: always run
    }

    [Fact]
    public void Redis_that_is_down_falls_back_to_this_instances_memory()
    {
        var down = ConnectionMultiplexer.Connect("127.0.0.1:1,abortConnect=false,connectTimeout=200,syncTimeout=200");
        var s = new RedisCounterStore(down, Options.Create(new RealtimeStoreOptions()), NullLogger<RedisCounterStore>.Instance);
        var t = new LoginThrottle(s);
        for (var i = 0; i < 10; i++) Assert.True(t.TryBegin("1.1.1.1", "a@x"));   // still counts, still answers
        Assert.False(t.TryBegin("1.1.1.1", "a@x"));
    }
}

[Collection("app")]
public class RuntimeCacheTests
{
    [Fact]
    public async Task Reloading_picks_up_a_setting_another_instance_saved()
    {
        using var app = new TestApp();
        app.CreateClient();
        var cfg = app.Services.GetRequiredService<PlatformRuntimeConfig>();
        var admin = app.Services.GetRequiredService<AdminAccess>();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.PlatformRuntimeSettings.FindAsync(1);   // what an admin on ANOTHER instance saved
            row!.StudentMaxProblems = 7; row.JourneyEnabled = false;
            var user = db.Users.First(); user.IsAdmin = true;
            await db.SaveChangesAsync();
            Assert.NotEqual(7, cfg.StudentMaxProblems);                // this instance still has the old copy
            Assert.False(admin.IsAdminEmail(user.Email));
        }

        using (var scope = app.Services.CreateScope())
            await RuntimeCacheLoader.LoadAsync(scope.ServiceProvider);

        Assert.Equal(7, cfg.StudentMaxProblems);
        Assert.False(cfg.JourneyEnabled);
        using (var scope = app.Services.CreateScope())
            Assert.True(admin.IsAdminEmail(scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.First().Email));
    }
}
