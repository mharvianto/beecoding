using BeeCoding.Controllers;
using Xunit;

namespace BeeCoding.Tests;

public class RunOutputTests
{
    [Fact]
    public void A_runaway_output_is_cut_for_the_run_box_and_a_normal_one_is_untouched()
    {
        Assert.Equal("hi\n", RunController.CutOutput("hi\n"));
        var cut = RunController.CutOutput(new string('1', 4 * 1024 * 1024));
        Assert.InRange(cut.Length, 100_000, 100_100);
        Assert.EndsWith("characters]", cut);
    }
}
