using System.Diagnostics;
using System.Text;

namespace BeeCoding.Services.Judge;

public record CompileResult(bool Ok, string Output, string? ExePath);

public class NativeCompiler(NativeToolchain tc, ILogger<NativeCompiler> log)
{
    private readonly NativeToolchain _tc = tc;
    private readonly ILogger<NativeCompiler> _log = log;

    // The compiler runs fully untrusted source, and a few hundred bytes can make it ask for gigabytes (a macro that doubles itself
    // thirty times): it gets its own memory / CPU / output-file ceiling, like the programs get. 2 GB of address space is far above what
    // a heavy <bits/stdc++.h> program needs (it compiles inside 1 GB).
    private const string PrlimitPath = "/usr/bin/prlimit";
    private const string CompilerLimits = "--as=2147483648 --cpu=20 --fsize=67108864";
    // What of the compiler's messages is kept: it is stored with the submission and sent to the browser.
    internal const int MaxOutputChars = 20_000;
    private static readonly bool HaveLimiter = File.Exists(PrlimitPath);
    private static int _warned;

    public static string SourceName(string language) => Normalize(language) == "c" ? "main.c" : "main.cpp";

    public static string Normalize(string language) =>
        (language ?? "").Trim().ToLowerInvariant() is "c" ? "c" : "cpp";

    public async Task<CompileResult> CompileAsync(string workDir, string language, string code, CancellationToken ct)
    {
        var lang = Normalize(language);
        var srcPath = Path.Combine(workDir, SourceName(lang));
        var exePath = Path.Combine(workDir, "prog");
        await File.WriteAllTextAsync(srcPath, code, ct);

        var (compilerPath, compilerArgs) = lang == "c"
            ? (_tc.GccPath, $"-O2 -std=gnu11 -pipe -o \"{exePath}\" \"{srcPath}\" -lm")
            : (_tc.GppPath, $"-O2 -std=gnu++17 -pipe -o \"{exePath}\" \"{srcPath}\"");

        // The compiler processes fully untrusted source. Jail it the same way runs are
        // jailed when bwrap is usable, so `#include "/etc/passwd"` (or #embed) can't read
        // host files and leak them back in the compile-error output.
        ProcessStartInfo psi;
        if (_tc.BwrapUsable)
        {
            var b = new StringBuilder();
            b.Append("--unshare-all --die-with-parent --new-session --clearenv ");
            b.Append("--setenv PATH /usr/bin:/bin --setenv TMPDIR /tmp ");
            b.Append("--ro-bind /usr /usr --tmpfs /tmp --proc /proc --dev /dev ");
            b.Append("--symlink usr/lib /lib --symlink usr/lib64 /lib64 ");
            b.Append("--symlink usr/bin /bin --symlink usr/sbin /sbin ");
            b.Append($"--bind \"{workDir}\" \"{workDir}\" --chdir \"{workDir}\" ");
            b.Append($"-- {Limited(compilerPath, compilerArgs)}");
            psi = new ProcessStartInfo(_tc.BwrapPath, b.ToString());
        }
        else
        {
            psi = HaveLimiter
                ? new ProcessStartInfo(PrlimitPath, $"{CompilerLimits} -- \"{compilerPath}\" {compilerArgs}")
                : new ProcessStartInfo(compilerPath, compilerArgs);
        }
        if (!HaveLimiter && Interlocked.Exchange(ref _warned, 1) == 0) _log.LogWarning("prlimit not found: the compiler runs without a memory limit");

        psi.WorkingDirectory = workDir;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;

        using var p = new Process { StartInfo = psi };
        var sb = new StringBuilder();
        var truncated = false;
        void Collect(string? line)
        {
            if (line is null) return;
            lock (sb)   // stdout and stderr arrive on different threads
            {
                if (sb.Length >= MaxOutputChars) { truncated = true; return; }   // keep draining the pipe, keep nothing
                sb.AppendLine(line);
            }
        }
        p.OutputDataReceived += (_, e) => Collect(e.Data);
        p.ErrorDataReceived += (_, e) => Collect(e.Data);

        try
        {
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(_tc.Options.CompileTimeoutMs);
            await p.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(p);
            return new CompileResult(false, sb.ToString() + "\n[compilation timed out]", null);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "compiler launch failed");
            return new CompileResult(false, "internal compiler error", null);
        }

        var ok = p.ExitCode == 0 && File.Exists(exePath);
        string output;
        lock (sb)
        {
            output = sb.ToString().Replace(workDir + "/", "").Replace(workDir, "").Trim();
            if (truncated) output += "\n[output truncated]";
        }
        return new CompileResult(ok, output, ok ? exePath : null);
    }

    /// <summary>The compiler command line, behind prlimit when it exists (the bwrap path has /usr bound in, so the same path works there).</summary>
    private static string Limited(string compilerPath, string compilerArgs) =>
        HaveLimiter ? $"{PrlimitPath} {CompilerLimits} -- \"{compilerPath}\" {compilerArgs}" : $"\"{compilerPath}\" {compilerArgs}";

    private static void TryKill(Process p)
    {
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
    }
}
