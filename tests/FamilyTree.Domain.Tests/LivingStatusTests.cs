using FamilyTree.Domain;

namespace FamilyTree.Domain.Tests;

public class LivingStatusTests
{
    private const int ThisYear = 2026;

    private static Person Person(string? born = null, bool died = false)
    {
        var person = FamilyTree.Domain.Person.Create(Sex.Unknown, [new NameDetails("Morag", null, NameType.Birth)]).Value;
        if (born is not null)
        {
            person.AddEvent(EventType.Birth, born, null);
        }

        if (died)
        {
            person.AddEvent(EventType.Death, null, null);
        }

        return person;
    }

    [Fact]
    public void Someone_with_no_dates_is_treated_as_living()
    {
        Assert.True(Person().MayBeLiving(ThisYear));
    }

    [Fact]
    public void Someone_with_a_birth_but_no_date_for_it_is_treated_as_living()
    {
        var person = Person();
        person.AddEvent(EventType.Birth, null, "Innellan");

        Assert.True(person.MayBeLiving(ThisYear));
    }

    [Fact]
    public void A_recorded_death_means_they_are_not_living_even_without_a_date()
    {
        Assert.False(Person(died: true).MayBeLiving(ThisYear));
        Assert.False(Person("1990", died: true).MayBeLiving(ThisYear));
    }

    [Theory]
    // Exact years, either side of the 110-year limit.
    [InlineData("1915", false)]
    [InlineData("1916", true)]
    [InlineData("12 March 1960", true)]
    [InlineData("1870", false)]
    // "About" could be later than it says.
    [InlineData("abt 1905", false)]
    [InlineData("abt 1906", true)]
    [InlineData("est 1906", true)]
    [InlineData("calc 1906", true)]
    // "Before" can't be later than it says.
    [InlineData("bef 1915", false)]
    [InlineData("bef 1916", true)]
    // A range could be as late as its end.
    [InlineData("bet 1900 and 1915", false)]
    [InlineData("bet 1900 and 1916", true)]
    [InlineData("Q2 1915", false)]
    // "After" has no limit, however long ago it starts.
    [InlineData("aft 1850", true)]
    public void A_birth_date_decides_it_by_the_latest_year_it_could_mean(string born, bool mayBeLiving)
    {
        Assert.Equal(mayBeLiving, Person(born).MayBeLiving(ThisYear));
    }
}
