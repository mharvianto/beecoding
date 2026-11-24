using System.Net.Http.Json;
using System.Text.Json;
using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class LspUsageTests
{
    [Fact]
    public async Task An_idle_server_stores_nothing_and_the_chart_adds_instances_up_with_the_peak()
    {
        Environment.SetEnvironmentVariable("Admin__Emails", "teacher@demo.test");
        try
        {
            using var app = new TestApp();
            var sampler = app.Services.GetServices<IHostedService>().OfType<LspUsageSampler>().Single();
            Assert.False(await sampler.SampleAsync(DateTime.UtcNow, default));   // nobody attached: no row

            var now = DateTime.UtcNow;
            var minute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc).AddMinutes(-10);
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.LspUsageSamples.AddRange(
                    new LspUsageSample { At = minute, Host = "a", Students = 20, Processes = 2, RssMb = 900 },
                    new LspUsageSample { At = minute, Host = "b", Students = 14, Processes = 1, RssMb = 600 },     // same minute, other instance: 34 students, 1500 MB
                    new LspUsageSample { At = minute.AddMinutes(1), Host = "a", Students = 3, Processes = 1 },
                    new LspUsageSample { At = now.AddDays(-40), Host = "a", Students = 99, Processes = 3 });   // older than the window
                await db.SaveChangesAsync();
            }

            var admin = app.CreateClient();
            (await admin.PostAsJsonAsync("/api/auth/login", new { email = "teacher@demo.test", password = "password" })).EnsureSuccessStatusCode();
            var r = await admin.GetFromJsonAsync<JsonElement>("/api/admin-ui/system/lsp-usage?hours=24");
            Assert.Equal(34, r.GetProperty("peakStudents").GetInt32());
            Assert.Equal(1500, r.GetProperty("peakRssMb").GetInt32());
            Assert.Equal(new[] { 34, 3 }, r.GetProperty("points").EnumerateArray().Select(p => p.GetProperty("students").GetInt32()).ToArray());
        }
        finally { Environment.SetEnvironmentVariable("Admin__Emails", null); }
    }
}

public class ClangdMemoryTests
{
    [Fact]
    public async Task Resident_memory_counts_a_running_clangd()
    {
        if (!File.Exists("/usr/bin/clangd")) return;   // same guard idea as the shared tests: needs clangd on this machine
        var s = await BeeCoding.Services.Lsp.ClangdSession.StartAsync(new BeeCoding.Services.Lsp.LspOptions { ClangdPath = "clangd", MemoryLimitMb = 1200 }, "c",
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        try
        {
            await Task.Delay(500);
            // At least ours: other test classes may run a clangd at the same time, so no upper bound and no check after it stops.
            Assert.True(BeeCoding.Services.Lsp.ClangdSession.ResidentMb() >= 5);   // MemoryLimitMb set: started through prlimit, which execs clangd
        }
        finally { await s.DisposeAsync(); }
    }
}
