using System.Diagnostics;
using System.Text;

namespace BeeCoding.Services.Lsp;

/// <summary>
/// One clangd process bound to a throwaway single-file workspace. Talks LSP over stdio
/// (Content-Length framed); this class exposes it as discrete JSON messages so the
/// WebSocket bridge can forward one message per frame.
/// </summary>
public sealed class ClangdSession : IAsyncDisposable
{
    private readonly Process _proc;
    private Stream _stdin = Stream.Null;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();

    public string WorkspaceDir { get; }
    public string MainFilePath { get; }
    public string MainFileUri => new Uri(MainFilePath).AbsoluteUri;

    /// <summary>Raised for each complete JSON-RPC message from clangd's stdout.</summary>
    public event Func<string, Task>? MessageReceived;
    public event Action? Exited;

    private ClangdSession(Process proc, string workspaceDir, string mainFilePath)
    {
        _proc = proc;
        WorkspaceDir = workspaceDir;
        MainFilePath = mainFilePath;
    }

    /// <summary>clang-format's built-in style presets — an allowlist, since this name ends
    /// up written verbatim into a config file clang-format parses (never accept arbitrary
    /// user-supplied YAML here).</summary>
    public static readonly string[] FormatStyles = { "LLVM", "Google", "Chromium", "Mozilla", "WebKit", "Microsoft", "GNU" };

    public static async Task<ClangdSession> StartAsync(LspOptions opt, string language, ILogger log, string? formatStyle = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "beecoding-lsp", "s_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        bool isC = string.Equals(language, "c", StringComparison.OrdinalIgnoreCase);
        var mainFile = Path.Combine(dir, isC ? "main.c" : "main.cpp");
        await File.WriteAllTextAsync(mainFile, "");
        await File.WriteAllTextAsync(Path.Combine(dir, "compile_flags.txt"),
            isC ? "-xc\n-std=gnu11\n" : "-xc++\n-std=gnu++17\n");

        // clang-format (used by clangd for textDocument/formatting) picks this up by
        // searching upward from the file being formatted — one file per session is plenty,
        // no need to re-write it per request.
        var style = FormatStyles.Contains(formatStyle, StringComparer.OrdinalIgnoreCase) ? formatStyle! : "LLVM";
        await File.WriteAllTextAsync(Path.Combine(dir, ".clang-format"), $"BasedOnStyle: {style}\n");

        var args = new List<string>
        {
            "--background-index=false", "--pch-storage=memory", "-j=1",
            "--limit-results=40", "--completion-style=bundled", "--header-insertion=never",
            $"--compile-commands-dir={dir}",
        };

        // Optional runaway guard via prlimit. RLIMIT_DATA (not RLIMIT_AS: clangd reserves
        // far more virtual address space than it ever faults in, so an --as cap makes it
        // abort while indexing standard headers).
        string file = opt.ClangdPath;
        if (opt.MemoryLimitMb > 0 && File.Exists("/usr/bin/prlimit"))
        {
            args.Insert(0, opt.ClangdPath);
            args.Insert(0, $"--data={(long)opt.MemoryLimitMb * 1024 * 1024}");
            file = "/usr/bin/prlimit";
        }

        var psi = new ProcessStartInfo(file)
        {
            WorkingDirectory = dir,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var session = new ClangdSession(proc, dir, mainFile);
        proc.Exited += (_, _) => session.Exited?.Invoke();
        proc.ErrorDataReceived += (_, e) => { if (e.Data is { Length: > 0 }) log.LogTrace("clangd: {Line}", e.Data); };

        proc.Start();
        session._stdin = proc.StandardInput.BaseStream;
        proc.BeginErrorReadLine();
        _ = session.ReadLoopAsync(log);
        return session;
    }

    public async Task SendAsync(string json, CancellationToken ct = default)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        await _writeLock.WaitAsync(ct);
        try
        {
            await _stdin.WriteAsync(header, ct);
            await _stdin.WriteAsync(body, ct);
            await _stdin.FlushAsync(ct);
        }
        finally { _writeLock.Release(); }
    }

    private async Task ReadLoopAsync(ILogger log)
    {
        var stdout = _proc.StandardOutput.BaseStream;
        var buf = new List<byte>(8192);
        var chunk = new byte[8192];
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                int contentLength = -1;
                // read headers up to \r\n\r\n
                while (true)
                {
                    int idx = FindHeaderEnd(buf);
                    if (idx >= 0)
                    {
                        var headerText = Encoding.ASCII.GetString(buf.ToArray(), 0, idx);
                        foreach (var line in headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
                            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                                int.TryParse(line["Content-Length:".Length..].Trim(), out contentLength);
                        buf.RemoveRange(0, idx + 4);
                        break;
                    }
                    int n = await stdout.ReadAsync(chunk, _cts.Token);
                    if (n == 0) return;
                    buf.AddRange(chunk.AsSpan(0, n).ToArray());
                }
                if (contentLength < 0) return;

                while (buf.Count < contentLength)
                {
                    int n = await stdout.ReadAsync(chunk, _cts.Token);
                    if (n == 0) return;
                    buf.AddRange(chunk.AsSpan(0, n).ToArray());
                }

                var json = Encoding.UTF8.GetString(buf.ToArray(), 0, contentLength);
                buf.RemoveRange(0, contentLength);
                if (MessageReceived is { } h) await h(json);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { log.LogDebug(ex, "clangd read loop ended"); }
        finally { Exited?.Invoke(); }
    }

    private static int FindHeaderEnd(List<byte> b)
    {
        for (int i = 0; i + 3 < b.Count; i++)
            if (b[i] == 13 && b[i + 1] == 10 && b[i + 2] == 13 && b[i + 3] == 10) return i;
        return -1;
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { if (!_proc.HasExited) _proc.Kill(entireProcessTree: true); } catch { }
        try { await _proc.WaitForExitAsync(new CancellationTokenSource(2000).Token); } catch { }
        _proc.Dispose();
        try { Directory.Delete(WorkspaceDir, recursive: true); } catch { }
        _cts.Dispose();
    }
}
