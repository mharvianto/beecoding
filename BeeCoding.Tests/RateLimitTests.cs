using BeeCoding.Services;
using Xunit;

namespace BeeCoding.Tests;

public class RateLimitTests
{
    // 50 requests of one user arriving together: exactly one may pass, not all of them.
    private static int Passed(Func<bool> acquire) =>
        Enumerable.Range(0, 50).Select(_ => Task.Run(acquire)).ToArray()
            .Select(t => t.Result).Count(ok => ok);

    [Fact]
    public void SubmitCooldown_lets_only_one_parallel_request_through()
    {
        var c = new SubmitCooldown();
        Assert.Equal(1, Passed(() => c.TryAcquire(7)));
    }

    [Fact]
    public void RateLimiter_lets_only_one_parallel_request_through()
    {
        var r = new RateLimiter(new PlatformRuntimeConfig());
        Assert.Equal(1, Passed(() => r.TryAcquire(7)));
    }
}
