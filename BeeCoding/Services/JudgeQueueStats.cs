using BeeCoding.Services.Judge;

namespace BeeCoding.Services;

/// <summary>How many jobs are waiting for a judge slot — works for both queue backends.</summary>
public static class JudgeQueueStats
{
    /// <summary>Jobs waiting to be picked up (not the ones already running); null when the backend can't say.</summary>
    public static async Task<int?> PendingAsync(IJudgeQueue queue)
    {
        try
        {
            return queue switch
            {
                InProcessJudgeQueue ip => ip.PendingJobCount >= 0 ? ip.PendingJobCount : null,
                RedisJudgeQueue rq => (int)Math.Min(int.MaxValue, await rq.PendingJobCountAsync()),
                _ => null,
            };
        }
        catch { return null; }   // a hiccup reading the count must never fail a submit
    }

    /// <summary>The in-process queue is bounded and a full one makes a submit wait for room; the Redis list is not bounded.</summary>
    public static bool IsFull(IJudgeQueue queue) => queue is InProcessJudgeQueue { IsFull: true };
}
