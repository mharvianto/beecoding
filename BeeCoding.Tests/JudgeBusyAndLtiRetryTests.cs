using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using BeeCoding.Services.Judge;
using BeeCoding.Services.Lti;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class JudgeBusyAndLtiRetryTests
{
    [Fact]
    public async Task A_full_in_process_queue_reports_full_so_a_submit_can_be_refused()
    {
        var q = new InProcessJudgeQueue(Options.Create(new JudgeOptions { QueueCapacity = 1 }));
        Assert.False(JudgeQueueStats.IsFull(q));
        await q.EnqueueGradeAsync(new GradeJob("board", 1, "cpp", "int main(){}", 1000, 65536, null, null, [], null));
        Assert.True(JudgeQueueStats.IsFull(q));
    }

    [Fact]
    public async Task A_grade_the_lms_did_not_take_is_retried_with_growing_waits_then_dropped_and_cleared_on_success()
    {
        using var app = new TestApp();
        using var scope = app.Services.CreateScope();
        var sync = scope.ServiceProvider.GetRequiredService<LtiGradeSyncService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.True(await sync.TrackResultAsync("board", 7, 9, ok: false));    // first failure: the teacher is told
        var row = await db.LtiSyncRetries.AsNoTracking().SingleAsync();
        Assert.Equal(0, row.Attempts);
        Assert.InRange((row.NextAttemptAt - DateTime.UtcNow).TotalMinutes, 0.5, 1.5);

        Assert.False(await sync.TrackResultAsync("board", 7, 9, ok: false));   // a retry that fails again: not told twice
        db.ChangeTracker.Clear();
        row = await db.LtiSyncRetries.AsNoTracking().SingleAsync();
        Assert.Equal(1, row.Attempts);
        Assert.InRange((row.NextAttemptAt - DateTime.UtcNow).TotalMinutes, 4, 6);

        await sync.TrackResultAsync("board", 7, 9, ok: true);                  // the LMS took it
        db.ChangeTracker.Clear();
        Assert.Empty(await db.LtiSyncRetries.ToListAsync());

        for (int i = 0; i < 7; i++) await sync.TrackResultAsync("practice", 3, 9, ok: false);
        db.ChangeTracker.Clear();
        Assert.Empty(await db.LtiSyncRetries.ToListAsync());                   // gave up after the last wait
    }
}
