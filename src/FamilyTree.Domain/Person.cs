namespace FamilyTree.Domain;

/// <summary>Sex as recorded in documents. Historical records are often incomplete, hence Unknown.</summary>
public enum Sex
{
    Unknown,
    Female,
    Male,
}

public enum NameType
{
    Birth,
    Married,
    Alias,
    Nickname,
}

/// <summary>What's needed to give a person a name, before it has been checked.</summary>
public sealed record NameDetails(string? GivenName, string? Surname, NameType Type);

/// <summary>One of a person's names. People often have several: a birth name, a married name.</summary>
public sealed class PersonName
{
    public const int MaxLength = 100;

    // For Entity Framework, which sets the properties itself.
    private PersonName()
    {
    }

    internal PersonName(int position, string givenName, string? surname, NameType type)
    {
        Position = position;
        GivenName = givenName;
        Surname = surname;
        Type = type;
    }

    /// <summary>The name's place in the person's list. The first is their main name.</summary>
    public int Position { get; private set; }

    public string GivenName { get; private set; } = string.Empty;

    /// <summary>Null when it isn't known.</summary>
    public string? Surname { get; private set; }

    public NameType Type { get; private set; }
}

public enum EventType
{
    Birth,
    Death,
}

/// <summary>Something that happened in a person's life, when and where it's known.</summary>
public sealed class LifeEvent
{
    public const int MaxPlaceLength = 200;

    // For Entity Framework, which sets the properties itself.
    private LifeEvent()
    {
    }

    internal LifeEvent(EventType type, GenealogicalDate? date, string? place)
    {
        Id = Guid.CreateVersion7();
        Type = type;
        Date = date;
        Place = place;
    }

    public Guid Id { get; private set; }

    public EventType Type { get; private set; }

    /// <summary>Null when it isn't known.</summary>
    public GenealogicalDate? Date { get; private set; }

    /// <summary>Free text for now. Null when it isn't known.</summary>
    public string? Place { get; private set; }
}

/// <summary>Someone in the family tree. Create one with <see cref="Create"/>, which enforces the rules.</summary>
public sealed class Person
{
    private readonly List<PersonName> names = [];
    private readonly List<LifeEvent> events = [];

    // For Entity Framework, which sets the properties itself.
    private Person()
    {
    }

    public Guid Id { get; private set; }

    public Sex Sex { get; private set; }

    public IReadOnlyList<PersonName> Names => names;

    public IReadOnlyList<LifeEvent> Events => events;

    /// <summary>The person's events in date order, with undated events last.</summary>
    public IEnumerable<LifeEvent> Timeline => events
        .OrderBy(lifeEvent => lifeEvent.Date is null)
        .ThenBy(lifeEvent => lifeEvent.Date?.SortKey);

    public static Result<Person> Create(Sex sex, IReadOnlyList<NameDetails> names)
    {
        var errors = Validate(names);
        if (errors.Count > 0)
        {
            return Result<Person>.Failure(errors);
        }

        // Version 7 GUIDs start with the time, so new rows go at the end of the table's
        // index instead of scattering across it.
        var person = new Person { Id = Guid.CreateVersion7(), Sex = sex };
        person.names.AddRange(names.Select((name, position) =>
            new PersonName(position, name.GivenName!.Trim(), Blank(name.Surname) ? null : name.Surname!.Trim(), name.Type)));

        return Result<Person>.Success(person);
    }

    /// <summary>
    /// Records an event. <paramref name="dateText"/> can be as fuzzy as the records are, or
    /// empty if the date isn't known. A person is born and dies once, so conflicting
    /// sources will need handling when sources arrive.
    /// </summary>
    public Result<LifeEvent> AddEvent(EventType type, string? dateText, string? place)
    {
        var errors = new Dictionary<string, string[]>();
        if (events.Any(existing => existing.Type == type))
        {
            errors["type"] = [$"This person already has a {type.ToString().ToLowerInvariant()} recorded."];
        }

        GenealogicalDate? date = null;
        if (!Blank(dateText))
        {
            var parsed = GenealogicalDate.Parse(dateText!);
            if (parsed.IsSuccess)
            {
                date = parsed.Value;
            }
            else
            {
                errors["date"] = parsed.Errors["date"];
            }
        }

        if (place?.Trim().Length > LifeEvent.MaxPlaceLength)
        {
            errors["place"] = [$"A place can't be longer than {LifeEvent.MaxPlaceLength} characters."];
        }

        if (errors.Count > 0)
        {
            return Result<LifeEvent>.Failure(errors);
        }

        var lifeEvent = new LifeEvent(type, date, Blank(place) ? null : place!.Trim());
        events.Add(lifeEvent);
        return Result<LifeEvent>.Success(lifeEvent);
    }

    private static Dictionary<string, string[]> Validate(IReadOnlyList<NameDetails> names)
    {
        var errors = new Dictionary<string, string[]>();
        if (names.Count == 0)
        {
            errors["names"] = ["A person needs at least one name."];
        }

        for (var i = 0; i < names.Count; i++)
        {
            if (Blank(names[i].GivenName))
            {
                errors[$"names[{i}].givenName"] = ["A given name is required."];
            }
            else if (names[i].GivenName!.Trim().Length > PersonName.MaxLength)
            {
                errors[$"names[{i}].givenName"] = [$"A given name can't be longer than {PersonName.MaxLength} characters."];
            }

            if (names[i].Surname?.Trim().Length > PersonName.MaxLength)
            {
                errors[$"names[{i}].surname"] = [$"A surname can't be longer than {PersonName.MaxLength} characters."];
            }
        }

        return errors;
    }

    private static bool Blank(string? text) => string.IsNullOrWhiteSpace(text);
}
