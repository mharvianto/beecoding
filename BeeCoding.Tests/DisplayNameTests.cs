using BeeCoding.Services;
using Xunit;

namespace BeeCoding.Tests;

public class DisplayNameTests
{
    [Fact]
    public void Typed_names_are_cleaned_and_held_to_two_to_forty_characters()
    {
        Assert.Null(DisplayNamePolicy.Validate("  Budi   Santoso ", out var ok));
        Assert.Equal("Budi Santoso", ok);
        Assert.NotNull(DisplayNamePolicy.Validate("A", out _));
        Assert.NotNull(DisplayNamePolicy.Validate(" ​\n ", out _));            // only invisible characters
        Assert.NotNull(DisplayNamePolicy.Validate(new string('x', 41), out _));
        Assert.Null(DisplayNamePolicy.Validate(new string('x', 40), out _));
        Assert.Null(DisplayNamePolicy.Validate("Bu‮di\r\nX", out var stripped));   // bidi override and newline removed
        Assert.Equal("Budi X", stripped);
    }

    [Fact]
    public void Names_from_outside_are_cut_not_refused()
    {
        Assert.Equal(40, DisplayNamePolicy.FromOutside(new string('x', 200), "LTI user").Length);
        Assert.Equal("LTI user", DisplayNamePolicy.FromOutside(null, "LTI user"));
        Assert.Equal("LTI user", DisplayNamePolicy.FromOutside("​", "LTI user"));
        var cut = DisplayNamePolicy.FromOutside(new string('x', 39) + "😀", "f");   // would split the emoji's surrogate pair
        Assert.Equal(39, cut.Length);
    }
}
