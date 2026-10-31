using FamilyTree.Domain;

namespace FamilyTree.Domain.Tests;

public class GenealogicalDateTests
{
    private static GenealogicalDate Date(string text) => GenealogicalDate.Parse(text).Value;

    [Theory]
    // Exact, and known only to the month or year.
    [InlineData("12 March 1901", "12 March 1901")]
    [InlineData("12 Mar 1901", "12 March 1901")]
    [InlineData("12th March 1901", "12 March 1901")]
    [InlineData("12/03/1901", "12 March 1901")]
    [InlineData("1901-03-12", "12 March 1901")]
    [InlineData("Mar 1901", "March 1901")]
    [InlineData("Sept 1901", "September 1901")]
    [InlineData("1901", "1901")]
    // Approximate.
    [InlineData("abt 1870", "c. 1870")]
    [InlineData("ABT 1870", "c. 1870")]
    [InlineData("about 1870", "c. 1870")]
    [InlineData("c. 1870", "c. 1870")]
    [InlineData("circa 1870", "c. 1870")]
    [InlineData("est 1870", "est. 1870")]
    [InlineData("calc 1870", "calc. 1870")]
    // Before and after.
    [InlineData("bef Mar 1901", "before March 1901")]
    [InlineData("before 1901", "before 1901")]
    [InlineData("aft 1880", "after 1880")]
    [InlineData("after 3 May 1880", "after 3 May 1880")]
    // Ranges.
    [InlineData("bet 1870 and 1875", "between 1870 and 1875")]
    [InlineData("between Mar 1870 and Jun 1871", "between March 1870 and June 1871")]
    [InlineData("from 1870 to 1875", "between 1870 and 1875")]
    [InlineData("1870-75", "between 1870 and 1875")]
    [InlineData("1870–1875", "between 1870 and 1875")]
    [InlineData("Q2 1880", "between April 1880 and June 1880")]
    public void Understands_how_family_history_records_write_dates(string text, string expected)
    {
        var result = GenealogicalDate.Parse(text);

        Assert.Equal(expected, result.Value.Display);
    }

    [Theory]
    [InlineData("next Tuesday")]
    [InlineData("31 Feb 1900")]
    [InlineData("29 Feb 1900")]
    [InlineData("13/13/1900")]
    [InlineData("Smarch 1901")]
    [InlineData("soon 1901")]
    [InlineData("bet 1880 and 1870")]
    [InlineData("12 1901")]
    [InlineData("March 12 1901x")]
    [InlineData("")]
    public void Refuses_what_it_cannot_understand_and_says_what_would_work(string text)
    {
        var result = GenealogicalDate.Parse(text);

        Assert.False(result.IsSuccess);
        Assert.Contains("abt 1870", Assert.Single(result.Errors["date"]));
    }

    [Fact]
    public void Accepts_leap_days_in_leap_years()
    {
        Assert.Equal("29 February 1904", Date("29 Feb 1904").Display);
    }

    [Fact]
    public void Keeps_the_date_as_it_was_entered()
    {
        Assert.Equal("Abt 1870", Date("  Abt 1870 ").OriginalText);
    }

    [Fact]
    public void Dates_sort_chronologically_however_they_were_written()
    {
        var dates = new[] { "after 1880", "1880", "before 1880", "Mar 1880", "12 Jan 1880", "abt 1870", "bet 1875 and 1890" };

        var sorted = dates.OrderBy(text => Date(text).SortKey).ToArray();

        Assert.Equal(["abt 1870", "bet 1875 and 1890", "before 1880", "1880", "12 Jan 1880", "Mar 1880", "after 1880"], sorted);
    }
}
