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

    internal void Change(string givenName, string? surname, NameType type)
    {
        GivenName = givenName;
        Surname = surname;
        Type = type;
    }
}

public enum EventType
{
    Birth,
    Death,
}

/// <summary>Something that happened in a person's life, when and where it's known.</summary>
public sealed class LifeEvent
{
    public const int MaxPlaceLength = EventDetails.MaxPlaceLength;

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

    internal void Change(GenealogicalDate? date, string? place)
    {
        Date = date;
        Place = place;
    }
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
        person.SetNames(names);

        return Result<Person>.Success(person);
    }

    /// <summary>Replaces the person's sex and names, by the same rules as <see cref="Create"/>.</summary>
    public Result<Person> Update(Sex sex, IReadOnlyList<NameDetails> names)
    {
        var errors = Validate(names);
        if (errors.Count > 0)
        {
            return Result<Person>.Failure(errors);
        }

        Sex = sex;
        SetNames(names);

        return Result<Person>.Success(this);
    }

    /// <summary>
    /// Existing names are changed in place, not replaced, because a name's position is part
    /// of its key in the database: removing and re-adding the same key in one save would clash.
    /// </summary>
    private void SetNames(IReadOnlyList<NameDetails> details)
    {
        names.RemoveAll(name => name.Position >= details.Count);

        for (var position = 0; position < details.Count; position++)
        {
            var givenName = details[position].GivenName!.Trim();
            var surname = Clean(details[position].Surname);

            if (names.Find(name => name.Position == position) is { } existing)
            {
                existing.Change(givenName, surname, details[position].Type);
            }
            else
            {
                names.Add(new PersonName(position, givenName, surname, details[position].Type));
            }
        }
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

        var date = EventDetails.Read(dateText, place, errors);
        if (errors.Count > 0)
        {
            return Result<LifeEvent>.Failure(errors);
        }

        var lifeEvent = new LifeEvent(type, date, EventDetails.CleanPlace(place));
        events.Add(lifeEvent);
        return Result<LifeEvent>.Success(lifeEvent);
    }

    /// <summary>Changes when and where an event happened. Its type stays the same.</summary>
    public Result<LifeEvent> ChangeEvent(Guid eventId, string? dateText, string? place)
    {
        var errors = new Dictionary<string, string[]>();
        var date = EventDetails.Read(dateText, place, errors);
        if (errors.Count > 0)
        {
            return Result<LifeEvent>.Failure(errors);
        }

        var lifeEvent = events.Single(existing => existing.Id == eventId);
        lifeEvent.Change(date, EventDetails.CleanPlace(place));
        return Result<LifeEvent>.Success(lifeEvent);
    }

    public void RemoveEvent(Guid eventId) => events.RemoveAll(existing => existing.Id == eventId);

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

    private static string? Clean(string? text) => Blank(text) ? null : text!.Trim();
}
