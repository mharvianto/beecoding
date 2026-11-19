using BeeCoding.Models;
using BeeCoding.Services;
using Xunit;

namespace BeeCoding.Tests;

public class PracticeAuthorTests
{
    [Fact]
    public void A_practice_problem_names_its_author_unless_the_account_is_gone()
    {
        var b = new BankProblem { Id = 1, Slug = "s", Title = "T", TestCases = [], Owner = new User { DisplayName = "Ana" } };
        Assert.Equal("Ana", Mapping.ToPracticeDto(b, false).AuthorName);

        b.Owner.DeletedAt = DateTime.UtcNow;
        Assert.Null(Mapping.ToPracticeDto(b, false).AuthorName);

        b.Owner = null;
        Assert.Null(Mapping.ToPracticeDto(b, false).AuthorName);
    }
}
