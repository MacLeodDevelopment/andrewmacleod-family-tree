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

/// <summary>Someone in the family tree. Create one with <see cref="Create"/>, which enforces the rules.</summary>
public sealed class Person
{
    private readonly List<PersonName> names = [];

    // For Entity Framework, which sets the properties itself.
    private Person()
    {
    }

    public Guid Id { get; private set; }

    public Sex Sex { get; private set; }

    public IReadOnlyList<PersonName> Names => names;

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
