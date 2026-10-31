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

    [Theory]
    [InlineData("1650")]
    [InlineData("12 Mar 1650")]
    [InlineData("abt 1540")]
    public void Handles_dates_centuries_old(string text)
    {
        Assert.True(GenealogicalDate.Parse(text).IsSuccess);
    }

    [Theory]
    // Britain used the Julian calendar until 1752, where every fourth year was a leap year.
    [InlineData("29 Feb 1700", "29 February 1700")]
    [InlineData("29 Feb 1500", "29 February 1500")]
    [InlineData("29 Feb 1752", "29 February 1752")]
    // After that, the Gregorian rules: 1800 and 1900 weren't leap years, but 2000 was.
    [InlineData("29 Feb 2000", "29 February 2000")]
    public void Leap_days_follow_the_calendar_of_the_time(string text, string expected)
    {
        Assert.Equal(expected, Date(text).Display);
    }

    [Theory]
    [InlineData("29 Feb 1701")]
    [InlineData("29 Feb 1800")]
    [InlineData("29 Feb 1900")]
    [InlineData("30 Feb 1700")]
    public void Leap_days_that_never_happened_are_refused(string text)
    {
        Assert.False(GenealogicalDate.Parse(text).IsSuccess);
    }

    [Theory]
    // Until 1752 the legal year began on 25 March, so early-year dates were often written both ways.
    [InlineData("1650/51", "1650/51")]
    [InlineData("1650/1651", "1650/51")]
    [InlineData("11 Feb 1731/2", "11 February 1731/32")]
    [InlineData("Mar 1699/00", "March 1699/00")]
    [InlineData("abt 1650/51", "c. 1650/51")]
    [InlineData("bef 1650/51", "before 1650/51")]
    [InlineData("bet 1650/51 and 1660", "between 1650/51 and 1660")]
    [InlineData("29 Feb 1699/00", "29 February 1699/00")]
    public void Understands_dual_years(string text, string expected)
    {
        Assert.Equal(expected, Date(text).Display);
    }

    [Theory]
    [InlineData("1650/52")]
    [InlineData("1650/49")]
    [InlineData("1800/01")]
    [InlineData("12 June 1650/51")]
    public void Refuses_dual_years_that_cannot_be_right(string text)
    {
        Assert.False(GenealogicalDate.Parse(text).IsSuccess);
    }

    [Fact]
    public void Dual_years_sort_by_the_later_year()
    {
        var dates = new[] { "Jun 1651", "11 Feb 1650/51", "Dec 1650" };

        var sorted = dates.OrderBy(text => Date(text).SortKey).ToArray();

        Assert.Equal(["Dec 1650", "11 Feb 1650/51", "Jun 1651"], sorted);
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
