using System.Diagnostics;
using System.Text;

namespace BeeCoding.Services.Judge;

public record ExecResult(
    int ExitCode,      // -1 if killed by signal
    int Signal,        // termsig, 0 if exited normally
    int PeakKb,        // peak RSS
    int WallMs,
    bool TimedOut,     // parent had to force-kill (runner/alarm did not report in time)
    bool StatsMissing,
    string Stdout,
    string Stderr);

/// <summary>
/// Executes a compiled program under the C "runner" helper (rlimits + wait4),
/// optionally wrapped in bubblewrap for filesystem isolation when available.
/// </summary>
public class NativeSandbox(NativeToolchain tc, ILogger<NativeSandbox> log)
{
    private readonly NativeToolchain _tc = tc;
    private readonly ILogger<NativeSandbox> _log = log;

    public async Task<ExecResult> ExecuteAsync(
        string workDir, string exePath, string stdin,
        int timeLimitMs, int memoryLimitKb, CancellationToken ct, string? inputFileName = null)
    {
        var o = _tc.Options;
        var statPath = Path.Combine(workDir, $"stat_{Guid.NewGuid():N}");

        int cpuSec = Math.Max(1, (int)Math.Ceiling(timeLimitMs / 1000.0));
        int wallSec = Math.Max(cpuSec + 1, (int)Math.Ceiling((timeLimitMs + o.HardWallBufferMs) / 1000.0) + 1);
        int asKb = memoryLimitKb;
        int stackKb = Math.Min(memoryLimitKb, 65_536);
        int fsizeKb = 8_192;
        int nproc = 64;

        var runnerArgs =
            $"\"{statPath}\" {cpuSec} {wallSec} {asKb} {stackKb} {fsizeKb} {nproc} \"{exePath}\"";

        ProcessStartInfo psi;
        if (_tc.BwrapUsable)
        {
            var b = new StringBuilder();
            b.Append("--unshare-all --die-with-parent --new-session --clearenv ");
            b.Append("--setenv PATH /usr/bin:/bin ");
            b.Append("--ro-bind /usr /usr --tmpfs /tmp --proc /proc --dev /dev ");
            b.Append("--symlink usr/lib /lib --symlink usr/lib64 /lib64 ");
            b.Append("--symlink usr/bin /bin --symlink usr/sbin /sbin ");
            // The runner binary lives in WorkRoot (outside workDir) — bind it in too.
            b.Append($"--ro-bind \"{_tc.RunnerPath}\" \"{_tc.RunnerPath}\" ");
            // Bind the work dir at the SAME path so stat/exe paths line up.
            b.Append($"--bind \"{workDir}\" \"{workDir}\" --chdir \"{workDir}\" ");
            b.Append($"-- \"{_tc.RunnerPath}\" {runnerArgs}");
            psi = new ProcessStartInfo(_tc.BwrapPath, b.ToString());
        }
        else
        {
            psi = new ProcessStartInfo(_tc.RunnerPath, runnerArgs);
        }

        psi.WorkingDirectory = workDir;
        psi.RedirectStandardInput = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        psi.Environment.Clear();
        psi.Environment["PATH"] = "/usr/bin:/bin";
        psi.Environment["HOME"] = workDir;

        // File-input mode: the program fopen()s this filename from its cwd (= workDir,
        // bound read-write into the sandbox at the same path) instead of reading stdin.
        if (!string.IsNullOrEmpty(inputFileName))
            await File.WriteAllTextAsync(Path.Combine(workDir, inputFileName), stdin, ct);

        using var p = new Process { StartInfo = psi };
        p.Start();

        var stdoutTask = ReadCappedAsync(p.StandardOutput, o.MaxOutputBytes);
        var stderrTask = ReadCappedAsync(p.StandardError, o.MaxOutputBytes);

        try
        {
            if (string.IsNullOrEmpty(inputFileName))
                await p.StandardInput.WriteAsync(stdin.AsMemory(), ct);
        }
        catch { /* program may not read stdin */ }
        finally
        {
            try { p.StandardInput.Close(); } catch { }
        }

        bool timedOut = false;
        using (var hardTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            hardTimeout.CancelAfter(timeLimitMs + o.HardWallBufferMs + 2_000);
            try
            {
                await p.WaitForExitAsync(hardTimeout.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                timedOut = true;
                try { p.Kill(entireProcessTree: true); } catch { }
                try { await p.WaitForExitAsync(ct); } catch { }
            }
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        int exitCode = -1, signal = 0, peakKb = 0, wallMs = 0;
        bool statsMissing = true;
        if (File.Exists(statPath))
        {
            var parts = (await File.ReadAllTextAsync(statPath, ct)).Trim().Split(' ');
            if (parts.Length == 4
                && int.TryParse(parts[0], out exitCode)
                && int.TryParse(parts[1], out signal)
                && int.TryParse(parts[2], out peakKb)
                && int.TryParse(parts[3], out wallMs))
            {
                statsMissing = false;
            }
            try { File.Delete(statPath); } catch { }
        }

        if (timedOut)
        {
            signal = signal == 0 ? 9 : signal;
            if (wallMs == 0) wallMs = timeLimitMs + _tc.Options.HardWallBufferMs;
        }

        return new ExecResult(exitCode, signal, peakKb, wallMs, timedOut, statsMissing, stdout, stderr);
    }

    private static async Task<string> ReadCappedAsync(StreamReader reader, int cap)
    {
        var buf = new char[8192];
        var sb = new StringBuilder();
        int read;
        while ((read = await reader.ReadAsync(buf, 0, buf.Length)) > 0)
        {
            if (sb.Length < cap)
                sb.Append(buf, 0, Math.Min(read, cap - sb.Length));
            // keep draining so the child never blocks on a full pipe
        }
        return sb.ToString();
    }
}
