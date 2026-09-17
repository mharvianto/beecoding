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
    public async Task<JsonDocument?> GetTodayAsync(string metric, CancellationToken ct = default)
    {
        var flag = metric == "mem" ? "-r" : "-u";
        var output = await RunAsync("sadf", ["-j", "--", flag], ct);
        if (output is null) return null;
        try { return JsonDocument.Parse(output); }
        catch (JsonException) { return null; }
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
