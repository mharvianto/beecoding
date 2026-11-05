using System.Diagnostics;
using BeeCoding.Services.Judge;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BeeCoding.Tests;

public class CompilerLimitsTests
{
    private static async Task<(CompileResult Result, TimeSpan Took)?> CompileAsync(string code)
    {
        var root = Path.Combine(Path.GetTempPath(), "beecoding-compile-" + Guid.NewGuid().ToString("N"));
        try
        {
            var tc = new NativeToolchain(Options.Create(new JudgeOptions { WorkRoot = root }), NullLogger<NativeToolchain>.Instance);
            try { tc.Initialize(); } catch (InvalidOperationException) { return null; }   // no gcc/g++ on this machine
            var dir = Directory.CreateDirectory(Path.Combine(root, "job")).FullName;
            var sw = Stopwatch.StartNew();
            var result = await new NativeCompiler(tc, NullLogger<NativeCompiler>.Instance).CompileAsync(dir, "cpp", code, CancellationToken.None);
            return (result, sw.Elapsed);
        }
        finally { try { Directory.Delete(root, true); } catch { /* best effort */ } }
    }

    [Fact]
    public async Task A_macro_that_doubles_itself_cannot_eat_the_machines_memory_or_flood_the_output()
    {
        // 600 bytes of source that make an unlimited cc1plus ask for gigabytes (2^30 tokens)
        var lines = new List<string> { "#define L0 x" };
        for (int i = 1; i <= 30; i++) lines.Add($"#define L{i} L{i - 1} L{i - 1}");
        lines.Add("int main(){ int L30; return 0; }");

        if (await CompileAsync(string.Join('\n', lines)) is not { } r) return;
        Assert.False(r.Result.Ok);
        Assert.True(r.Result.Output.Length <= 20_100, $"output kept: {r.Result.Output.Length} chars");
        Assert.True(r.Took < TimeSpan.FromSeconds(9), $"took {r.Took}");   // stopped by the limit, not by the 10 s timeout
    }

    [Fact]
    public async Task A_normal_heavy_program_still_compiles_under_the_limit()
    {
        const string code = "#include <bits/stdc++.h>\nusing namespace std;\nint main(){ vector<map<string,set<int>>> v(3); unordered_map<int,vector<pair<int,int>>> g; priority_queue<tuple<int,int,int>> pq; cout << v.size() << \"\\n\"; }";
        if (await CompileAsync(code) is not { } r) return;
        Assert.True(r.Result.Ok, r.Result.Output);
    }
}
