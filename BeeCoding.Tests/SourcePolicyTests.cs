using BeeCoding.Services.Judge;
using Xunit;

namespace BeeCoding.Tests;

public class SourcePolicyTests
{
    private static string? Headers(string code) => SourcePolicy.Violation(code, "algorithm");
    private static string? Symbol(string code) => SourcePolicy.Violation(code, null, "sort");

    [Fact]
    public void A_banned_include_is_refused_but_dropping_it_with_a_comment_is_fine()
    {
        Assert.NotNull(Headers("#include <algorithm>\nint main(){}"));
        Assert.NotNull(Headers("  #  include <ALGORITHM>\nint main(){}"));                 // spacing and case
        Assert.Null(Headers("// #include <algorithm>\nint main(){}"));                    // commented out
        Assert.Null(Headers("/* #include <algorithm> */\nint main(){}"));
        Assert.Null(Headers("/*\n#include <algorithm>\n*/\n#include <cstdio>\nint main(){}"));
        Assert.NotNull(Headers("/* x */ #include <algorithm>\nint main(){}"));            // a comment elsewhere does not hide it
    }

    [Fact]
    public void A_line_continuation_does_not_hide_an_include_or_a_banned_symbol()
    {
        Assert.NotNull(Headers("#inc\\\nlude <algorithm>\nint main(){}"));
        Assert.NotNull(Headers("#inc\\\r\nlude <algorithm>\nint main(){}"));
        Assert.NotNull(Symbol("int main(){ so\\\nrt(a, b); }"));
    }

    [Fact]
    public void A_restricted_problem_refuses_the_umbrella_header_and_a_banned_symbol_in_a_comment_or_string_is_ignored()
    {
        Assert.NotNull(SourcePolicy.Violation("#include <bits/stdc++.h>\nint main(){}", "algorithm"));
        Assert.Null(SourcePolicy.Violation("#include <bits/stdc++.h>\nint main(){}", null));   // nothing restricted: anything goes
        Assert.Null(Symbol("// we do not use sort here\nint main(){ puts(\"sort of\"); }"));
        Assert.NotNull(Symbol("int main(){ std::sort(a, b); }"));
        Assert.Null(Symbol("int sorted = 0;"));                                            // an identifier that merely contains it
    }
}
