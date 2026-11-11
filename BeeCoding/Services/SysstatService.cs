using System.Diagnostics;
using System.Text.Json;

namespace BeeCoding.Services;

/// <summary>
/// Reads host CPU/memory history from sysstat (sar/sadf) — one machine's own local data
/// only. Behind a multi-instance deployment (a load balancer across several VMs, or the
/// web/judge split in DEPLOY.md §2.6), each instance can only ever report on itself: a
/// request answered by instance A never sees instance B's numbers. See
/// AdminUiController.Sysstat's Hostname field — always check it before trusting a chart,
/// since a later request may land on a different instance. For real multi-host monitoring,
/// a dedicated stack (Prometheus + node_exporter + Grafana) is the right tool, not this.
/// </summary>
public class SysstatService
{
    private bool? _installed;

    public async Task<bool> IsInstalledAsync(CancellationToken ct = default)
    {
        _installed ??= await RunAsync("sadf", ["-V"], ct) is not null;
        return _installed.Value;
    }

    /// <summary>Today's (server-local calendar day) history for one metric — "cpu" or
    /// "mem" — as sysstat's own JSON output (sadf -j), parsed but not reshaped, since the
    /// exact field set varies a bit by sysstat version. Null if sysstat isn't installed, has
    /// no data yet for today, or its output couldn't be parsed.</summary>
    public async Task<JsonDocument?> GetTodayAsync(string metric, int daysAgo = 0, CancellationToken ct = default)
    {
        var flag = metric == "mem" ? "-r" : "-u";
        string[] args = ["-j", "--", flag];
        if (daysAgo > 0)
        {
            var file = DayFile(DateTime.Now.Date.AddDays(-daysAgo));
            if (file is null) return null;
            args = ["-j", file, "--", flag];   // sadf reads that day's saved file instead of today's
        }
        var output = await RunAsync("sadf", args, ct);
        if (output is null) return null;
        try { return JsonDocument.Parse(output); }
        catch (JsonException) { return null; }
    }

    /// <summary>Start of the "hour" or "6h" bucket (UTC) a sample falls in.</summary>
    public static DateTime BucketStart(DateTime utc, string bucket)
    {
        int step = bucket == "6h" ? 6 : 1;
        return new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour / step * step, 0, 0, DateTimeKind.Utc);
    }

    // Debian/Ubuntu keep /var/log/sysstat/saDD (or saYYYYMMDD), RHEL /var/log/sa/saDD. saDD is reused every month, so a
    // file not written on the wanted day is last month's leftover and is refused.
    private static string? DayFile(DateTime day)
    {
        // HISTORY above 28 makes sysstat file each month under its own YYYYMM folder
        foreach (var dir in new[] { "/var/log/sysstat", "/var/log/sa" }.SelectMany(d => new[] { d, Path.Combine(d, $"{day:yyyyMM}") }))
            foreach (var name in new[] { $"sa{day:yyyyMMdd}", $"sa{day:dd}" })
            {
                var f = Path.Combine(dir, name);
                if (File.Exists(f) && File.GetLastWriteTime(f).Date >= day && File.GetLastWriteTime(f).Date <= day.AddDays(1)) return f;
            }
        return null;
    }

    private static async Task<string?> RunAsync(string file, string[] args, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var proc = Process.Start(psi);
            if (proc is null) return null;
            var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            return proc.ExitCode == 0 ? stdout : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or ObjectDisposedException)
        {
            return null;   // binary not on PATH, or similar — "not installed" from the caller's POV
        }
    }
}
