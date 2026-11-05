using BeeCoding.Models;
using BeeCoding.Services.Judge;
using Xunit;

namespace BeeCoding.Tests;

public class JudgeRulesTests
{
    private const int Limit = 1000, MemKb = 32768;
    private static ExecResult Run(int exit = 0, int signal = 0, int peakKb = 1000, int wallMs = 50, bool timedOut = false, bool statsMissing = false, string stderr = "")
        => new(exit, signal, peakKb, wallMs, timedOut, statsMissing, "", stderr);
    private static Verdict Classify(ExecResult r) => VerdictEvaluator.ClassifyRun(r, Limit, MemKb);

    [Fact]
    public void A_clean_exit_is_accepted_and_every_way_of_running_out_of_time_is_a_time_limit()
    {
        Assert.Equal(Verdict.Accepted, Classify(Run()));
        Assert.Equal(Verdict.TimeLimit, Classify(Run(timedOut: true)));           // the parent had to kill it
        Assert.Equal(Verdict.TimeLimit, Classify(Run(exit: -1, signal: 24)));      // SIGXCPU: cpu rlimit
        Assert.Equal(Verdict.TimeLimit, Classify(Run(exit: -1, signal: 9)));       // SIGKILL
        Assert.Equal(Verdict.TimeLimit, Classify(Run(wallMs: Limit + 400)));       // exited, but too slowly on the wall clock
        Assert.Equal(Verdict.Accepted, Classify(Run(wallMs: Limit + 399)));
    }

    [Fact]
    public void A_crash_is_a_memory_limit_only_when_the_allocator_or_the_peak_says_so_otherwise_a_runtime_error()
    {
        Assert.Equal(Verdict.MemoryLimit, Classify(Run(exit: -1, signal: 6, stderr: "terminate called after throwing std::bad_alloc")));
        Assert.Equal(Verdict.MemoryLimit, Classify(Run(exit: -1, signal: 11, peakKb: MemKb * 70 / 100 + 1)));   // segfault with RSS at (just over) 70% of the limit
        Assert.Equal(Verdict.RuntimeError, Classify(Run(exit: -1, signal: 11, peakKb: 2000)));              // plain segfault
        Assert.Equal(Verdict.RuntimeError, Classify(Run(exit: 1)));                                          // non-zero exit
        Assert.Equal(Verdict.RuntimeError, Classify(Run(statsMissing: true)));                               // no report from the runner is never a pass
    }

    [Fact]
    public void Output_is_compared_without_trailing_spaces_blank_lines_or_windows_line_ends_but_nothing_else()
    {
        Assert.True(VerdictEvaluator.OutputMatches("1 2\n3\n", "1 2\r\n3"));
        Assert.True(VerdictEvaluator.OutputMatches("ok   \n\n\n", "ok"));
        Assert.True(VerdictEvaluator.OutputMatches("", "\n"));
        Assert.False(VerdictEvaluator.OutputMatches("1  2", "1 2"));         // inner whitespace counts
        Assert.False(VerdictEvaluator.OutputMatches("\nok", "ok"));          // a leading blank line counts
        Assert.False(VerdictEvaluator.OutputMatches("OK", "ok"));            // case counts
        Assert.False(VerdictEvaluator.OutputMatches("1\n2", "1"));
    }

    [Theory]
    [InlineData("input.txt", "input.txt")]
    [InlineData("  data_1.in ", "data_1.in")]
    [InlineData("../etc/passwd", "..etcpasswd")]    // separators are stripped, never kept
    [InlineData("a/b\\c", "abc")]
    [InlineData(".", null)]
    [InlineData("..", null)]
    [InlineData("...", null)]
    [InlineData("prog", null)]                      // the compiled program
    [InlineData("main.cpp", null)]
    [InlineData("stat_1234", null)]                 // the runner's stats file
    [InlineData("", null)]
    [InlineData("///", null)]
    public void An_input_file_name_can_only_ever_name_a_plain_file_in_the_work_directory(string given, string? expected)
        => Assert.Equal(expected, InputFilePolicy.Normalize(given));
}
